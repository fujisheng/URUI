#pragma once
#include "urui.h"
#include <URUI/Core/ObserverPtr.h>
#include <URUI/Core/Element.h>
#include <URUI/Core/ElementDocument.h>
#include <atomic>
#include <cstdint>
#include <stdexcept>
#include <thread>
#include <unordered_map>
#include <vector>

// The document owns attached nodes; this registry owns detached roots. Tokens
// identify objects, never IDs or recycled addresses, and are surface-thread only.
class ElementReferences
{
    std::atomic<uint64_t> invalidationRevision{1};
    void InvalidateReferences() noexcept
    {
        auto value = invalidationRevision.load(std::memory_order_relaxed);
        while (!invalidationRevision.compare_exchange_weak(value, value == UINT64_MAX ? 1 : value + 1,
                                                           std::memory_order_relaxed))
        {
        }
    }
    std::atomic<std::thread::id> owner{};
    struct Entry
    {
        URUI::ObserverPtr<URUI::Element> observer;
        URUI::ElementDocument* document;
        uint32_t eventInterest = 0x000ffffeu;
        uint64_t interestVersion = 0;
        URUI::Element* get() const
        {
            return observer.get();
        }
        explicit operator bool() const
        {
            return bool(observer);
        }
    };
    struct DetachedRoot
    {
        URUI::ElementPtr element;
        URUI::ElementDocument* document;
    };
    struct InvalidatedQueue
    {
        std::vector<uint64_t> tokens;
        bool overflow = false;
    };
    std::unordered_map<uint64_t, Entry> entries;
    std::unordered_map<URUI::Element*, uint64_t> reverse;
    std::unordered_map<URUI::Element*, DetachedRoot> detached;
    std::unordered_map<URUI::ElementDocument*, InvalidatedQueue> invalidatedByDoc;
    bool available = false;
    uint64_t next = 1;

public:
    using TeardownHook = void (*)(void* user, URUI::ElementDocument* doc, uint64_t token) noexcept;

private:
    TeardownHook teardownHook = nullptr;
    void* teardownUser = nullptr;

    void RecordInvalidated(URUI::ElementDocument* doc, uint64_t token) noexcept
    {
        if (!doc || !token)
        {
            return;
        }
        try
        {
            auto& queue = invalidatedByDoc[doc];
            if (queue.tokens.size() < MaxInvalidatedPerDocument)
            {
                queue.tokens.push_back(token);
            }
            else
            {
                queue.overflow = true;
            }
        }
        catch (...)
        {
            auto it = invalidatedByDoc.find(doc);
            if (it != invalidatedByDoc.end())
            {
                it->second.overflow = true;
            }
        }
    }

public:
    static constexpr size_t MaxOutstanding = 65536;
    static constexpr size_t MaxInvalidatedPerDocument = 4096;

    void SetTeardownHook(TeardownHook hook, void* user) noexcept
    {
        teardownHook = hook;
        teardownUser = user;
    }

    // 原生内部控件也能销毁已包装节点，O(1) 回收句柄并记录每文档失效队列。
    void OnElementDestroy(URUI::Element* element) noexcept
    {
        InvalidateReferences();
        if (!element)
        {
            return;
        }
        const auto rev = reverse.find(element);
        if (rev != reverse.end())
        {
            const uint64_t token = rev->second;
            reverse.erase(rev);
            URUI::ElementDocument* doc = nullptr;
            const auto entry = entries.find(token);
            if (entry != entries.end())
            {
                doc = entry->second.document;
                entries.erase(entry);
            }
            RecordInvalidated(doc, token);
            if (teardownHook)
            {
                teardownHook(teardownUser, doc, token);
            }
        }
    }

    uint64_t Revision() const noexcept
    {
        return invalidationRevision.load(std::memory_order_relaxed);
    }

    void Begin() noexcept
    {
        owner.store(std::this_thread::get_id(), std::memory_order_release);
    }

    bool Owner() const noexcept
    {
        return owner.load(std::memory_order_acquire) == std::this_thread::get_id();
    }

    void DocumentLoaded() noexcept
    {
        available = true;
    }

    void Clear()
    {
        InvalidateReferences();
        available = false;
        entries.clear();
        reverse.clear();
        invalidatedByDoc.clear();
        // Destroy ownerless trees while the URUI context and pools still exist.
        detached.clear();
    }

    URUI::ElementDocument* OwnerDocument(uint64_t token) const
    {
        if (!Owner() || !available)
        {
            return nullptr;
        }
        const auto it = entries.find(token);
        return it != entries.end() && it->second ? it->second.document : nullptr;
    }

    int ClearDocument(URUI::ElementDocument* document)
    {
        if (!Owner())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        if (!document)
        {
            return URUI_ERR_INVALID_ARG;
        }
        InvalidateReferences();
        invalidatedByDoc.erase(document);
        for (auto it = entries.begin(); it != entries.end();)
        {
            if (it->second.document != document)
            {
                ++it;
                continue;
            }
            const uint64_t token = it->first;
            if (auto* pointer = it->second.get())
            {
                reverse.erase(pointer);
            }
            it = entries.erase(it);
            if (teardownHook)
            {
                teardownHook(teardownUser, document, token);
            }
        }
        for (auto it = detached.begin(); it != detached.end();)
        {
            if (it->second.document == document)
            {
                it = detached.erase(it);
            }
            else
            {
                ++it;
            }
        }
        return URUI_OK;
    }

    int DrainInvalidated(URUI::ElementDocument* document, uint64_t* out, int capacity, int* out_total,
                         int* out_overflow)
    {
        if (!Owner())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        if (!document || capacity < 0 || (!out && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto it = invalidatedByDoc.find(document);
        if (it == invalidatedByDoc.end())
        {
            if (out_total)
            {
                *out_total = 0;
            }
            if (out_overflow)
            {
                *out_overflow = 0;
            }
            return 0;
        }
        auto& queue = it->second;
        const int total = static_cast<int>(queue.tokens.size());
        const int overflow = queue.overflow ? 1 : 0;
        if (out_total)
        {
            *out_total = total;
        }
        if (out_overflow)
        {
            *out_overflow = overflow;
        }
        if (!out)
        {
            return 0;
        }
        const int copied = capacity < total ? capacity : total;
        for (int i = 0; i < copied; ++i)
        {
            out[i] = queue.tokens[size_t(i)];
        }
        if (copied == total)
        {
            queue.tokens.clear();
            queue.overflow = false;
        }
        else if (copied > 0)
        {
            queue.tokens.erase(queue.tokens.begin(), queue.tokens.begin() + copied);
        }
        return copied;
    }

    bool Member(URUI::Element* el, URUI::ElementDocument* doc) const
    {
        if (!Owner() || !available || !doc || !el)
        {
            return false;
        }
        auto* root = el;
        while (root != doc && root->GetParentNode())
        {
            root = root->GetParentNode();
        }
        if (root == doc)
        {
            return true;
        }
        const auto it = detached.find(root);
        return it != detached.end() && it->second.document == doc && it->second.element;
    }

    bool Attached(URUI::Element* el, URUI::ElementDocument* doc) const
    {
        if (!Member(el, doc))
        {
            return false;
        }
        while (el && el != doc)
        {
            el = el->GetParentNode();
        }
        return el == doc;
    }

    void Prune()
    {
        for (auto it = entries.begin(); it != entries.end();)
        {
            if (!it->second)
            {
                it = entries.erase(it);
            }
            else
            {
                ++it;
            }
        }
        for (auto it = reverse.begin(); it != reverse.end();)
        {
            auto token = entries.find(it->second);
            if (token == entries.end() || token->second.get() != it->first)
            {
                it = reverse.erase(it);
            }
            else
            {
                ++it;
            }
        }
    }

    int SetEventInterest(uint64_t token, uint32_t mask, uint64_t version)
    {
        if (!Owner())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        auto found = entries.find(token);
        if (found == entries.end() || !found->second)
        {
            return URUI_ERR_NOT_READY;
        }
        if (!version || (mask & ~0x000ffffeu))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto& entry = found->second;
        if (version <= entry.interestVersion)
        {
            return URUI_ERR_NOT_READY;
        }
        entry.eventInterest = mask;
        entry.interestVersion = version;
        return URUI_OK;
    }

    bool EventInterest(URUI::Element* element, int type) const
    {
        const auto token = reverse.find(element);
        if (token == reverse.end())
        {
            return true;
        }
        const auto entry = entries.find(token->second);
        // Unknown/unwrapped native nodes conservatively retain publication.
        if (entry == entries.end() || entry->second.get() != element)
        {
            return true;
        }
        return (entry->second.eventInterest & (uint32_t(1) << type)) != 0;
    }

    uint64_t Track(URUI::Element* el, URUI::ElementDocument* doc)
    {
        if (!Member(el, doc))
        {
            return 0;
        }
        auto old = reverse.find(el);
        if (old != reverse.end())
        {
            auto token = entries.find(old->second);
            if (token != entries.end() && token->second.get() == el)
            {
                return token->first;
            }
            // An expired ObserverPtr must not revive when an allocator reuses its address.
            reverse.erase(old);
        }
        if (entries.size() >= MaxOutstanding)
        {
            Prune();
        }
        if (!next || next == UINT64_MAX || entries.size() >= MaxOutstanding)
        {
            return 0;
        }
        const uint64_t token = next++;
        entries.emplace(token, Entry{el->GetObserverPtr(), doc});
        try
        {
            reverse.emplace(el, token);
        }
        catch (...)
        {
            entries.erase(token);
            throw;
        }
        return token;
    }

    uint64_t Acquire(URUI::ElementDocument* doc, const char* id)
    {
        return Owner() && available && doc && id && *id ? Track(doc->GetElementById(id), doc) : 0;
    }

    URUI::Element* Resolve(uint64_t token, URUI::ElementDocument* doc)
    {
        if (!Owner() || !available || !doc || !token)
        {
            return nullptr;
        }
        auto it = entries.find(token);
        if (it == entries.end())
        {
            return nullptr;
        }
        auto* el = it->second.get();
        return Member(el, doc) ? el : nullptr;
    }

    // Resolve identity and attachment with one registry lookup and ancestor walk.
    // Detached roots still require ownership; an alive orphan is not a valid token.
    int ResolveState(uint64_t token, URUI::ElementDocument* doc, URUI::Element*& element)
    {
        element = nullptr;
        if (!Owner() || !available || !doc || !token)
        {
            return 0;
        }
        const auto found = entries.find(token);
        if (found == entries.end())
        {
            return 0;
        }
        auto* value = found->second.get();
        if (!value)
        {
            return 0;
        }
        auto* root = value;
        while (root != doc && root->GetParentNode())
        {
            root = root->GetParentNode();
        }
        if (root == doc)
        {
            element = value;
            return 3;
        }
        const auto detachedRoot = detached.find(root);
        if (detachedRoot == detached.end() || detachedRoot->second.document != doc || !detachedRoot->second.element)
        {
            return 0;
        }
        element = value;
        return 1;
    }

    uint64_t Create(URUI::ElementDocument* doc, const char* tag)
    {
        if (!Owner() || !available || !doc || !tag || !*tag || detached.size() >= MaxOutstanding)
        {
            return 0;
        }
        auto element = doc->CreateElement(tag);
        if (!element)
        {
            return 0;
        }
        auto* pointer = element.get();
        detached.emplace(pointer, DetachedRoot{std::move(element), doc});
        auto token = Track(pointer, doc);
        if (!token)
        {
            detached.erase(pointer);
        }
        return token;
    }

    uint64_t Clone(URUI::Element* source, URUI::ElementDocument* doc)
    {
        if (!Member(source, doc) || source == doc || detached.size() >= MaxOutstanding)
        {
            return 0;
        }
        auto element = source->Clone();
        if (!element)
        {
            return 0;
        }
        auto* pointer = element.get();
        detached.emplace(pointer, DetachedRoot{std::move(element), doc});
        auto token = Track(pointer, doc);
        if (!token)
        {
            detached.erase(pointer);
        }
        return token;
    }

    int Detach(URUI::Element* el, URUI::ElementDocument* doc)
    {
        if (!Member(el, doc) || el == doc)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* parent = el->GetParentNode();
        if (!parent)
        {
            return URUI_OK;
        }
        if (detached.size() >= MaxOutstanding)
        {
            return URUI_ERR_BUDGET;
        }
        // Allocate the owner slot before removing the old parent ownership.
        auto added = detached.emplace(el, DetachedRoot{nullptr, doc});
        if (!added.second)
        {
            return URUI_ERR_NOT_READY;
        }
        added.first->second.element = parent->RemoveChild(el);
        if (!added.first->second.element)
        {
            detached.erase(added.first);
            return URUI_ERR_NOT_READY;
        }
        return URUI_OK;
    }

    int Insert(URUI::Element* parent, URUI::Element* child, int index, URUI::ElementDocument* doc)
    {
        if (!Member(parent, doc) || !Member(child, doc) || child == doc || parent->GetTagName() == "#text")
        {
            return URUI_ERR_INVALID_ARG;
        }
        for (auto* p = parent; p; p = p->GetParentNode())
        {
            if (p == child)
            {
                return URUI_ERR_INVALID_ARG;
            }
        }
        const int count = parent->GetNumChildren();
        if (index < -1 || index > count)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* oldParent = child->GetParentNode();
        const int finalCount = count - (oldParent == parent ? 1 : 0);
        const int target = index < 0 || index > finalCount ? finalCount : index;
        if (oldParent == parent && parent->GetChild(target) == child)
        {
            return URUI_OK;
        }
        URUI::ElementPtr owned;
        if (oldParent)
        {
            owned = oldParent->RemoveChild(child);
        }
        else
        {
            auto it = detached.find(child);
            if (it == detached.end())
            {
                return URUI_ERR_INVALID_ARG;
            }
            owned = std::move(it->second.element);
            detached.erase(it);
        }
        if (!owned)
        {
            return URUI_ERR_NOT_READY;
        }
        // Determine the adjacent DOM node after removal, including same-parent moves.
        auto* adjacent = target < finalCount ? parent->GetChild(target) : nullptr;
        return (adjacent ? parent->InsertBefore(std::move(owned), adjacent) : parent->AppendChild(std::move(owned)))
                   ? URUI_OK
                   : URUI_ERR_NOT_READY;
    }

    int InsertBefore(URUI::Element* parent, URUI::Element* child, URUI::Element* ref_child, URUI::ElementDocument* doc)
    {
        if (!Member(parent, doc) || !Member(child, doc) || child == doc || parent->GetTagName() == "#text")
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (ref_child && (!Member(ref_child, doc) || ref_child->GetParentNode() != parent))
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (ref_child == child)
        {
            return URUI_OK;
        }
        for (auto* p = parent; p; p = p->GetParentNode())
        {
            if (p == child)
            {
                return URUI_ERR_INVALID_ARG;
            }
        }
        auto* oldParent = child->GetParentNode();
        if (oldParent == parent)
        {
            if (ref_child)
            {
                if (child->GetNextSibling() == ref_child)
                {
                    return URUI_OK;
                }
            }
            else if (parent->GetLastChild() == child)
            {
                return URUI_OK;
            }
        }
        URUI::ElementPtr owned;
        if (oldParent)
        {
            owned = oldParent->RemoveChild(child);
        }
        else
        {
            auto it = detached.find(child);
            if (it == detached.end())
            {
                return URUI_ERR_INVALID_ARG;
            }
            owned = std::move(it->second.element);
            detached.erase(it);
        }
        if (!owned)
        {
            return URUI_ERR_NOT_READY;
        }
        return (ref_child ? parent->InsertBefore(std::move(owned), ref_child) : parent->AppendChild(std::move(owned)))
                   ? URUI_OK
                   : URUI_ERR_NOT_READY;
    }

    int ClearChildren(URUI::Element* parent, URUI::ElementDocument* doc)
    {
        if (!Member(parent, doc))
        {
            return URUI_ERR_INVALID_ARG;
        }
        const int count = parent->GetNumChildren();
        if (detached.size() + size_t(count) > MaxOutstanding)
        {
            return URUI_ERR_BUDGET;
        }
        // Reserve all registry nodes before any detach; ordinary failure is atomic.
        detached.reserve(detached.size() + size_t(count));
        std::vector<URUI::Element*> added;
        added.reserve(size_t(count));
        try
        {
            for (int i = 0; i < count; ++i)
            {
                auto* child = parent->GetChild(i);
                if (detached.emplace(child, DetachedRoot{nullptr, doc}).second)
                {
                    added.push_back(child);
                }
            }
        }
        catch (...)
        {
            for (auto* child : added)
            {
                detached.erase(child);
            }
            throw;
        }
        // Remove in reverse order to avoid O(N^2) vector shifts inside Element::RemoveChild.
        for (int i = parent->GetNumChildren() - 1; i >= 0; --i)
        {
            auto* child = parent->GetChild(i);
            detached.at(child).element = parent->RemoveChild(child);
            if (!detached.at(child).element)
            {
                throw std::runtime_error("DOM child removal failed");
            }
        }
        return URUI_OK;
    }

    int Destroy(URUI::Element* el, URUI::ElementDocument* doc)
    {
        if (!Member(el, doc) || el == doc)
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (auto* parent = el->GetParentNode())
        {
            auto owned = parent->RemoveChild(el);
            if (!owned)
            {
                return URUI_ERR_NOT_READY;
            }
        }
        else
        {
            detached.erase(el);
        }
        // OnElementDestroy cleans reverse/entries in O(1) as the subtree destructs.
        return URUI_OK;
    }

    int Release(uint64_t token)
    {
        if (!Owner())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        auto it = entries.find(token);
        if (it == entries.end())
        {
            return URUI_ABSENT;
        }
        URUI::ElementDocument* doc = it->second.document;
        if (auto* pointer = it->second.get())
        {
            reverse.erase(pointer);
        }
        InvalidateReferences();
        entries.erase(it);
        RecordInvalidated(doc, token);
        if (teardownHook)
        {
            teardownHook(teardownUser, doc, token);
        }
        return URUI_OK;
    }

    int Count() const
    {
        return Owner() ? static_cast<int>(entries.size()) : URUI_ERR_WRONG_THREAD;
    }

    int Count(URUI::ElementDocument* document) const
    {
        if (!Owner())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        int count = 0;
        for (const auto& entry : entries)
        {
            if (entry.second.document == document)
            {
                ++count;
            }
        }
        return count;
    }
};
