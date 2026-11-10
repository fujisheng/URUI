#include "test_document.h"
#include <URUI/Core.h>
#include <URUI/Core/ComputedValues.h>
#include <cassert>
#include <cstring>
#include <iostream>
#include <vector>

static std::vector<UruiLayerCommand> Layers()
{
    const int count = urui_get_layer_commands(nullptr, 0); assert(count >= 0);
    std::vector<UruiLayerCommand> result(count);
    assert(urui_get_layer_commands(result.data(), count) == count); return result;
}
int main(int argc, char** argv)
{
    assert(argc == 2 && urui_create(200, 120) == 1);
    if (!std::strcmp(argv[1], "opacity")) {
        const auto owner = LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}#parent{width:100px;height:80px;background-color:red;opacity:.5;}#half{width:40px;height:40px;background-color:blue;opacity:.5;}#normal{width:30px;height:30px;background-color:green;}</style></head><body><div id='parent'><div id='half'></div><div id='normal'></div></div></body></html>", "ui/opacity.html");
        auto* doc = URUI::GetContext("urui")->GetDocument(0);
        assert(doc->GetElementById("parent")->GetComputedValues().opacity() == .5f);
        assert(doc->GetElementById("half")->GetComputedValues().opacity() == .5f);
        assert(doc->GetElementById("normal")->GetComputedValues().opacity() == 1.f);
        urui_update(); urui_render();
        int opacity_count = 0;
        for (const auto& command : Layers()) if (command.kind == 6) {
            UruiFilter filter{}; char name[64]; assert(urui_filter_info(command.mask, &filter, name, sizeof(name)) > 0);
            if (!std::strcmp(name, "opacity")) { assert(filter.value == .5f && urui_filter_document(command.mask) == owner); ++opacity_count; }
        }
        assert(opacity_count == 2);
        const int count = urui_get_commands(nullptr, 0); std::vector<UruiCommand> commands(count);
        assert(urui_get_commands(commands.data(), count) == count);
        for (const auto& command : commands) {
            int vertices = 0, indices = 0; assert(urui_get_geometry_info(command.geometry, &vertices, &indices) == 1);
            std::vector<UruiVertex> vs(vertices); std::vector<int> ids(indices);
            assert(urui_copy_geometry(command.geometry, vs.data(), vertices, ids.data(), indices) == 1);
            for (const auto& vertex : vs) assert(vertex.a == 255);
        }
    } else if (!std::strcmp(argv[1], "controls")) {
        const auto owner = LoadTestDocument("<!doctype html><html><head><style>body{margin:0;}button,input,select,textarea,progress{width:80px;height:30px;}</style></head><body><button id='button'>Button</button><input id='input'><select id='select'><option>Option</option></select><textarea id='textarea'>Text</textarea><progress id='progress'></progress></body></html>", "ui/controls.html");
        urui_update();
        for (const auto* id : {"button", "input", "select", "textarea", "progress"}) {
            float bounds[6]; assert(urui_get_element_box(owner, id, bounds) == 1);
            if (bounds[2] != 80 || bounds[3] != 30) std::cerr << id << " " << bounds[2] << "x" << bounds[3] << '\n';
            assert(bounds[2] == 80 && bounds[3] == 30);
        }
    } else {
        const bool surface = !std::strcmp(argv[1], "surface");
        LoadTestDocument("<!doctype html><html><head><style>body{margin:0;background-color:red;width:120px;height:80px;}</style></head><body></body></html>", "ui/a.html");
        const std::string html = "<!doctype html><html><head><style>body{margin:0;}#panel{width:80px;height:60px;backdrop-filter:brightness(.5);" + std::string(surface ? "--urui-backdrop-scope:surface;" : "") + "}</style></head><body><div id='panel'></div></body></html>";
        const auto owner = LoadTestDocument(html.c_str(), "ui/b.html"); urui_update(); urui_render();
        bool copied_surface = false, has_filter = false;
        for (const auto& command : Layers()) {
            if (command.kind == 4 && command.source == 0 && command.target != 0) copied_surface = true;
            if (command.kind == 6) {
                assert(command.source != 0); // Filtering must never modify the shared destination.
                assert(urui_filter_document(command.mask) == owner); has_filter = true;
            }
        }
        assert(has_filter && copied_surface == surface);
    }
    urui_destroy(); std::cout << "Document compositing and filter provenance passed\n";
}
