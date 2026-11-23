#include "document_registry.h"
#include "resource_store.h"
#include <atomic>
#include <limits>

namespace URUI::Bridge
{
    // 进程级单调文档句柄；原子递增以支持多 Surface 并发创建，表面销毁后也不重用旧身份。
    static std::atomic<uint64_t> nextDocumentHandle{1};

    DocumentRegistry::DocumentRegistry(URUI::Context& value, ElementReferences& table, RetireDocument retire,
                                       void* user)
        : context(value), references(table), owner(std::this_thread::get_id()), retireDocument(retire), retireUser(user)
    {
    }

    DocumentRegistry::~DocumentRegistry()
    {
        Clear();
    }

    void DocumentRegistry::Clear()
    {
        while (!states.empty())
        {
            if (Close(states.begin()->first) != URUI_OK)
            {
                break;
            }
        }
    }

    int DocumentRegistry::Create(uint64_t* result)
    {
        if (!Owner())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        if (!result)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *result = 0;
        if (states.size() >= ElementReferences::MaxOutstanding)
        {
            return URUI_ERR_BUDGET;
        }
        uint64_t handle = nextDocumentHandle.load(std::memory_order_relaxed);
        do
        {
            if (!handle || handle == std::numeric_limits<uint64_t>::max())
            {
                return URUI_ERR_BUDGET;
            }
        } while (!nextDocumentHandle.compare_exchange_weak(handle, handle + 1, std::memory_order_relaxed));
        try
        {
            auto state = std::make_unique<DocumentState>();
            state->handle = handle;
            state->resourceGroup = handle;
            states.emplace(handle, std::move(state));
            *result = handle;
            return URUI_OK;
        }
        catch (...)
        {
            return URUI_ERR_INTERNAL;
        }
    }

    DocumentState* DocumentRegistry::Find(uint64_t handle)
    {
        if (!Owner() || !handle)
        {
            return nullptr;
        }
        const auto it = states.find(handle);
        return it == states.end() ? nullptr : it->second.get();
    }

    uint64_t DocumentRegistry::OwnerOf(uint64_t elementToken) const
    {
        return HandleOf(references.OwnerDocument(elementToken));
    }

    uint64_t DocumentRegistry::HandleOf(URUI::ElementDocument* document) const
    {
        if (!Owner())
        {
            return 0;
        }
        const auto it = handles.find(document);
        return it == handles.end() ? 0 : it->second;
    }

    int DocumentRegistry::Publish(uint64_t handle, URUI::ElementDocument* document)
    {
        if (!Owner())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        auto* state = Find(handle);
        if (!state)
        {
            return URUI_ERR_NOT_READY;
        }
        if (state->retiring)
        {
            return URUI_ERR_REENTRANCY;
        }
        if (!document || document->GetContext() != &context || handles.count(document))
        {
            return URUI_ERR_INVALID_ARG;
        }
        try
        {
            // 完成可能分配的身份记录后，再使旧树失效。
            context.ReserveDocumentRetirement();
            auto reference = document->GetObserverPtr();
            handles.emplace(document, handle);
            state->templates.Clear();
            state->clicks.clear();
            if (auto* previous = state->Element())
            {
                state->retiring = true;
                if (retireDocument)
                {
                    retireDocument(retireUser, handle, previous);
                }
                previous->Close();
                references.ClearDocument(previous);
                handles.erase(previous);
            }
            state->root = reference;
            state->retiring = false;
            references.DocumentLoaded();
            return URUI_OK;
        }
        catch (...)
        {
            state->retiring = false;
            handles.erase(document);
            return URUI_ERR_INTERNAL;
        }
    }

    int DocumentRegistry::Close(uint64_t handle)
    {
        if (!Owner())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        const auto it = states.find(handle);
        if (it == states.end())
        {
            return URUI_ERR_NOT_READY;
        }
        if (it->second->retiring || it->second->loading)
        {
            return URUI_ERR_REENTRANCY;
        }
        if (it->second->Element())
        {
            context.ReserveDocumentRetirement();
        }
        it->second->retiring = true;
        if (it->second->fragment)
        {
            it->second->fragment->Commit();
            it->second->fragment.reset();
        }
        it->second->clicks.clear();
        // Cancellation callbacks still need the published owner and live tokens.
        if (retireDocument)
        {
            retireDocument(retireUser, handle, it->second->Element());
        }
        if (it->second->resources)
        {
            it->second->resources->Retire();
        }
        if (it->second->pendingResources)
        {
            it->second->pendingResources->Retire();
        }
        auto state = std::move(it->second);
        states.erase(it);
        if (auto* document = state->Element())
        {
            references.ClearDocument(document);
            handles.erase(document);
            try
            {
                document->Close();
            }
            catch (...)
            {
                return URUI_ERR_INTERNAL;
            }
        }
        return URUI_OK;
    }
}
