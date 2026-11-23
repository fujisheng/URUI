#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include <URUI/Core/ComputedValues.h>
#include <cassert>
#include <cstring>
#include <iostream>
#include <vector>

static std::vector<URUILayerCommand> Layers()
{
    const int count = urui_get_layer_commands(g_test_surface, nullptr, 0);
    assert(count >= 0);
    std::vector<URUILayerCommand> result(count);
    assert(urui_get_layer_commands(g_test_surface, result.data(), count) == count);
    return result;
}
int main(int argc, char** argv)
{
    assert(argc == 2 && urui_surface_create(200, 120, &g_test_surface) == 1);
    if (!std::strcmp(argv[1], "opacity"))
    {
        const auto owner = LoadTestDocument(g_test_surface, 
            "<!doctype html><html><head><style>body{margin:0;}#parent{width:100px;height:80px;background-color:red;opacity:.5;}#half{width:40px;height:40px;background-color:blue;opacity:.5;}#normal{width:30px;height:30px;background-color:green;}</style></head><body><div id='parent'><div id='half'></div><div id='normal'></div></div></body></html>",
            "ui/opacity.html");
        auto* doc = URUI::GetContext(0)->GetDocument(0);
        assert(doc->GetElementById("parent")->GetComputedValues().opacity() == .5f);
        assert(doc->GetElementById("half")->GetComputedValues().opacity() == .5f);
        assert(doc->GetElementById("normal")->GetComputedValues().opacity() == 1.f);
        urui_surface_update(g_test_surface);
        urui_surface_render(g_test_surface);
        int opacity_count = 0;
        for (const auto& command : Layers())
        {
            if (command.kind == 6)
            {
                URUIFilter filter{};
                char name[64];
                int required = 0;
                assert(urui_filter_info(g_test_surface, command.mask, &filter, name, sizeof(name), &required) ==
                       URUI_OK);
                if (!std::strcmp(name, "opacity"))
                {
                    assert(filter.value == .5f && urui_filter_document(g_test_surface, command.mask) == owner);
                    ++opacity_count;
                }
            }
        }
        assert(opacity_count == 2);
        const int count = urui_get_commands(g_test_surface, nullptr, 0);
        std::vector<URUICommand> commands(count);
        assert(urui_get_commands(g_test_surface, commands.data(), count) == count);
        for (const auto& command : commands)
        {
            int vertices = 0;
            int indices = 0;
            assert(urui_get_geometry_info(g_test_surface, command.geometry, &vertices, &indices) == 1);
            std::vector<URUIVertex> vs(vertices);
            std::vector<int> ids(indices);
            assert(urui_copy_geometry(g_test_surface, command.geometry, vs.data(), vertices, ids.data(), indices) == 1);
            for (const auto& vertex : vs)
            {
                assert(vertex.a == 255);
            }
        }
    }
    else if (!std::strcmp(argv[1], "controls"))
    {
        const auto owner = LoadTestDocument(g_test_surface, 
            "<!doctype html><html><head><style>body{margin:0;}button,input,select,textarea,progress{width:80px;height:30px;}</style></head><body><button id='button'>Button</button><input id='input'><select id='select'><option>Option</option></select><textarea id='textarea'>Text</textarea><progress id='progress'></progress></body></html>",
            "ui/controls.html");
        urui_surface_update(g_test_surface);
        for (const auto* id : {"button", "input", "select", "textarea", "progress"})
        {
            float bounds[6];
            assert(TestGetElementBox(g_test_surface, owner, id, bounds) == 1);
            if (bounds[2] != 80 || bounds[3] != 30)
            {
                std::cerr << id << " " << bounds[2] << "x" << bounds[3] << '\n';
            }
            assert(bounds[2] == 80 && bounds[3] == 30);
        }
    }
    else
    {
        const bool surface = !std::strcmp(argv[1], "surface");
        LoadTestDocument(g_test_surface, 
            "<!doctype html><html><head><style>body{margin:0;background-color:red;width:120px;height:80px;}</style></head><body></body></html>",
            "ui/a.html");
        const std::string html =
            "<!doctype html><html><head><style>body{margin:0;}#panel{width:80px;height:60px;backdrop-filter:brightness(.5);" +
            std::string(surface ? "--urui-backdrop-scope:surface;" : "") +
            "}</style></head><body><div id='panel'></div></body></html>";
        const auto owner = LoadTestDocument(g_test_surface, html.c_str(), "ui/b.html");
        urui_surface_update(g_test_surface);
        urui_surface_render(g_test_surface);
        bool copied_surface = false;
        bool has_filter = false;
        for (const auto& command : Layers())
        {
            if (command.kind == 4 && command.source == 0 && command.target != 0)
            {
                copied_surface = true;
            }
            if (command.kind == 6)
            {
                // Filtering must never modify the shared destination.
                assert(command.source != 0);
                assert(urui_filter_document(g_test_surface, command.mask) == owner);
                has_filter = true;
            }
        }
        assert(has_filter && copied_surface == surface);
    }
    urui_surface_destroy(g_test_surface);
    std::cout << "Document compositing and filter provenance passed\n";
}
