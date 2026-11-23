#include "bridge_runtime.h"
#include "Engine/Source/Core/CompiledDocument.h"
#include "Engine/Source/Core/HtmlBuilder.h"
#include "Engine/Source/Core/HtmlParser.h"
#include <URUI/Core/StreamMemory.h>
#include <URUI/Core/StyleSheetContainer.h>
#include <algorithm>
#include <cmath>
#include <cstring>

namespace URUI::Bridge
{
    namespace
    {
        void ApplyPresentation(SurfaceContext& surface, DocumentState& state)
        {
            if (auto* root = state.Element())
            {
                root->SetStackOrder(state.layer, state.order);
                if (state.visible && (!root->IsPresented() || root->IsModal() != state.modal))
                {
                    root->Show(state.modal ? URUI::ModalFlag::Modal : URUI::ModalFlag::None,
                               state.modal ? URUI::FocusFlag::Auto : URUI::FocusFlag::None);
                }
                else if (!state.visible && root->IsPresented())
                {
                    CancelDocumentInput(surface, root);
                    root->Hide();
                }
                CancelBlockedDocumentInput(surface);
            }
        }

        int InvalidHtml(SurfaceContext& surface, const URUI::Vector<URUI::Diagnostic>& diagnostics)
        {
            if (!diagnostics.empty())
            {
                const auto& error = diagnostics.front();
                surface.SetError(error.sourceUrl + ":" + std::to_string(error.span.line) + ":" +
                                 std::to_string(error.span.column) + " [" + error.code + "] " + error.message);
                if (error.code == "HTML_MEMORY")
                {
                    return URUI_ERR_INTERNAL;
                }
            }
            return URUI_ERR_VALIDATION;
        }

        struct LoadScope
        {
            DocumentState& state;
            bool published = false;
            explicit LoadScope(DocumentState& s) : state(s)
            {
                state.loading = true;
            }
            ~LoadScope()
            {
                state.loading = false;
                if (!published)
                {
                    DiscardPendingResources(state);
                }
            }
        };

        int LoadDocumentImpl(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const char* html,
                             const char* source_url, const uint8_t* bytes, int length)
        try
        {
            SurfaceContext* surface = nullptr;
            int status = LookupSurface(surfaceHandle, surface, true, true);
            if (status != URUI_OK)
            {
                return status;
            }
            DocumentState* state = nullptr;
            status = LookupDocument(*surface, owner, state, false, true);
            if (status != URUI_OK)
            {
                return status;
            }
            if (state->fragment)
            {
                return URUI_ERR_REENTRANCY;
            }
            if (bytes ? length <= 0 : (!html || !source_url || !*source_url))
            {
                return URUI_ERR_INVALID_ARG;
            }
            ActiveSurfaceScope scope(surface);
            auto resources = EnsurePendingResources(*surface, *state);
            LoadScope loading(*state);
            URUI::HtmlParseResult parsed;
            URUI::SharedPtr<const URUI::CompiledDocument> compiled;
            if (bytes)
            {
                URUI::String error;
                compiled = URUI::ReadCompiledDocumentCached(bytes, size_t(length), error);
                if (!compiled)
                {
                    surface->SetError(error);
                    return URUI_ERR_VALIDATION;
                }
                if (compiled->html.kind != URUI::HtmlDocumentKind::Document)
                {
                    return URUI_ERR_INVALID_ARG;
                }
            }
            else
            {
                parsed = URUI::ParseHtmlDocument(html, source_url);
                if (!parsed)
                {
                    return InvalidHtml(*surface, parsed.diagnostics);
                }
            }
            auto events = CreateCandidateEvents(*surface, owner);
            surface->pendingDocuments.reserve(surface->pendingDocuments.size() + 1);
            URUI::Vector<URUI::Diagnostic> diagnostics;
            auto tree = bytes
                            ? URUI::Factory::InstanceCompiledDocument(surface->context, *compiled, diagnostics,
                                                                      resources)
                            : URUI::Factory::InstanceParsedHtmlDocument(surface->context, *parsed.document, diagnostics,
                                                                        resources);
            if (!tree)
            {
                if (!diagnostics.empty())
                {
                    InvalidHtml(*surface, diagnostics);
                }
                return URUI_ERR_INTERNAL;
            }
            AttachCandidateEvents(*events, *static_cast<URUI::ElementDocument*>(tree.get()));
            tree->AddEventListener("click", &surface->clickListener);
            auto* candidate = surface->context->AttachLoadedDocument(std::move(tree));
            struct CandidateScope
            {
                URUI::ElementDocument* document;
                ~CandidateScope()
                {
                    if (document)
                    {
                        try
                        {
                            document->Close();
                        }
                        catch (...)
                        {
                        }
                    }
                }
            } candidateScope{candidate};
            candidate->SetStackOrder(state->layer, state->order);
            candidate->UpdateDocument();
            if (!resources->FailedImage().empty())
            {
                surface->SetError("Image dependency failed: " + resources->FailedImage());
                return URUI_ERR_INTERNAL;
            }
            const int published = surface->documentRegistry->Publish(owner, candidate);
            if (published != URUI_OK)
            {
                return published;
            }
            state->resources = std::move(resources);
            state->pendingResources.reset();
            loading.published = true;
            candidateScope.document = nullptr;
            surface->pendingDocuments.push_back(std::move(events));
            surface->renderer.MarkDocumentsChanged();
            try
            {
                if (state->visible)
                {
                    candidate->Show(state->modal ? URUI::ModalFlag::Modal : URUI::ModalFlag::None,
                                    state->modal ? URUI::FocusFlag::Auto : URUI::FocusFlag::None,
                                    URUI::ScrollFlag::Auto, false);
                }
            }
            catch (...)
            {
                surface->SetError("URUI presentation notification failed after document commit");
            }
            CancelBlockedDocumentInput(*surface);
            return URUI_OK;
        }
        catch (...)
        {
            EngineRuntime::SetThreadError("URUI document operation failed");
            return URUI_ERR_INTERNAL;
        }
    }

    URUI::String CanonicalResourceUrl(const char* source)
    {
        URUI::SystemInterface sys;
        URUI::String result;
        sys.JoinPath(result, "", source);
        return result;
    }

    std::shared_ptr<DocumentResources> EnsurePendingResources(SurfaceContext& surface, DocumentState& state)
    {
        if (!state.pendingResources)
        {
            if (!surface.nextResourceScope || surface.nextResourceScope == UINT64_MAX)
            {
                throw std::length_error("Resource scope identities exhausted");
            }
            const auto identity = surface.nextResourceScope++;
            if (state.resources)
            {
                state.pendingResources = state.resources->Clone(identity, state.handle);
            }
            else
            {
                state.pendingResources = std::make_shared<DocumentResources>(
                    identity, state.handle, state.resourceGroup, *surface.fileResourceCache,
                    surface.resourceFiles ? surface.resourceFiles->Original() : nullptr);
            }
        }
        URUI::Context* ctx = surface.context;
        state.pendingResources->SetTextureRetirement(
            [ctx](uint64_t identity)
            {
                if (ctx)
                {
                    ctx->GetRenderManager().RetireResourceContext(identity);
                }
            });
        return state.pendingResources;
    }

    void DiscardPendingResources(DocumentState& state) noexcept
    {
        if (state.pendingResources)
        {
            state.pendingResources->Retire();
        }
        state.pendingResources.reset();
    }
}

using namespace URUI::Bridge;

extern "C"
{
    int urui_document_resource_api_version(void) { return 4; }

    int urui_resolve_resource_url(const char* base, const char* source, char* output, int capacity, int* required)
    try
    {
        if (!base || !source || !required || capacity < 0 || (!output && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        URUI::SystemInterface system_interface;
        URUI::String canonical;
        system_interface.JoinPath(canonical, base, source);
        return EngineRuntime::CopyString(canonical, output, capacity, required);
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_resolve_resource_url failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_set_replacement(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner,
                                      URUIDocumentHandle previous)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* candidate = nullptr;
        DocumentState* active = nullptr;
        status = LookupDocument(*surface, owner, candidate, false, true);
        if (status != URUI_OK)
        {
            return status;
        }
        status = LookupDocument(*surface, previous, active, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (owner == previous || candidate->Element() || candidate->resources || candidate->pendingResources)
        {
            return URUI_ERR_INVALID_ARG;
        }
        candidate->resourceGroup = active->resourceGroup;
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("URUI document operation failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_register_resource(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const char* source,
                                        const uint8_t* bytes, int length)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, false, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!source || !*source || length < 0 || (!bytes && length))
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        EnsurePendingResources(*surface, *state)->Register(CanonicalResourceUrl(source), bytes, size_t(length));
        return URUI_OK;
    }
    catch (const std::invalid_argument& error)
    {
        if (auto* s = FindSurfaceInternal(surfaceHandle))
        {
            s->SetError(error.what());
        }
        EngineRuntime::SetThreadError(error.what());
        return URUI_ERR_INVALID_ARG;
    }
    catch (const std::length_error& error)
    {
        if (auto* s = FindSurfaceInternal(surfaceHandle))
        {
            s->SetError(error.what());
        }
        EngineRuntime::SetThreadError(error.what());
        return URUI_ERR_BUDGET;
    }
    catch (...)
    {
        if (auto* s = FindSurfaceInternal(surfaceHandle))
        {
            s->SetError("URUI document operation failed");
        }
        EngineRuntime::SetThreadError("URUI document operation failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_register_rgba_texture(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner,
                                            const char* source, int width, int height, const uint8_t* pixels,
                                            int length, uint64_t* handle)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, false, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!source || !*source || !pixels || !handle || width <= 0 || height <= 0 || width > 8192 || height > 8192 ||
            int64_t(width) * height * 4 != length)
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        const auto id = surface->renderer.RegisterScopedImage(
            EnsurePendingResources(*surface, *state), CanonicalResourceUrl(source), width, height, pixels,
            size_t(length));
        *handle = id;
        return URUI_OK;
    }
    catch (const std::length_error& error)
    {
        if (auto* s = FindSurfaceInternal(surfaceHandle))
        {
            s->SetError(error.what());
        }
        EngineRuntime::SetThreadError(error.what());
        return URUI_ERR_BUDGET;
    }
    catch (const std::exception& error)
    {
        if (auto* s = FindSurfaceInternal(surfaceHandle))
        {
            s->SetError(error.what());
        }
        EngineRuntime::SetThreadError(error.what());
        return URUI_ERR_INVALID_ARG;
    }
    catch (...)
    {
        if (auto* s = FindSurfaceInternal(surfaceHandle))
        {
            s->SetError("URUI document operation failed");
        }
        EngineRuntime::SetThreadError("URUI document operation failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_register_external_texture(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner,
                                                const char* source, int width, int height, uint64_t resource)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, false, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!source || !*source || width <= 0 || height <= 0 || width > 8192 || height > 8192 ||
            !(resource & BorrowedTextureBit))
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        surface->renderer.RegisterScopedImage(EnsurePendingResources(*surface, *state), CanonicalResourceUrl(source),
                                              width, height, nullptr, 0, resource);
        return URUI_OK;
    }
    catch (const std::length_error& error)
    {
        if (auto* s = FindSurfaceInternal(surfaceHandle))
        {
            s->SetError(error.what());
        }
        EngineRuntime::SetThreadError(error.what());
        return URUI_ERR_BUDGET;
    }
    catch (const std::exception& error)
    {
        if (auto* s = FindSurfaceInternal(surfaceHandle))
        {
            s->SetError(error.what());
        }
        EngineRuntime::SetThreadError(error.what());
        return URUI_ERR_INVALID_ARG;
    }
    catch (...)
    {
        if (auto* s = FindSurfaceInternal(surfaceHandle))
        {
            s->SetError("URUI document operation failed");
        }
        EngineRuntime::SetThreadError("URUI document operation failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_register_sprite(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const char* name,
                                      const char* image_source, float x, float y, float width, float height,
                                      float scale)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, false, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!name || !*name || !image_source || !*image_source || !std::isfinite(x) || !std::isfinite(y) ||
            !std::isfinite(width) || !std::isfinite(height) || !std::isfinite(scale) || x < 0 || y < 0 || width <= 0 ||
            height <= 0 || scale <= 0 || !std::isfinite(width * scale) || !std::isfinite(height * scale))
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        const auto source = CanonicalResourceUrl(image_source);
        auto resources = EnsurePendingResources(*surface, *state);
        const auto texture = surface->renderer.textures.find(resources->FindImage(source));
        if (texture == surface->renderer.textures.end() || double(x) + width > texture->second.width ||
            double(y) + height > texture->second.height)
        {
            surface->SetError("Sprite region needs a registered source texture and in-bounds source pixels: " + source);
            return URUI_ERR_INVALID_ARG;
        }
        resources->RegisterSprite(name, source, URUI::Rectanglef::FromPositionSize({x, y}, {width, height}), scale);
        return URUI_OK;
    }
    catch (const std::exception& error)
    {
        EngineRuntime::SetThreadError(error.what());
        return URUI_ERR_INVALID_ARG;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("URUI document operation failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_unregister_texture(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const char* source)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, false, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!source || !*source)
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        return EnsurePendingResources(*surface, *state)->RemoveImage(CanonicalResourceUrl(source)) ? URUI_OK
                                                                                                   : URUI_ABSENT;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("URUI document operation failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_preload_image(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const char* source,
                                    int* width, int* height)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!source || !*source || !width || !height)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *width = *height = 0;
        ActiveSurfaceScope scope(surface);
        auto texture = surface->context->GetRenderManager().LoadTexture(source, state->Element()->GetSourceURL(),
                                                                        state->resources);
        const auto dims = texture.GetDimensions();
        if (dims.x <= 0 || dims.y <= 0)
        {
            return URUI_ABSENT;
        }
        *width = dims.x;
        *height = dims.y;
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("URUI document operation failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_release_image(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const char* source)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!source || !*source)
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        URUI::String canonical;
        URUI::GetSystemInterface()->JoinPath(canonical, state->Element()->GetSourceURL(), source);
        const auto released = surface->context->GetRenderManager().ReleaseTexture(canonical, state->resources);
        state->resources->RemoveImage(canonical);
        return released ? URUI_OK : URUI_ABSENT;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("URUI document operation failed");
        return URUI_ERR_INTERNAL;
    }

    URUIDocumentHandle urui_surface_element_owner(URUISurfaceHandle surfaceHandle, uint64_t token)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        return surface->documentRegistry->OwnerOf(token);
    }

    URUIDocumentHandle urui_surface_focused_document(URUISurfaceHandle surfaceHandle)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        auto* focused = surface->context->GetFocusElement();
        return focused && focused->IsVisible(true) &&
                       !surface->context->IsDocumentBlockedByModal(focused->GetOwnerDocument())
                   ? surface->documentRegistry->HandleOf(focused->GetOwnerDocument())
                   : 0;
    }

    uint64_t urui_surface_pick(URUISurfaceHandle surfaceHandle, float x, float y, URUIDocumentHandle* owner)
    try
    {
        if (owner)
        {
            *owner = 0;
        }
        SurfaceContext* surface = nullptr;
        if (!owner || LookupSurface(surfaceHandle, surface, true, false) != URUI_OK || !std::isfinite(x) ||
            !std::isfinite(y))
        {
            return 0;
        }
        ActiveSurfaceScope scope(surface);
        auto* element = surface->context->GetElementAtPoint({x, y});
        auto* document = element ? element->GetOwnerDocument() : nullptr;
        const auto handle = surface->documentRegistry->HandleOf(document);
        if (!handle)
        {
            return 0;
        }
        const auto token = surface->elementReferences.Track(element, document);
        if (token)
        {
            *owner = handle;
        }
        return token;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_surface_pick failed");
        return 0;
    }

    int urui_document_create(URUISurfaceHandle surfaceHandle, URUIDocumentHandle* result)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        return surface->documentRegistry->Create(result);
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_document_create failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_load_html(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const char* html,
                                const char* source_url)
    {
        return LoadDocumentImpl(surfaceHandle, owner, html, source_url, nullptr, 0);
    }

    int urui_document_load_compiled(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const uint8_t* bytes,
                                    int length)
    {
        if (!bytes || length <= 0)
        {
            return URUI_ERR_INVALID_ARG;
        }
        return LoadDocumentImpl(surfaceHandle, owner, nullptr, nullptr, bytes, length);
    }

    int urui_document_close(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!owner)
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        const int closed = surface->documentRegistry->Close(owner);
        if (closed == URUI_OK)
        {
            surface->renderer.MarkDocumentsChanged();
        }
        return closed;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_document_close failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_set_presentation(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, int layer, int order,
                                       int visible, int modal)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, false, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if ((visible != 0 && visible != 1) || (modal != 0 && modal != 1))
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        state->layer = layer;
        state->order = order;
        state->visible = visible != 0;
        state->modal = modal != 0;
        ApplyPresentation(*surface, *state);
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_document_set_presentation failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_document_add_stylesheet(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const char* css,
                                     const char* source_url, int before_author)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document || !css || !*css || !source_url || !*source_url || (before_author != 0 && before_author != 1))
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        auto sheet = URUI::MakeShared<URUI::StyleSheetContainer>();
        URUI::StreamMemory cssStream(reinterpret_cast<const URUI::byte*>(css), std::strlen(css));
        cssStream.SetSourceURL(source_url);
        if (!sheet->LoadStyleSheetContainer(&cssStream, 1, document))
        {
            surface->SetError(sheet->GetResourceError().empty() ? "CSS resource could not be parsed"
                                                                : sheet->GetResourceError());
            return URUI_ERR_VALIDATION;
        }
        if (const auto* current = document->GetStyleSheetContainer())
        {
            sheet = before_author ? sheet->CombineStyleSheetContainer(*current)
                                  : current->CombineStyleSheetContainer(*sheet);
        }
        if (!sheet)
        {
            return URUI_ERR_NOT_READY;
        }
        document->SetStyleSheetContainer(std::move(sheet));
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Native stylesheet resource failed");
        return URUI_ERR_INTERNAL;
    }

    uint64_t urui_document_root(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        DocumentState* state = nullptr;
        if (LookupDocument(*surface, owner, state, true, false) != URUI_OK)
        {
            return 0;
        }
        return surface->elementReferences.Track(state->Element(), state->Element());
    }
    catch (...)
    {
        return 0;
    }

    uint64_t urui_document_body(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        DocumentState* state = nullptr;
        if (LookupDocument(*surface, owner, state, true, false) != URUI_OK)
        {
            return 0;
        }
        auto* root = state->Element();
        for (int i = 0; i < root->GetNumChildren(); ++i)
        {
            if (auto* child = root->GetChild(i); child->GetTagName() == "body")
            {
                return surface->elementReferences.Track(child, root);
            }
        }
        return 0;
    }
    catch (...)
    {
        return 0;
    }

    uint64_t urui_document_layout_version(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        auto* document = ResolveDocument(*surface, owner);
        return document ? document->GetLayoutVersion() : 0;
    }

    int urui_document_drain_invalidated_elements(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner,
                                                 uint64_t* out, int capacity, int* out_total, int* out_overflow)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document)
        {
            return URUI_ERR_NOT_READY;
        }
        return surface->elementReferences.DrainInvalidated(document, out, capacity, out_total, out_overflow);
    }

    int urui_element_set_inner_html(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                    const char* html, const char* source_url)
    {
        const int status = urui_element_begin_inner_html(surfaceHandle, owner, token, html, source_url);
        return status == URUI_OK ? urui_element_end_inner_html(surfaceHandle, owner, token, 1) : status;
    }

    int urui_element_begin_inner_html(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                      const char* html, const char* source_url)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (state->fragment)
        {
            return URUI_ERR_REENTRANCY;
        }
        if (!token || !html || !source_url || !*source_url)
        {
            return URUI_ERR_INVALID_ARG;
        }
        const auto recordedOwner = surface->documentRegistry->OwnerOf(token);
        if (recordedOwner && recordedOwner != owner)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* element = surface->elementReferences.Resolve(token, state->Element());
        if (!element)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        auto parsed = URUI::ParseHtmlFragment(html, element->GetTagName(), source_url);
        if (!parsed)
        {
            return InvalidHtml(*surface, parsed.diagnostics);
        }
        URUI::Vector<URUI::Diagnostic> diagnostics;
        auto change = URUI::InnerHtmlReplacement::Begin(*element, *parsed.document, diagnostics);
        if (!change)
        {
            if (!diagnostics.empty())
            {
                InvalidHtml(*surface, diagnostics);
            }
            return URUI_ERR_INTERNAL;
        }
        state->fragment = std::move(change);
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_element_begin_inner_html failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_element_end_inner_html(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                    int commit)
    try
    {
        SurfaceContext* surface = nullptr;
        int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        DocumentState* state = nullptr;
        status = LookupDocument(*surface, owner, state, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (commit != 0 && commit != 1)
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (!state->fragment || state->fragment->Target() != surface->elementReferences.Resolve(token, state->Element()))
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        if (commit)
        {
            state->fragment->Commit();
        }
        state->fragment.reset();
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_element_end_inner_html failed");
        return URUI_ERR_INTERNAL;
    }
}
