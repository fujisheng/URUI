#if defined(URUI_DEBUG)
// Private diagnostic entry points. Selftests mutate the isolated test document.
// Call selftests only after create and before loading application documents.
namespace
{
    // 自检拥有显式文档身份，不占用或切换应用页面。
    struct DiagnosticDocument
    {
        URUIDocumentHandle owner = 0;
        ~DiagnosticDocument()
        {
            Close();
        }
        bool Create()
        {
            Close();
            return urui_document_create(&owner) == 1;
        }
        bool Load(const char* html)
        {
            if (!owner && !Create())
            {
                return false;
            }
            return urui_document_load_html(owner, html, "/urui-debug/fixture.html") == 1 &&
                   urui_document_set_presentation(owner, 0, 0, 1, 0) == 1;
        }
        URUI::ElementDocument* Element() const
        {
            return URUI::Elements::Document(owner);
        }
        void Close()
        {
            if (owner)
            {
                urui_document_close(owner);
            }
            owner = 0;
        }
    };
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
#if defined(URUI_DEBUG)
        if (mode < 0 || mode > 2)
        {
            return 0;
        }
        URUI::StyleProbe::mode = mode;
        URUI::StyleProbe::total = {};
        return 1;
#else
        return mode == 0 ? 1 : 0;
#endif
    }

    int urui_style_probe_take(URUIStyleProbe* out)
    {
        if (!out)
        {
            return 0;
        }
#if defined(URUI_DEBUG)
        *out = URUI::StyleProbe::total;
        URUI::StyleProbe::total = {};
        return 1;
#else
        *out = {};
        return 0;
#endif
    }

    int urui_lookup_profile_enable(int mode)
    {
#if defined(URUI_DEBUG)
        if (mode < 0 || mode > 2)
        {
            return 0;
        }
        URUI::LookupProbe::profile_mode = mode;
        URUI::LookupProbe::profile = {};
        return 1;
#else
        return mode == 0 ? 1 : 0;
#endif
    }

    int urui_lookup_profile_take(URUILookupProfile* out)
    {
        if (!out)
        {
            return 0;
        }
#if defined(URUI_DEBUG)
        *out = URUI::LookupProbe::profile;
        URUI::LookupProbe::profile = {};
        return 1;
#else
        *out = {};
        return 0;
#endif
    }

    int urui_lookup_inline_enable(int enabled)
    {
#if defined(URUI_DEBUG)
        if (enabled != 0 && enabled != 1)
        {
            return 0;
        }
        URUI::LookupProbe::inline_enabled = enabled != 0;
        return 1;
#else
        return enabled == 0 ? 1 : 0;
#endif
    }

    int urui_texture_lifecycle_selftest(void)
    {
        DiagnosticDocument fixture;
        if (!fixture.Create())
        {
            return 0;
        }
        int pass = 0;
        uint64_t handle = 0;
        const char* source = "/urui-debug/image.png";
        const char* html =
            "<!doctype html><html><head></head><body><img src='/urui-debug/image.png' style='width:40px;height:40px;'></body></html>";
        const uint8_t pixels[] = {255, 0, 0, 128, 0, 255, 0, 255};
        const uint8_t updated[] = {0, 0, 255, 255, 0, 255, 0, 255};
        auto Draws = [](uint64_t texture)
        {
            urui_update();
            urui_render();
            for (const auto& command : render->commands)
            {
                if (command.texture == texture)
                {
                    return true;
                }
            }
            return false;
        };
        if (urui_document_register_rgba_texture(fixture.owner, source, 2, 1, pixels, 8, &handle) == 1 && handle)
        {
            int width = 0;
            int height = 0;
            int bytes = 0;
            uint8_t copy[8] = {};
            if (urui_get_texture_info(handle, &width, &height, &bytes) == 1 && width == 2 && height == 1 &&
                bytes == 8 && urui_copy_texture(handle, copy, 8) == 1 && copy[0] == 128 && copy[3] == 128 &&
                copy[5] == 255)
            {
                pass |= 1;
            }
            if (fixture.Load(html))
            {
                if (Draws(handle))
                {
                    pass |= 2;
                }
                // 资源修改只进入候选集合，不能破坏仍在使用的页面。
                if (urui_document_unregister_texture(fixture.owner, source) == 1 && Draws(handle))
                {
                    pass |= 4;
                }
            }
            fixture.Close();
            if (!urui_texture_exists(handle))
            {
                pass |= 8;
            }
        }
        if (!fixture.Create())
        {
            return pass;
        }
        uint64_t invalid = 0;
        if (urui_document_register_rgba_texture(fixture.owner, source, 2, 1, pixels, 7, &invalid) < 0)
        {
            pass |= 16;
        }
        uint64_t reloaded = 0;
        if (urui_document_register_rgba_texture(fixture.owner, source, 2, 1, updated, 8, &reloaded) == 1 && reloaded &&
            reloaded != handle && !urui_texture_exists(handle))
        {
            pass |= 32;
        }
        if (fixture.Load(html) && Draws(reloaded))
        {
            uint8_t copy[8] = {};
            if (urui_copy_texture(reloaded, copy, 8) == 1 && copy[2] == 255 && copy[3] == 255)
            {
                pass |= 64;
            }
            uint64_t replacement = 0;
            if (urui_document_register_rgba_texture(fixture.owner, source, 2, 1, pixels, 8, &replacement) == 1 &&
                replacement != reloaded && Draws(reloaded) && fixture.Load(html) && Draws(replacement) &&
                !urui_texture_exists(reloaded))
            {
                pass |= 128;
            }
        }
        return pass;
    }

    int urui_touch_selftest(void)
    {
        DiagnosticDocument fixture;
        int pass = 0;
        char id[40] = {};
        auto touch = [](int phase, int pointer, float x, float y)
        { return urui_pointer_event(pointer, 1, phase, x, y, 0, phase < 2 ? 1 : 0, 0, 0, 0); };
        if (!fixture.Load("<!doctype html><html><head></head><body style='margin:0;'><div id='act-a' "
                          "style='position:absolute;left:0;top:0;width:90px;height:90px;background-color:#fff;'><"
                          "/div><div id='act-b' "
                          "style='position:absolute;left:100px;top:0;width:90px;height:90px;background-color:#"
                          "fff;'></div></body></html>"))
        {
            return pass;
        }
        urui_update();
        if (urui_hit_test_id(fixture.owner, 25, 25, id, sizeof(id)) == 5 && std::strcmp(id, "act-a") == 0)
        {
            pass |= 1;
        }
        touch(0, 11, 25, 25);
        touch(2, 11, 25, 25);
        if (!clicks.empty() && ClickId(clicks.back()) == "act-a")
        {
            pass |= 2;
        }
        clicks.clear();
        touch(0, 12, 25, 25);
        touch(3, 12, 25, 25);
        if (clicks.empty())
        {
            pass |= 4;
        }
        if (touch(2, 999, 25, 25) == 1 && touch(3, 999, 25, 25) == 1 && clicks.empty())
        {
            pass |= 8;
        }
        touch(0, 13, 25, 25);
        touch(1, 13, 125, 25);
        if (urui_hit_test_id(fixture.owner, 125, 25, id, sizeof(id)) == 5 && std::strcmp(id, "act-b") == 0)
        {
            pass |= 16;
        }
        touch(2, 13, 125, 25);
        fixture.Close();
        if (fixture.Load("<!doctype html><html><head></head><body><div id='act-new' "
                         "style='width:90px;height:90px;background-color:#fff;'></div></body></html>"))
        {
            urui_update();
            touch(0, 14, 25, 25);
            fixture.Close();
            if (fixture.Load("<!doctype html><html><head></head><body><div id='act-new' "
                             "style='width:90px;height:90px;background-color:#fff;'></div></body></html>"))
            {
                urui_update();
                if (touch(0, 14, 25, 25) == 1 && touch(3, 14, 25, 25) == 1)
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
            urui_update();
            clicks.clear();
            touch(0, 15, 25, 25);
            touch(2, 15, 25, 25);
            if (!clicks.empty() && ClickId(clicks.back()) == "plain")
            {
                pass |= 64;
            }
            fixture.Close();
        }
        if (fixture.Load("<!doctype html><html><head></head><body style='margin:0;'><div id='first' "
                         "style='width:90px;height:90px;background-color:#fff;'></div></body></html>"))
        {
            urui_update();
            clicks.clear();
            // Surface 指针协议允许多指，次指结束只能产生自己的点击。
            if (touch(0, 20, 25, 25) == 1 && touch(0, 21, 25, 25) == 1 && touch(2, 21, 25, 25) == 1 &&
                touch(3, 20, 25, 25) == 1 && clicks.size() == 1 && ClickId(clicks.back()) == "first")
            {
                pass |= 128;
            }
            fixture.Close();
        }
        if (fixture.Load("<!doctype html><html><head></head><body style='margin:0;'><div id='gone' "
                         "style='width:90px;height:90px;background-color:#fff;'></div></body></html>"))
        {
            urui_update();
            clicks.clear();
            if (touch(0, 22, 25, 25) == 1 && urui_remove_element(fixture.owner, "gone") == 1 &&
                touch(1, 22, 30, 30) == 1 && touch(2, 22, 30, 30) == 1 && clicks.empty())
            {
                pass |= 256;
            }
            fixture.Close();
        }
        return pass;
    }

    int urui_dom_selftest(void)
    {
        DiagnosticDocument fixture;
        int pass = 0;
        if (!fixture.Load(
                "<!doctype html><html><head></head><body><div id='root'><div id='dup'></div><div id='dup'></div></div></body></html>"))
        {
            return 0;
        }
        auto* document = fixture.Element();
        auto* root = document->GetElementById("root");
        if (root && root->GetNumChildren() == 2 && document->GetElementById("dup") == root->GetChild(0))
        {
            pass |= 1;
        }
        if (urui_set_attribute(fixture.owner, "dup", "data-test", "first") == 1 &&
            root->GetChild(0)->GetAttribute<URUI::String>("data-test", "") == "first" &&
            root->GetChild(1)->GetAttribute<URUI::String>("data-test", "").empty())
        {
            pass |= 2;
        }
        if (urui_append_element(fixture.owner, "root", "div", "dynamic") == 1 &&
            urui_element_exists(fixture.owner, "dynamic") == 1)
        {
            pass |= 4;
        }
        if (urui_set_style(fixture.owner, "dynamic", "opacity", "0.5") == 1 &&
            urui_set_inner_html(fixture.owner, "dynamic", "<span id='nested'>hello</span>") == 1 &&
            urui_element_exists(fixture.owner, "nested") == 1)
        {
            pass |= 8;
        }
        if (urui_remove_element(fixture.owner, "dynamic") == 1 && urui_element_exists(fixture.owner, "dynamic") == 0 &&
            urui_element_exists(fixture.owner, "nested") == 0)
        {
            pass |= 16;
        }
        if (urui_set_attribute(fixture.owner, "dup", "id", "changed") == 1 &&
            urui_element_exists(fixture.owner, "changed") == 1 && urui_element_exists(fixture.owner, "dup") == 1)
        {
            pass |= 32;
        }
        fixture.Close();
        if (urui_element_exists(fixture.owner, "root") == 0 &&
            urui_set_style(fixture.owner, "root", "opacity", "1") == 0)
        {
            pass |= 64;
        }
        return pass;
    }

    int urui_transform_selftest(void)
    {
        DiagnosticDocument fixture;
        if (!fixture.Load("<!doctype html><html><head></head><body><div "
                          "style='position:absolute;left:20px;top:20px;width:60px;height:60px;background-color:#"
                          "fff;transform:translate(12px,4px) rotate(15deg);'></div></body></html>"))
        {
            return 0;
        }
        urui_update();
        urui_render();
        int count = urui_command_count();
        std::vector<float> transforms(static_cast<size_t>(count) * 16);
        int result = urui_get_command_transforms(transforms.data(), static_cast<int>(transforms.size()));
        bool changed = false;
        for (int i = 0; i < count; i++)
        {
            const float* m = transforms.data() + i * 16;
            if (std::abs(m[0] - 1.f) > 0.001f || std::abs(m[1]) > 0.001f || std::abs(m[4]) > 0.001f ||
                std::abs(m[12]) > 0.001f)
            {
                changed = true;
            }
        }
        fixture.Close();
        return count > 0 && result == count && changed ? 1 : 0;
    }
}

#endif
