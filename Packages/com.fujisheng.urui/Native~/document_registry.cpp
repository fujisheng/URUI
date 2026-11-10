#include "document_registry.h"
#include "resource_store.h"
#include <limits>

namespace URUI::Bridge
{
// 单活动 Surface 的所属线程分配；表面销毁后也不重用旧身份。
static uint64_t nextDocumentHandle = 1;

DocumentRegistry::DocumentRegistry(URUI::Context& value, ElementReferences& table, RetireDocument retire)
    : context(value), references(table), owner(std::this_thread::get_id()), retire_document(retire)
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
        if (Close(states.begin()->first) != 1) break;
    }
}

int DocumentRegistry::Create(uint64_t* result)
{
    if (!Owner()) return -3;
    if (!result) return -1;
    *result = 0;
    if (states.size() >= ElementReferences::MaxOutstanding ||
        !nextDocumentHandle || nextDocumentHandle == std::numeric_limits<uint64_t>::max()) return -4;
    try
    {
        const auto handle = nextDocumentHandle++;
        auto state = std::make_unique<DocumentState>();
        state->handle = handle;
        state->resource_group = handle;
        states.emplace(handle, std::move(state));
        *result = handle;
        return 1;
    }
    catch (...) { return -8; }
}

DocumentState* DocumentRegistry::Find(uint64_t handle)
{
    if (!Owner() || !handle) return nullptr;
    const auto it = states.find(handle);
    return it == states.end() ? nullptr : it->second.get();
}

uint64_t DocumentRegistry::OwnerOf(uint64_t elementToken) const
{
    return HandleOf(references.OwnerDocument(elementToken));
}

uint64_t DocumentRegistry::HandleOf(URUI::ElementDocument* document) const
{
    if (!Owner()) return 0;
    const auto it = handles.find(document);
    return it == handles.end() ? 0 : it->second;
}

int DocumentRegistry::Publish(uint64_t handle, URUI::ElementDocument* document)
{
    if (!Owner()) return -3;
    auto* state = Find(handle);
    if (!state) return -2;
    if (state->retiring) return -5;
    if (!document || document->GetContext() != &context || handles.count(document)) return -1;
    try
    {
        // 完成可能分配的身份记录后，再使旧树失效。
        context.ReserveDocumentRetirement();
        auto reference = document->GetObserverPtr();
        handles.emplace(document, handle);
        if (auto* previous = state->Element())
        {
            state->retiring = true;
            if (retire_document) retire_document(previous);
            previous->Close();
            references.ClearDocument(previous);
            handles.erase(previous);
        }
        state->root = reference;
        state->retiring = false;
        references.DocumentLoaded();
        return 1;
    }
    catch (...)
    {
        state->retiring = false;
        handles.erase(document);
        return -8;
    }
}

int DocumentRegistry::Close(uint64_t handle)
{
    if (!Owner()) return -3;
    const auto it = states.find(handle);
    if (it == states.end()) return -2;
    if (it->second->retiring || it->second->loading) return -5;
    if (it->second->Element()) context.ReserveDocumentRetirement();
    it->second->retiring = true;
    if (it->second->fragment) { it->second->fragment->Commit(); it->second->fragment.reset(); }
    // Cancellation callbacks still need the published owner and live tokens.
    if (auto* document = it->second->Element(); document && retire_document) retire_document(document);
    if (it->second->resources) it->second->resources->Retire();
    if (it->second->pending_resources) it->second->pending_resources->Retire();
    auto state = std::move(it->second);
    states.erase(it);
    if (auto* document = state->Element())
    {
        references.ClearDocument(document);
        handles.erase(document);
        try { document->Close(); }
        catch (...) { return -8; }
    }
    return 1;
}
}
