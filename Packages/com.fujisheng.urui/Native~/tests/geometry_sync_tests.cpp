#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <cassert>
#include <cmath>
#include <thread>

static void Box(URUIDocumentHandle owner, uint64_t token, float x, float y, float width, float height)
{
    float box[16]{};
    assert(urui_element_box(g_test_surface, owner, token, box, 16) == 1);
    const float expected[] = {x, y, width, height};
    for (int i = 0; i < 4; ++i)
    {
        assert(std::abs(box[8 + i] - expected[i]) < .01f);
    }
}
int main()
{
    assert(urui_surface_synchronize_geometry(g_test_surface) < 0);
    assert(urui_surface_create(800, 600, &g_test_surface) == 1);
    const auto owner = LoadTestDocument(g_test_surface, 
        "<!doctype html><html><head><style>body{margin:0;}#a{position:absolute;left:10px;top:20px;width:100px;height:80px;transform:translate(30px,40px) scale(2);transform-origin:0px 0px;}#b{position:absolute;left:5px;top:7px;width:20px;height:10px;background:red;}</style></head><body><div id='a'><div id='b'></div></div></body></html>",
        "/geometry.html");
    const auto root = urui_document_root(g_test_surface, owner);
    const auto child = urui_element_find(g_test_surface, owner, root, "b", nullptr);
    assert(child);
    urui_surface_update(g_test_surface);
    assert(urui_surface_synchronize_geometry(g_test_surface) == 1);
    assert(urui_command_count(g_test_surface) == 0);
    Box(owner, child, 50, 74, 40, 20);
    int outside = 1;
    std::thread thread([&] { outside = urui_surface_synchronize_geometry(g_test_surface); });
    thread.join();
    assert(outside < 0);
    urui_surface_render(g_test_surface);
    Box(owner, child, 50, 74, 40, 20);
    assert(urui_command_count(g_test_surface) > 0);
    assert(TestSetStyle(g_test_surface, owner, "a", "transform", "translate(4px,8px) scale(3)") == 1);
    urui_surface_update(g_test_surface);
    assert(urui_surface_synchronize_geometry(g_test_surface) == 1);
    Box(owner, child, 29, 49, 60, 30);
    urui_surface_render(g_test_surface);
    Box(owner, child, 29, 49, 60, 30);
    assert(TestSetStyle(g_test_surface, owner, "b", "display", "none") == 1);
    urui_surface_update(g_test_surface);
    assert(urui_surface_synchronize_geometry(g_test_surface) == 1);
    assert(TestSetStyle(g_test_surface, owner, "a", "transform", "scale(0.25)") == 1);
    urui_surface_update(g_test_surface);
    assert(urui_surface_synchronize_geometry(g_test_surface) == 1);
    assert(TestSetStyle(g_test_surface, owner, "b", "display", "block") == 1);
    urui_surface_update(g_test_surface);
    assert(urui_surface_synchronize_geometry(g_test_surface) == 1);
    Box(owner, child, 11.25f, 21.75f, 5, 2.5f);
    urui_surface_render(g_test_surface);
    Box(owner, child, 11.25f, 21.75f, 5, 2.5f);
    const auto initially_hidden = LoadTestDocument(g_test_surface, 
        "<!doctype html><html><head><style>body{margin:0;}#canvas{position:absolute;left:0;top:0;width:3200px;height:2400px;transform-origin:0 0;}#host{position:absolute;left:50%;top:50%;width:100px;height:100px;margin-left:-50px;margin-top:-50px;}#item{position:absolute;left:0;top:0;width:100px;height:100px;}[hidden]{display:none;}</style></head><body><main id='canvas'><div id='host'><div id='item' hidden><span></span></div></div></main></body></html>",
        "/hidden-geometry.html");
    const auto hidden_root = urui_document_root(g_test_surface, initially_hidden);
    const auto item = urui_element_find(g_test_surface, initially_hidden, hidden_root, "item", nullptr);
    assert(item);
    assert(TestSetStyle(g_test_surface, initially_hidden, "canvas", "transform", "scale(0.25)") == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(TestSetStyle(g_test_surface, initially_hidden, "item", "display", "block") == 1);
    urui_surface_update(g_test_surface);
    assert(urui_surface_synchronize_geometry(g_test_surface) == 1);
    Box(initially_hidden, item, 387.5f, 287.5f, 25, 25);
    urui_surface_render(g_test_surface);
    Box(initially_hidden, item, 387.5f, 287.5f, 25, 25);
    urui_surface_destroy(g_test_surface);
}
