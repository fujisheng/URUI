#pragma once
#include "element_references.h"
#include <URUI/Core/Context.h>
#include "Engine/Source/Core/HtmlBuilder.h"
#include "Engine/Source/Core/CompiledDocument.h"
#include <memory>
#include <thread>
#include <unordered_map>

namespace URUI::Bridge
{
    class DocumentResources;
    struct DocumentState
    {
        uint64_t handle;
        URUI::ObserverPtr<URUI::Element> root;
        int layer = 0;
        int order = 0;
        bool visible = false;
        bool modal = false;
        bool retiring = false;
        bool listening = false;
        bool loading = false;
        uint64_t resourceGroup = 0;
        std::shared_ptr<DocumentResources> resources;
        std::shared_ptr<DocumentResources> pendingResources;
        URUI::UniquePtr<URUI::InnerHtmlReplacement> fragment;
        URUI::CompiledTemplateCache templates;
        URUI::ElementDocument* Element() const
        {
            return static_cast<URUI::ElementDocument*>(root.get());
        }
    };

    // Surface 拥有注册表；共享身份表只按所属文档清理，不选择隐式当前文档。
    class DocumentRegistry
    {
    public:
        using RetireDocument = void (*)(URUI::ElementDocument*) noexcept;

    private:
        URUI::Context& context;
        ElementReferences& references;
        std::thread::id owner;
        RetireDocument retireDocument;
        std::unordered_map<uint64_t, std::unique_ptr<DocumentState>> states;
        std::unordered_map<URUI::ElementDocument*, uint64_t> handles;
        bool Owner() const
        {
            return owner == std::this_thread::get_id();
        }

    public:
        DocumentRegistry(URUI::Context& context, ElementReferences& references, RetireDocument retire = nullptr);
        ~DocumentRegistry();
        DocumentRegistry(const DocumentRegistry&) = delete;
        DocumentRegistry& operator=(const DocumentRegistry&) = delete;
        int Create(uint64_t* result);
        DocumentState* Find(uint64_t handle);
        uint64_t OwnerOf(uint64_t elementToken) const;
        uint64_t HandleOf(URUI::ElementDocument* document) const;
        int Publish(uint64_t handle, URUI::ElementDocument* document);
        int Close(uint64_t handle);
        void Clear();
        size_t Count() const
        {
            return states.size();
        }
    };
}
