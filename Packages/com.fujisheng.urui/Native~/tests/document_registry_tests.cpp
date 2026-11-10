#include "urui.h"
#include "element_references.h"
#include <URUI/Core.h>
#include <cassert>
#include <iostream>
#include <thread>
#if __has_include("document_registry.h")
#include "document_registry.h"
#define URUI_HAS_DOCUMENT_REGISTRY 1
#endif

#if defined(URUI_HAS_DOCUMENT_REGISTRY)
static URUI::ElementDocument* MakeDocument(URUI::Context& context)
{
    auto* document = context.CreateDocument();
    assert(document);
    auto child = document->CreateElement("div");
    child->SetId("same");
    document->AppendChild(std::move(child));
    return document;
}

static void RegistryContracts(URUI::Context& context)
{
    ElementReferences references;
    references.Begin();
    URUI::Bridge::DocumentRegistry registry(context, references);
    uint64_t a = 0, b = 0;
    assert(registry.Create(&a) == 1 && registry.Create(&b) == 1 && a != b);
    assert(registry.Find(a) && registry.Find(a)->Element() == nullptr);
    auto* documentA = MakeDocument(context);
    auto* documentB = MakeDocument(context);
    assert(registry.Publish(a, documentA) == 1);
    assert(registry.Publish(b, documentB) == 1);
    auto tokenA = references.Acquire(documentA, "same");
    auto tokenB = references.Acquire(documentB, "same");
    assert(tokenA && tokenB && tokenA != tokenB);
    assert(registry.OwnerOf(tokenA) == a && registry.OwnerOf(tokenB) == b);
    auto detached = references.Create(documentA, "div");
    assert(detached && references.Resolve(detached, documentB) == nullptr);
    assert(registry.Close(a) == 1);
    assert(registry.Find(a) == nullptr && registry.OwnerOf(tokenA) == 0);
    assert(references.Resolve(detached, documentA) == nullptr);
    assert(references.Resolve(tokenB, documentB) != nullptr);
    assert(registry.Close(a) == -2);
    uint64_t reopened = 0;
    assert(registry.Create(&reopened) == 1 && reopened != a && reopened != b);
    int foreignResult = 0;
    std::thread foreign([&] { uint64_t handle = 0; foreignResult = registry.Create(&handle); });
    foreign.join();
    assert(foreignResult == -3);
    assert(registry.Close(b) == 1 && registry.Close(reopened) == 1);
    assert(references.Count() == 0);
}
#endif

int main()
{
    assert(urui_create(1280, 720) == 1);
    auto* context = URUI::GetContext("urui");
    assert(context);
    std::thread foreign_destroy([] { urui_destroy(); });
    foreign_destroy.join();
    UruiDocumentHandle after_foreign_destroy = 0;
    assert(urui_document_create(&after_foreign_destroy) == 1);
    assert(urui_document_close(after_foreign_destroy) == 1);
    assert(URUI::GetContext("urui") == context);
    {
        auto* a = context->CreateDocument();
        auto* b = context->CreateDocument();
        assert(a && b);
        ElementReferences references;
        references.Begin();
        references.DocumentLoaded();
        const auto detached = references.Create(a, "div");
        assert(detached);
        // 脱离树仍属于创建它的文档，不能因为共用注册表而被另一文档接管。
        assert(references.Resolve(detached, b) == nullptr);
        assert(references.Insert(b, references.Resolve(detached, a), -1, b) == -1);
        assert(references.Resolve(detached, a) != nullptr);
        references.Clear();
        a->Close();
        b->Close();
    }
#if defined(URUI_HAS_DOCUMENT_REGISTRY)
    RegistryContracts(*context);
#else
    urui_destroy();
    std::cerr << "FAIL: per-document registry is not implemented\n";
    return 1;
#endif
    urui_destroy();
    std::cout << "PASS: detached nodes remain document scoped\n";
}
