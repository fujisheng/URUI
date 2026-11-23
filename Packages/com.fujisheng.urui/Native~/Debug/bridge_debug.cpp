#if defined(URUI_DEBUG)
#include "../bridge_runtime.h"
#include "diagnostics.h"
#include <cmath>
#include <cstring>
#include <string>
#include <vector>

using namespace URUI::Bridge;
using namespace URUI::Debug;

namespace
{
    struct DiagnosticDocument
    {
        URUISurfaceHandle surface = 0;
        URUIDocumentHandle owner = 0;
        explicit DiagnosticDocument(URUISurfaceHandle s) : surface(s)
        {
        }
        ~DiagnosticDocument()
        {
            Close();
        }
        bool Create()
        {
            Close();
            return urui_document_create(surface, &owner) == URUI_OK;
        }
        bool Load(const char* html)
        {
            if (!owner && !Create())
            {
                return false;
            }
            return urui_document_load_html(surface, owner, html, "/urui-debug/fixture.html") == URUI_OK &&
                   urui_document_set_presentation(surface, owner, 0, 0, 1, 0) == URUI_OK;
        }
        URUI::ElementDocument* Element() const
        {
            auto* s = FindSurfaceInternal(surface);
            return s ? ResolveDocument(*s, owner) : nullptr;
        }
        void Close()
        {
            if (owner)
            {
                urui_document_close(surface, owner);
            }
            owner = 0;
        }
    };

    std::string ReadElementId(URUISurfaceHandle surface, URUIDocumentHandle owner, uint64_t token)
    {
        if (!token)
        {
            return {};
        }
        char buffer[128]{};
        int required = 0;
        if (urui_element_read(surface, owner, token, 0, nullptr, buffer, sizeof(buffer), &required) == URUI_OK)
        {
            return std::string(buffer);
        }
        return {};
    }

    std::string PickId(URUISurfaceHandle surface, URUIDocumentHandle expectedOwner, float x, float y)
    {
        URUIDocumentHandle owner = 0;
        const uint64_t token = urui_surface_pick(surface, x, y, &owner);
        if (!token || owner != expectedOwner)
        {
            return {};
        }
        return ReadElementId(surface, owner, token);
    }

    bool PopClickId(URUISurfaceHandle surface, URUIDocumentHandle owner, std::string& outId)
    {
        uint64_t token = 0;
        if (urui_element_pop_click(surface, owner, &token) != URUI_OK)
        {
            return false;
        }
        outId = ReadElementId(surface, owner, token);
        return true;
    }

    void DrainClicks(URUISurfaceHandle surface, URUIDocumentHandle owner)
    {
        uint64_t token = 0;
        while (urui_element_pop_click(surface, owner, &token) == URUI_OK)
        {
        }
    }
}

extern "C"
{
    int urui_profile_take(URUIProfile* out)
    {
        if (!out)
        {
            return 0;
        }
        *out = profile;
        profile = {};
        return 1;
    }

    void urui_api_profile_enable(int enabled)
    {
        api_profile_enabled = enabled != 0;
        api_profile = {};
    }

    int urui_api_profile_take(URUIApiProfile* out)
    {
        if (!out)
        {
            return 0;
        }
        *out = api_profile;
        api_profile = {};
        return 1;
    }

    int urui_typed_probe_mode(int mode)
    {
        if (mode < 0 || mode > 2)
        {
            return 0;
        }
        typed_probe_mode = mode;
        typed_probe = {};
        return 1;
    }

    int urui_typed_probe_take(URUITypedProbe* out)
    {
        if (!out)
        {
            return 0;
        }
        *out = typed_probe;
        typed_probe = {};
        return 1;
    }

    int urui_style_probe_mode(int mode)
    {
        if (mode < 0 || mode > 2)
        {
            return 0;
        }
        URUI::StyleProbe::mode = mode;
        URUI::StyleProbe::total = {};
        return 1;
    }

    int urui_style_probe_take(URUIStyleProbe* out)
    {
        if (!out)
        {
            return 0;
        }
        *out = URUI::StyleProbe::total;
        URUI::StyleProbe::total = {};
        return 1;
    }

    int urui_lookup_profile_enable(int mode)
    {
        if (mode < 0 || mode > 2)
        {
            return 0;
        }
        URUI::LookupProbe::profile_mode = mode;
        URUI::LookupProbe::profile = {};
        return 1;
    }

    int urui_lookup_profile_take(URUILookupProfile* out)
    {
        if (!out)
        {
            return 0;
        }
        *out = URUI::LookupProbe::profile;
        URUI::LookupProbe::profile = {};
        return 1;
    }

    int urui_lookup_inline_enable(int enabled)
    {
        if (enabled != 0 && enabled != 1)
        {
            return 0;
        }
        URUI::LookupProbe::inline_enabled = enabled != 0;
        return 1;
    }

    int urui_texture_lifecycle_selftest(URUISurfaceHandle surfaceHandle)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, true) != URUI_OK)
        {
            return 0;
        }
        DiagnosticDocument fixture(surfaceHandle);
        if (!fixture.Create())
        {
            return 0;
        }
        int pass = 0;
        uint64_t handle = 0;
        const char* source = "/urui-debug/image.png";
        const char* html = "<!doctype html><html><head></head><body><img src='/urui-debug/image.png' "
                           "style='width:40px;height:40px;'></body></html>";
        const uint8_t pixels[] = {255, 0, 0, 128, 0, 255, 0, 255};
        const uint8_t updated[] = {0, 0, 255, 255, 0, 255, 0, 255};
        auto Draws = [&](uint64_t texture)
        {
            urui_surface_update(surfaceHandle);
            urui_surface_render(surfaceHandle);
            for (const auto& command : surface->renderer.commands)
            {
                if (command.texture == texture)
                {
                    return true;
                }
            }
            return false;
        };
        if (urui_document_register_rgba_texture(surfaceHandle, fixture.owner, source, 2, 1, pixels, 8, &handle) ==
                URUI_OK &&
            handle)
        {
            int width = 0;
            int height = 0;
            int bytes = 0;
            uint8_t copy[8] = {};
            if (urui_get_texture_info(surfaceHandle, handle, &width, &height, &bytes) == URUI_OK && width == 2 &&
                height == 1 && bytes == 8 && urui_copy_texture(surfaceHandle, handle, copy, 8) == URUI_OK &&
                copy[0] == 128 && copy[3] == 128 && copy[5] == 255)
            {
                pass |= 1;
            }
            if (fixture.Load(html))
            {
                if (Draws(handle))
                {
                    pass |= 2;
                }
                if (urui_document_unregister_texture(surfaceHandle, fixture.owner, source) == URUI_OK && Draws(handle))
                {
                    pass |= 4;
                }
            }
            fixture.Close();
            if (!urui_texture_exists(surfaceHandle, handle))
            {
                pass |= 8;
            }
        }
        if (!fixture.Create())
        {
            return pass;
        }
        uint64_t invalid = 0;
        if (urui_document_register_rgba_texture(surfaceHandle, fixture.owner, source, 2, 1, pixels, 7, &invalid) < 0)
        {
            pass |= 16;
        }
        uint64_t reloaded = 0;
        if (urui_document_register_rgba_texture(surfaceHandle, fixture.owner, source, 2, 1, updated, 8, &reloaded) ==
                URUI_OK &&
            reloaded && reloaded != handle && !urui_texture_exists(surfaceHandle, handle))
        {
            pass |= 32;
        }
        if (fixture.Load(html) && Draws(reloaded))
        {
            uint8_t copy[8] = {};
            if (urui_copy_texture(surfaceHandle, reloaded, copy, 8) == URUI_OK && copy[2] == 255 && copy[3] == 255)
            {
                pass |= 64;
            }
            uint64_t replacement = 0;
            if (urui_document_register_rgba_texture(surfaceHandle, fixture.owner, source, 2, 1, pixels, 8,
                                                    &replacement) == URUI_OK &&
                replacement != reloaded && Draws(reloaded) && fixture.Load(html) && Draws(replacement) &&
                !urui_texture_exists(surfaceHandle, reloaded))
            {
                pass |= 128;
            }
        }
        return pass;
    }

    int urui_touch_selftest(URUISurfaceHandle surfaceHandle)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, true) != URUI_OK)
        {
            return 0;
        }
        DiagnosticDocument fixture(surfaceHandle);
        int pass = 0;
        auto touch = [&](int phase, int pointer, float x, float y)
        { return urui_pointer_event(surfaceHandle, pointer, 1, phase, x, y, 0, phase < 2 ? 1 : 0, 0, 0, 0); };
        if (!fixture.Load("<!doctype html><html><head></head><body style='margin:0;'><div id='act-a' "
                          "style='position:absolute;left:0;top:0;width:90px;height:90px;background-color:#fff;'><"
                          "/div><div id='act-b' "
                          "style='position:absolute;left:100px;top:0;width:90px;height:90px;background-color:#"
                          "fff;'></div></body></html>"))
        {
            return pass;
        }
        urui_surface_update(surfaceHandle);
        if (PickId(surfaceHandle, fixture.owner, 25, 25) == "act-a")
        {
            pass |= 1;
        }
        touch(0, 11, 25, 25);
        touch(2, 11, 25, 25);
        std::string clicked;
        if (PopClickId(surfaceHandle, fixture.owner, clicked) && clicked == "act-a")
        {
            pass |= 2;
        }
        DrainClicks(surfaceHandle, fixture.owner);
        touch(0, 12, 25, 25);
        touch(3, 12, 25, 25);
        if (!PopClickId(surfaceHandle, fixture.owner, clicked))
        {
            pass |= 4;
        }
        if (touch(2, 999, 25, 25) == URUI_OK && touch(3, 999, 25, 25) == URUI_OK &&
            !PopClickId(surfaceHandle, fixture.owner, clicked))
        {
            pass |= 8;
        }
        touch(0, 13, 25, 25);
        touch(1, 13, 125, 25);
        if (PickId(surfaceHandle, fixture.owner, 125, 25) == "act-b")
        {
            pass |= 16;
        }
        touch(2, 13, 125, 25);
        fixture.Close();
        if (fixture.Load("<!doctype html><html><head></head><body><div id='act-new' "
                         "style='width:90px;height:90px;background-color:#fff;'></div></body></html>"))
        {
            urui_surface_update(surfaceHandle);
            touch(0, 14, 25, 25);
            fixture.Close();
            if (fixture.Load("<!doctype html><html><head></head><body><div id='act-new' "
                             "style='width:90px;height:90px;background-color:#fff;'></div></body></html>"))
            {
                urui_surface_update(surfaceHandle);
                if (touch(0, 14, 25, 25) == URUI_OK && touch(3, 14, 25, 25) == URUI_OK)
                {
                    pass |= 32;
                }
                fixture.Close();
            }
        }
        if (fixture.Load("<!doctype html><html><head></head><body style='margin:0;'><div id='plain' "
                         "style='position:absolute;left:0;top:0;width:90px;height:90px;background-color:#fff;'></"
                         "div></body></html>"))
        {
            urui_surface_update(surfaceHandle);
            DrainClicks(surfaceHandle, fixture.owner);
            touch(0, 15, 25, 25);
            touch(2, 15, 25, 25);
            if (PopClickId(surfaceHandle, fixture.owner, clicked) && clicked == "plain")
            {
                pass |= 64;
            }
            fixture.Close();
        }
        if (fixture.Load("<!doctype html><html><head></head><body style='margin:0;'><div id='first' "
                         "style='width:90px;height:90px;background-color:#fff;'></div></body></html>"))
        {
            urui_surface_update(surfaceHandle);
            DrainClicks(surfaceHandle, fixture.owner);
            if (touch(0, 20, 25, 25) == URUI_OK && touch(0, 21, 25, 25) == URUI_OK &&
                touch(2, 21, 25, 25) == URUI_OK && touch(3, 20, 25, 25) == URUI_OK &&
                PopClickId(surfaceHandle, fixture.owner, clicked) && clicked == "first" &&
                !PopClickId(surfaceHandle, fixture.owner, clicked))
            {
                pass |= 128;
            }
            fixture.Close();
        }
        if (fixture.Load("<!doctype html><html><head></head><body style='margin:0;'><div id='gone' "
                         "style='width:90px;height:90px;background-color:#fff;'></div></body></html>"))
        {
            urui_surface_update(surfaceHandle);
            DrainClicks(surfaceHandle, fixture.owner);
            const uint64_t gone = urui_element_ref_acquire(surfaceHandle, fixture.owner, "gone");
            if (gone && touch(0, 22, 25, 25) == URUI_OK &&
                urui_element_destroy(surfaceHandle, fixture.owner, gone) == URUI_OK &&
                touch(1, 22, 30, 30) == URUI_OK && touch(2, 22, 30, 30) == URUI_OK &&
                !PopClickId(surfaceHandle, fixture.owner, clicked))
            {
                pass |= 256;
            }
            fixture.Close();
        }
        return pass;
    }

    int urui_dom_selftest(URUISurfaceHandle surfaceHandle)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, true) != URUI_OK)
        {
            return 0;
        }
        DiagnosticDocument fixture(surfaceHandle);
        int pass = 0;
        if (!fixture.Load("<!doctype html><html><head></head><body><div id='root'><div id='dup'></div><div "
                          "id='dup'></div></div></body></html>"))
        {
            return 0;
        }
        auto* document = fixture.Element();
        auto* rootEl = document ? document->GetElementById("root") : nullptr;
        if (rootEl && rootEl->GetNumChildren() == 2 && document->GetElementById("dup") == rootEl->GetChild(0))
        {
            pass |= 1;
        }
        const uint64_t rootTok = urui_element_ref_acquire(surfaceHandle, fixture.owner, "root");
        const uint64_t dupTok = urui_element_ref_acquire(surfaceHandle, fixture.owner, "dup");
        if (dupTok && urui_element_write(surfaceHandle, fixture.owner, dupTok, 2, "data-test", "first") == URUI_OK &&
            rootEl->GetChild(0)->GetAttribute<URUI::String>("data-test", "") == "first" &&
            rootEl->GetChild(1)->GetAttribute<URUI::String>("data-test", "").empty())
        {
            pass |= 2;
        }
        const uint64_t dynTok = urui_element_create(surfaceHandle, fixture.owner, "div");
        if (rootTok && dynTok && urui_element_write(surfaceHandle, fixture.owner, dynTok, 0, nullptr, "dynamic") == URUI_OK &&
            urui_element_insert(surfaceHandle, fixture.owner, rootTok, dynTok, -1) == URUI_OK &&
            urui_element_find(surfaceHandle, fixture.owner, rootTok, "dynamic", nullptr) == dynTok)
        {
            pass |= 4;
        }
        if (dynTok && urui_element_write(surfaceHandle, fixture.owner, dynTok, 4, "opacity", "0.5") == URUI_OK &&
            urui_element_write(surfaceHandle, fixture.owner, dynTok, 3, nullptr, "<span id='nested'>hello</span>") ==
                URUI_OK &&
            urui_element_find(surfaceHandle, fixture.owner, rootTok, "nested", nullptr) != 0)
        {
            pass |= 8;
        }
        if (dynTok && urui_element_destroy(surfaceHandle, fixture.owner, dynTok) == URUI_OK &&
            urui_element_find(surfaceHandle, fixture.owner, rootTok, "dynamic", nullptr) == 0 &&
            urui_element_find(surfaceHandle, fixture.owner, rootTok, "nested", nullptr) == 0)
        {
            pass |= 16;
        }
        if (dupTok && urui_element_write(surfaceHandle, fixture.owner, dupTok, 2, "id", "changed") == URUI_OK &&
            urui_element_find(surfaceHandle, fixture.owner, rootTok, "changed", nullptr) != 0 &&
            urui_element_find(surfaceHandle, fixture.owner, rootTok, "dup", nullptr) != 0)
        {
            pass |= 32;
        }
        const URUIDocumentHandle closedOwner = fixture.owner;
        fixture.Close();
        if (urui_element_ref_valid(surfaceHandle, closedOwner, rootTok) == 0 &&
            urui_element_write(surfaceHandle, closedOwner, rootTok, 4, "opacity", "1") < 0)
        {
            pass |= 64;
        }
        return pass;
    }

    int urui_transform_selftest(URUISurfaceHandle surfaceHandle)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, true) != URUI_OK)
        {
            return 0;
        }
        DiagnosticDocument fixture(surfaceHandle);
        if (!fixture.Load("<!doctype html><html><head></head><body><div "
                          "style='position:absolute;left:20px;top:20px;width:60px;height:60px;background-color:#"
                          "fff;transform:translate(12px,4px) rotate(15deg);'></div></body></html>"))
        {
            return 0;
        }
        urui_surface_update(surfaceHandle);
        URUIPlanInputPacket packet{};
        if (urui_plan_begin(surfaceHandle, &packet) != URUI_OK)
        {
            return 0;
        }
        bool changed = false;
        for (uint32_t i = 0; i < packet.command_count; ++i)
        {
            const float* m = packet.matrices + static_cast<size_t>(i) * 16;
            if (std::abs(m[0] - 1.f) > 0.001f || std::abs(m[1]) > 0.001f || std::abs(m[4]) > 0.001f ||
                std::abs(m[12]) > 0.001f)
            {
                changed = true;
            }
        }
        const uint32_t count = packet.command_count;
        urui_plan_release(surfaceHandle, packet.lease);
        fixture.Close();
        return count > 0 && changed ? 1 : 0;
    }
}
#endif
