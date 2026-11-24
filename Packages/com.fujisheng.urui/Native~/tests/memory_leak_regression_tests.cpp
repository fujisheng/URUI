// Negative memory-leak regression tests for URUI Native C++ layer.
// Links bridge sources directly (like texture_handle_tests) so internal state
// can be verified strictly without exposing test hooks in the production ABI.
#include "../bridge_runtime.h"
#include "test_document.h"
#include <array>
#include <cassert>
#include <fstream>
#include <iostream>
#include <iterator>
#include <string>
#include <vector>

namespace
{
    using namespace URUI::Bridge;
    using namespace URUI::RenderPlan;

    const std::array<float, 16> IdentityMatrix{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
    const URUIVertex TestQuad[4] = {
        {0, 0, 0, 0, 255, 255, 255, 255},
        {16, 0, 1, 0, 255, 255, 255, 255},
        {16, 16, 1, 1, 255, 255, 255, 255},
        {0, 16, 0, 1, 255, 255, 255, 255},
    };
    const int TestIndices[6] = {0, 1, 2, 0, 2, 3};

    // 1. Reproduces live Home idle dynamic-geometry churn at RenderPlan::Session level.
    // Before the fix, releasing geometry IDs never retired dead batches in Session, so
    // geometry_bytes climbed linearly to the 64 MiB wall and thrashed malloc/free every frame.
    void TestRetiredGeometryReusesBatchStorageWithoutGrowingToBudget()
    {
        Session session;
        session.Viewport(1280, 720);
        URUIPlanBindings bindings{};
        uint64_t nextGeoId = 100;
        uint64_t prevGeoId = 0;
        uint64_t allocsAtFrame200 = 0;
        uint64_t allocsAtFrame1000 = 0;
        uint64_t maxGeometryBytes = 0;

        auto resolve = [](uint64_t id) -> GeometrySource
        {
            return id ? GeometrySource{TestQuad, 4, TestIndices, 6} : GeometrySource{};
        };

        for (int frame = 0; frame < 1000; ++frame)
        {
            const uint64_t geoId = nextGeoId++;
            if (prevGeoId != 0)
            {
                session.RetireGeometry(prevGeoId);
            }
            prevGeoId = geoId;

            URUICommand draws[4]{};
            std::array<float, 16> transforms[4]{IdentityMatrix, IdentityMatrix, IdentityMatrix, IdentityMatrix};
            for (int i = 0; i < 4; ++i)
            {
                draws[i].geometry = (i == 0) ? geoId : uint64_t(i);
                draws[i].tx = float(i * 24);
            }

            URUIPlanInputPacket input{};
            URUIRenderPlanPacket plan{};
            assert(session.Begin(1, draws, transforms, 4, nullptr, 0, nullptr, 0, input) == 1);
            assert(session.Compile(input.lease, bindings, resolve, plan) == 1);
            if (plan.geometry_bytes > maxGeometryBytes)
            {
                maxGeometryBytes = plan.geometry_bytes;
            }
            if (frame == 200)
            {
                allocsAtFrame200 = plan.allocation_count;
            }
            if (frame == 999)
            {
                allocsAtFrame1000 = plan.allocation_count;
            }
            assert(session.Release(input.lease) == 1);
            assert(session.Release(plan.lease) == 1);
        }

        // Retained geometry must stay tiny (< 256 KiB), nowhere near the 64 MiB ceiling.
        assert(maxGeometryBytes < 256 * 1024);
        // Steady-state slot reuse must avoid heap churn across 800 consecutive dynamic frames.
        assert(allocsAtFrame1000 == allocsAtFrame200);
        std::cout << "  [PASS] RetiredGeometryReusesBatchStorageWithoutGrowingToBudget (peak=" << maxGeometryBytes
                  << " B, steady_allocs=" << (allocsAtFrame1000 - allocsAtFrame200) << ")\n";
    }

    // 2. End-to-end Surface + DOM dynamic CSS width animation (exact Home IdleChestProgress pattern).
    void TestAnimatedSurfaceDynamicGeometryStaysBounded()
    {
        TestSurface surface(640, 360);
        const char* html =
            "<!doctype html><html><head><style>"
            "body{margin:0;width:640px;height:360px;background-color:#111;}"
            "#bar{width:10px;height:20px;background-color:#3af;border-radius:4px;}"
            "#static{width:100px;height:40px;background-color:#444;}"
            "</style></head><body><div id='static'></div><div id='bar'></div></body></html>";
        URUIDocumentHandle doc = LoadTestDocument(surface, html, "/leak-bar.html");
        const uint64_t bar = FindTestElement(surface, doc, "bar");
        assert(bar != 0);

        URUIPlanBindings bindings{};
        uint64_t allocsAt100 = 0;
        uint64_t allocsAt350 = 0;
        uint64_t peakGeometryBytes = 0;

        for (int frame = 0; frame < 350; ++frame)
        {
            const std::string width = std::to_string(10 + (frame % 200)) + "px";
            assert(urui_element_write(surface, doc, bar, 4, "width", width.c_str()) == URUI_OK);
            assert(urui_surface_update(surface) == URUI_OK);
            assert(urui_surface_render(surface) == URUI_OK);

            URUIPlanInputPacket input{};
            URUIRenderPlanPacket plan{};
            assert(urui_plan_begin(surface, &input) == URUI_OK);
            assert(urui_plan_compile(surface, input.lease, &bindings, &plan) == URUI_OK);
            if (plan.geometry_bytes > peakGeometryBytes)
            {
                peakGeometryBytes = plan.geometry_bytes;
            }
            if (frame == 100)
            {
                allocsAt100 = plan.allocation_count;
            }
            if (frame == 349)
            {
                allocsAt350 = plan.allocation_count;
            }
            assert(urui_plan_release(surface, input.lease) == URUI_OK);
            assert(urui_plan_release(surface, plan.lease) == URUI_OK);
        }

        assert(peakGeometryBytes < 256 * 1024);
        assert((allocsAt350 - allocsAt100) <= 8);
        assert(urui_document_close(surface, doc) == URUI_OK);
        std::cout << "  [PASS] AnimatedSurfaceDynamicGeometryStaysBounded (peak=" << peakGeometryBytes
                  << " B, steady_allocs=" << (allocsAt350 - allocsAt100) << ")\n";
    }

    // 3. Dead & disabled event subscriptions must be pruned on urui_surface_update (B9).
    void TestDeadSubscriptionsPrunedDuringSurfaceUpdate()
    {
        TestSurface surface(320, 240);
        auto* ctx = FindSurfaceInternal(surface);
        assert(ctx != nullptr);

        const char* html = "<!doctype html><html><head></head><body><div id='host'></div></body></html>";
        URUIDocumentHandle doc = LoadTestDocument(surface, html, "/leak-subs.html");
        const uint64_t host = FindTestElement(surface, doc, "host");
        assert(host != 0);
        const size_t baseline = ctx->subscriptions.size();

        std::string markup;
        for (int i = 0; i < 120; ++i)
        {
            markup += "<div id='row-" + std::to_string(i) + "'></div>";
        }
        assert(urui_element_set_inner_html(surface, doc, host, markup.c_str(), "/leak-subs.html") == URUI_OK);
        assert(urui_surface_update(surface) == URUI_OK);

        for (int i = 0; i < 120; ++i)
        {
            const std::string id = "row-" + std::to_string(i);
            const uint64_t tok = FindTestElement(surface, doc, id.c_str());
            assert(tok != 0);
            assert(urui_element_listen(surface, doc, tok, 1, 0, 1) == URUI_OK);
        }
        assert(ctx->subscriptions.size() >= baseline + 120);

        // Disable half explicitly, destroy the other half via SetInnerHtml("").
        for (int i = 0; i < 60; ++i)
        {
            const std::string id = "row-" + std::to_string(i);
            const uint64_t tok = FindTestElement(surface, doc, id.c_str());
            assert(urui_element_listen(surface, doc, tok, 1, 0, 0) == URUI_OK);
        }
        assert(urui_element_set_inner_html(surface, doc, host, "", "/leak-subs.html") == URUI_OK);
        assert(urui_surface_update(surface) == URUI_OK);

        assert(ctx->subscriptions.size() == baseline);
        assert(urui_document_close(surface, doc) == URUI_OK);
        std::cout << "  [PASS] DeadSubscriptionsPrunedDuringSurfaceUpdate (baseline=" << baseline
                  << ", after=" << ctx->subscriptions.size() << ")\n";
    }

    // 4. Evicted pinned StyleStore snapshots must be reclaimed immediately upon Release (B6).
    void TestPinnedStyleSnapshotEvictsOnRelease()
    {
        TestSurface surface(320, 240);
        const char* html = "<!doctype html><html><head></head><body><div id='host'></div></body></html>";
        URUIDocumentHandle doc = LoadTestDocument(surface, html, "/leak-style.html");
        const uint64_t host = FindTestElement(surface, doc, "host");
        assert(host != 0);

        for (int i = 0; i < 200; ++i)
        {
            const std::string markup = "<div id='item' style='width:" + std::to_string(10 + i) + "px;'></div>";
            assert(urui_element_set_inner_html(surface, doc, host, markup.c_str(), "/leak-style.html") == URUI_OK);
            assert(urui_surface_update(surface) == URUI_OK);
            const uint64_t item = FindTestElement(surface, doc, "item");
            assert(item != 0);

            URUIFrozenStyleHandle frozen{};
            assert(urui_style_snapshot_freeze(surface, doc, item, nullptr, &frozen) == URUI_OK);
            // Destroy the element and prune while the snapshot is pinned.
            assert(urui_element_set_inner_html(surface, doc, host, "", "/leak-style.html") == URUI_OK);
            assert(urui_surface_update(surface) == URUI_OK);
            // Releasing the last pin must erase the evicted snapshot immediately.
            assert(urui_style_snapshot_release(surface, doc, &frozen) == URUI_OK);
        }

        uint64_t diag[4]{};
        assert(urui_style_diagnostics(surface, diag, 4) == URUI_OK);
        assert(diag[3] <= 2);
        assert(urui_document_close(surface, doc) == URUI_OK);
        std::cout << "  [PASS] PinnedStyleSnapshotEvictsOnRelease (retained_snapshots=" << diag[3] << ")\n";
    }

    // 5. Repeated reloads with identical --urui-font-effect and varying inline gradients / textures stay bounded (B2, B3, B4).
    void TestFontEffectsAndDecoratorAndTextureCachesStayBounded(const char* fontsDir)
    {
        TestSurface surface(320, 240);
        auto* ctx = FindSurfaceInternal(surface);
        assert(ctx != nullptr);
        const std::string fontPath = std::string(fontsDir) + "/LiberationSans-Regular.ttf.bytes";
        std::ifstream input(fontPath, std::ios::binary);
        std::vector<uint8_t> fontBytes((std::istreambuf_iterator<char>(input)), {});
        assert(!fontBytes.empty());
        assert(urui_surface_set_default_font_face(surface, 1, fontBytes.data(), int(fontBytes.size()), 0, 0, 400) == URUI_OK);

        const uint8_t px[4] = {255, 128, 64, 255};
        size_t texturesAfterWarmup = 0;
        for (int i = 0; i < 120; ++i)
        {
            URUIDocumentHandle doc = 0;
            assert(urui_document_create(surface, &doc) == URUI_OK);
            assert(urui_document_set_presentation(surface, doc, 0, 0, 1, 0) == URUI_OK);
            const std::string imgUrl = "/dyn-tex-" + std::to_string(i) + ".png";
            uint64_t texId = 0;
            assert(urui_document_register_rgba_texture(surface, doc, imgUrl.c_str(), 1, 1, px, 4, &texId) == URUI_OK);
            const std::string html =
                "<!doctype html><html><head><style>"
                "body{font-family:sans-serif;font-size:14px;}"
                ".t{--urui-font-effect:outline(1px black);}"
                ".d{background-image:linear-gradient(" +
                std::to_string(i % 360) + "deg, #111, #eee);}"
                "</style></head><body><div class='d'><span class='t'>Hello</span><img src='" +
                imgUrl + "' style='width:8px;height:8px;'></div></body></html>";
            assert(urui_document_load_html(surface, doc, html.c_str(), "/leak-caches.html") == URUI_OK);
            assert(urui_surface_update(surface) == URUI_OK);
            assert(urui_surface_render(surface) == URUI_OK);
            assert(urui_document_close(surface, doc) == URUI_OK);
            if (i == 5)
            {
                texturesAfterWarmup = ctx->renderer.textures.size();
            }
        }
        // Semantic FontEffect deduplication (B2) ensures repeated parses of identical
        // outline(1px black) reuse the existing font layer & atlas instead of creating 120 new layers/textures.
        const size_t texturesAfterAll = ctx->renderer.textures.size();
        assert(texturesAfterAll <= texturesAfterWarmup + 2);
        std::cout << "  [PASS] FontEffectsAndDecoratorAndTextureCachesStayBounded (warmup_tex="
                  << texturesAfterWarmup << ", final_tex=" << texturesAfterAll << ")\n";
    }
}

int main(int argc, char** argv)
{
    assert(argc >= 2);
    std::cout << "Running URUI native memory leak regression tests...\n";
    TestRetiredGeometryReusesBatchStorageWithoutGrowingToBudget();
    TestAnimatedSurfaceDynamicGeometryStaysBounded();
    TestDeadSubscriptionsPrunedDuringSurfaceUpdate();
    TestPinnedStyleSnapshotEvictsOnRelease();
    TestFontEffectsAndDecoratorAndTextureCachesStayBounded(argv[1]);
    std::cout << "ALL native memory leak regression tests PASSED.\n";
    return 0;
}
