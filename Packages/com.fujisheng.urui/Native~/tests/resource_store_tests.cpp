#include "urui.h"
#include "resource_store.h"
#include "Engine/Source/Core/HtmlParser.h"
#include <URUI/Core.h>
#include <cassert>
#include <cstring>
#include <iostream>

static void Register(URUI::Bridge::DocumentResources& resources, const char* css)
{
    resources.Register("ui/theme.css", reinterpret_cast<const uint8_t*>(css), std::strlen(css));
}
int main()
{
    assert(urui_create(400, 200) == 1);
    URUI::Bridge::FileResourceCache cache;
    const uint8_t bytes[] = {1, 0, 2, 3};
    auto first = cache.Acquire(1, "ui/shared.bin", bytes, sizeof(bytes));
    auto second = cache.Acquire(2, "ui/shared.bin", bytes, sizeof(bytes));
    assert(first->bytes.get() == second->bytes.get());
    auto third = cache.Acquire(3, "ui/shared.bin", bytes, sizeof(bytes));
    assert(third->bytes == first->bytes);
    bool typeRejected = false;
    try { cache.Acquire(4, "ui/shared.bin", bytes, sizeof(bytes), "image"); }
    catch (const std::invalid_argument&) { typeRejected = true; }
    assert(typeRejected);
    const uint8_t changed[] = {1, 0, 2, 4};
    bool contentRejected = false;
    try { cache.Acquire(4, "ui/shared.bin", changed, sizeof(changed)); }
    catch (const std::invalid_argument&) { contentRejected = true; }
    assert(contentRejected);
    auto a = std::make_shared<URUI::Bridge::DocumentResources>(1, 1, 1, cache, URUI::GetFileInterface());
    auto b = std::make_shared<URUI::Bridge::DocumentResources>(2, 2, 2, cache, URUI::GetFileInterface());
    Register(*a, "#box{width:37px;}");
    bool rejected = false;
    try { Register(*b, "#box{width:91px;}"); } catch (const std::invalid_argument&) { rejected = true; }
    assert(rejected);
    auto candidate = a->Clone(3, 1);
    Register(*candidate, "#box{width:51px;}");
    auto* context = URUI::GetContext("urui");
    const auto parsed = URUI::ParseHtmlDocument("<!doctype html><html><head><link rel='stylesheet' href='theme.css'></head><body><div id='box'></div></body></html>", "ui/a.html");
    assert(parsed);
    URUI::Vector<URUI::Diagnostic> diagnostics;
    auto oldTree = URUI::Factory::InstanceParsedHtmlDocument(context, *parsed.document, diagnostics, a);
    auto newTree = URUI::Factory::InstanceParsedHtmlDocument(context, *parsed.document, diagnostics, candidate);
    assert(oldTree && newTree);
    auto* oldDocument = context->AttachLoadedDocument(std::move(oldTree));
    auto* newDocument = context->AttachLoadedDocument(std::move(newTree));
    assert(oldDocument->GetElementById("box")->GetProperty("width")->Get<float>() == 37);
    assert(newDocument->GetElementById("box")->GetProperty("width")->Get<float>() == 51);
    candidate->Retire();
    Register(*b, "#box{width:37px;}");
    auto stream = a->GetFileInterface()->Open("ui/theme.css"); assert(stream);
    a->Retire();
    char content[64] = {}; assert(a->GetFileInterface()->Read(content, sizeof(content)-1, stream) == 17);
    a->GetFileInterface()->Close(stream);
    assert(std::strcmp(content, "#box{width:37px;}") == 0);
    candidate.reset(); a.reset(); b.reset();
    urui_destroy();
    std::cout << "Explicit file domains, immutable sharing, URI conflict and pending isolation passed\n";
}
