#pragma once
#if defined(URUI_DEBUG)
#include "../urui.h"
#include <chrono>
#include <RmlUi/Core/Element.h>
namespace urui_lookup
{
    extern thread_local bool inline_enabled;
    extern thread_local int profile_mode;
    extern thread_local bool style_scope;
    extern thread_local UruiLookupProfile profile;
    struct StyleScope
    {
        bool active, old = false;
        StyleScope() : active(profile_mode != 0)
        {
            if (active)
            {
                old = style_scope;
                style_scope = true;
            }
        }
        ~StyleScope()
        {
            if (active)
                style_scope = old;
        }
    };
    struct Scope
    {
        int mode;
        bool style;
        std::chrono::steady_clock::time_point start;
        Scope() : mode(profile_mode), style(style_scope)
        {
            if (mode)
            {
                ++profile.queries;
                if (style)
                    ++profile.style_queries;
                start = std::chrono::steady_clock::now();
            }
        }
        void Visit(size_t frontier)
        {
            if (mode > 1)
            {
                ++profile.nodes_visited;
                if (frontier > profile.max_frontier)
                    profile.max_frontier = frontier;
            }
        }
        ~Scope()
        {
            if (mode)
            {
                auto ns = std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now() - start)
                              .count();
                profile.lookup_ns += ns;
                if (style)
                    profile.style_lookup_ns += ns;
            }
        }
    };
    // Per-call 2 KiB FIFO. This is not an element/ID cache or a DOM size limit.
    // Preserve breadth-first order; spill remaining queue before current children.
    inline Rml::Element* InlineFind(Rml::Element* root, const Rml::String& id, Scope& scope)
    {
        constexpr unsigned capacity = 256, mask = capacity - 1;
        Rml::Element* fifo[capacity];
        unsigned head = 0, count = 1;
        fifo[0] = root;
        if (scope.mode > 1)
            ++profile.inline_calls;
        while (count)
        {
            scope.Visit(count);
            auto* element = fifo[head];
            head = (head + 1) & mask;
            --count;
            if (element->GetId() == id)
                return element;
            const int children = element->GetNumChildren();
            if (children > static_cast<int>(capacity - count))
            {
                if (scope.mode > 1)
                    ++profile.overflow_calls;
                Rml::Queue<Rml::Element*> pending;
                for (unsigned i = 0; i < count; i++)
                    pending.push(fifo[(head + i) & mask]);
                for (int i = 0; i < children; i++)
                    pending.push(element->GetChild(i));
                while (!pending.empty())
                {
                    scope.Visit(pending.size());
                    auto* next = pending.front();
                    pending.pop();
                    if (next->GetId() == id)
                        return next;
                    for (int i = 0; i < next->GetNumChildren(); i++)
                        pending.push(next->GetChild(i));
                }
                return nullptr;
            }
            for (int i = 0; i < children; i++)
            {
                fifo[(head + count) & mask] = element->GetChild(i);
                ++count;
            }
        }
        return nullptr;
    }
} // namespace urui_lookup

#endif
