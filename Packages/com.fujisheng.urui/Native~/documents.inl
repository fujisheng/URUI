namespace URUI::Documents
{
    int Check(bool mutation)
    {
        if (!context || !documentRegistry)
        {
            return -2;
        }
        if (contextThread != std::this_thread::get_id())
        {
            return -3;
        }
        if (mutation && URUI::Interaction::callbackDepth)
        {
            return -5;
        }
        return 1;
    }
    int Find(URUIDocumentHandle owner, URUI::Bridge::DocumentState*& state, bool loaded, bool mutation)
    {
        const int status = Check(mutation);
        if (status != 1)
        {
            return status;
        }
        if (!owner)
        {
            return -1;
        }
        state = documentRegistry->Find(owner);
        if (state && mutation && (state->retiring || state->loading))
        {
            return -5;
        }
        return state && (!loaded || state->Element()) ? 1 : -2;
    }
    int Failure() noexcept
    {
        try
        {
            error_message = "URUI document operation failed";
        }
        catch (...)
        {
        }
        return -8;
    }
    void ApplyPresentation(URUI::Bridge::DocumentState& state)
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
                URUI::Interaction::CancelDocumentInput(root);
                root->Hide();
            }
            URUI::Interaction::CancelBlockedDocumentInput();
        }
    }
    int InvalidHtml(const URUI::Vector<URUI::Diagnostic>& diagnostics)
    {
        if (!diagnostics.empty())
        {
            const auto& error = diagnostics.front();
            error_message = error.sourceUrl + ":" + std::to_string(error.span.line) + ":" +
                            std::to_string(error.span.column) + " [" + error.code + "] " + error.message;
            if (error.code == "HTML_MEMORY")
            {
                return -8;
            }
        }
        return -9;
    }
}

namespace URUI::Resources
{
    URUI::String Canonical(const char* source)
    {
        URUI::String result;
        URUI::GetSystemInterface()->JoinPath(result, "", source);
        return result;
    }
    std::shared_ptr<URUI::Bridge::DocumentResources> Pending(URUI::Bridge::DocumentState& state)
    {
        if (!state.pendingResources)
        {
            if (!nextResourceScope || nextResourceScope == UINT64_MAX)
            {
                throw std::length_error("Resource scope identities exhausted");
            }
            const auto identity = nextResourceScope++;
            if (state.resources)
            {
                state.pendingResources = state.resources->Clone(identity, state.handle);
            }
            else
            {
                state.pendingResources = std::make_shared<URUI::Bridge::DocumentResources>(
                    identity, state.handle, state.resourceGroup, *fileResourceCache, resourceFiles->Original());
            }
        }
        state.pendingResources->SetTextureRetirement(
            [](uint64_t identity)
            {
                if (context)
                {
                    context->GetRenderManager().RetireResourceContext(identity);
                }
            });
        return state.pendingResources;
    }
    void Discard(URUI::Bridge::DocumentState& state) noexcept
    {
        if (state.pendingResources)
        {
            state.pendingResources->Retire();
        }
        state.pendingResources.reset();
    }
    struct LoadScope
    {
        URUI::Bridge::DocumentState& state;
        bool published = false;
        explicit LoadScope(URUI::Bridge::DocumentState& state) : state(state)
        {
            state.loading = true;
        }
        ~LoadScope()
        {
            state.loading = false;
            if (!published)
            {
                Discard(state);
            }
        }
    };
}

extern "C"
{
    int urui_resolve_resource_url(const char* base, const char* source, char* output, int capacity)
    try
    {
        if (!base || !source || capacity < 0 || (!output && capacity))
        {
            return -1;
        }
        if (started && contextThread != std::this_thread::get_id())
        {
            return -3;
        }
        // URL 规范化不依赖文档或后端，Editor 导出也使用同一实现。
        URUI::SystemInterface system_interface;
        URUI::String canonical;
        system_interface.JoinPath(canonical, base, source);
        if (canonical.size() >= INT32_MAX)
        {
            return -4;
        }
        const int required = int(canonical.size()) + 1;
        if (!output && !capacity)
        {
            return required;
        }
        if (capacity < required)
        {
            return -required;
        }
        std::memcpy(output, canonical.c_str(), size_t(required));
        return required;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_document_resource_api_version(void)
    {
        return 3;
    }

    int urui_document_set_replacement(URUIDocumentHandle owner, URUIDocumentHandle previous)
    try
    {
        URUI::Bridge::DocumentState *candidate = nullptr, *active = nullptr;
        auto status = URUI::Documents::Find(owner, candidate, false, true);
        if (status != 1)
        {
            return status;
        }
        status = URUI::Documents::Find(previous, active, true, true);
        if (status != 1)
        {
            return status;
        }
        if (owner == previous || candidate->Element() || candidate->resources || candidate->pendingResources)
        {
            return -1;
        }
        candidate->resourceGroup = active->resourceGroup;
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_document_register_resource(URUIDocumentHandle owner, const char* source, const uint8_t* bytes, int length)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = URUI::Documents::Find(owner, state, false, true);
        if (status != 1)
        {
            return status;
        }
        if (!source || !*source || length < 0 || (!bytes && length))
        {
            return -1;
        }
        URUI::Resources::Pending(*state)->Register(URUI::Resources::Canonical(source), bytes, size_t(length));
        return 1;
    }
    catch (const std::invalid_argument& error)
    {
        try
        {
            error_message = error.what();
        }
        catch (...)
        {
        }
        return -1;
    }
    catch (const std::length_error& error)
    {
        try
        {
            error_message = error.what();
        }
        catch (...)
        {
        }
        return -4;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_document_register_rgba_texture(URUIDocumentHandle owner, const char* source, int width, int height,
                                            const uint8_t* pixels, int length, uint64_t* handle)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = URUI::Documents::Find(owner, state, false, true);
        if (status != 1)
        {
            return status;
        }
        if (!source || !*source || !pixels || !handle || width <= 0 || height <= 0 || width > 8192 || height > 8192 ||
            int64_t(width) * height * 4 != length)
        {
            return -1;
        }
        const auto id =
            render->RegisterScopedImage(URUI::Resources::Pending(*state), URUI::Resources::Canonical(source), width,
                                        height, pixels, size_t(length));
        *handle = id;
        return 1;
    }
    catch (const std::exception& error)
    {
        try
        {
            error_message = error.what();
        }
        catch (...)
        {
        }
        return -1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_document_register_external_texture(URUIDocumentHandle owner, const char* source, int width, int height,
                                                uint64_t resource)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = URUI::Documents::Find(owner, state, false, true);
        if (status != 1)
        {
            return status;
        }
        if (!source || !*source || width <= 0 || height <= 0 || width > 8192 || height > 8192 ||
            !(resource & borrowedTextureBit))
        {
            return -1;
        }
        render->RegisterScopedImage(URUI::Resources::Pending(*state), URUI::Resources::Canonical(source), width, height,
                                    nullptr, 0, resource);
        return 1;
    }
    catch (const std::exception& error)
    {
        try
        {
            error_message = error.what();
        }
        catch (...)
        {
        }
        return -1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_document_register_sprite(URUIDocumentHandle owner, const char* name, const char* image_source, float x,
                                      float y, float width, float height, float scale)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = URUI::Documents::Find(owner, state, false, true);
        if (status != 1)
        {
            return status;
        }
        if (!name || !*name || !image_source || !*image_source || !std::isfinite(x) || !std::isfinite(y) ||
            !std::isfinite(width) || !std::isfinite(height) || !std::isfinite(scale) || x < 0 || y < 0 || width <= 0 ||
            height <= 0 || scale <= 0 || !std::isfinite(width * scale) || !std::isfinite(height * scale))
        {
            return -1;
        }
        const auto source = URUI::Resources::Canonical(image_source);
        auto resources = URUI::Resources::Pending(*state);
        const auto texture = render->textures.find(resources->FindImage(source));
        if (texture == render->textures.end() || double(x) + width > texture->second.width ||
            double(y) + height > texture->second.height)
        {
            error_message = "Sprite region needs a registered source texture and in-bounds source pixels: " + source;
            return -1;
        }
        resources->RegisterSprite(name, source, URUI::Rectanglef::FromPositionSize({x, y}, {width, height}), scale);
        return 1;
    }
    catch (const std::exception& error)
    {
        try
        {
            error_message = error.what();
        }
        catch (...)
        {
        }
        return -1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_document_unregister_texture(URUIDocumentHandle owner, const char* source)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = URUI::Documents::Find(owner, state, false, true);
        if (status != 1)
        {
            return status;
        }
        if (!source || !*source)
        {
            return -1;
        }
        return URUI::Resources::Pending(*state)->RemoveImage(URUI::Resources::Canonical(source)) ? 1 : 0;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_document_preload_image(URUIDocumentHandle owner, const char* source, int* width, int* height)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = URUI::Documents::Find(owner, state, true, true);
        if (status != 1)
        {
            return status;
        }
        if (!source || !*source || !width || !height)
        {
            return -1;
        }
        *width = *height = 0;
        auto texture =
            context->GetRenderManager().LoadTexture(source, state->Element()->GetSourceURL(), state->resources);
        const auto dims = texture.GetDimensions();
        if (dims.x <= 0 || dims.y <= 0)
        {
            return 0;
        }
        *width = dims.x;
        *height = dims.y;
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_document_release_image(URUIDocumentHandle owner, const char* source)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = URUI::Documents::Find(owner, state, true, true);
        if (status != 1)
        {
            return status;
        }
        if (!source || !*source)
        {
            return -1;
        }
        URUI::String canonical;
        URUI::GetSystemInterface()->JoinPath(canonical, state->Element()->GetSourceURL(), source);
        const auto released = context->GetRenderManager().ReleaseTexture(canonical, state->resources);
        state->resources->RemoveImage(canonical);
        return released ? 1 : 0;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    URUIDocumentHandle urui_surface_element_owner(uint64_t token)
    {
        return documentRegistry ? documentRegistry->OwnerOf(token) : 0;
    }

    URUIDocumentHandle urui_surface_focused_document(void)
    {
        if (!elementReferences.Owner() || !context || !documentRegistry)
        {
            return 0;
        }
        auto* focused = context->GetFocusElement();
        return focused && focused->IsVisible(true) && !context->IsDocumentBlockedByModal(focused->GetOwnerDocument())
                   ? documentRegistry->HandleOf(focused->GetOwnerDocument())
                   : 0;
    }

    uint64_t urui_surface_pick(float x, float y, URUIDocumentHandle* owner)
    try
    {
        if (owner)
        {
            *owner = 0;
        }
        if (!owner || URUI::Documents::Check(false) != 1 || !std::isfinite(x) || !std::isfinite(y))
        {
            return 0;
        }
        auto* element = context->GetElementAtPoint({x, y});
        auto* document = element ? element->GetOwnerDocument() : nullptr;
        const auto handle = documentRegistry->HandleOf(document);
        if (!handle)
        {
            return 0;
        }
        const auto token = elementReferences.Track(element, document);
        if (token)
        {
            *owner = handle;
        }
        return token;
    }
    catch (...)
    {
        URUI::Documents::Failure();
        return 0;
    }

    int urui_document_create(URUIDocumentHandle* result)
    try
    {
        const int status = URUI::Documents::Check(true);
        return status == 1 ? documentRegistry->Create(result) : status;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    static int LoadDocumentImpl(URUIDocumentHandle owner, const char* html, const char* source_url,
                                const uint8_t* bytes, int length)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const int status = URUI::Documents::Find(owner, state, false, true);
        if (status != 1)
        {
            return status;
        }
        if (state->fragment)
        {
            return -5;
        }
        if (bytes ? length <= 0 : (!html || !source_url || !*source_url))
        {
            return -1;
        }
        auto resources = URUI::Resources::Pending(*state);
        URUI::Resources::LoadScope loading(*state);
        URUI::HtmlParseResult parsed;
        URUI::CompiledDocument compiled;
        if (bytes)
        {
            URUI::String error;
            if (!URUI::ReadCompiledDocument(bytes, size_t(length), compiled, error))
            {
                error_message = error;
                return -9;
            }
            if (compiled.html.kind != URUI::HtmlDocumentKind::Document)
            {
                return -1;
            }
        }
        else
        {
            parsed = URUI::ParseHtmlDocument(html, source_url);
            if (!parsed)
            {
                return URUI::Documents::InvalidHtml(parsed.diagnostics);
            }
        }
        auto events = std::make_unique<URUI::Interaction::CandidateEvents>(owner);
        URUI::Interaction::pendingDocuments.reserve(URUI::Interaction::pendingDocuments.size() + 1);
        URUI::Vector<URUI::Diagnostic> diagnostics;
        auto tree = bytes
                        ? URUI::Factory::InstanceCompiledDocument(context, std::move(compiled), diagnostics, resources)
                        : URUI::Factory::InstanceParsedHtmlDocument(context, *parsed.document, diagnostics, resources);
        if (!tree)
        {
            if (!diagnostics.empty())
            {
                URUI::Documents::InvalidHtml(diagnostics);
            }
            return -8;
        }
        events->Attach(*static_cast<URUI::ElementDocument*>(tree.get()));
        tree->AddEventListener("click", listener.get());
        auto* candidate = context->AttachLoadedDocument(std::move(tree));
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
        // Complete the update formerly performed by Show while still hidden and
        // unpublished. Failure must not revoke the old page, focus or resources.
        candidate->UpdateDocument();
        if (!resources->FailedImage().empty())
        {
            error_message = "Image dependency failed: " + resources->FailedImage();
            return -8;
        }
        const int published = documentRegistry->Publish(owner, candidate);
        if (published != 1)
        {
            return published;
        }
        state->resources = std::move(resources);
        state->pendingResources.reset();
        loading.published = true;
        candidateScope.document = nullptr;
        URUI::Interaction::pendingDocuments.push_back(std::move(events));
        render->plan.DocumentsChanged();
        render->drawPlan.Invalidate(render->documentGeneration = URUI::RenderPlan::Identity());
        // From here the new tree is committed. Notification/focus failures cannot
        // turn a successful commit into a reported load failure with stale tokens.
        try
        {
            if (state->visible)
            {
                candidate->Show(state->modal ? URUI::ModalFlag::Modal : URUI::ModalFlag::None,
                                state->modal ? URUI::FocusFlag::Auto : URUI::FocusFlag::None, URUI::ScrollFlag::Auto,
                                false);
            }
        }
        catch (...)
        {
            ReportBridgeFailure("URUI presentation notification failed after document commit");
        }
        URUI::Interaction::CancelBlockedDocumentInput();
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_document_load_html(URUIDocumentHandle owner, const char* html, const char* source_url)
    {
        return LoadDocumentImpl(owner, html, source_url, nullptr, 0);
    }

    int urui_document_close(URUIDocumentHandle owner)
    try
    {
        const int status = URUI::Documents::Check(true);
        if (status != 1)
        {
            return status;
        }
        if (!owner)
        {
            return -1;
        }
        const int closed = documentRegistry->Close(owner);
        if (closed == 1)
        {
            render->plan.DocumentsChanged();
            render->drawPlan.Invalidate(render->documentGeneration = URUI::RenderPlan::Identity());
        }
        return closed;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    uint64_t urui_document_root(URUIDocumentHandle owner)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        if (URUI::Documents::Find(owner, state, true, false) != 1)
        {
            return 0;
        }
        return elementReferences.Track(state->Element(), state->Element());
    }
    catch (...)
    {
        URUI::Documents::Failure();
        return 0;
    }

    int urui_document_set_presentation(URUIDocumentHandle owner, int layer, int order, int visible, int modal)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const int status = URUI::Documents::Find(owner, state, false, true);
        if (status != 1)
        {
            return status;
        }
        if ((visible != 0 && visible != 1) || (modal != 0 && modal != 1))
        {
            return -1;
        }
        state->layer = layer;
        state->order = order;
        state->visible = visible != 0;
        state->modal = modal != 0;
        URUI::Documents::ApplyPresentation(*state);
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    uint64_t urui_document_body(URUIDocumentHandle owner)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        if (URUI::Documents::Find(owner, state, true, false) != 1)
        {
            return 0;
        }
        auto* root = state->Element();
        for (int i = 0; i < root->GetNumChildren(); ++i)
        {
            if (auto* child = root->GetChild(i); child->GetTagName() == "body")
            {
                return elementReferences.Track(child, root);
            }
        }
        return 0;
    }
    catch (...)
    {
        URUI::Documents::Failure();
        return 0;
    }

    int urui_element_set_inner_html(URUIDocumentHandle owner, uint64_t token, const char* html, const char* source_url)
    {
        const int status = urui_element_begin_inner_html(owner, token, html, source_url);
        return status == 1 ? urui_element_end_inner_html(owner, token, 1) : status;
    }

    int urui_element_begin_inner_html(URUIDocumentHandle owner, uint64_t token, const char* html,
                                      const char* source_url)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const int status = URUI::Documents::Find(owner, state, true, true);
        if (status != 1)
        {
            return status;
        }
        if (state->fragment)
        {
            return -5;
        }
        if (!token || !html || !source_url || !*source_url)
        {
            return -1;
        }
        const auto recordedOwner = documentRegistry->OwnerOf(token);
        if (recordedOwner && recordedOwner != owner)
        {
            return -1;
        }
        auto* element = elementReferences.Resolve(token, state->Element());
        if (!element)
        {
            return -2;
        }
        auto parsed = URUI::ParseHtmlFragment(html, element->GetTagName(), source_url);
        if (!parsed)
        {
            return URUI::Documents::InvalidHtml(parsed.diagnostics);
        }
        URUI::Vector<URUI::Diagnostic> diagnostics;
        auto change = URUI::InnerHtmlReplacement::Begin(*element, *parsed.document, diagnostics);
        if (!change)
        {
            if (!diagnostics.empty())
            {
                URUI::Documents::InvalidHtml(diagnostics);
            }
            return -8;
        }
        state->fragment = std::move(change);
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_element_end_inner_html(URUIDocumentHandle owner, uint64_t token, int commit)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const int status = URUI::Documents::Find(owner, state, true, true);
        if (status != 1)
        {
            return status;
        }
        if (commit != 0 && commit != 1)
        {
            return -1;
        }
        if (!state->fragment || state->fragment->Target() != elementReferences.Resolve(token, state->Element()))
        {
            return -2;
        }
        if (commit)
        {
            state->fragment->Commit();
        }
        else
        {
            state->fragment->Rollback();
        }
        state->fragment.reset();
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }
}
