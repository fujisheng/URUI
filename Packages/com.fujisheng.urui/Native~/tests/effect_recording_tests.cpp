#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include "urui.h"
#include "../render_plan_protocol.h"
#include <cassert>
#include <fstream>
#include <iterator>
#include <iostream>
#include <vector>
#include <cstring>
int Resolve(URUISurfaceHandle, URUIDocumentHandle, const char*, uint64_t* id, uint64_t* source, int* w, int* h)
{
    *source = 1;
    *id = 0x8000000000000001ULL;
    *w = 64;
    *h = 64;
    return 1;
}
void Release(URUISurfaceHandle, uint64_t)
{
}
int main(int argc, char** argv)
{
    assert(argc == 2);
    std::ifstream input(argv[1]);
    std::string markup((std::istreambuf_iterator<char>(input)), {});
    assert(urui_surface_create(600, 400, &g_test_surface) == 1);
    assert(urui_surface_set_image_resolver(g_test_surface, Resolve, Release) == 1);
    const auto owner = LoadTestDocument(g_test_surface, markup.c_str(), argv[1]);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    std::vector<URUIPaintCommand> effects(2);
    assert(urui_get_paint_commands(g_test_surface, nullptr, 0) == 2);
    assert(urui_get_paint_commands(g_test_surface, effects.data(), 2) == 2);
    assert(effects[0].element != effects[1].element && effects[0].shader != effects[1].shader);
    for (auto effect : effects)
    {
        assert(effect.reserved == 1);
        char name[40];
        int required = 0;
        assert(urui_paint_shader_name(g_test_surface, effect.shader, name, 40, &required) == URUI_OK && required > 0);
        assert(!strcmp(name, "probe"));
    }
    assert(urui_effect_api_version() == 8);
    // Validate image effect ink bounds expansion and oversized rejection through RenderPlan v9.
    {
        URUIPlanInputPacket planInput{};
        assert(urui_plan_begin(g_test_surface, &planInput) == URUI_OK);
        URUIPlanPreparePacket first{};
        assert(urui_plan_resolve(g_test_surface, planInput.lease, nullptr, 0, nullptr, &first) == URUI_OK);
        assert(first.count == 2 && first.requests[0].kind == URUI_PLAN_PREPARE_IMAGE);

        // Oversized layer ink bounds must fail resolution cleanly without leaking state.
        std::vector<URUIPlanFeedback> oversized(first.count);
        for (uint32_t i = 0; i < first.count; ++i)
        {
            oversized[i].request = first.requests[i].request;
            oversized[i].kind = URUI_PLAN_PREPARE_IMAGE;
            oversized[i].resource = 1001 + i;
            oversized[i].pixel_width = 64;
            oversized[i].pixel_height = 64;
            oversized[i].ink[2] = i == 0 ? 10437.25f : 64.f;
            oversized[i].ink[3] = i == 0 ? 5218.63f : 64.f;
            oversized[i].uv[2] = 1;
            oversized[i].uv[3] = 1;
        }
        URUIPlanPreparePacket rejected{};
        assert(urui_plan_resolve(g_test_surface, planInput.lease, oversized.data(), uint32_t(oversized.size()), nullptr,
                                 &rejected) < 0);
        assert(urui_plan_release(g_test_surface, planInput.lease) == URUI_OK);
    }
    {
        URUIPlanInputPacket planInput{};
        assert(urui_plan_begin(g_test_surface, &planInput) == URUI_OK);
        URUIPlanPreparePacket first{};
        assert(urui_plan_resolve(g_test_surface, planInput.lease, nullptr, 0, nullptr, &first) == URUI_OK);
        std::vector<URUIPlanFeedback> feedbacks;
        for (uint32_t i = 0; i < first.count; ++i)
        {
            if (first.requests[i].kind == URUI_PLAN_PREPARE_IMAGE)
            {
                URUIPlanFeedback fb{};
                fb.request = first.requests[i].request;
                fb.kind = URUI_PLAN_PREPARE_IMAGE;
                fb.resource = 2001 + i;
                fb.pixel_width = 64;
                fb.pixel_height = 64;
                if (feedbacks.empty())
                {
                    fb.ink[0] = -25;
                    fb.ink[1] = -8;
                    fb.ink[2] = 350;
                    fb.ink[3] = 200;
                }
                else
                {
                    fb.ink[0] = 0;
                    fb.ink[1] = 0;
                    fb.ink[2] = 64;
                    fb.ink[3] = 64;
                }
                fb.uv[2] = 1;
                fb.uv[3] = 1;
                feedbacks.push_back(fb);
            }
        }
        URUIPlanPreparePacket expanded{};
        assert(urui_plan_resolve(g_test_surface, planInput.lease, feedbacks.data(), uint32_t(feedbacks.size()),
                                 nullptr, &expanded) == URUI_OK);
        assert(expanded.ready == 1 && expanded.region_count > 0);
        assert(expanded.regions[0].bounds[0] <= -25 && expanded.regions[0].bounds[2] >= 350);
        assert(urui_plan_release(g_test_surface, planInput.lease) == URUI_OK);
    }
    std::vector<URUICommand> commands(urui_command_count(g_test_surface));
    urui_get_commands(g_test_surface, commands.data(), commands.size());
    std::vector<uint64_t> geometry;
    for (auto effect : effects)
    {
        geometry.push_back(commands[effect.command_index].geometry);
    }
    auto root = urui_document_root(g_test_surface, owner);
    auto a = urui_element_find(g_test_surface, owner, root, "a", nullptr);
    assert(urui_element_class(g_test_surface, owner, a, "plain", 1) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(urui_get_paint_commands(g_test_surface, nullptr, 0) == 1);
    commands.resize(urui_command_count(g_test_surface));
    urui_get_commands(g_test_surface, commands.data(), commands.size());
    bool retained = false;
    for (auto command : commands)
    {
        retained |= command.geometry == geometry[0];
    }
    // Same image mesh, no overlay.
    assert(retained);
    assert(urui_element_class(g_test_surface, owner, a, "plain", 0) == 1);
    assert(urui_element_number(g_test_surface, owner, a, "--probe-progress", .6f, 0) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(urui_get_paint_commands(g_test_surface, nullptr, 0) == 2);
    assert(urui_element_class(g_test_surface, owner, a, "hidden", 1) == 1);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    assert(urui_get_paint_commands(g_test_surface, nullptr, 0) == 1);
    assert(urui_document_close(g_test_surface, owner) == 1);
    urui_surface_destroy(g_test_surface);
    std::cout
        << "image effects: independent identities, original geometry, visibility and explicit cache rejection passed\n";
}
