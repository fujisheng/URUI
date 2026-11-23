#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
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
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    URUIDocumentHandle a = 0;
    URUIDocumentHandle b = 0;
    assert(urui_document_create(g_test_surface, &a) == 1 && a);
    assert(urui_document_create(g_test_surface, &b) == 1 && b && a != b);
    assert(urui_document_root(g_test_surface, a) == 0 && urui_document_body(g_test_surface, a) == 0);
    const char* html = "<!doctype html><html><head></head><body><div id='same'></div></body></html>";
    assert(urui_document_load_html(g_test_surface, a, html, "ui/a.html") == 1);
    assert(urui_document_load_html(g_test_surface, b, html, "ui/b.html") == 1);
    auto* context = URUI::GetContext(0);
    assert(context->GetNumDocuments() == 2);
    assert(context->GetDocument(0)->GetSourceURL() == "ui/a.html");
    assert(context->GetDocument(1)->GetSourceURL() == "ui/b.html");
    const auto rootA = urui_document_root(g_test_surface, a);
    const auto rootB = urui_document_root(g_test_surface, b);
    const auto bodyA = urui_document_body(g_test_surface, a);
    const auto bodyB = urui_document_body(g_test_surface, b);
    assert(rootA && rootB && rootA != rootB && bodyA && bodyB && bodyA != bodyB);
    const auto sameA = urui_element_find(g_test_surface, a, rootA, "same", nullptr);
    const auto sameB = urui_element_find(g_test_surface, b, rootB, "same", nullptr);
    assert(sameA && sameB && sameA != sameB);
    assert(urui_surface_element_owner(g_test_surface, sameA) == a && urui_surface_element_owner(g_test_surface, sameB) == b);
    assert(TestSetAttribute(g_test_surface, a, "same", "data-page", "A") == 1);
    assert(TestSetAttribute(g_test_surface, b, "same", "data-page", "B") == 1);
    char text[16];
    assert(TestGetAttribute(g_test_surface, a, "same", "data-page", text, sizeof(text)) == 1 &&
           std::string(text) == "A");
    assert(TestGetAttribute(g_test_surface, b, "same", "data-page", text, sizeof(text)) == 1 &&
           std::string(text) == "B");
    assert(TestSetStyle(g_test_surface, a, "same", "width", "12px") == 1);
    assert(TestSetStyle(g_test_surface, b, "same", "width", "34px") == 1);
    urui_surface_update(g_test_surface);
    float boxA[6];
    float boxB[6];
    assert(TestGetElementBox(g_test_surface, a, "same", boxA) == 1 && boxA[2] == 12);
    assert(TestGetElementBox(g_test_surface, b, "same", boxB) == 1 && boxB[2] == 34);
    const auto createdA = urui_element_create(g_test_surface, a, "div");
    const auto createdB = urui_element_create(g_test_surface, b, "div");
    assert(createdA && createdB);
    assert(urui_element_ref_release(g_test_surface, a, sameB) == URUI_ERR_INVALID_ARG && urui_element_ref_valid(g_test_surface, b, sameB) == 1);
    assert(urui_element_insert(g_test_surface, a, bodyA, createdB, -1) == URUI_ERR_INVALID_ARG);
    assert(urui_element_number(g_test_surface, a, createdB, "width", 25, 1) == URUI_ERR_NOT_READY);
    assert(urui_element_insert(g_test_surface, a, bodyA, createdA, -1) == 1);
    assert(urui_element_number(g_test_surface, a, createdA, "width", 25, 1) == 1);
    assert(urui_element_child_count(g_test_surface, b, bodyB) == 1);
    assert(urui_element_set_inner_html(g_test_surface, a, bodyB, "<p>foreign</p>", "ui/fragment.html") < 0);
    assert(urui_element_set_inner_html(g_test_surface, a, bodyA, "<p>invalid", "ui/fragment.html") == URUI_ERR_VALIDATION);
    assert(context->GetDocument(0)->GetElementById("same"));
    assert(urui_document_load_html(g_test_surface, a, "<!doctype html><html><head></head><body><div/></body></html>", "ui/bad.html") ==
           URUI_ERR_VALIDATION);
    assert(urui_document_root(g_test_surface, a) == rootA && urui_document_body(g_test_surface, a) == bodyA);
    URUI::ElementInstancerGeneric<FailingUpdateElement> failingInstancer;
    URUI::Factory::RegisterElementInstancer("urui-fail-update", &failingInstancer);
    assert(urui_document_load_html(g_test_surface, 
               a, "<!doctype html><html><head></head><body><urui-fail-update></urui-fail-update></body></html>",
               "ui/failing-update.html") == URUI_ERR_INTERNAL);
    assert(urui_document_root(g_test_surface, a) == rootA && context->GetNumDocuments() == 2);
    assert(urui_element_begin_inner_html(g_test_surface, a, bodyA, "<p id='candidate'>pending</p>", "ui/fragment.html") == 1);
    const auto candidate = urui_element_find(g_test_surface, a, bodyA, "candidate", nullptr);
    assert(candidate && !urui_element_find(g_test_surface, a, bodyA, "same", nullptr));
    assert(urui_element_begin_inner_html(g_test_surface, a, bodyA, "", "ui/nested.html") == URUI_ERR_REENTRANCY);
    assert(urui_document_load_html(g_test_surface, a, html, "ui/reload.html") == URUI_ERR_REENTRANCY);
    assert(urui_element_end_inner_html(g_test_surface, b, bodyB, 0) == URUI_ERR_NOT_READY);
    assert(urui_element_end_inner_html(g_test_surface, a, bodyA, 2) == URUI_ERR_INVALID_ARG);
    assert(urui_element_end_inner_html(g_test_surface, a, bodyA, 0) == 1);
    assert(urui_element_ref_valid(g_test_surface, a, candidate) == 0);
    assert(urui_element_find(g_test_surface, a, bodyA, "same", nullptr) == sameA);
    assert(urui_element_ref_valid(g_test_surface, a, createdA) == 1 && urui_element_ref_valid(g_test_surface, b, sameB) == 1);
    assert(urui_element_end_inner_html(g_test_surface, a, bodyA, 1) == -2);
    assert(urui_element_set_inner_html(g_test_surface, a, bodyA, "<p id='new'>ok</p>", "ui/fragment.html") == 1);
    assert(context->GetDocument(0)->GetElementById("new"));
    int foreign = 0;
    std::thread thread([&] { foreign = urui_document_close(g_test_surface, a); });
    thread.join();
    assert(foreign == -3 && urui_document_root(g_test_surface, a) == rootA);
    assert(urui_document_close(g_test_surface, a) == 1);
    assert(urui_surface_element_owner(g_test_surface, sameA) == 0 && urui_surface_element_owner(g_test_surface, sameB) == b);
    assert(urui_document_root(g_test_surface, a) == 0 && urui_document_close(g_test_surface, a) == -2);
    assert(urui_document_root(g_test_surface, b) == rootB && urui_document_body(g_test_surface, b) == bodyB);
    assert(urui_element_set_inner_html(g_test_surface, a, bodyA, "<div></div>", "ui/fragment.html") == -2);
    assert(urui_document_load_html(g_test_surface, b, html, "ui/reloaded.html") == 1);
    assert(urui_document_root(g_test_surface, b) != rootB);
    assert(urui_element_set_inner_html(g_test_surface, b, bodyB, "<div></div>", "ui/fragment.html") == -2);
    URUIDocumentHandle special = 0;
    assert(urui_document_create(g_test_surface, &special) == 1);
    assert(urui_document_load_html(g_test_surface, 
               special,
               "<!doctype html><html><head></head><body>"
               "<select id='select'><option id='option' value='old' selected>Old</option></select>"
               "<textarea id='area'>Old</textarea><template id='template'><b id='template-item'>Old</b></template>"
               "<div id='target-one'></div><div id='target-two'></div></body></html>",
               "ui/special.html") == 1);
    auto* special_document = context->GetDocument(context->GetNumDocuments() - 1);
    const auto special_root = urui_document_root(g_test_surface, special);
    const auto item_template = urui_element_find(g_test_surface, special, special_root, "template", nullptr);
    const auto first_target = urui_element_find(g_test_surface, special, special_root, "target-one", nullptr);
    const auto second_target = urui_element_find(g_test_surface, special, special_root, "target-two", nullptr);
    assert(item_template && first_target && second_target);
    assert(urui_element_find(g_test_surface, special, special_root, "template-item", nullptr) == 0);
    assert(urui_element_clone_template_content(g_test_surface, b, item_template, urui_document_body(g_test_surface, b)) < 0);
    assert(urui_element_clone_template_content(g_test_surface, special, first_target, second_target) < 0);
    assert(urui_element_clone_template_content(g_test_surface, special, item_template, first_target) == 1);
    assert(urui_element_clone_template_content(g_test_surface, special, item_template, first_target) < 0);
    assert(urui_element_clone_template_content(g_test_surface, special, item_template, second_target) == 1);
    const auto first_item = urui_element_find(g_test_surface, special, first_target, "template-item", nullptr);
    const auto second_item = urui_element_find(g_test_surface, special, second_target, "template-item", nullptr);
    assert(first_item && second_item && first_item != second_item);
    assert(special_document->GetElementById("target-one")->GetNumChildren() == 1);
    assert(special_document->GetElementById("target-two")->GetNumChildren() == 1);
    const auto detached_target = urui_element_create(g_test_surface, special, "div");
    assert(detached_target);
    assert(urui_element_clone_template_content(g_test_surface, special, item_template, detached_target) == 1);
    assert(urui_element_find(g_test_surface, special, detached_target, "template-item", nullptr) != 0);
    const auto option = urui_element_find(g_test_surface, special, urui_document_root(g_test_surface, special), "option", nullptr);
    const char* targets[] = {"select", "area", "template"};
    const char* replacements[] = {"<option value='new'>New</option>", "New", "<i>New</i>"};
    for (int i = 0; i < 3; ++i)
    {
        auto* element = special_document->GetElementById(targets[i]);
        const auto token = urui_element_find(g_test_surface, special, urui_document_root(g_test_surface, special), targets[i], nullptr);
        const auto before = element->GetInnerHTML();
        assert(urui_element_begin_inner_html(g_test_surface, special, token, replacements[i], "ui/special-fragment.html") == 1);
        assert(element->GetInnerHTML() != before);
        assert(urui_element_end_inner_html(g_test_surface, special, token, 0) == 1);
        assert(element->GetInnerHTML() == before);
    }
    assert(urui_element_find(g_test_surface, special, urui_document_root(g_test_surface, special), "option", nullptr) == option);
    assert(urui_element_begin_inner_html(g_test_surface, special, urui_document_body(g_test_surface, special), "<div></div>", "ui/close.html") == 1);
    assert(urui_document_close(g_test_surface, special) == 1);
    assert(urui_surface_element_owner(g_test_surface, option) == 0);
    urui_surface_destroy(g_test_surface);
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    URUIDocumentHandle c = 0;
    assert(urui_document_create(g_test_surface, &c) == 1 && c != a && c != b);
    assert(urui_document_root(g_test_surface, b) == 0);
    urui_surface_destroy(g_test_surface);
    std::cout << "Explicit document lifecycle API contracts passed\n";
}
