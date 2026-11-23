#pragma once
#include "HtmlDocument.h"
#include <URUI/Core/Stream.h>
#include <URUI/Core/StyleSheetContainer.h>
#include <list>

namespace URUI
{
    class FileInterface;
    struct CompiledDocument
    {
        HtmlDocument html;
        String title;
        SharedPtr<PropertySpecification> specification;
        SharedPtr<StyleSheetContainer> styles;
        uint64_t identity = 0;
        uint64_t parentIdentity = 0;
    };

    // 编辑器与动态文档共用编译器；加载器只读类型化数据，不重新解析源码。
    bool CompileDocument(const String& html, const String& source, FileInterface* files, Vector<byte>& output,
                         Vector<Diagnostic>& diagnostics, bool fragment = false);
    bool ReadCompiledDocument(const byte* data, size_t size, CompiledDocument& output, String& error,
                              const PropertySpecification* rebind = nullptr);
    SharedPtr<const CompiledDocument> ReadCompiledDocumentCached(const byte* data, size_t size, String& error);
    void ClearCompiledDocumentCache();
    bool CompileTemplate(const String& html, const String& source, const byte* parent, size_t parent_size,
                         Vector<byte>& output, Vector<Diagnostic>& diagnostics);
    bool BuildCompiledDocument(ElementDocument& target, CompiledDocument&& compiled, Vector<Diagnostic>& diagnostics);
    bool BuildCompiledDocument(ElementDocument& target, const CompiledDocument& compiled,
                               Vector<Diagnostic>& diagnostics);

    // 文档私有的不可变片段计划；实例化仍创建独立 DOM，不持有页面投影。
    class CompiledTemplateCache
    {
        struct Entry
        {
            Vector<byte> encoded;
            SharedPtr<const CompiledDocument> plan;
        };
        std::list<Entry> entries;
        const PropertySpecification* specification = nullptr;
        uint64_t revision = 0;
        size_t encodedBytes = 0;

    public:
        SharedPtr<const CompiledDocument> Read(const byte* data, size_t size, const PropertySpecification& owner,
                                               String& error);
        void Clear();
    };
}
