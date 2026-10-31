#pragma once
#include <RmlUi/Core/ObserverPtr.h>
#include <RmlUi/Core/Element.h>
#include <RmlUi/Core/ElementDocument.h>
#include <thread>
#include <unordered_map>
#include <cstdint>
// Explicit object identity, never a selector/ID cache. ObserverPtr is weak and
// not thread safe; all registry operations are confined to the context thread.
class ElementReferences
{
    std::thread::id owner{};
    std::unordered_map<uint64_t, Rml::ObserverPtr<Rml::Element>> entries;
    bool available = false;
    uint64_t next = 1; // Never reset, including Shutdown/recreate. Wrap fails closed.
public:
    static constexpr size_t MaxOutstanding = 65536;
    void Begin()
    {
        owner = std::this_thread::get_id();
    }
    bool Owner() const
    {
        return owner == std::this_thread::get_id();
    }
    void DocumentLoaded()
    {
        available = true;
    }
    void Clear()
    {
        available = false;
        entries.clear();
        entries.rehash(0);
    } // Before document close/pool shutdown.
    uint64_t Acquire(Rml::ElementDocument* doc, const char* id)
    {
        if (!Owner() || !available || !doc || !id || !*id || !next || entries.size() >= MaxOutstanding)
            return 0;
        auto* el = doc->GetElementById(id);
        if (!el)
            return 0;
        const uint64_t token = next++;
        entries.emplace(token, el->GetObserverPtr());
        return token;
    }
    Rml::Element* Resolve(uint64_t token, Rml::ElementDocument* doc)
    {
        if (!Owner() || !available || !doc || !token)
            return nullptr;
        auto it = entries.find(token);
        if (it == entries.end())
            return nullptr;
        auto* el = it->second.get();
        if (!el)
        {
            entries.erase(it);
            return nullptr;
        }
        // Reject live but detached objects too; do not assume owner-document means attached.
        auto* root = el;
        while (root && root != doc)
            root = root->GetParentNode();
        return root == doc ? el : nullptr;
    }
    int Release(uint64_t token)
    {
        if (!Owner())
            return -3;
        return entries.erase(token) ? 1 : 0;
    }
    int Count() const
    {
        return Owner() ? static_cast<int>(entries.size()) : -3;
    }
};
