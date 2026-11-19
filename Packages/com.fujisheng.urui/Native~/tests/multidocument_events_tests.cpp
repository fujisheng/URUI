#include "test_document.h"
#include <cassert>
#include <map>
#include <iostream>

static std::map<URUIDocumentHandle, int> clicks;
static std::map<URUIDocumentHandle, int> lost;
static std::map<URUIDocumentHandle, int> created;
static std::map<URUIDocumentHandle, int> cancelled;
static std::map<URUIDocumentHandle, int> pressed;
static bool captureOnDown = false;
static bool checkCaptureLost = false;
static int Callback(const URUIElementEvent* event)
{
    assert(event && event->document && event->target && event->current);
    if (event->type == 1)
    {
        ++clicks[event->document];
    }
    if (event->type == 16)
    {
        ++lost[event->document];
    }
    if (event->type == 5)
    {
        ++cancelled[event->document];
    }
    if (event->type == 2)
    {
        ++pressed[event->document];
    }
    if (event->type == 19)
    {
        char name[128];
        uint64_t identity = 0;
        int iterations = 0;
        assert(urui_animation_current_event(event->document, name, sizeof(name), &identity, &iterations) > 0);
        assert(identity && iterations == 1);
        ++created[event->document];
    }
    if (event->type == 2 && captureOnDown)
    {
        assert(urui_pointer_capture(event->document, event->pointer, event->target) == 1);
    }
    if (event->type == 16 && checkCaptureLost)
    {
        assert(urui_pointer_capture(event->document, event->pointer, event->target) < 0);
    }
    return 0;
}
static void Pointer(int phase, float x, int buttons)
{
    assert(urui_pointer_event(0, 0, phase, x, 10, 0, buttons, 0, 0, 0) == 1);
}
int main()
{
    assert(sizeof(URUIElementEvent) == 80);
    assert(urui_create(400, 200) == 1);
    const auto a = LoadTestDocument(
        "<!doctype html><html><head><style>body{margin:0;}div{position:absolute;left:0;top:0;width:80px;height:40px;}</style></head><body><div id='same'></div></body></html>",
        "ui/events-a.html");
    const auto b = LoadTestDocument(
        "<!doctype html><html><head><style>body{margin:0;}div{position:absolute;left:100px;top:0;width:80px;height:40px;}</style></head><body><div id='same'></div></body></html>",
        "ui/events-b.html");
    assert(urui_set_event_callback(Callback) == 1);
    assert(urui_document_listen(a) == 1 && urui_document_listen(b) == 1);
    urui_update();
    const auto nodeA = urui_element_find(a, urui_document_root(a), "same", nullptr);
    const auto nodeB = urui_element_find(b, urui_document_root(b), "same", nullptr);
    assert(nodeA && nodeB && nodeA != nodeB);
    Pointer(0, 10, 1);
    Pointer(2, 10, 0);
    Pointer(0, 110, 1);
    Pointer(2, 110, 0);
    assert(clicks[a] == 1 && clicks[b] == 1);
    Pointer(0, 110, 1);
    assert(urui_pointer_capture(a, 0, nodeA) == -1);
    assert(urui_pointer_capture(b, 0, nodeB) == 1);
    assert(urui_pointer_capture(a, 0, 0) == -1);
    assert(urui_pointer_captured(a, 0) == 0 && urui_pointer_captured(b, 0) == nodeB);
    assert(urui_document_close(a) == 1);
    assert(urui_pointer_captured(b, 0) == nodeB && urui_element_ref_valid(b, nodeB) == 1);
    Pointer(2, 110, 0);
    assert(clicks[b] == 2 && lost[b] == 1 && lost[a] == 0);
    Pointer(0, 110, 1);
    assert(urui_pointer_capture(b, 0, nodeB) == 1);
    assert(urui_document_close(b) == 1);
    assert(lost[b] == 2 && urui_pointer_captured(b, 0) == 0);
    const auto c = LoadTestDocument(
        "<!doctype html><html><head><style>body{margin:0;}div{width:80px;height:40px;}</style></head><body><div id='same'></div></body></html>",
        "ui/events-c.html");
    assert(urui_document_listen(c) == 1);
    urui_update();
    const auto nodeC = urui_element_find(c, urui_document_root(c), "same", nullptr);
    captureOnDown = checkCaptureLost = true;
    Pointer(0, 10, 1);
    assert(urui_pointer_captured(c, 0) == nodeC);
    assert(urui_document_set_presentation(c, 0, 0, 0, 0) == 1);
    assert(lost[c] == 1 && urui_pointer_captured(c, 0) == 0);
    assert(urui_document_set_presentation(c, 0, 0, 1, 0) == 1);
    Pointer(0, 10, 1);
    Pointer(2, 10, 0);
    assert(clicks[c] == 1 && lost[c] == 2);
    Pointer(0, 10, 1);
    assert(urui_document_load_html(c, "<!doctype html><html><head></head><body></body></html>",
                                   "ui/events-c-reloaded.html") == 1);
    assert(lost[c] == 3 && urui_element_ref_valid(c, nodeC) == 0);
    urui_update();
    urui_destroy();
    assert(urui_create(400, 200) == 1);
    assert(urui_set_ui_time(0) == 1 && urui_set_event_callback(Callback) == 1);
    URUIDocumentHandle pending = 0;
    assert(urui_document_create(&pending) == 1);
    assert(urui_document_listen(pending) == 1);
    const char* animated =
        "<!doctype html><html><head><style>@keyframes appear{from{opacity:0;}to{opacity:1;}}div{animation:appear 1s linear;}</style></head><body><div></div></body></html>";
    assert(urui_document_load_html(pending, animated, "ui/initial-animation.html") == 1);
    assert(created[pending] == 0);
    urui_update();
    assert(created[pending] == 1);
    assert(urui_document_load_html(pending, "<html></html>", "ui/invalid.html") == -9);
    urui_update();
    assert(created[pending] == 1);
    assert(urui_document_load_html(pending, animated, "ui/reloaded-animation.html") == 1);
    assert(created[pending] == 1);
    urui_update();
    assert(created[pending] == 2);
    assert(urui_document_load_html(pending, animated, "ui/never-delivered.html") == 1);
    assert(urui_document_close(pending) == 1);
    urui_update();
    assert(created[pending] == 2);
    urui_destroy();
    assert(urui_create(400, 200) == 1);
    assert(urui_set_event_callback(Callback) == 1);
    const auto background = LoadTestDocument(
        "<!doctype html><html><head><style>body{margin:0;}div{width:80px;height:40px;}</style></head><body><div id='same'></div></body></html>",
        "ui/background.html");
    URUIDocumentHandle modal = 0;
    assert(urui_document_create(&modal) == 1);
    assert(urui_document_load_html(
               modal, "<!doctype html><html><head><style>body{margin:0;}</style></head><body></body></html>",
               "ui/modal.html") == 1);
    assert(urui_document_listen(background) == 1);
    urui_update();
    captureOnDown = checkCaptureLost = true;
    Pointer(0, 10, 1);
    assert(urui_pointer_captured(background, 0) != 0);
    assert(urui_document_set_presentation(modal, 1, 0, 1, 1) == 1);
    assert(lost[background] == 1 && urui_pointer_captured(background, 0) == 0);
    Pointer(1, 10, 1);
    Pointer(2, 10, 0);
    assert(clicks[background] == 0);
    assert(urui_document_set_presentation(modal, 1, 0, 1, 0) == 1);
    assert(urui_element_set_inner_html(modal, urui_document_body(modal),
                                       "<div style='position:absolute;left:100px;top:0;width:80px;height:40px;'></div>",
                                       "ui/other.html") == 1);
    assert(urui_document_listen(modal) == 1);
    urui_update();
    captureOnDown = false;
    const auto cancelledBefore = cancelled[background];
    const auto pressedBefore = pressed[background];
    Pointer(0, 10, 1);
    assert(pressed[background] == pressedBefore + 1);
    Pointer(1, 110, 1);
    Pointer(3, 110, 0);
    assert(cancelled[background] == cancelledBefore + 1 && cancelled[modal] == 0);
    urui_destroy();
    std::cout << "Document-owned events, capture isolation and scoped close passed\n";
}
