#include "urui.h"
#include <URUI/Core.h>
#include <cassert>
#include <thread>
#include <iostream>
#include <stdexcept>

class FailingUpdateElement : public URUI::Element
{
public:
    explicit FailingUpdateElement(const URUI::String& tag) : URUI::Element(tag)
    {
    }
    void OnUpdate() override
    {
        throw std::runtime_error("candidate update failure");
    }
};

int main()
{
    assert(urui_create(400, 200) == 1);
    URUIDocumentHandle a = 0;
    URUIDocumentHandle b = 0;
    assert(urui_document_create(&a) == 1 && a);
    assert(urui_document_create(&b) == 1 && b && a != b);
    assert(urui_document_root(a) == 0 && urui_document_body(a) == 0);
    const char* html = "<!doctype html><html><head></head><body><div id='same'></div></body></html>";
    assert(urui_document_load_html(a, html, "ui/a.html") == 1);
    assert(urui_document_load_html(b, html, "ui/b.html") == 1);
    auto* context = URUI::GetContext("urui");
    assert(context->GetNumDocuments() == 2);
    assert(context->GetDocument(0)->GetSourceURL() == "ui/a.html");
    assert(context->GetDocument(1)->GetSourceURL() == "ui/b.html");
    const auto rootA = urui_document_root(a);
    const auto rootB = urui_document_root(b);
    const auto bodyA = urui_document_body(a);
    const auto bodyB = urui_document_body(b);
    assert(rootA && rootB && rootA != rootB && bodyA && bodyB && bodyA != bodyB);
    const auto sameA = urui_element_find(a, rootA, "same", nullptr);
    const auto sameB = urui_element_find(b, rootB, "same", nullptr);
    assert(sameA && sameB && sameA != sameB);
    assert(urui_surface_element_owner(sameA) == a && urui_surface_element_owner(sameB) == b);
    assert(urui_set_attribute(a, "same", "data-page", "A") == 1);
    assert(urui_set_attribute(b, "same", "data-page", "B") == 1);
    char text[16];
    int required = 0;
    assert(urui_get_attribute_utf8(a, "same", "data-page", text, sizeof(text), &required) == 1 &&
           std::string(text) == "A");
    assert(urui_get_attribute_utf8(b, "same", "data-page", text, sizeof(text), &required) == 1 &&
           std::string(text) == "B");
    assert(urui_set_style(a, "same", "width", "12px") == 1);
    assert(urui_set_style(b, "same", "width", "34px") == 1);
    urui_update();
    float boxA[6];
    float boxB[6];
    assert(urui_get_element_box(a, "same", boxA) == 1 && boxA[2] == 12);
    assert(urui_get_element_box(b, "same", boxB) == 1 && boxB[2] == 34);
    const auto createdA = urui_element_create(a, "div");
    const auto createdB = urui_element_create(b, "div");
    assert(createdA && createdB);
    assert(urui_element_ref_release(a, sameB) == -1 && urui_element_ref_valid(b, sameB) == 1);
    assert(urui_element_insert(a, bodyA, createdB, -1) == -1);
    assert(urui_element_number(a, createdB, "width", 25, 1) == -1);
    assert(urui_element_insert(a, bodyA, createdA, -1) == 1);
    assert(urui_element_number(a, createdA, "width", 25, 1) == 1);
    assert(urui_element_child_count(b, bodyB) == 1);
    assert(urui_element_set_inner_html(a, bodyB, "<p>foreign</p>", "ui/fragment.html") == -1);
    assert(urui_element_set_inner_html(a, bodyA, "<p>invalid", "ui/fragment.html") == -9);
    assert(context->GetDocument(0)->GetElementById("same"));
    assert(urui_document_load_html(a, "<!doctype html><html><head></head><body><div/></body></html>", "ui/bad.html") ==
           -9);
    assert(urui_document_root(a) == rootA && urui_document_body(a) == bodyA);
    URUI::ElementInstancerGeneric<FailingUpdateElement> failingInstancer;
    URUI::Factory::RegisterElementInstancer("urui-fail-update", &failingInstancer);
    assert(urui_document_load_html(
               a, "<!doctype html><html><head></head><body><urui-fail-update></urui-fail-update></body></html>",
               "ui/failing-update.html") == -8);
    assert(urui_document_root(a) == rootA && context->GetNumDocuments() == 2);
    assert(urui_element_begin_inner_html(a, bodyA, "<p id='candidate'>pending</p>", "ui/fragment.html") == 1);
    const auto candidate = urui_element_find(a, bodyA, "candidate", nullptr);
    assert(candidate && !urui_element_find(a, bodyA, "same", nullptr));
    assert(urui_element_begin_inner_html(a, bodyA, "", "ui/nested.html") == -5);
    assert(urui_document_load_html(a, html, "ui/reload.html") == -5);
    assert(urui_element_end_inner_html(b, bodyB, 0) == -2);
    assert(urui_element_end_inner_html(a, bodyA, 2) == -1);
    assert(urui_element_end_inner_html(a, bodyA, 0) == 1);
    assert(urui_element_ref_valid(a, candidate) == 0);
    assert(urui_element_find(a, bodyA, "same", nullptr) == sameA);
    assert(urui_element_ref_valid(a, createdA) == 1 && urui_element_ref_valid(b, sameB) == 1);
    assert(urui_element_end_inner_html(a, bodyA, 1) == -2);
    assert(urui_element_set_inner_html(a, bodyA, "<p id='new'>ok</p>", "ui/fragment.html") == 1);
    assert(context->GetDocument(0)->GetElementById("new"));
    int foreign = 0;
    std::thread thread([&] { foreign = urui_document_close(a); });
    thread.join();
    assert(foreign == -3 && urui_document_root(a) == rootA);
    assert(urui_document_close(a) == 1);
    assert(urui_surface_element_owner(sameA) == 0 && urui_surface_element_owner(sameB) == b);
    assert(urui_document_root(a) == 0 && urui_document_close(a) == -2);
    assert(urui_document_root(b) == rootB && urui_document_body(b) == bodyB);
    assert(urui_element_set_inner_html(a, bodyA, "<div></div>", "ui/fragment.html") == -2);
    assert(urui_document_load_html(b, html, "ui/reloaded.html") == 1);
    assert(urui_document_root(b) != rootB);
    assert(urui_element_set_inner_html(b, bodyB, "<div></div>", "ui/fragment.html") == -2);
    URUIDocumentHandle special = 0;
    assert(urui_document_create(&special) == 1);
    assert(urui_document_load_html(
               special,
               "<!doctype html><html><head></head><body>"
               "<select id='select'><option id='option' value='old' selected>Old</option></select>"
               "<textarea id='area'>Old</textarea><template id='template'><b>Old</b></template></body></html>",
               "ui/special.html") == 1);
    auto* special_document = context->GetDocument(context->GetNumDocuments() - 1);
    const auto option = urui_element_find(special, urui_document_root(special), "option", nullptr);
    const char* targets[] = {"select", "area", "template"};
    const char* replacements[] = {"<option value='new'>New</option>", "New", "<i>New</i>"};
    for (int i = 0; i < 3; ++i)
    {
        auto* element = special_document->GetElementById(targets[i]);
        const auto token = urui_element_find(special, urui_document_root(special), targets[i], nullptr);
        const auto before = element->GetInnerHTML();
        assert(urui_element_begin_inner_html(special, token, replacements[i], "ui/special-fragment.html") == 1);
        assert(element->GetInnerHTML() != before);
        assert(urui_element_end_inner_html(special, token, 0) == 1);
        assert(element->GetInnerHTML() == before);
    }
    assert(urui_element_find(special, urui_document_root(special), "option", nullptr) == option);
    assert(urui_element_begin_inner_html(special, urui_document_body(special), "<div></div>", "ui/close.html") == 1);
    assert(urui_document_close(special) == 1);
    assert(urui_surface_element_owner(option) == 0);
    urui_destroy();
    assert(urui_create(400, 200) == 1);
    URUIDocumentHandle c = 0;
    assert(urui_document_create(&c) == 1 && c != a && c != b);
    assert(urui_document_root(b) == 0);
    urui_destroy();
    std::cout << "Explicit document lifecycle API contracts passed\n";
}
