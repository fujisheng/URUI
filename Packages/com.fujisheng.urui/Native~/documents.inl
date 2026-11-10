namespace urui_documents
{
    int Check(bool mutation)
    {
        if (!context || !document_registry) return -2;
        if (context_thread != std::this_thread::get_id()) return -3;
        if (mutation && urui_interaction::callback_depth) return -5;
        return 1;
    }
    int Find(UruiDocumentHandle owner, URUI::Bridge::DocumentState*& state, bool loaded, bool mutation)
    {
        const int status = Check(mutation);
        if (status != 1) return status;
        if (!owner) return -1;
        state = document_registry->Find(owner);
        if (state && mutation && (state->retiring || state->loading)) return -5;
        return state && (!loaded || state->Element()) ? 1 : -2;
    }
    int Failure() noexcept
    {
        try { error_message = "URUI document operation failed"; } catch (...) {}
        return -8;
    }
    void ApplyPresentation(URUI::Bridge::DocumentState& state)
    {
        if (auto* root = state.Element()) {
            root->SetStackOrder(state.layer, state.order);
            if (state.visible && (!root->IsPresented() || root->IsModal() != state.modal))
                root->Show(state.modal ? URUI::ModalFlag::Modal : URUI::ModalFlag::None,
                    state.modal ? URUI::FocusFlag::Auto : URUI::FocusFlag::None);
            else if (!state.visible && root->IsPresented()) {
                urui_interaction::CancelDocumentInput(root);
                root->Hide();
            }
            urui_interaction::CancelBlockedDocumentInput();
        }
    }
    int InvalidHtml(const URUI::Vector<URUI::Diagnostic>& diagnostics)
    {
        if (!diagnostics.empty()) {
            const auto& error = diagnostics.front();
            error_message = error.sourceUrl + ":" + std::to_string(error.span.line) + ":" +
                std::to_string(error.span.column) + " [" + error.code + "] " + error.message;
            if (error.code == "HTML_MEMORY") return -8;
        }
        return -9;
    }
}

namespace urui_resources
{
    URUI::String Canonical(const char* source) {
        URUI::String result; URUI::GetSystemInterface()->JoinPath(result, "", source); return result;
    }
    std::shared_ptr<URUI::Bridge::DocumentResources> Pending(URUI::Bridge::DocumentState& state)
    {
        if (!state.pending_resources) {
            if (!next_resource_scope || next_resource_scope == UINT64_MAX) throw std::length_error("Resource scope identities exhausted");
            const auto identity = next_resource_scope++;
            if (state.resources) state.pending_resources = state.resources->Clone(identity, state.handle);
            else state.pending_resources = std::make_shared<URUI::Bridge::DocumentResources>(identity, state.handle,
                state.resource_group, *file_resource_cache, resource_files->Original());
        }
        state.pending_resources->SetTextureRetirement([](uint64_t identity) {
            if (context) context->GetRenderManager().RetireResourceContext(identity);
        });
        return state.pending_resources;
    }
    void Discard(URUI::Bridge::DocumentState& state) noexcept
    {
        if (state.pending_resources) state.pending_resources->Retire();
        state.pending_resources.reset();
    }
    struct LoadScope
    {
        URUI::Bridge::DocumentState& state;
        bool published = false;
        explicit LoadScope(URUI::Bridge::DocumentState& state) : state(state) { state.loading = true; }
        ~LoadScope() { state.loading = false; if (!published) Discard(state); }
    };
}

extern "C"
{
    int urui_resolve_resource_url(const char* base, const char* source, char* output, int capacity)
    try {
        if (!base || !source || capacity < 0 || (!output && capacity)) return -1;
        if (started && context_thread != std::this_thread::get_id()) return -3;
        // URL 规范化不依赖文档或后端，Editor 导出也使用同一实现。
        URUI::SystemInterface system_interface;
        URUI::String canonical; system_interface.JoinPath(canonical, base, source);
        if (canonical.size() >= INT32_MAX) return -4;
        const int required = int(canonical.size()) + 1;
        if (!output && !capacity) return required;
        if (capacity < required) return -required;
        std::memcpy(output, canonical.c_str(), size_t(required)); return required;
    } catch (...) { return urui_documents::Failure(); }

    int urui_document_resource_api_version(void) { return 3; }

    int urui_document_set_replacement(UruiDocumentHandle owner, UruiDocumentHandle previous)
    try {
        URUI::Bridge::DocumentState *candidate = nullptr, *active = nullptr;
        auto status = urui_documents::Find(owner, candidate, false, true);
        if (status != 1) return status;
        status = urui_documents::Find(previous, active, true, true);
        if (status != 1) return status;
        if (owner == previous || candidate->Element() || candidate->resources || candidate->pending_resources) return -1;
        candidate->resource_group = active->resource_group;
        return 1;
    } catch (...) { return urui_documents::Failure(); }

    int urui_document_register_resource(UruiDocumentHandle owner, const char* source, const uint8_t* bytes, int length)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = urui_documents::Find(owner, state, false, true);
        if (status != 1) return status;
        if (!source || !*source || length < 0 || (!bytes && length)) return -1;
        urui_resources::Pending(*state)->Register(urui_resources::Canonical(source), bytes, size_t(length));
        return 1;
    } catch (const std::invalid_argument& error) {
        try { error_message = error.what(); } catch (...) {}
        return -1;
    } catch (const std::length_error& error) {
        try { error_message = error.what(); } catch (...) {}
        return -4;
    } catch (...) { return urui_documents::Failure(); }

    int urui_document_register_rgba_texture(UruiDocumentHandle owner, const char* source, int width, int height,
        const uint8_t* pixels, int length, uint64_t* handle)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = urui_documents::Find(owner, state, false, true);
        if (status != 1) return status;
        if (!source || !*source || !pixels || !handle || width <= 0 || height <= 0 || width > 8192 || height > 8192 ||
            int64_t(width) * height * 4 != length) return -1;
        const auto id = render->RegisterScopedImage(urui_resources::Pending(*state), urui_resources::Canonical(source), width, height, pixels, size_t(length));
        *handle = id; return 1;
    } catch (const std::exception& error) {
        try { error_message = error.what(); } catch (...) {} return -1;
    } catch (...) { return urui_documents::Failure(); }

    int urui_document_register_external_texture(UruiDocumentHandle owner, const char* source, int width, int height, uint64_t resource)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = urui_documents::Find(owner, state, false, true);
        if (status != 1) return status;
        if (!source || !*source || width <= 0 || height <= 0 || width > 8192 || height > 8192 || !(resource & borrowed_texture_bit)) return -1;
        render->RegisterScopedImage(urui_resources::Pending(*state), urui_resources::Canonical(source), width, height, nullptr, 0, resource);
        return 1;
    } catch (const std::exception& error) {
        try { error_message = error.what(); } catch (...) {} return -1;
    } catch (...) { return urui_documents::Failure(); }

    int urui_document_register_sprite(UruiDocumentHandle owner, const char* name, const char* image_source,
        float x, float y, float width, float height, float scale)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = urui_documents::Find(owner,state,false,true);
        if (status != 1) return status;
        if (!name || !*name || !image_source || !*image_source || !std::isfinite(x) || !std::isfinite(y) ||
            !std::isfinite(width) || !std::isfinite(height) || !std::isfinite(scale) || x < 0 || y < 0 || width <= 0 || height <= 0 ||
            scale <= 0 || !std::isfinite(width*scale) || !std::isfinite(height*scale)) return -1;
        const auto source = urui_resources::Canonical(image_source);
        auto resources = urui_resources::Pending(*state);
        const auto texture = render->textures.find(resources->FindImage(source));
        if (texture == render->textures.end() || double(x)+width > texture->second.width || double(y)+height > texture->second.height) {
            error_message = "Sprite region needs a registered source texture and in-bounds source pixels: " + source;
            return -1;
        }
        resources->RegisterSprite(name,source,URUI::Rectanglef::FromPositionSize({x,y},{width,height}),scale);
        return 1;
    } catch (const std::exception& error) {
        try { error_message = error.what(); } catch (...) {} return -1;
    } catch (...) { return urui_documents::Failure(); }

    int urui_document_unregister_texture(UruiDocumentHandle owner, const char* source)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = urui_documents::Find(owner, state, false, true);
        if (status != 1) return status;
        if (!source || !*source) return -1;
        return urui_resources::Pending(*state)->RemoveImage(urui_resources::Canonical(source)) ? 1 : 0;
    } catch (...) { return urui_documents::Failure(); }

    int urui_document_preload_image(UruiDocumentHandle owner, const char* source, int* width, int* height)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = urui_documents::Find(owner, state, true, true);
        if (status != 1) return status;
        if (!source || !*source || !width || !height) return -1;
        *width = *height = 0;
        auto texture = context->GetRenderManager().LoadTexture(source, state->Element()->GetSourceURL(), state->resources);
        const auto dims = texture.GetDimensions();
        if (dims.x <= 0 || dims.y <= 0) return 0;
        *width = dims.x; *height = dims.y; return 1;
    } catch (...) { return urui_documents::Failure(); }

    int urui_document_release_image(UruiDocumentHandle owner, const char* source)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const auto status = urui_documents::Find(owner, state, true, true);
        if (status != 1) return status;
        if (!source || !*source) return -1;
        URUI::String canonical;
        URUI::GetSystemInterface()->JoinPath(canonical, state->Element()->GetSourceURL(), source);
        const auto released = context->GetRenderManager().ReleaseTexture(canonical, state->resources);
        state->resources->RemoveImage(canonical);
        return released ? 1 : 0;
    } catch (...) { return urui_documents::Failure(); }

    UruiDocumentHandle urui_surface_element_owner(uint64_t token)
    {
        return document_registry ? document_registry->OwnerOf(token) : 0;
    }

    UruiDocumentHandle urui_surface_focused_document(void)
    {
        if (!element_references.Owner() || !context || !document_registry) return 0;
        auto* focused = context->GetFocusElement();
        return focused && focused->IsVisible(true) && !context->IsDocumentBlockedByModal(focused->GetOwnerDocument())
            ? document_registry->HandleOf(focused->GetOwnerDocument()) : 0;
    }

    uint64_t urui_surface_pick(float x, float y, UruiDocumentHandle* owner)
    try {
        if (owner) *owner = 0;
        if (!owner || urui_documents::Check(false) != 1 || !std::isfinite(x) || !std::isfinite(y)) return 0;
        auto* element = context->GetElementAtPoint({x, y});
        auto* document = element ? element->GetOwnerDocument() : nullptr;
        const auto handle = document_registry->HandleOf(document);
        if (!handle) return 0;
        const auto token = element_references.Track(element, document);
        if (token) *owner = handle;
        return token;
    } catch (...) { urui_documents::Failure(); return 0; }

    int urui_document_create(UruiDocumentHandle* result)
    try {
        const int status = urui_documents::Check(true);
        return status == 1 ? document_registry->Create(result) : status;
    } catch (...) { return urui_documents::Failure(); }

    int urui_document_load_html(UruiDocumentHandle owner, const char* html, const char* source_url)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const int status = urui_documents::Find(owner, state, false, true);
        if (status != 1) return status;
        if (state->fragment) return -5;
        if (!html || !source_url || !*source_url) return -1;
        auto resources = urui_resources::Pending(*state);
        urui_resources::LoadScope loading(*state);
        auto parsed = URUI::ParseHtmlDocument(html, source_url);
        if (!parsed) return urui_documents::InvalidHtml(parsed.diagnostics);
        auto events = std::make_unique<urui_interaction::CandidateEvents>(owner);
        urui_interaction::pending_documents.reserve(urui_interaction::pending_documents.size() + 1);
        URUI::Vector<URUI::Diagnostic> diagnostics;
        auto tree = URUI::Factory::InstanceParsedHtmlDocument(context, *parsed.document, diagnostics, resources);
        if (!tree) {
            if (!diagnostics.empty()) urui_documents::InvalidHtml(diagnostics);
            return -8;
        }
        events->Attach(*static_cast<URUI::ElementDocument*>(tree.get()));
        tree->AddEventListener("click", listener.get());
        auto* candidate = context->AttachLoadedDocument(std::move(tree));
        struct CandidateScope {
            URUI::ElementDocument* document;
            ~CandidateScope() { if (document) { try { document->Close(); } catch (...) {} } }
        } candidate_scope{candidate};
        candidate->SetStackOrder(state->layer, state->order);
        // Complete the update formerly performed by Show while still hidden and
        // unpublished. Failure must not revoke the old page, focus or resources.
        candidate->UpdateDocument();
        if (!resources->FailedImage().empty()) {
            error_message = "Image dependency failed: " + resources->FailedImage();
            return -8;
        }
        const int published = document_registry->Publish(owner, candidate);
        if (published != 1) return published;
        state->resources = std::move(resources);
        state->pending_resources.reset();
        loading.published = true;
        candidate_scope.document = nullptr;
        urui_interaction::pending_documents.push_back(std::move(events));
        render->plan.DocumentsChanged();
        // From here the new tree is committed. Notification/focus failures cannot
        // turn a successful commit into a reported load failure with stale tokens.
        try {
            if (state->visible) candidate->Show(state->modal ? URUI::ModalFlag::Modal : URUI::ModalFlag::None,
                state->modal ? URUI::FocusFlag::Auto : URUI::FocusFlag::None, URUI::ScrollFlag::Auto, false);
        } catch (...) { ReportBridgeFailure("URUI presentation notification failed after document commit"); }
        urui_interaction::CancelBlockedDocumentInput();
        return 1;
    } catch (...) { return urui_documents::Failure(); }

    int urui_document_close(UruiDocumentHandle owner)
    try {
        const int status = urui_documents::Check(true);
        if (status != 1) return status;
        if (!owner) return -1;
        const int closed = document_registry->Close(owner);
        if (closed == 1) render->plan.DocumentsChanged();
        return closed;
    } catch (...) { return urui_documents::Failure(); }

    uint64_t urui_document_root(UruiDocumentHandle owner)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        if (urui_documents::Find(owner, state, true, false) != 1) return 0;
        return element_references.Track(state->Element(), state->Element());
    } catch (...) { urui_documents::Failure(); return 0; }

    int urui_document_set_presentation(UruiDocumentHandle owner, int layer, int order, int visible, int modal)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const int status = urui_documents::Find(owner, state, false, true);
        if (status != 1) return status;
        if ((visible != 0 && visible != 1) || (modal != 0 && modal != 1)) return -1;
        state->layer = layer; state->order = order; state->visible = visible != 0; state->modal = modal != 0;
        urui_documents::ApplyPresentation(*state);
        return 1;
    } catch (...) { return urui_documents::Failure(); }

    uint64_t urui_document_body(UruiDocumentHandle owner)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        if (urui_documents::Find(owner, state, true, false) != 1) return 0;
        auto* root = state->Element();
        for (int i = 0; i < root->GetNumChildren(); ++i)
            if (auto* child = root->GetChild(i); child->GetTagName() == "body") return element_references.Track(child, root);
        return 0;
    } catch (...) { urui_documents::Failure(); return 0; }

    int urui_element_set_inner_html(UruiDocumentHandle owner, uint64_t token, const char* html, const char* source_url)
    {
        const int status = urui_element_begin_inner_html(owner, token, html, source_url);
        return status == 1 ? urui_element_end_inner_html(owner, token, 1) : status;
    }

    int urui_element_begin_inner_html(UruiDocumentHandle owner, uint64_t token, const char* html, const char* source_url)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const int status = urui_documents::Find(owner, state, true, true);
        if (status != 1) return status;
        if (state->fragment) return -5;
        if (!token || !html || !source_url || !*source_url) return -1;
        const auto recorded_owner = document_registry->OwnerOf(token);
        if (recorded_owner && recorded_owner != owner) return -1;
        auto* element = element_references.Resolve(token, state->Element());
        if (!element) return -2;
        auto parsed = URUI::ParseHtmlFragment(html, element->GetTagName(), source_url);
        if (!parsed) return urui_documents::InvalidHtml(parsed.diagnostics);
        URUI::Vector<URUI::Diagnostic> diagnostics;
        auto change = URUI::InnerHtmlReplacement::Begin(*element, *parsed.document, diagnostics);
        if (!change) {
            if (!diagnostics.empty()) urui_documents::InvalidHtml(diagnostics);
            return -8;
        }
        state->fragment = std::move(change);
        return 1;
    } catch (...) { return urui_documents::Failure(); }

    int urui_element_end_inner_html(UruiDocumentHandle owner, uint64_t token, int commit)
    try {
        URUI::Bridge::DocumentState* state = nullptr;
        const int status = urui_documents::Find(owner, state, true, true);
        if (status != 1) return status;
        if (commit != 0 && commit != 1) return -1;
        if (!state->fragment || state->fragment->Target() != element_references.Resolve(token, state->Element())) return -2;
        if (commit) state->fragment->Commit();
        else state->fragment->Rollback();
        state->fragment.reset();
        return 1;
    } catch (...) { return urui_documents::Failure(); }
}
