namespace URUI::CompiledDocuments
{
    class Files final : public URUI::FileInterface
    {
        struct Cursor
        {
            const uint8_t* data;
            size_t size;
            size_t position = 0;
        };
        std::unordered_map<URUI::String, Cursor> sources;
        std::unordered_map<Cursor*, std::unique_ptr<Cursor>> opened;

    public:
        Files(const URUICompileResource* resources, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                const auto& source = resources[i];
                if (!source.uri || !*source.uri || source.length < 0 || (source.length && !source.bytes))
                {
                    throw std::invalid_argument("Invalid compile dependency.");
                }
                const auto uri = URUI::Resources::Canonical(source.uri);
                if (!sources.emplace(uri, Cursor{source.bytes, size_t(source.length)}).second)
                {
                    throw std::invalid_argument("Duplicate compile dependency URI.");
                }
            }
        }
        URUI::FileHandle Open(const URUI::String& path) override
        {
            const auto found = sources.find(URUI::Resources::Canonical(path.c_str()));
            if (found == sources.end())
            {
                return 0;
            }
            auto cursor = std::make_unique<Cursor>(found->second);
            auto* key = cursor.get();
            opened.emplace(key, std::move(cursor));
            return reinterpret_cast<URUI::FileHandle>(key);
        }
        void Close(URUI::FileHandle handle) override
        {
            opened.erase(reinterpret_cast<Cursor*>(handle));
        }
        size_t Read(void* target, size_t size, URUI::FileHandle handle) override
        {
            auto& cursor = *opened.at(reinterpret_cast<Cursor*>(handle));
            const auto count = std::min(size, cursor.size - cursor.position);
            if (count)
            {
                std::memcpy(target, cursor.data + cursor.position, count);
            }
            cursor.position += count;
            return count;
        }
        bool Seek(URUI::FileHandle handle, long offset, int origin) override
        {
            auto& cursor = *opened.at(reinterpret_cast<Cursor*>(handle));
            if (origin != SEEK_SET && origin != SEEK_CUR && origin != SEEK_END)
            {
                return false;
            }
            const auto base = origin == SEEK_SET ? 0 : int64_t(origin == SEEK_CUR ? cursor.position : cursor.size);
            if (offset < -base || offset > int64_t(cursor.size) - base)
            {
                return false;
            }
            cursor.position = size_t(base + offset);
            return true;
        }
        size_t Tell(URUI::FileHandle handle) override
        {
            return opened.at(reinterpret_cast<Cursor*>(handle))->position;
        }
        size_t Length(URUI::FileHandle handle) override
        {
            return opened.at(reinterpret_cast<Cursor*>(handle))->size;
        }
    };
}

extern "C"
{
    int urui_element_compiled_info(URUIDocumentHandle owner, uint64_t token, int kind, const char* key,
                                   URUICompiledNodeInfo* info)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const int status = URUI::Documents::Find(owner, state, true, false);
        if (status != 1)
        {
            return status;
        }
        if (!info || kind < 0 || kind > 2 || (kind && (!key || !*key)))
        {
            return -1;
        }
        auto* element = elementReferences.Resolve(token, state->Element());
        if (!element)
        {
            return -2;
        }
        if (!element->GetCompiledBlueprint())
        {
            return 0;
        }
        *info = {element->GetCompiledBlueprint(),  element->GetCompiledQueryRevision(),   element->GetCompiledNodeId(),
                 element->GetCompiledSubtreeEnd(), int(element->GetCompiledQueryFlags()), 0};
        if ((info->flags & 8) && kind)
        {
            info->root_match = kind == 1 ? element->GetId() == key : element->IsClassSet(key);
        }
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_element_compiled_tokens(URUIDocumentHandle owner, uint64_t token, uint64_t identity, uint64_t revision,
                                     uint64_t* output, int capacity)
    try
    {
        URUI::Bridge::DocumentState* state = nullptr;
        const int status = URUI::Documents::Find(owner, state, true, false);
        if (status != 1)
        {
            return status;
        }
        auto* root = elementReferences.Resolve(token, state->Element());
        if (!root)
        {
            return -2;
        }
        if (!identity || root->GetCompiledBlueprint() != identity || root->GetCompiledQueryRevision() != revision ||
            !(root->GetCompiledQueryFlags() & 1))
        {
            return 0;
        }
        const uint32_t first = root->GetCompiledNodeId() == UINT32_MAX ? 0 : root->GetCompiledNodeId();
        const uint32_t end = root->GetCompiledSubtreeEnd();
        if (end < first || end - first > 1000000 || capacity < 0 || uint32_t(capacity) < end - first ||
            (capacity && !output))
        {
            return -1;
        }
        if (end > first)
        {
            std::fill(output, output + (end - first), uint64_t(0));
        }
        std::vector<URUI::Element*> pending{root};
        uint32_t previous = 0;
        bool have_previous = false;
        while (!pending.empty())
        {
            auto* element = pending.back();
            pending.pop_back();
            const auto id = element->GetCompiledNodeId();
            if (element == root && (root->GetCompiledQueryFlags() & 8))
            {
            }
            else
            {
                if (element->GetCompiledBlueprint() != identity || id < first || id >= end ||
                    (have_previous && id <= previous))
                {
                    return 0;
                }
                previous = id;
                have_previous = true;
                const auto reference = elementReferences.Track(element, state->Element());
                if (!reference)
                {
                    return -4;
                }
                output[id - first] = reference;
            }
            for (int i = element->GetNumChildren() - 1; i >= 0; --i)
            {
                pending.push_back(element->GetChild(i));
            }
        }
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_compiled_document_metadata(const uint8_t* bytes, int length, char* output, int capacity, int* required)
    try
    {
        if (!bytes || length <= 0 || capacity < 0 || (capacity && !output) || !required)
        {
            return -1;
        }
        if (started && contextThread != std::this_thread::get_id())
        {
            return -3;
        }
        if (URUI::Interaction::callbackDepth)
        {
            return -5;
        }
        const bool temporary = !started;
        if (temporary && !urui_create(1, 1))
        {
            return -8;
        }
        struct Shutdown
        {
            bool temporary;
            ~Shutdown()
            {
                if (temporary)
                {
                    urui_destroy();
                }
            }
        } shutdown{temporary};
        URUI::CompiledDocument compiled;
        URUI::String error;
        if (!URUI::ReadCompiledDocument(bytes, size_t(length), compiled, error))
        {
            error_message = error;
            return -9;
        }
        uint32_t count = 0;
        for (const auto& node : compiled.html.nodes)
        {
            count = std::max(count, node.subtreeEnd);
        }
        URUI::String json = "{\"identity\":\"" + std::to_string(compiled.identity) + "\",\"parentIdentity\":\"" +
                            std::to_string(compiled.parentIdentity) + "\",\"nodeCount\":" + std::to_string(count) +
                            ",\"nodes\":[";
        bool first = true;
        std::function<void(const std::vector<URUI::HtmlNode>&, int)> append = [&](const auto& nodes, int parent)
        {
            for (const auto& node : nodes)
            {
                if (node.kind == URUI::HtmlNodeKind::Element || node.kind == URUI::HtmlNodeKind::Template)
                {
                    if (!first)
                    {
                        json += ',';
                    }
                    first = false;
                    URUI::String name;
                    URUI::String marker;
                    URUI::String input_type;
                    for (const auto& attribute : node.attributes)
                    {
                        if (attribute.name == "id")
                        {
                            name = attribute.value;
                        }
                        if (attribute.name == "data-urui-type")
                        {
                            marker = attribute.value;
                        }
                        if (attribute.name == "type")
                        {
                            input_type = attribute.value;
                        }
                    }
                    json += "{\"id\":" + std::to_string(node.localId) + ",\"end\":" + std::to_string(node.subtreeEnd) +
                            ",\"parent\":" + std::to_string(parent) + ",\"tag\":" + URUI::Authoring::Quote(node.tag) +
                            ",\"name\":" + URUI::Authoring::Quote(name) +
                            ",\"marker\":" + URUI::Authoring::Quote(marker) +
                            ",\"inputType\":" + URUI::Authoring::Quote(input_type) + ",\"classes\":[";
                    bool classFirst = true;
                    for (const auto& item : node.compiledClasses)
                    {
                        if (!classFirst)
                        {
                            json += ',';
                        }
                        classFirst = false;
                        json += URUI::Authoring::Quote(item);
                    }
                    json += "]}";
                }
                append(node.children, int(node.localId));
            }
        };
        append(compiled.html.nodes, -1);
        json += "]}";
        if (json.size() >= size_t(INT32_MAX))
        {
            return -4;
        }
        *required = int(json.size()) + 1;
        if (capacity < *required)
        {
            return -3;
        }
        std::memcpy(output, json.c_str(), size_t(*required));
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }
    int urui_compile_template(const uint8_t* parent, int parent_length, const char* html, const char* source_url,
                              uint8_t* output, int capacity, int* required)
    try
    {
        if (!parent || parent_length <= 0 || !html || !source_url || !*source_url || capacity < 0 ||
            (capacity && !output) || !required)
        {
            return -1;
        }
        if (started && contextThread != std::this_thread::get_id())
        {
            return -3;
        }
        if (URUI::Interaction::callbackDepth)
        {
            return -5;
        }
        const bool temporary = !started;
        if (temporary && !urui_create(1, 1))
        {
            return -8;
        }
        struct Shutdown
        {
            bool temporary;
            ~Shutdown()
            {
                if (temporary)
                {
                    urui_destroy();
                }
            }
        } shutdown{temporary};
        URUI::Vector<URUI::byte> bytes;
        URUI::Vector<URUI::Diagnostic> diagnostics;
        if (!URUI::CompileTemplate(html, source_url, parent, size_t(parent_length), bytes, diagnostics))
        {
            return URUI::Documents::InvalidHtml(diagnostics);
        }
        if (bytes.size() > size_t(INT32_MAX))
        {
            return -4;
        }
        *required = int(bytes.size());
        if (capacity < *required)
        {
            return -3;
        }
        std::memcpy(output, bytes.data(), bytes.size());
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_element_begin_compiled_template(URUIDocumentHandle owner, uint64_t token, const uint8_t* bytes, int length)
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
        if (!token || !bytes || length <= 0)
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
        URUI::String error;
        const auto compiled =
            state->templates.Read(bytes, size_t(length), state->Element()->GetStyleSpecification(), error);
        if (!compiled)
        {
            error_message = error;
            return -9;
        }
        if (compiled->html.kind != URUI::HtmlDocumentKind::Fragment || !compiled->parentIdentity ||
            compiled->parentIdentity != state->Element()->GetCompiledIdentity())
        {
            error_message = "Compiled template does not belong to this document generation.";
            return -1;
        }
        URUI::Vector<URUI::Diagnostic> diagnostics;
        auto change = URUI::InnerHtmlReplacement::Begin(*element, compiled->html, diagnostics);
        if (!change)
        {
            if (!diagnostics.empty())
            {
                URUI::Documents::InvalidHtml(diagnostics);
            }
            return -8;
        }
        uint32_t end = 0;
        for (const auto& node : compiled->html.nodes)
        {
            end = std::max(end, node.subtreeEnd);
        }
        change->SetCompiledRoot(compiled->identity, end);
        state->fragment = std::move(change);
        return 1;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_compile_document(const char* html, const char* source_url, int fragment,
                              const URUICompileResource* resources, int resource_count, uint8_t* output, int capacity,
                              int* required)
    try
    {
        if (!html || !source_url || !*source_url || (fragment != 0 && fragment != 1) || !required || capacity < 0 ||
            (capacity && !output) || resource_count < 0 || resource_count > 100000 || (resource_count && !resources))
        {
            return -1;
        }
        if (started && contextThread != std::this_thread::get_id())
        {
            return -3;
        }
        if (URUI::Interaction::callbackDepth)
        {
            return -5;
        }
        const bool temporary = !started;
        if (temporary && !urui_create(1, 1))
        {
            return -8;
        }
        struct Shutdown
        {
            bool temporary;
            ~Shutdown()
            {
                if (temporary)
                {
                    urui_destroy();
                }
            }
        } shutdown{temporary};
        URUI::CompiledDocuments::Files files(resources, resource_count);
        URUI::Vector<URUI::byte> bytes;
        URUI::Vector<URUI::Diagnostic> diagnostics;
        if (!URUI::CompileDocument(html, source_url, &files, bytes, diagnostics, fragment != 0))
        {
            return URUI::Documents::InvalidHtml(diagnostics);
        }
        if (bytes.size() > size_t(INT32_MAX))
        {
            return -4;
        }
        *required = int(bytes.size());
        if (capacity < *required)
        {
            return -3;
        }
        std::memcpy(output, bytes.data(), bytes.size());
        return 1;
    }
    catch (const std::exception& failure)
    {
        error_message = failure.what();
        return -8;
    }
    catch (...)
    {
        return URUI::Documents::Failure();
    }

    int urui_document_load_compiled(URUIDocumentHandle owner, const uint8_t* bytes, int length)
    {
        if (!bytes || length <= 0)
        {
            return -1;
        }
        return LoadDocumentImpl(owner, nullptr, nullptr, bytes, length);
    }
}
