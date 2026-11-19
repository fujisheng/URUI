#include "urui.h"
#include "CompiledDocument.h"
#include "HtmlBuilder.h"
#include <URUI/Core.h>
#include <URUI/Core/PropertySpecification.h>
#include <URUI/Core/StyleSheetContainer.h>
#include <URUI/Core/ElementInstancer.h>
#include <cassert>
#include <iostream>

class ReentrantStyleElement : public URUI::Element
{
    bool setting = false;

public:
    explicit ReentrantStyleElement(const URUI::String& tag) : Element(tag)
    {
    }
    void OnAttributeChange(const URUI::ElementAttributes& attributes) override
    {
        Element::OnAttributeChange(attributes);
        if (!setting && attributes.find("style") != attributes.end())
        {
            setting = true;
            SetAttribute("style", "display:block;width:17px;");
            setting = false;
        }
    }
};

void PoisonSource(URUI::Vector<URUI::HtmlNode>& nodes)
{
    for (auto& node : nodes)
    {
        if (node.tag == "style")
        {
            for (auto& text : node.children)
            {
                text.text = "broken {";
            }
        }
        for (auto& attribute : node.attributes)
        {
            if (attribute.name == "style")
            {
                attribute.value = "color:red;width:999px;";
            }
        }
        PoisonSource(node.children);
    }
}

int main()
{
    assert(urui_create(400, 200) == 1);
    URUI::ElementInstancerGeneric<ReentrantStyleElement> reentrant;
    URUI::Factory::RegisterElementInstancer("urui-reentrant", &reentrant);
    URUI::Vector<URUI::byte> bytes;
    URUI::Vector<URUI::Diagnostic> diagnostics;
    const char* html =
        "<!doctype html><html><head><title>compiled</title><style>"
        "@property --length { syntax: '<length>'; inherits: true; initial-value: 12px; }"
        "body { margin:0; --w:40px; } div {width:var(--w);height:20px;}"
        "#box.a:not(.off){height:30px!important;} #box {height:5px;} #registered{width:var(--length);}"
        "#complex{background-image:linear-gradient(45deg,red,blue);filter:brightness(.5) blur(2px);"
        "box-shadow:1px 2px 3px 4px #123456;--urui-font-effect:outline(1px black);"
        "animation:pulse 2s cubic-bezier(.2,.3,.4,.5);transition:opacity 1s linear;}"
        "@keyframes pulse{from{opacity:.1;}to{opacity:.9;}}"
        "@media (min-width:500px) { #box {width:80px;} }"
        "</style></head><body><div id='box' class='a' style='color:#123456;transform:translate(10px,20%);'></div>"
        "<div id='registered'></div><div id='complex'></div>"
        "<urui-reentrant id='reentrant' style='display:block;width:5px;'></urui-reentrant>"
        "<template id='row'><span style='display:block;width:25px;'>A&amp;B</span></template></body></html>";
    assert(URUI::CompileDocument(html, "ui/compiled.html", nullptr, bytes, diagnostics));
    assert(!bytes.empty());
    URUI::CompiledDocument compiled;
    URUI::String error;
    assert(URUI::ReadCompiledDocument(bytes.data(), bytes.size(), compiled, error));
    assert(compiled.title == "compiled");
    PoisonSource(compiled.html.nodes);
    auto* context = URUI::GetContext("urui");
    auto* first = context->CreateDocument();
    assert(URUI::BuildCompiledDocument(*first, std::move(compiled), diagnostics));
    first->Show();
    context->Update();
    auto* box = first->GetElementById("box");
    assert(box && box->GetBox().GetSize().x == 40 && box->GetBox().GetSize().y == 30);
    assert(first->GetElementById("registered")->GetBox().GetSize().x == 12);
    assert(first->GetElementById("reentrant")->GetBox().GetSize().x == 17);
    assert(box->GetProperty("color")->Get<URUI::Colourb>() == URUI::Colourb(0x12, 0x34, 0x56));
    auto* complex = first->GetElementById("complex");
    assert(complex->GetProperty("background-image")->Get<URUI::DecoratorsPtr>()->list.size() == 1);
    assert(complex->GetProperty("filter")->Get<URUI::FiltersPtr>()->list.size() == 2);
    assert(complex->GetProperty("box-shadow")->Get<URUI::BoxShadowList>().front().blur_radius.number == 3);
    assert(complex->GetProperty("--urui-font-effect")->Get<URUI::FontEffectsPtr>()->list.size() == 1);
    assert(complex->GetProperty("animation")->Get<URUI::AnimationList>().front().duration == 2);
    auto* row = urui_dynamic_cast<URUI::ElementTemplate*>(first->GetElementById("row"));
    assert(row);
    auto cloned = row->CloneContent(diagnostics);
    assert(cloned.size() == 1);
    auto* item = cloned.front().get();
    first->AppendChild(std::move(cloned.front()));
    context->Update();
    assert(item->GetBox().GetSize().x == 25);
    box->SetProperty("--w", "65px");
    context->Update();
    assert(box->GetBox().GetSize().x == 65);
    box->SetClass("off", true);
    context->Update();
    assert(box->GetBox().GetSize().y == 5);
    context->SetDimensions({600, 200});
    context->Update();
    assert(box->GetBox().GetSize().x == 80);
    assert(URUI::ReadCompiledDocument(bytes.data(), bytes.size(), compiled, error));
    auto* second = context->CreateDocument();
    assert(URUI::BuildCompiledDocument(*second, std::move(compiled), diagnostics));
    second->Show();
    context->Update();
    assert(second->GetElementById("box") != box);
    assert(second->GetElementById("box")->GetBox().GetSize().y == 30);
    for (size_t size : {size_t(0), size_t(7), bytes.size() - 1})
    {
        assert(!URUI::ReadCompiledDocument(bytes.data(), size, compiled, error));
        assert(compiled.title == "compiled");
    }
    auto damaged = bytes;
    damaged.back() ^= 1;
    assert(!URUI::ReadCompiledDocument(damaged.data(), damaged.size(), compiled, error));
    int required = 0;
    assert(urui_compile_document(html, "ui/compiled.html", 0, nullptr, 0, nullptr, 0, &required) == -3);
    bytes.resize(required);
    assert(urui_compile_document(html, "ui/compiled.html", 0, nullptr, 0, bytes.data(), int(bytes.size()), &required) ==
           1);
    URUIDocumentHandle owner = 0;
    assert(urui_document_create(&owner) == 1);
    assert(urui_document_load_html(owner, "<!doctype html><html><head></head><body><div id='old'></div></body></html>",
                                   "ui/old.html") == 1);
    const auto oldRoot = urui_document_root(owner);
    assert(urui_document_load_compiled(owner, damaged.data(), int(damaged.size())) < 0);
    assert(urui_document_root(owner) == oldRoot && urui_element_exists(owner, "old") == 1);
    assert(urui_document_load_compiled(owner, bytes.data(), int(bytes.size())) == 1);
    assert(urui_document_root(owner) != oldRoot && urui_element_exists(owner, "box") == 1);
    const char* fragment = "<div id='template-child' style='display:block;width:var(--length);height:10px;'></div>";
    int templateSize = 0;
    assert(urui_compile_template(bytes.data(), int(bytes.size()), fragment, "ui/row.html", nullptr, 0, &templateSize) ==
           -3);
    std::vector<uint8_t> templateBytes(templateSize);
    assert(urui_compile_template(bytes.data(), int(bytes.size()), fragment, "ui/row.html", templateBytes.data(),
                                 templateSize, &templateSize) == 1);
    {
        URUI::CompiledDocument parent;
        assert(URUI::ReadCompiledDocument(bytes.data(), bytes.size(), parent, error));
        URUI::CompiledTemplateCache cache;
        const auto original = cache.Read(templateBytes.data(), templateBytes.size(), *parent.specification, error);
        assert(original &&
               cache.Read(templateBytes.data(), templateBytes.size(), *parent.specification, error) == original);
        auto corrupted = templateBytes;
        corrupted.back() ^= 1;
        assert(!cache.Read(corrupted.data(), corrupted.size(), *parent.specification, error));
        assert(cache.Read(templateBytes.data(), templateBytes.size(), *parent.specification, error) == original);
        parent.specification->NotifyRegisteredPropertiesChanged();
        const auto revised = cache.Read(templateBytes.data(), templateBytes.size(), *parent.specification, error);
        assert(revised && revised != original);
        cache.Clear();
        assert(cache.Read(templateBytes.data(), templateBytes.size(), *parent.specification, error) != revised);
        cache.Clear();
        const auto oldest = cache.Read(templateBytes.data(), templateBytes.size(), *parent.specification, error);
        for (int i = 0; i < 9; ++i)
        {
            URUI::Vector<URUI::byte> next;
            assert(URUI::CompileTemplate("<div id='entry-" + std::to_string(i) + "'></div>", "ui/cache.html",
                                         bytes.data(), bytes.size(), next, diagnostics));
            assert(cache.Read(next.data(), next.size(), *parent.specification, error));
        }
        assert(cache.Read(templateBytes.data(), templateBytes.size(), *parent.specification, error) != oldest);
    }
    const auto body = urui_document_body(owner);
    URUICompiledNodeInfo nodeInfo{};
    assert(urui_element_compiled_info(owner, body, 0, nullptr, &nodeInfo) == 1 && (nodeInfo.flags & 7) == 7);
    std::vector<uint64_t> mapped(nodeInfo.end - nodeInfo.local_id);
    assert(urui_element_compiled_tokens(owner, body, nodeInfo.identity, nodeInfo.revision, mapped.data(),
                                        int(mapped.size())) == 1);
    assert(std::find(mapped.begin(), mapped.end(), body) != mapped.end());
    assert(urui_set_attribute(owner, "box", "class", "a off") == 1);
    assert(urui_element_compiled_tokens(owner, body, nodeInfo.identity, nodeInfo.revision, mapped.data(),
                                        int(mapped.size())) == 0);
    assert(urui_element_compiled_info(owner, body, 0, nullptr, &nodeInfo) == 1 && (nodeInfo.flags & 3) == 3 &&
           !(nodeInfo.flags & 4));
    assert(urui_element_begin_compiled_template(owner, body, templateBytes.data(), templateSize) == 1);
    urui_update();
    float rectangle[6];
    assert(urui_get_element_box(owner, "template-child", rectangle) == 1 && rectangle[2] == 12);
    assert(urui_element_end_inner_html(owner, body, 0) == 1);
    assert(urui_element_exists(owner, "box") == 1 && urui_element_exists(owner, "template-child") == 0);
    assert(urui_element_begin_compiled_template(owner, body, templateBytes.data(), templateSize) == 1);
    assert(urui_element_end_inner_html(owner, body, 1) == 1);
    assert(urui_element_compiled_info(owner, body, 0, nullptr, &nodeInfo) == 1 && (nodeInfo.flags & 15) == 15 &&
           nodeInfo.local_id == UINT32_MAX);
    const auto clonedHost = urui_element_clone(owner, body);
    assert(clonedHost);
    URUICompiledNodeInfo clonedInfo{};
    assert(urui_element_compiled_info(owner, clonedHost, 0, nullptr, &clonedInfo) == 1 &&
           (clonedInfo.flags & 15) == 15);
    std::vector<uint64_t> clonedTokens(clonedInfo.end, 0);
    assert(urui_element_compiled_tokens(owner, clonedHost, clonedInfo.identity, clonedInfo.revision,
                                        clonedTokens.data(), int(clonedTokens.size())) == 1);
    mapped.assign(nodeInfo.end, 0);
    assert(urui_element_compiled_tokens(owner, body, nodeInfo.identity, nodeInfo.revision, mapped.data(),
                                        int(mapped.size())) == 1);
    assert(urui_set_style(owner, "template-child", "--length", "37px") == 1);
    urui_update();
    assert(urui_get_element_box(owner, "template-child", rectangle) == 1 && rectangle[2] == 37);
    assert(urui_element_begin_compiled_template(owner, body, templateBytes.data(), templateSize) == 1);
    assert(urui_element_end_inner_html(owner, body, 1) == 1);
    urui_update();
    assert(urui_get_element_box(owner, "template-child", rectangle) == 1 && rectangle[2] == 12);
    int metadataSize = 0;
    assert(urui_compiled_document_metadata(templateBytes.data(), templateSize, nullptr, 0, &metadataSize) == -3);
    std::vector<char> metadata(metadataSize);
    assert(urui_compiled_document_metadata(templateBytes.data(), templateSize, metadata.data(), metadataSize,
                                           &metadataSize) == 1);
    assert(std::string(metadata.data()).find("\"name\":\"template-child\"") != std::string::npos);
    assert(urui_document_close(owner) == 1);
    assert(!URUI::CompileDocument("<!doctype html><html><head></head><body><div></body></html>", "bad.html", nullptr,
                                  bytes, diagnostics));
    urui_destroy();
    std::cout << "Compiled HTML/CSS dynamic and corruption contracts passed\n";
}
