#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include "urui.h"
#include "style_properties.h"
#include <cassert>
#include <cmath>
#include <cstring>
#include <iostream>
#include <limits>
#include <string>
#include <vector>

namespace
{
    int events = 0;
    int Observe(const URUIElementEvent* event)
    {
        if (event->type == 17 || event->type == 19)
        {
            ++events;
        }
        return 0;
    }
    float Number(URUIDocumentHandle document, uint64_t token, const char* property)
    {
        char value[256];
        int required = 0;
        assert(urui_element_style_read(g_test_surface, document, token, 0, property, value, 256, &required) == 1);
        return std::stof(value);
    }
    void Near(float expected, float actual)
    {
        if (std::abs(expected - actual) > .003f)
        {
            std::cerr << expected << " != " << actual << '\n';
            std::abort();
        }
    }
    float SnapshotNumber(URUIDocumentHandle document, uint64_t token, const char* property)
    {
        uint64_t revision = 0;
        int count = 0;
        int bytes = 0;
        assert(urui_element_style_snapshot(g_test_surface, document, token, 0, nullptr, 0, nullptr, 0, &revision, &count, &bytes) ==
               URUI_ERR_BUFFER_TOO_SMALL);
        std::vector<URUIStyleValue> values(count);
        std::vector<char> names(bytes);
        assert(urui_element_style_snapshot(g_test_surface, document, token, revision, values.data(), count, names.data(), bytes,
                                           &revision, &count, &bytes) == 1);
        for (const auto& value : values)
        {
            if (!std::strcmp(names.data() + value.name_offset, property))
            {
                return value.x;
            }
        }
        std::abort();
    }
    void Color(URUIDocumentHandle document, uint64_t token, float red, float blue, float opacity)
    {
        uint64_t revision = 0;
        int count = 0;
        int bytes = 0;
        assert(urui_element_style_snapshot(g_test_surface, document, token, 0, nullptr, 0, nullptr, 0, &revision, &count, &bytes) ==
               URUI_ERR_BUFFER_TOO_SMALL);
        std::vector<URUIStyleValue> values(count);
        std::vector<char> names(bytes);
        assert(urui_element_style_snapshot(g_test_surface, document, token, revision, values.data(), count, names.data(), bytes,
                                           &revision, &count, &bytes) == 1);
        bool found = false;
        for (const auto& value : values)
        {
            if (!std::strcmp(names.data() + value.name_offset, "background-color"))
            {
                Near(red, value.x);
                Near(blue, value.z);
                found = true;
            }
        }
        assert(found);
        found = false;
        for (const auto& value : values)
        {
            if (!std::strcmp(names.data() + value.name_offset, "opacity"))
            {
                Near(opacity, value.x);
                found = true;
            }
        }
        assert(found);
    }
}
int main()
{
    const char* html = R"HTML(<!doctype html><html><head><style>
@property --mix {syntax:"<number>";inherits:true;initial-value:0;}
body{margin:0;width:400px;height:200px;}
#box,#guard{position:absolute;width:100px;height:40px;opacity:.25;background-color:red;}
#guard{opacity:.65!important;}
@keyframes full{from{opacity:0;transform:translateX(0px);background-color:red;--mix:0;}to{opacity:1;transform:translateX(100px);background-color:blue;--mix:1;}}
@keyframes implicit{to{opacity:1;}}
@keyframes half{from{opacity:.5;}to{opacity:.75;}}
@keyframes variable{from{opacity:var(--start);}to{opacity:1;}}
@keyframes mix-only{from{--mix:0;}to{--mix:1;}}
#dependent,#child{opacity:var(--mix);}
</style></head><body><div id="box" style="--start:.2;"><div id="child"></div></div><div id="guard"></div><div id="dependent"></div></body></html>)HTML";
    assert(urui_sequence_api_version() == 2);
    assert(urui_surface_create(400, 200, &g_test_surface) == 1);
    assert(urui_surface_set_ui_time(g_test_surface, 0) == 1);
    const auto document = LoadTestDocument(g_test_surface, html, "ui/sequence.html");
    urui_surface_update(g_test_surface);
    const auto root = urui_document_root(g_test_surface, document), box = urui_element_find(g_test_surface, document, root, "box", nullptr),
               guard = urui_element_find(g_test_surface, document, root, "guard", nullptr);
    assert(box && guard);
    assert(urui_set_event_callback(g_test_surface, Observe) == 1);
    assert(urui_document_listen(g_test_surface, document) == 1);
    char names[512];
    int required = 0;
    assert(urui_keyframes_properties(g_test_surface, document, box, "full", names, 512, &required) == 1);
    assert(std::strstr(names, "opacity") && std::strstr(names, "transform") && std::strstr(names, "--mix"));
    URUIAnimationSample full{"full", "linear", 0};
    assert(urui_sequence_sample(g_test_surface, document, box, 7, &full, 1, 2) == 1);
    urui_surface_update(g_test_surface);
    Near(.25f, Number(document, box, "opacity"));
    for (float position : {0.f, .8f, .2f, .8f, 1.f})
    {
        full.progress = position;
        assert(urui_sequence_sample(g_test_surface, document, box, 7, &full, 1, 1) == 1);
        urui_surface_update(g_test_surface);
        urui_surface_render(g_test_surface);
        Near(position, Number(document, box, "opacity"));
        Near(position, Number(document, box, "--mix"));
        // The production CSS interpolator mixes colors in approximate linear space.
        if (position == .8f)
        {
            Color(document, box, .44706f, .89412f, position);
        }
        else if (position == .2f)
        {
            Color(document, box, .89412f, .44706f, position);
        }
        else
        {
            Color(document, box, 1 - position, position, position);
        }
        float matrix[32];
        assert(urui_element_local_transform(g_test_surface, document, box, matrix, 32) > 0);
        if (std::abs(matrix[12] - position * 100) > .003f)
        {
            char value[1024];
            int required;
            urui_element_style_read(g_test_surface, document, box, 0, "transform", value, 1024, &required);
            std::cerr << "transform at " << position << ": " << value << " matrix " << matrix[12] << '\n';
        }
        Near(position * 100, matrix[12]);
        const auto child = urui_element_find(g_test_surface, document, box, "child", nullptr);
        Near(position, SnapshotNumber(document, child, "opacity"));
    }
    assert(events == 0);
    full.progress = .1f;
    assert(urui_sequence_sample(g_test_surface, document, box, 8, &full, 1, 2) < 0);
    Near(1, Number(document, box, "opacity"));
    URUIAnimationSample invalid{"missing", "linear", .5f};
    assert(urui_sequence_sample(g_test_surface, document, box, 7, &invalid, 1, 1) < 0);
    Near(1, Number(document, box, "opacity"));
    invalid = {"full", "invalid-easing", .5f};
    assert(urui_sequence_sample(g_test_surface, document, box, 7, &invalid, 1, 1) < 0);
    invalid = {"full", "linear", std::numeric_limits<float>::quiet_NaN()};
    assert(urui_sequence_sample(g_test_surface, document, box, 7, &invalid, 1, 1) < 0);
    assert(TestSetStyle(g_test_surface, document, "box", "opacity", ".4") == 1);
    assert(urui_sequence_sample(g_test_surface, document, box, 7, nullptr, 0, 1) == 1);
    urui_surface_update(g_test_surface);
    Near(.4f, Number(document, box, "opacity"));
    URUIAnimationSample implicit{"implicit", "linear", 0};
    assert(urui_sequence_sample(g_test_surface, document, box, 9, &implicit, 1, 2) == 1);
    for (float position : {.8f, .2f, .8f})
    {
        implicit.progress = position;
        assert(urui_sequence_sample(g_test_surface, document, box, 9, &implicit, 1, 1) == 1);
        urui_surface_update(g_test_surface);
        Near(.4f + .6f * position, Number(document, box, "opacity"));
    }
    assert(urui_sequence_sample(g_test_surface, document, box, 9, nullptr, 0, 1) == 1);
    URUIAnimationSample sequential[] = {{"full", "linear", 1}, {"half", "linear", 1}};
    assert(urui_sequence_sample(g_test_surface, document, box, 10, sequential, 2, 2) == 1);
    assert(urui_sequence_sample(g_test_surface, document, box, 10, sequential, 2, 1) == 1);
    urui_surface_update(g_test_surface);
    Near(.75f, Number(document, box, "opacity"));
    Near(1, Number(document, box, "--mix"));
    assert(urui_sequence_sample(g_test_surface, document, box, 10, nullptr, 0, 1) == 1);
    URUIAnimationSample variable{"variable", "linear", .5f};
    assert(urui_sequence_sample(g_test_surface, document, box, 11, &variable, 1, 2) == 1);
    assert(urui_sequence_sample(g_test_surface, document, box, 11, &variable, 1, 1) == 1);
    urui_surface_update(g_test_surface);
    Near(.6f, Number(document, box, "opacity"));
    assert(urui_sequence_sample(g_test_surface, document, box, 11, nullptr, 0, 1) == 1);
    assert(urui_sequence_sample(g_test_surface, document, guard, 12, &full, 1, 2) == 1);
    assert(urui_sequence_sample(g_test_surface, document, guard, 12, &full, 1, 1) == 1);
    urui_surface_update(g_test_surface);
    Near(.65f, Number(document, guard, "opacity"));
    assert(urui_sequence_sample(g_test_surface, document, guard, 12, nullptr, 0, 1) == 1);
    assert(events == 0);
    const auto dependent = urui_element_find(g_test_surface, document, root, "dependent", nullptr);
    URUIAnimationSample mix{"mix-only", "linear", .75f};
    assert(urui_sequence_sample(g_test_surface, document, dependent, 13, &mix, 1, 2) == 1);
    assert(urui_sequence_sample(g_test_surface, document, dependent, 13, &mix, 1, 1) == 1);
    urui_surface_update(g_test_surface);
    Near(.75f, Number(document, dependent, "opacity"));
    assert(urui_sequence_sample(g_test_surface, document, dependent, 13, nullptr, 0, 1) == 1);
    urui_surface_update(g_test_surface);
    Near(0, Number(document, dependent, "opacity"));
    assert(urui_sequence_sample(g_test_surface, document, box, 14, &full, 1, 2) == 1);
    assert(urui_sequence_sample(g_test_surface, document, box, 14, nullptr, 0, 3) == 1);
    full.progress = .5f;
    assert(urui_sequence_sample(g_test_surface, document, box, 14, &full, 1, 1) == 1);
    urui_surface_update(g_test_surface);
    Near(.5f, Number(document, box, "opacity"));
    assert(urui_sequence_sample(g_test_surface, document, box, 14, nullptr, 0, 1) == 1);
    assert(urui_sequence_sample(g_test_surface, document, UINT64_MAX, 7, &full, 1, 1) < 0);
    std::string excessive = "<!doctype html><html><head><style>@keyframes huge{";
    for (int index = 0; index <= 4096; ++index)
    {
        excessive += std::to_string(index * 100.0 / 4096) + "%{opacity:0;}";
    }
    excessive += "}</style></head><body><div id='large'></div></body></html>";
    const auto large_document = LoadTestDocument(g_test_surface, excessive.c_str(), "ui/large-sequence.html");
    urui_surface_update(g_test_surface);
    const auto large = urui_element_find(g_test_surface, large_document, urui_document_root(g_test_surface, large_document), "large", nullptr);
    assert(urui_keyframes_properties(g_test_surface, large_document, large, "huge", names, 512, &required) < 0);
    assert(urui_document_close(g_test_surface, large_document) == 1);
    assert(urui_document_close(g_test_surface, document) == 1);
    assert(urui_sequence_sample(g_test_surface, document, box, 7, &full, 1, 1) < 0);
    urui_surface_destroy(g_test_surface);
    std::cout << "sequence native samples, baseline, ownership, cascade and silent backwards seek passed\n";
}
