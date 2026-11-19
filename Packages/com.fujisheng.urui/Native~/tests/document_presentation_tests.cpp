#include "urui.h"
#include <URUI/Core.h>
#include <cassert>
#include <iostream>

static void InitialModalBlocksFocusBeforeFirstSurfaceUpdate()
{
    assert(urui_create(400, 300) == 1);
    URUIDocumentHandle home = 0;
    URUIDocumentHandle modal = 0;
    assert(urui_document_create(&home) == 1 && urui_document_create(&modal) == 1);
    const char* page = "<!doctype html><html><head></head><body><input id='focus' autofocus></body></html>";
    assert(urui_document_set_presentation(home, 0, 1, 1, 0) == 1);
    assert(urui_document_load_html(home, page, "/initial-home.html") == 1);
    assert(urui_document_set_presentation(modal, 8000, 2, 1, 1) == 1);
    assert(urui_document_load_html(modal, page, "/initial-modal.html") == 1);
    const auto field = urui_element_find(home, urui_document_root(home), "focus", nullptr);
    assert(urui_element_focus(home, field, 1) == 0);
    assert(urui_element_focus(home, urui_document_root(home), 1) == 0);
    assert(urui_surface_focused_document() == modal);
    urui_destroy();
}

int main()
{
    InitialModalBlocksFocusBeforeFirstSurfaceUpdate();
    assert(urui_create(400, 200) == 1);
    URUIDocumentHandle a = 0;
    URUIDocumentHandle b = 0;
    URUIDocumentHandle c = 0;
    assert(urui_document_create(&a) == 1 && urui_document_create(&b) == 1 && urui_document_create(&c) == 1);
    const char* htmlA =
        "<!doctype html><html><head><style>html{z-index:1000;} body{margin:0;}div{position:absolute;left:0;top:0;width:100px;height:50px;}</style></head><body><div id='same'></div></body></html>";
    const char* htmlB =
        "<!doctype html><html><head><style>html{z-index:-1000;} body{margin:0;}div{position:absolute;left:0;top:0;width:100px;height:50px;}</style></head><body><div id='same'></div></body></html>";
    assert(urui_document_load_html(a, htmlA, "ui/a.html") == 1);
    assert(urui_document_load_html(b, htmlB, "ui/b.html") == 1);
    auto* context = URUI::GetContext("urui");
    auto* docA = context->GetDocument(0);
    auto* docB = context->GetDocument(1);
    assert(urui_document_set_presentation(a, 0, 0, 1, 0) == 1);
    assert(urui_document_set_presentation(b, 0, 1, 1, 0) == 1);
    urui_update();
    assert(context->GetElementAtPoint({10, 10})->GetOwnerDocument() == docB);
    assert(docA->GetProperty("z-index")->Get<float>() == 1000 && docB->GetProperty("z-index")->Get<float>() == -1000);
    assert(urui_document_set_presentation(a, 1, -100, 1, 0) == 1);
    urui_update();
    assert(context->GetElementAtPoint({10, 10})->GetOwnerDocument() == docA);
    assert(urui_document_set_presentation(a, 1, -100, 0, 0) == 1);
    urui_update();
    assert(!docA->IsVisible(true));
    assert(!docA->Focus());
    assert(context->GetElementAtPoint({10, 10})->GetOwnerDocument() == docB);
    const char* hidden = "<!doctype html><html style='visibility:hidden'><head></head><body><div></div></body></html>";
    assert(urui_document_load_html(c, hidden, "ui/hidden.html") == 1);
    auto* docC = context->GetDocument(2);
    assert(urui_document_set_presentation(c, 10, 0, 1, 0) == 1);
    urui_update();
    assert(docC->GetComputedValues().visibility() == URUI::Style::Visibility::Hidden && !docC->IsVisible(true));
    assert(!docC->Focus());
    assert(urui_document_set_presentation(c, 10, 0, 0, 0) == 1);
    urui_update();
    assert(docC->GetProperty("visibility")->Get<URUI::Style::Visibility>() == URUI::Style::Visibility::Hidden);
    assert(docB->Focus());
    auto detached = docB->CreateElement("div");
    assert(!detached->Focus());
    detached.reset();
    URUIDocumentHandle modal = 0;
    URUIDocumentHandle overlay = 0;
    assert(urui_document_create(&modal) == 1 && urui_document_create(&overlay) == 1);
    assert(urui_document_set_presentation(modal, 20, 0, 1, 1) == 1);
    const char* dialog =
        "<!doctype html><html><head><style>body{margin:0;}div{position:absolute;left:200px;top:0;width:20px;height:20px;}</style></head><body><div></div></body></html>";
    assert(urui_document_load_html(modal, dialog, "ui/modal.html") == 1);
    urui_update();
    auto* focused = context->GetFocusElement();
    auto* dialogDoc = focused ? focused->GetOwnerDocument() : nullptr;
    assert(dialogDoc && dialogDoc->GetSourceURL() == "ui/modal.html");
    auto* blocked = context->GetElementAtPoint({10, 10});
    assert(!blocked || blocked->GetOwnerDocument() != docB);
    assert(!docB->Focus());
    assert(urui_document_set_presentation(overlay, 30, 0, 1, 0) == 1);
    assert(urui_document_load_html(overlay, htmlB, "ui/overlay.html") == 1);
    urui_update();
    auto* above = context->GetElementAtPoint({10, 10});
    auto* aboveDoc = above ? above->GetOwnerDocument() : nullptr;
    assert(aboveDoc && aboveDoc->GetSourceURL() == "ui/overlay.html");
    assert(aboveDoc->Focus());
    assert(urui_document_close(overlay) == 1);
    urui_update();
    assert(urui_document_close(modal) == 1);
    urui_update();
    assert(context->GetFocusElement()->GetOwnerDocument() == docB);
    urui_destroy();
    assert(urui_create(400, 200) == 1);
    URUIDocumentHandle single = 0;
    assert(urui_document_create(&single) == 1);
    assert(urui_document_load_html(
               single, "<!doctype html><html><head></head><body><input id='editor' value='before'></body></html>",
               "ui/single.html") == 1);
    assert(urui_document_set_presentation(single, 0, 0, 1, 0) == 1);
    urui_update();
    const auto editor = urui_element_find(single, urui_document_root(single), "editor", nullptr);
    assert(editor && urui_element_focus(single, editor, 1) == 1);
    assert(urui_surface_focused_document() == single);
    assert(urui_document_set_presentation(single, 0, 0, 0, 0) == 1);
    assert(urui_surface_focused_document() != single);
    char value[64];
    int required = 0;
    assert(urui_text_input("X") >= 0);
    assert(urui_control_value_read(single, editor, value, sizeof(value), &required) == 1);
    assert(std::string(value) == "before");
    assert(urui_key_event(127, 1, 0) >= 0);
    assert(urui_control_value_read(single, editor, value, sizeof(value), &required) == 1 &&
           std::string(value) == "before");
    assert(urui_document_set_presentation(single, 0, 0, 1, 0) == 1 && urui_element_focus(single, editor, 1) == 1);
    URUIDocumentHandle unfocusableModal = 0;
    assert(urui_document_create(&unfocusableModal) == 1);
    assert(urui_document_load_html(unfocusableModal, "<!doctype html><html><head></head><body></body></html>",
                                   "ui/unfocusable-modal.html") == 1);
    auto* modalRoot = URUI::GetContext("urui")->GetDocument(1);
    modalRoot->SetProperty(URUI::PropertyId::Focus, URUI::Property(int(URUI::Style::Focus::None), URUI::Unit::KEYWORD));
    assert(urui_document_set_presentation(unfocusableModal, 1, 0, 1, 1) == 1);
    assert(urui_text_input("X") >= 0);
    assert(urui_control_value_read(single, editor, value, sizeof(value), &required) == 1 &&
           std::string(value) == "before");
    assert(urui_key_event(127, 1, 0) >= 0);
    assert(urui_control_value_read(single, editor, value, sizeof(value), &required) == 1 &&
           std::string(value) == "before");
    urui_destroy();
    std::cout << "Document presentation order and author visibility contracts passed\n";
}
