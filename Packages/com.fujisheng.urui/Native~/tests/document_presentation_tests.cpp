#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include <cassert>
#include <iostream>

static void InitialModalBlocksFocusBeforeFirstSurfaceUpdate()
{
    assert(urui_surface_create(400, 300, &g_test_surface) == 1);
    URUIDocumentHandle home = 0;
    URUIDocumentHandle modal = 0;
    assert(urui_document_create(g_test_surface, &home) == 1 && urui_document_create(g_test_surface, &modal) == 1);
    const char* page = "<!doctype html><html><head></head><body><input id='focus' autofocus></body></html>";
    assert(urui_document_set_presentation(g_test_surface, home, 0, 1, 1, 0) == 1);
    assert(urui_document_load_html(g_test_surface, home, page, "/initial-home.html") == 1);
    assert(urui_document_set_presentation(g_test_surface, modal, 8000, 2, 1, 1) == 1);
    assert(urui_document_load_html(g_test_surface, modal, page, "/initial-modal.html") == 1);
    const auto field = urui_element_find(g_test_surface, home, urui_document_root(g_test_surface, home), "focus", nullptr);
    assert(urui_element_focus(g_test_surface, home, field, 1) == 0);
    assert(urui_element_focus(g_test_surface, home, urui_document_root(g_test_surface, home), 1) == 0);
    assert(urui_surface_focused_document(g_test_surface) == modal);
    urui_surface_destroy(g_test_surface);
}

static void NonModalDocumentShellDoesNotShadowLowerAuthorHits()
{
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    URUIDocumentHandle lower = 0;
    URUIDocumentHandle upperOverlay = 0;
    assert(urui_document_create(g_test_surface, &lower) == 1 &&
           urui_document_create(g_test_surface, &upperOverlay) == 1);
    const char* lowerHtml =
        "<!doctype html><html><head><style>html,body{margin:0;width:100%;height:100%;}"
        "button{position:absolute;left:20px;top:20px;width:60px;height:40px;}</style></head>"
        "<body><button id='confirm'></button></body></html>";
    const char* tipsHtml =
        "<!doctype html><html><head><style>html,body{margin:0;width:100%;height:100%;}"
        "main,main *{pointer-events:none;}</style></head>"
        "<body><main><div></div></main></body></html>";
    assert(urui_document_set_presentation(g_test_surface, lower, 0, 1, 1, 0) == 1);
    assert(urui_document_load_html(g_test_surface, lower, lowerHtml, "/lower.html") == 1);
    assert(urui_document_set_presentation(g_test_surface, upperOverlay, 16000, 2, 1, 0) == 1);
    assert(urui_document_load_html(g_test_surface, upperOverlay, tipsHtml, "/tips.html") == 1);
    urui_surface_update(g_test_surface);

    const auto confirmBtn = urui_element_find(g_test_surface, lower, urui_document_root(g_test_surface, lower), "confirm", nullptr);
    const auto upperBody = urui_document_body(g_test_surface, upperOverlay);
    assert(confirmBtn && upperBody);

    URUIDocumentHandle owner = 0;
    const auto buttonHit = urui_surface_pick(g_test_surface, 40, 35, &owner);
    assert(owner == lower && buttonHit == confirmBtn);
    assert(urui_element_pick(g_test_surface, lower, 40, 35) == confirmBtn);

    const auto blankHit = urui_surface_pick(g_test_surface, 300, 150, &owner);
    assert(owner == upperOverlay && blankHit == upperBody);

    // Modal document between lower and non-modal overlay still blocks lower buttons, while its own controls work.
    URUIDocumentHandle middleModal = 0;
    assert(urui_document_create(g_test_surface, &middleModal) == 1);
    const char* modalHtml =
        "<!doctype html><html><head><style>html,body{margin:0;width:100%;height:100%;}"
        "button{position:absolute;left:150px;top:20px;width:60px;height:40px;}</style></head>"
        "<body><button id='dialog-btn'></button></body></html>";
    assert(urui_document_set_presentation(g_test_surface, middleModal, 8000, 3, 1, 1) == 1);
    assert(urui_document_load_html(g_test_surface, middleModal, modalHtml, "/middle-modal.html") == 1);
    urui_surface_update(g_test_surface);

    const auto dialogBtn = urui_element_find(g_test_surface, middleModal, urui_document_root(g_test_surface, middleModal), "dialog-btn", nullptr);
    const auto modalBody = urui_document_body(g_test_surface, middleModal);
    assert(dialogBtn && modalBody);

    const auto modalBtnHit = urui_surface_pick(g_test_surface, 170, 35, &owner);
    assert(owner == middleModal && modalBtnHit == dialogBtn);
    const auto blockedLowerHit = urui_surface_pick(g_test_surface, 40, 35, &owner);
    assert(owner == middleModal && blockedLowerHit == modalBody);
    assert(urui_document_close(g_test_surface, middleModal) == 1);

    // Explicit author hit surface on a non-modal upper document still blocks lower documents.
    URUIDocumentHandle authorBlocker = 0;
    assert(urui_document_create(g_test_surface, &authorBlocker) == 1);
    const char* blockerHtml =
        "<!doctype html><html><head><style>html,body{margin:0;width:100%;height:100%;}"
        "#cover{position:absolute;left:0;top:0;width:100%;height:100%;}</style></head>"
        "<body><div id='cover'></div></body></html>";
    assert(urui_document_set_presentation(g_test_surface, authorBlocker, 10000, 4, 1, 0) == 1);
    assert(urui_document_load_html(g_test_surface, authorBlocker, blockerHtml, "/blocker.html") == 1);
    urui_surface_update(g_test_surface);
    const auto cover = urui_element_find(g_test_surface, authorBlocker, urui_document_root(g_test_surface, authorBlocker), "cover", nullptr);
    assert(cover);
    const auto coverHit = urui_surface_pick(g_test_surface, 40, 35, &owner);
    assert(owner == authorBlocker && coverHit == cover);

    urui_surface_destroy(g_test_surface);
}

int main()
{
    InitialModalBlocksFocusBeforeFirstSurfaceUpdate();
    NonModalDocumentShellDoesNotShadowLowerAuthorHits();
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    URUIDocumentHandle a = 0;
    URUIDocumentHandle b = 0;
    URUIDocumentHandle c = 0;
    assert(urui_document_create(g_test_surface, &a) == 1 && urui_document_create(g_test_surface, &b) == 1 && urui_document_create(g_test_surface, &c) == 1);
    const char* htmlA =
        "<!doctype html><html><head><style>html{z-index:1000;} body{margin:0;}div{position:absolute;left:0;top:0;width:100px;height:50px;}</style></head><body><div id='same'></div></body></html>";
    const char* htmlB =
        "<!doctype html><html><head><style>html{z-index:-1000;} body{margin:0;}div{position:absolute;left:0;top:0;width:100px;height:50px;}</style></head><body><div id='same'></div></body></html>";
    assert(urui_document_load_html(g_test_surface, a, htmlA, "ui/a.html") == 1);
    assert(urui_document_load_html(g_test_surface, b, htmlB, "ui/b.html") == 1);
    auto* context = URUI::GetContext(0);
    auto* docA = context->GetDocument(0);
    auto* docB = context->GetDocument(1);
    assert(urui_document_set_presentation(g_test_surface, a, 0, 0, 1, 0) == 1);
    assert(urui_document_set_presentation(g_test_surface, b, 0, 1, 1, 0) == 1);
    urui_surface_update(g_test_surface);
    assert(context->GetElementAtPoint({10, 10})->GetOwnerDocument() == docB);
    assert(docA->GetProperty("z-index")->Get<float>() == 1000 && docB->GetProperty("z-index")->Get<float>() == -1000);
    assert(urui_document_set_presentation(g_test_surface, a, 1, -100, 1, 0) == 1);
    urui_surface_update(g_test_surface);
    assert(context->GetElementAtPoint({10, 10})->GetOwnerDocument() == docA);
    assert(urui_document_set_presentation(g_test_surface, a, 1, -100, 0, 0) == 1);
    urui_surface_update(g_test_surface);
    assert(!docA->IsVisible(true));
    assert(!docA->Focus());
    assert(context->GetElementAtPoint({10, 10})->GetOwnerDocument() == docB);
    const char* hidden = "<!doctype html><html style='visibility:hidden'><head></head><body><div></div></body></html>";
    assert(urui_document_load_html(g_test_surface, c, hidden, "ui/hidden.html") == 1);
    auto* docC = context->GetDocument(2);
    assert(urui_document_set_presentation(g_test_surface, c, 10, 0, 1, 0) == 1);
    urui_surface_update(g_test_surface);
    assert(docC->GetComputedValues().visibility() == URUI::Style::Visibility::Hidden && !docC->IsVisible(true));
    assert(!docC->Focus());
    assert(urui_document_set_presentation(g_test_surface, c, 10, 0, 0, 0) == 1);
    urui_surface_update(g_test_surface);
    assert(docC->GetProperty("visibility")->Get<URUI::Style::Visibility>() == URUI::Style::Visibility::Hidden);
    assert(docB->Focus());
    auto detached = docB->CreateElement("div");
    assert(!detached->Focus());
    detached.reset();
    URUIDocumentHandle modal = 0;
    URUIDocumentHandle overlay = 0;
    assert(urui_document_create(g_test_surface, &modal) == 1 && urui_document_create(g_test_surface, &overlay) == 1);
    assert(urui_document_set_presentation(g_test_surface, modal, 20, 0, 1, 1) == 1);
    const char* dialog =
        "<!doctype html><html><head><style>body{margin:0;}div{position:absolute;left:200px;top:0;width:20px;height:20px;}</style></head><body><div></div></body></html>";
    assert(urui_document_load_html(g_test_surface, modal, dialog, "ui/modal.html") == 1);
    urui_surface_update(g_test_surface);
    auto* focused = context->GetFocusElement();
    auto* dialogDoc = focused ? focused->GetOwnerDocument() : nullptr;
    assert(dialogDoc && dialogDoc->GetSourceURL() == "ui/modal.html");
    auto* blocked = context->GetElementAtPoint({10, 10});
    assert(!blocked || blocked->GetOwnerDocument() != docB);
    assert(!docB->Focus());
    assert(urui_document_set_presentation(g_test_surface, overlay, 30, 0, 1, 0) == 1);
    assert(urui_document_load_html(g_test_surface, overlay, htmlB, "ui/overlay.html") == 1);
    urui_surface_update(g_test_surface);
    auto* above = context->GetElementAtPoint({10, 10});
    auto* aboveDoc = above ? above->GetOwnerDocument() : nullptr;
    assert(aboveDoc && aboveDoc->GetSourceURL() == "ui/overlay.html");
    assert(aboveDoc->Focus());
    assert(urui_document_close(g_test_surface, overlay) == 1);
    urui_surface_update(g_test_surface);
    assert(urui_document_close(g_test_surface, modal) == 1);
    urui_surface_update(g_test_surface);
    assert(context->GetFocusElement()->GetOwnerDocument() == docB);
    urui_surface_destroy(g_test_surface);
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    URUIDocumentHandle single = 0;
    assert(urui_document_create(g_test_surface, &single) == 1);
    assert(urui_document_load_html(g_test_surface, 
               single, "<!doctype html><html><head></head><body><input id='editor' value='before'></body></html>",
               "ui/single.html") == 1);
    assert(urui_document_set_presentation(g_test_surface, single, 0, 0, 1, 0) == 1);
    urui_surface_update(g_test_surface);
    const auto editor = urui_element_find(g_test_surface, single, urui_document_root(g_test_surface, single), "editor", nullptr);
    assert(editor && urui_element_focus(g_test_surface, single, editor, 1) == 1);
    assert(urui_surface_focused_document(g_test_surface) == single);
    assert(urui_document_set_presentation(g_test_surface, single, 0, 0, 0, 0) == 1);
    assert(urui_surface_focused_document(g_test_surface) != single);
    char value[64];
    int required = 0;
    assert(urui_text_input(g_test_surface, "X") >= 0);
    assert(urui_control_value_read(g_test_surface, single, editor, value, sizeof(value), &required) == 1);
    assert(std::string(value) == "before");
    assert(urui_key_event(g_test_surface, 127, 1, 0) >= 0);
    assert(urui_control_value_read(g_test_surface, single, editor, value, sizeof(value), &required) == 1 &&
           std::string(value) == "before");
    assert(urui_document_set_presentation(g_test_surface, single, 0, 0, 1, 0) == 1 && urui_element_focus(g_test_surface, single, editor, 1) == 1);
    URUIDocumentHandle unfocusableModal = 0;
    assert(urui_document_create(g_test_surface, &unfocusableModal) == 1);
    assert(urui_document_load_html(g_test_surface, unfocusableModal, "<!doctype html><html><head></head><body></body></html>",
                                   "ui/unfocusable-modal.html") == 1);
    auto* modalRoot = URUI::GetContext(0)->GetDocument(1);
    modalRoot->SetProperty(URUI::PropertyId::Focus, URUI::Property(int(URUI::Style::Focus::None), URUI::Unit::KEYWORD));
    assert(urui_document_set_presentation(g_test_surface, unfocusableModal, 1, 0, 1, 1) == 1);
    assert(urui_text_input(g_test_surface, "X") >= 0);
    assert(urui_control_value_read(g_test_surface, single, editor, value, sizeof(value), &required) == 1 &&
           std::string(value) == "before");
    assert(urui_key_event(g_test_surface, 127, 1, 0) >= 0);
    assert(urui_control_value_read(g_test_surface, single, editor, value, sizeof(value), &required) == 1 &&
           std::string(value) == "before");
    urui_surface_destroy(g_test_surface);
    std::cout << "Document presentation order and author visibility contracts passed\n";
}
