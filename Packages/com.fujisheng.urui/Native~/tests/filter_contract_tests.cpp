#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include "urui.h"
#include "../render_plan_protocol.h"
#include <cassert>
#include <fstream>
#include <iterator>
#include <vector>
#include <cstring>
#include <iostream>
int main(int argc, char** argv)
{
    assert(argc == 2);
    std::ifstream f(argv[1]);
    std::string markup((std::istreambuf_iterator<char>(f)), {});
    assert(urui_surface_create(600, 400, &g_test_surface) == 1);
    const auto owner = LoadTestDocument(g_test_surface, markup.c_str(), argv[1]);
    urui_surface_update(g_test_surface);
    urui_surface_render(g_test_surface);
    int count = urui_get_layer_commands(g_test_surface, nullptr, 0);
    assert(count > 0);
    std::vector<URUILayerCommand> commands(count);
    assert(urui_get_layer_commands(g_test_surface, commands.data(), count) == count);
    const char* expected[] = {"brightness", "contrast", "blur", "drop-shadow"};
    int index = 0;
    for (auto c : commands)
    {
        if (c.kind == 6)
        {
            assert(index < 4 && c.blend == 0);
            URUIFilter d;
            char name[64];
            int required = 0;
            assert(urui_filter_info(g_test_surface, c.mask, &d, name, 64, &required) == URUI_OK && required > 0);
            assert(!strcmp(name, expected[index]));
            if (index < 2)
            {
                assert(d.value == .5f);
            }
            if (index == 2)
            {
                assert(d.sigma == 2);
            }
            if (index == 3)
            {
                assert(d.offset_x == 4 && d.offset_y == 3 && d.sigma == 1 && d.red == 1 && d.alpha == 1);
            }
            index++;
        }
    }
    assert(index == 4);
    URUIPlanInputPacket input{};
    assert(urui_plan_begin(g_test_surface, &input) == URUI_OK);
    URUIPlanPreparePacket prepare{};
    assert(urui_plan_resolve(g_test_surface, input.lease, nullptr, 0, nullptr, &prepare) == URUI_OK);
    assert(prepare.region_count > 0);
    const auto& region = prepare.regions[0];
    assert(region.bounds[2] > 140 && region.bounds[3] > 70);
    assert(region.bounds[0] < 10 && region.bounds[1] < 30);
    assert(urui_plan_release(g_test_surface, input.lease) == URUI_OK);
    assert(urui_document_close(g_test_surface, owner) == 1);
    urui_surface_destroy(g_test_surface);
    std::cout << "ordered native filters, original parsed values and transformed overflow/halo regions passed\n";
}
