#include "urui.h"
#include "test_document.h"
#include <cassert>
#include <cmath>
#include <thread>

static void Box(URUIDocumentHandle owner, uint64_t token, float x, float y, float width, float height)
{
    float box[16]{};
    assert(urui_element_box(owner, token, box, 16) == 1);
    const float expected[] = {x, y, width, height};
    for (int i = 0; i < 4; ++i)
    {
        assert(std::abs(box[8 + i] - expected[i]) < .01f);
    }
}
int main()
{
    assert(urui_synchronize_geometry() < 0);
    assert(urui_create(800, 600) == 1);
    const auto owner = LoadTestDocument(
        "<!doctype html><html><head><style>body{margin:0;}#a{position:absolute;left:10px;top:20px;width:100px;height:80px;transform:translate(30px,40px) scale(2);transform-origin:0px 0px;}#b{position:absolute;left:5px;top:7px;width:20px;height:10px;background:red;}</style></head><body><div id='a'><div id='b'></div></div></body></html>",
        "/geometry.html");
    const auto root = urui_document_root(owner);
    const auto child = urui_element_find(owner, root, "b", nullptr);
    assert(child);
    urui_update();
    assert(urui_synchronize_geometry() == 1);
    assert(urui_command_count() == 0);
    Box(owner, child, 50, 74, 40, 20);
    int outside = 1;
    std::thread thread([&] { outside = urui_synchronize_geometry(); });
    thread.join();
    assert(outside < 0);
    urui_render();
    Box(owner, child, 50, 74, 40, 20);
    assert(urui_command_count() > 0);
    assert(urui_set_style(owner, "a", "transform", "translate(4px,8px) scale(3)") == 1);
    urui_update();
    assert(urui_synchronize_geometry() == 1);
    Box(owner, child, 29, 49, 60, 30);
    urui_render();
    Box(owner, child, 29, 49, 60, 30);
    urui_destroy();
}
