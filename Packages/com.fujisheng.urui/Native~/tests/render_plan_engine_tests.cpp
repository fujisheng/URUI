#include "test_document.h"
#include "../render_plan_protocol.h"
#include <cassert>
#include <cmath>
#include <iostream>

static int Image(URUIDocumentHandle owner, const char*, uint64_t* resource, uint64_t* source, int* width, int* height)
{
    *resource = 0x8000000000000000ULL | owner;
    *source = owner;
    *width = *height = 64;
    return 1;
}
static void Release(uint64_t)
{
}
struct Snapshot
{
    URUIPlanInputPacket input{};
    URUIRenderPlanPacket plan{};
    Snapshot()
    {
        assert(urui_plan_begin(&input) == 1);
        URUIPlanBindings bindings{};
        assert(urui_plan_compile(input.lease, &bindings, &plan) == 1);
    }
    ~Snapshot()
    {
        assert(urui_plan_release(plan.lease) == 1);
        assert(urui_plan_release(input.lease) == 1);
    }
};
int main()
{
    assert(urui_create(128, 128) == 1);
    assert(urui_set_image_resolver(Image, Release) == 1);
    auto owner = LoadTestDocument(
        "<!doctype html><html><head><style>body{margin:0;}img{position:absolute;width:8px;height:8px;}</style></head><body><img id='image' src='white.png' data-urui-rect='0 0 8 8'></body></html>",
        "/engine-parameters.html");
    urui_update();
    uint64_t geometry = 0;
    uint64_t batch = 0;
    {
        Snapshot snapshot;
        assert(snapshot.input.command_count == 1);
        geometry = snapshot.input.commands[0].geometry;
        batch = snapshot.plan.operations[0].geometry;
    }
    assert(urui_set_style(owner, "image", "--urui-graphic-tint", "rgba(128,64,32,0.5)") == 1);
    urui_update();
    {
        Snapshot snapshot;
        // No upstream topology regeneration.
        assert(snapshot.input.commands[0].geometry == geometry);
        assert(snapshot.plan.operations[0].geometry == batch);
        assert(snapshot.plan.parameters[0].tint[3] > .49f && snapshot.plan.parameters[0].tint[3] < .51f);
        assert(snapshot.plan.parameters[0].tint[0] > snapshot.plan.parameters[0].tint[1]);
    }
    assert(urui_set_attribute(owner, "image", "data-urui-rect", "8 0 8 8") == 1);
    urui_update();
    {
        Snapshot snapshot;
        assert(snapshot.input.commands[0].geometry == geometry);
        assert(snapshot.plan.operations[0].geometry == batch);
        assert(std::abs(snapshot.plan.parameters[0].uv[2] - .125f) < .00001f);
        assert(std::abs(snapshot.plan.parameters[0].uv[0] - .125f) < .00001f);
    }
    assert(
        urui_document_load_html(
            owner,
            "<!doctype html><html><head><style>body{margin:0;}</style></head><body><div id='solid' style='width:8px;height:8px;background:red;'></div></body></html>",
            "/solid-parameters.html") == 1);
    urui_update();
    {
        Snapshot snapshot;
        assert(snapshot.input.command_count == 1);
        geometry = snapshot.input.commands[0].geometry;
        batch = snapshot.plan.operations[0].geometry;
    }
    assert(urui_set_style(owner, "solid", "background-color", "blue") == 1);
    urui_update();
    {
        Snapshot snapshot;
        assert(snapshot.input.commands[0].geometry == geometry);
        assert(snapshot.plan.operations[0].geometry == batch);
        assert(snapshot.plan.parameters[0].tint[2] == 1);
    }
    urui_destroy();
    std::cout << "native image uniform/topology contracts passed\n";
}
