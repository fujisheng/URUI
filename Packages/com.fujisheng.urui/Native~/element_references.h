#pragma once
#include <URUI/Core/ObserverPtr.h>
#include <URUI/Core/Element.h>
#include <URUI/Core/ElementDocument.h>
#include <URUI/Core/Plugin.h>
#include <atomic>
#include <thread>
#include <unordered_map>
#include <cstdint>
#include <vector>
#include <stdexcept>

// The document owns attached nodes; this registry owns detached roots. Tokens
// identify objects, never IDs or recycled addresses, and are context-thread only.
class ElementReferences : public URUI::Plugin
{
    std::atomic<uint64_t> invalidationRevision{1};
    void InvalidateReferences()
    {
        auto value = invalidationRevision.load(std::memory_order_relaxed);
        while (!invalidationRevision.compare_exchange_weak(value, value == UINT64_MAX ? 1 : value + 1,
                                                           std::memory_order_relaxed))
        {
        }
    }
    std::thread::id owner{};
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
    std::unordered_map<uint64_t, Entry> entries;
    std::unordered_map<URUI::Element*, uint64_t> reverse;
    struct DetachedRoot
    {
        URUI::ElementPtr element;
        URUI::ElementDocument* document;
    };
    std::unordered_map<URUI::Element*, DetachedRoot> detached;
    bool available = false;
    uint64_t next = 1;

public:
    int GetEventClasses() override
    {
        return EVT_BASIC | EVT_ELEMENT;
    }
    // 原生内部控件也能销毁已包装节点，不能只观察托管层的 Destroy 调用。
    void OnElementDestroy(URUI::Element*) override
    {
        InvalidateReferences();
    }
    uint64_t Revision() const
    {
        return invalidationRevision.load(std::memory_order_relaxed);
    }
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
        InvalidateReferences();
        available = false;
        entries.clear();
        reverse.clear();
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
            return -3;
        }
        if (!document)
        {
            return -1;
        }
        InvalidateReferences();
        for (auto it = entries.begin(); it != entries.end();)
        {
            if (it->second.document != document)
            {
                ++it;
                continue;
            }
            if (auto* pointer = it->second.get())
            {
                reverse.erase(pointer);
            }
            it = entries.erase(it);
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
        return 1;
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
            return -3;
        }
        auto found = entries.find(token);
        if (found == entries.end() || !found->second)
        {
            return -2;
        }
        if (!version || (mask & ~0x000ffffeu))
        {
            return -1;
        }
        auto& entry = found->second;
        if (version <= entry.interestVersion)
        {
            return -2;
        }
        entry.eventInterest = mask;
        entry.interestVersion = version;
        return 1;
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
            return -1;
        }
        auto* parent = el->GetParentNode();
        if (!parent)
        {
            return 1;
        }
        if (detached.size() >= MaxOutstanding)
        {
            return -2;
        }
        // Allocate the owner slot before removing the old parent ownership.
        auto added = detached.emplace(el, DetachedRoot{nullptr, doc});
        if (!added.second)
        {
            return -2;
        }
        added.first->second.element = parent->RemoveChild(el);
        if (!added.first->second.element)
        {
            detached.erase(added.first);
            return -2;
        }
        return 1;
    }
    int Insert(URUI::Element* parent, URUI::Element* child, int index, URUI::ElementDocument* doc)
    {
        if (!Member(parent, doc) || !Member(child, doc) || child == doc || parent->GetTagName() == "#text")
        {
            return -1;
        }
        for (auto* p = parent; p; p = p->GetParentNode())
        {
            if (p == child)
            {
                return -1;
            }
        }
        const int count = parent->GetNumChildren();
        if (index < -1 || index > count)
        {
            return -1;
        }
        auto* oldParent = child->GetParentNode();
        const int finalCount = count - (oldParent == parent ? 1 : 0);
        const int target = index < 0 || index > finalCount ? finalCount : index;
        if (oldParent == parent && parent->GetChild(target) == child)
        {
            return 1;
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
                return -1;
            }
            owned = std::move(it->second.element);
            detached.erase(it);
        }
        if (!owned)
        {
            return -2;
        }
        // Determine the adjacent DOM node after removal, including same-parent moves.
        auto* adjacent = target < finalCount ? parent->GetChild(target) : nullptr;
        return (adjacent ? parent->InsertBefore(std::move(owned), adjacent) : parent->AppendChild(std::move(owned)))
                   ? 1
                   : -2;
    }
    int ClearChildren(URUI::Element* parent, URUI::ElementDocument* doc)
    {
        if (!Member(parent, doc))
        {
            return -1;
        }
        const int count = parent->GetNumChildren();
        if (detached.size() + size_t(count) > MaxOutstanding)
        {
            return -2;
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
        while (parent->GetNumChildren())
        {
            auto* child = parent->GetChild(0);
            detached.at(child).element = parent->RemoveChild(child);
            if (!detached.at(child).element)
            {
                throw std::runtime_error("DOM child removal failed");
            }
        }
        return 1;
    }
    int Destroy(URUI::Element* el, URUI::ElementDocument* doc)
    {
        if (!Member(el, doc) || el == doc)
        {
            return -1;
        }
        if (auto* parent = el->GetParentNode())
        {
            auto owned = parent->RemoveChild(el);
            if (!owned)
            {
                return -2;
            }
        }
        else
        {
            detached.erase(el);
        }
        Prune();
        return 1;
    }
    int Release(uint64_t token)
    {
        if (!Owner())
        {
            return -3;
        }
        auto it = entries.find(token);
        if (it == entries.end())
        {
            return 0;
        }
        if (auto* pointer = it->second.get())
        {
            reverse.erase(pointer);
        }
        InvalidateReferences();
        entries.erase(it);
        return 1;
    }
    int Count() const
    {
        return Owner() ? static_cast<int>(entries.size()) : -3;
    }
    int Count(URUI::ElementDocument* document) const
    {
        if (!Owner())
        {
            return -3;
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
