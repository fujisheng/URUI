#include "test_document.h"
#include "urui.h"
#include <cassert>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <vector>

static std::vector<uint8_t> ReadFixtureFont()
{
    const auto path = std::filesystem::path(__FILE__).parent_path() / "fixtures/fonts/LatoLatin-Regular.ttf";
    std::ifstream input(path, std::ios::binary);
    assert(input.good());
    return std::vector<uint8_t>((std::istreambuf_iterator<char>(input)), {});
}

static void TestMultiSurfaceIsolationAndFontLifetime()
{
    TestSurface a(320, 200);
    TestSurface b(640, 480);
    assert(a.handle != 0 && b.handle != 0 && a.handle != b.handle);

    const auto fontBytes = ReadFixtureFont();
    assert(urui_surface_set_default_font_face(a, 0, fontBytes.data(), int(fontBytes.size()), 0, 0, 400) == URUI_OK);

    // Independent UI clocks per surface.
    assert(urui_surface_set_ui_time(a, 0.25) == URUI_OK);
    assert(urui_surface_set_ui_time(b, 0.75) == URUI_OK);

    // Independent clipboards per surface.
    assert(urui_clipboard_write(a, "clip-A") == URUI_OK);
    assert(urui_clipboard_write(b, "clip-B") == URUI_OK);
    char clipBuf[64]{};
    int req = 0;
    assert(urui_clipboard_read(a, clipBuf, sizeof(clipBuf), &req) == URUI_OK && std::strcmp(clipBuf, "clip-A") == 0);
    assert(urui_clipboard_read(b, clipBuf, sizeof(clipBuf), &req) == URUI_OK && std::strcmp(clipBuf, "clip-B") == 0);

    const char* htmlA =
        "<!doctype html><html><head><style>body{margin:0;}#box{width:100px;height:40px;background:red;font-size:16px;}</style></head>"
        "<body><div id='box'>Surface A</div></body></html>";
    const char* htmlB =
        "<!doctype html><html><head><style>body{margin:0;}#box{width:220px;height:60px;background:blue;font-size:16px;}</style></head>"
        "<body><div id='box'>Surface B</div></body></html>";

    const auto docA = LoadTestDocument(a, htmlA, "/surface-a.html");
    const auto docB = LoadTestDocument(b, htmlB, "/surface-b.html");
    assert(urui_surface_update(a) == URUI_OK);
    assert(urui_surface_update(b) == URUI_OK);

    const auto boxA = FindTestElement(a, docA, "box");
    const auto boxB = FindTestElement(b, docB, "box");
    assert(boxA && boxB);

    // Cross-surface rejection: A's document/token cannot be accessed via B.
    assert(urui_document_root(b, docA) == 0);
    assert(urui_element_state_flags(b, docA, boxA) == 0);
    assert(urui_element_write(b, docA, boxA, 4, "width", "10px") == URUI_ERR_NOT_READY);

    // Render text on BOTH surfaces so FontFaceLayer binds CallbackTexture to both RenderManagers.
    assert(urui_surface_render(a) == URUI_OK && urui_command_count(a) > 0);
    assert(urui_surface_render(b) == URUI_OK && urui_command_count(b) > 0);

    // Destroy Surface A while Surface B continues updating, rendering text, and compiling RenderPlans.
    a.Destroy();
    assert(urui_surface_update(a) == URUI_ERR_NOT_READY);

    assert(urui_element_write(b, docB, boxB, 1, nullptr, "Surface B Alive") == URUI_OK);
    assert(urui_surface_update(b) == URUI_OK);
    assert(urui_surface_render(b) == URUI_OK && urui_command_count(b) > 0);

    URUIPlanInputPacket input{};
    URUIRenderPlanPacket plan{};
    URUIPlanBindings bindings{};
    assert(urui_plan_begin(b, &input) == URUI_OK && input.command_count > 0);
    assert(urui_plan_compile(b, input.lease, &bindings, &plan) == URUI_OK && plan.operation_count > 0);
    assert(urui_plan_release(b, plan.lease) == URUI_OK);
    assert(urui_plan_release(b, input.lease) == URUI_OK);
}

static void TestAbiV13NewExports()
{
    TestSurface surface(400, 300);
    const char* html =
        "<!doctype html><html><head><style>"
        "body{margin:0;}"
        "#wrap{position:relative;width:200px;height:120px;overflow:scroll;padding:4px;}"
        "#item{position:absolute;left:10px;top:20px;width:80px;height:30px;background:red;transform-origin:40px 15px;}"
        "#hidden{display:none;}"
        "@keyframes fade{from{opacity:0;}to{opacity:1;}}"
        "@keyframes shift{from{transform:translateX(0px);}to{transform:translateX(100px);}}"
        "</style></head><body>"
        "<div id='wrap'><div id='item'></div><button id='btn' disabled>Off</button><div id='hidden'></div></div>"
        "<div id='list'><span id='c1'>1</span><span id='c3'>3</span></div>"
        "</body></html>";
    const auto doc = LoadTestDocument(surface, html, "/abi-v13.html");
    assert(urui_surface_update(surface) == URUI_OK);

    const auto wrap = FindTestElement(surface, doc, "wrap");
    const auto item = FindTestElement(surface, doc, "item");
    const auto btn = FindTestElement(surface, doc, "btn");
    const auto hidden = FindTestElement(surface, doc, "hidden");
    const auto list = FindTestElement(surface, doc, "list");
    const auto c1 = FindTestElement(surface, doc, "c1");
    const auto c3 = FindTestElement(surface, doc, "c3");
    assert(wrap && item && btn && hidden && list && c1 && c3);

    // 1. urui_element_state_flags
    const int itemFlags = urui_element_state_flags(surface, doc, item);
    assert((itemFlags & URUI_STATE_VALID) && (itemFlags & URUI_STATE_ATTACHED) &&
           (itemFlags & URUI_STATE_VISIBLE_IN_HIERARCHY) && !(itemFlags & URUI_STATE_DISABLED_IN_HIERARCHY));
    const int btnFlags = urui_element_state_flags(surface, doc, btn);
    assert((btnFlags & URUI_STATE_DISABLED_IN_HIERARCHY) != 0);
    const int hiddenFlags = urui_element_state_flags(surface, doc, hidden);
    assert((hiddenFlags & URUI_STATE_VALID) && (hiddenFlags & URUI_STATE_ATTACHED) &&
           !(hiddenFlags & URUI_STATE_VISIBLE_IN_HIERARCHY));

    // 2. urui_element_trs_ex (preserve_origin = 1 keeps 40px 15px; preserve_origin = 0 resets to 0px 0px)
    const float trs10[10] = {12.f, 8.f, 0.f, 0.f, 0.f, 1.f, 0.f, 2.f, 1.f, 1.f};
    assert(urui_element_trs_ex(surface, doc, item, trs10, 10, 1) == URUI_OK);
    assert(urui_surface_update(surface) == URUI_OK);
    char originBuf[64]{};
    int req = 0;
    assert(urui_element_style_read(surface, doc, item, 0, "--urui-transform-origin-x", originBuf, sizeof(originBuf),
                                   &req) == URUI_OK);
    assert(std::strstr(originBuf, "40px") != nullptr);

    // 3. urui_element_spatial_snapshot
    URUIElementSpatialSnapshot snap{};
    assert(urui_element_spatial_snapshot(surface, doc, item, &snap) == URUI_OK);
    assert(snap.layout[0] == 10.f && snap.layout[1] == 20.f && snap.layout[2] == 80.f && snap.layout[3] == 30.f);
    assert((snap.state_flags & (URUI_STATE_VALID | URUI_STATE_ATTACHED | URUI_STATE_VISIBLE_IN_HIERARCHY)) ==
           (URUI_STATE_VALID | URUI_STATE_ATTACHED | URUI_STATE_VISIBLE_IN_HIERARCHY));

    const float trs12[12] = {5.f, 6.f, 0.f, 0.f, 0.f, 1.f, 0.f, 1.f, 1.f, 1.f, 25.f, 50.f};
    assert(urui_element_trs_ex(surface, doc, item, trs12, 12, 0) == URUI_OK);
    assert(urui_surface_update(surface) == URUI_OK);
    assert(urui_element_style_read(surface, doc, item, 0, "--urui-transform-origin-x", originBuf, sizeof(originBuf),
                                   &req) == URUI_OK);
    assert(std::strstr(originBuf, "25") != nullptr);

    // 4. urui_element_insert_before
    const auto c2 = urui_element_create(surface, doc, "span");
    const auto c4 = urui_element_create(surface, doc, "span");
    assert(c2 && c4);
    assert(urui_element_insert_before(surface, doc, list, c2, c3) == URUI_OK);
    assert(urui_element_insert_before(surface, doc, list, c4, 0) == URUI_OK);
    assert(urui_element_child_count(surface, doc, list) == 4);
    assert(urui_element_child_at(surface, doc, list, 0) == c1);
    assert(urui_element_child_at(surface, doc, list, 1) == c2);
    assert(urui_element_child_at(surface, doc, list, 2) == c3);
    assert(urui_element_child_at(surface, doc, list, 3) == c4);

    // 5. urui_document_layout_version & urui_surface_layout_version
    assert(urui_surface_update(surface) == URUI_OK);
    const uint64_t docVer0 = urui_document_layout_version(surface, doc);
    const uint64_t surfVer0 = urui_surface_layout_version(surface);
    assert(docVer0 > 0 && surfVer0 > 0);
    // Idle update must not bump layout version.
    assert(urui_surface_update(surface) == URUI_OK);
    assert(urui_document_layout_version(surface, doc) == docVer0);
    // Layout change must bump layout version.
    assert(urui_element_number(surface, doc, item, "width", 120.f, 1) == URUI_OK);
    assert(urui_surface_update(surface) == URUI_OK);
    assert(urui_document_layout_version(surface, doc) > docVer0);

    // 6. urui_document_drain_invalidated_elements
    assert(urui_element_destroy(surface, doc, c2) == URUI_OK);
    assert(urui_element_destroy(surface, doc, c4) == URUI_OK);
    uint64_t drained[8]{};
    int total = 0;
    int overflow = 0;
    const int drainedCount =
        urui_document_drain_invalidated_elements(surface, doc, drained, 8, &total, &overflow);
    assert(drainedCount >= 2 && overflow == 0 && total == drainedCount);
    bool sawC2 = false;
    bool sawC4 = false;
    for (int i = 0; i < drainedCount; ++i)
    {
        sawC2 |= (drained[i] == c2);
        sawC4 |= (drained[i] == c4);
    }
    assert(sawC2 && sawC4);
    // Second drain is empty.
    assert(urui_document_drain_invalidated_elements(surface, doc, drained, 8, &total, &overflow) == 0);
    assert(total == 0 && overflow == 0);

    // 7. urui_sequence_apply_batch (capture mode=2 then sample mode=1)
    URUIAnimationSample samples[2] = {
        {"fade", "linear", 0.5f},
        {"shift", "linear", 0.5f},
    };
    URUISequenceBatchItem captureBatch[2] = {
        {item, 101, 0, 1, 2, 0},
        {btn, 102, 1, 1, 2, 0},
    };
    assert(urui_sequence_apply_batch(surface, doc, captureBatch, 2, samples, 2) == URUI_OK);
    URUISequenceBatchItem applyBatch[2] = {
        {item, 101, 0, 1, 1, 0},
        {btn, 102, 1, 1, 1, 0},
    };
    assert(urui_sequence_apply_batch(surface, doc, applyBatch, 2, samples, 2) == URUI_OK);
    assert(urui_surface_update(surface) == URUI_OK);
    char opBuf[32]{};
    assert(urui_element_style_read(surface, doc, item, 0, "opacity", opBuf, sizeof(opBuf), &req) == URUI_OK);
    assert(std::abs(std::strtof(opBuf, nullptr) - 0.5f) < 0.02f);

#if defined(URUI_DEBUG)
    // 8. Debug selftests with explicit URUISurfaceHandle
    {
        TestSurface dbg(256, 256);
        assert(urui_lookup_queue_selftest() == 255);
        assert(urui_texture_lifecycle_selftest(dbg) == 255);
        assert(urui_touch_selftest(dbg) == 511);
        assert(urui_dom_selftest(dbg) == 127);
        assert(urui_transform_selftest(dbg) == 1);
    }
#endif
}

int main()
{
    assert(urui_bridge_abi_version() == 13);
    assert(urui_render_plan_version() == 9);
    assert(urui_element_api_version() == 4);
    assert(urui_sequence_api_version() == 2);
    TestMultiSurfaceIsolationAndFontLifetime();
    TestAbiV13NewExports();
    std::cout << "Multi-Surface isolation, font lifetime, and ABI v13 exports passed\n";
}
