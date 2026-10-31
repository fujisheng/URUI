#if defined(URUI_DEBUG)
// Private diagnostic entry points. Selftests mutate the isolated test document.
// Call selftests only after create and before loading application documents.
extern "C"
{
    int urui_profile_take(UruiProfile* out)
    {
        if (!out)
            return 0;
        *out = profile;
        profile = {};
        return 1;
    }

    void urui_api_profile_enable(int enabled)
    {
        api_profile_enabled = enabled != 0;
        api_profile = {};
    }

    int urui_api_profile_take(UruiApiProfile* out)
    {
        if (!out)
            return 0;
        *out = api_profile;
        api_profile = {};
        return 1;
    }

    int urui_typed_probe_mode(int mode)
    {
        if (mode < 0 || mode > 2)
            return 0;
        typed_probe_mode = mode;
        typed_probe = {};
        return 1;
    }

    int urui_typed_probe_take(UruiTypedProbe* out)
    {
        if (!out)
            return 0;
        *out = typed_probe;
        typed_probe = {};
        return 1;
    }

    int urui_style_probe_mode(int mode)
    {
#if defined(URUI_DEBUG)
        if (mode < 0 || mode > 2)
            return 0;
        urui_style_probe::mode = mode;
        urui_style_probe::total = {};
        return 1;
#else
        return mode == 0 ? 1 : 0;
#endif
    }

    int urui_style_probe_take(UruiStyleProbe* out)
    {
        if (!out)
            return 0;
#if defined(URUI_DEBUG)
        *out = urui_style_probe::total;
        urui_style_probe::total = {};
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
            return 0;
        urui_lookup::profile_mode = mode;
        urui_lookup::profile = {};
        return 1;
#else
        return mode == 0 ? 1 : 0;
#endif
    }

    int urui_lookup_profile_take(UruiLookupProfile* out)
    {
        if (!out)
            return 0;
#if defined(URUI_DEBUG)
        *out = urui_lookup::profile;
        urui_lookup::profile = {};
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
            return 0;
        urui_lookup::inline_enabled = enabled != 0;
        return 1;
#else
        return enabled == 0 ? 1 : 0;
#endif
    }

    int urui_texture_lifecycle_selftest(void)
    {
        int pass = 0;
        uint64_t handle = 0;
        const uint8_t pixels[] = {255, 0, 0, 128, 0, 255, 0, 255};
        if (urui_register_rgba_texture("test/image.png", 2, 1, pixels, 8, &handle) == 1 && handle != 0)
        {
            int w = 0, h = 0, bytes = 0;
            uint8_t copy[8] = {};
            if (urui_get_texture_info(handle, &w, &h, &bytes) == 1 && w == 2 && h == 1 && bytes == 8 &&
                urui_copy_texture(handle, copy, 8) == 1 && copy[0] == 128 && copy[3] == 128 && copy[5] == 255)
                pass |= 1;
            if (urui_load_document(
                    "<rml><body><img src='test/image.png' style='width:40px;height:40px;' /></body></rml>"))
            {
                urui_update();
                urui_render();
                for (const auto& command : render->commands)
                    if (command.texture == handle)
                    {
                        pass |= 2;
                        break;
                    }
                if (urui_unregister_texture("test/image.png") < 0)
                    pass |= 4;
                urui_unload_document();
            }
            if (urui_unregister_texture("test/image.png") == 1 && urui_texture_exists(handle) == 0)
                pass |= 8;
        }
        if (urui_register_rgba_texture("test/bad.png", 2, 1, pixels, 7, &handle) < 0)
            pass |= 16;
        uint64_t first = 0, replacement = 0;
        if (urui_register_rgba_texture("test/replaced.png", 2, 1, pixels, 8, &first) == 1 &&
            urui_register_rgba_texture("test/replaced.png", 2, 1, pixels, 8, &replacement) == 1 &&
            first != replacement && urui_texture_exists(first) == 0 && urui_texture_exists(replacement) == 1 &&
            urui_unregister_texture("test/replaced.png") == 1)
            pass |= 32;
        const uint8_t updated[] = {0, 0, 255, 255, 0, 255, 0, 255};
        uint64_t reloaded = 0;
        if (urui_register_rgba_texture("test/image.png", 2, 1, updated, 8, &reloaded) == 1 && reloaded != handle &&
            urui_load_document("<rml><body><img src='test/image.png' style='width:40px;height:40px;' /></body></rml>"))
        {
            urui_update();
            urui_render();
            bool new_handle = false;
            for (const auto& command : render->commands)
                if (command.texture == reloaded)
                    new_handle = true;
            uint8_t copy[8] = {};
            if (new_handle && urui_copy_texture(reloaded, copy, 8) == 1 && copy[2] == 255 && copy[3] == 255)
                pass |= 64;
            urui_unload_document();
            urui_unregister_texture("test/image.png");
        }
        uint64_t before = 0, after = 0;
        if (urui_register_rgba_texture("test/swap.png", 2, 1, pixels, 8, &before) == 1 &&
            urui_load_document("<rml><body><img src='test/swap.png' style='width:40px;height:40px;' /></body></rml>"))
        {
            urui_update();
            urui_render();
            urui_unload_document();
            if (urui_register_rgba_texture("test/swap.png", 2, 1, updated, 8, &after) == 1 && before != after &&
                urui_texture_exists(before) == 0 &&
                urui_load_document(
                    "<rml><body><img src='test/swap.png' style='width:40px;height:40px;' /></body></rml>"))
            {
                urui_update();
                urui_render();
                for (const auto& command : render->commands)
                    if (command.texture == after)
                        pass |= 128;
                urui_unload_document();
            }
            urui_unregister_texture("test/swap.png");
        }
        return pass;
    }

    int urui_touch_selftest(void)
    {
        int pass = 0;
        char id[40] = {};
        if (!urui_load_document("<rml><body style='margin:0;'><div id='act-a' "
                                "style='position:absolute;left:0;top:0;width:90px;height:90px;background-color:#fff;'><"
                                "/div><div id='act-b' "
                                "style='position:absolute;left:100px;top:0;width:90px;height:90px;background-color:#"
                                "fff;'></div></body></rml>"))
            return pass;
        urui_update();
        if (urui_hit_test_id(25, 25, id, sizeof(id)) == 5 && std::strcmp(id, "act-a") == 0)
            pass |= 1;
        urui_touch_event(0, 11, 25, 25);
        urui_touch_event(2, 11, 25, 25);
        if (!clicks.empty() && clicks.back() == "act-a")
            pass |= 2;
        clicks.clear();
        urui_touch_event(0, 12, 25, 25);
        urui_touch_event(3, 12, 25, 25);
        if (clicks.empty())
            pass |= 4;
        if (urui_touch_event(1, 999, 25, 25) == 0 && urui_touch_event(3, 999, 25, 25) == 0)
            pass |= 8;
        urui_touch_event(0, 13, 25, 25);
        urui_touch_event(1, 13, 125, 25);
        if (urui_hit_test_id(125, 25, id, sizeof(id)) == 5 && std::strcmp(id, "act-b") == 0)
            pass |= 16;
        urui_touch_event(2, 13, 125, 25);
        urui_unload_document();
        if (urui_load_document("<rml><body><div id='act-new' "
                               "style='width:90px;height:90px;background-color:#fff;'></div></body></rml>"))
        {
            urui_update();
            urui_touch_event(0, 14, 25, 25);
            urui_unload_document();
            if (urui_load_document("<rml><body><div id='act-new' "
                                   "style='width:90px;height:90px;background-color:#fff;'></div></body></rml>"))
            {
                urui_update();
                if (urui_touch_event(0, 14, 25, 25) == 1 && urui_touch_event(3, 14, 25, 25) == 1)
                    pass |= 32;
                urui_unload_document();
            }
        }
        if (urui_load_document("<rml><body style='margin:0;'><div id='plain' "
                               "style='position:absolute;left:0;top:0;width:90px;height:90px;background-color:#fff;'></"
                               "div></body></rml>"))
        {
            urui_update();
            clicks.clear();
            urui_touch_event(0, 15, 25, 25);
            urui_touch_event(2, 15, 25, 25);
            if (!clicks.empty() && clicks.back() == "plain")
                pass |= 64;
            urui_unload_document();
        }
        if (urui_load_document("<rml><body style='margin:0;'><div id='first' "
                               "style='width:90px;height:90px;background-color:#fff;'></div></body></rml>"))
        {
            urui_update();
            if (urui_touch_event(0, 20, 25, 25) == 1 && urui_touch_event(0, 21, 25, 25) == -2 &&
                urui_touch_event(2, 21, 25, 25) == 0 && urui_touch_event(3, 20, 25, 25) == 1)
                pass |= 128;
            urui_unload_document();
        }
        if (urui_load_document("<rml><body style='margin:0;'><div id='gone' "
                               "style='width:90px;height:90px;background-color:#fff;'></div></body></rml>"))
        {
            urui_update();
            clicks.clear();
            if (urui_touch_event(0, 22, 25, 25) == 1 && urui_remove_element("gone") == 1 &&
                urui_touch_event(1, 22, 30, 30) == 1 && urui_touch_event(2, 22, 30, 30) == 1 && clicks.empty())
                pass |= 256;
            urui_unload_document();
        }
        return pass;
    }

    int urui_dom_selftest(void)
    {
        int pass = 0;
        if (!urui_load_document(
                "<rml><body><div id='root'><div id='dup'></div><div id='dup'></div></div></body></rml>"))
            return 0;
        auto* root = document->GetElementById("root");
        if (root && root->GetNumChildren() == 2 && document->GetElementById("dup") == root->GetChild(0))
            pass |= 1;
        if (urui_set_attribute("dup", "data-test", "first") == 1 &&
            root->GetChild(0)->GetAttribute<Rml::String>("data-test", "") == "first" &&
            root->GetChild(1)->GetAttribute<Rml::String>("data-test", "").empty())
            pass |= 2;
        if (urui_append_element("root", "div", "dynamic") == 1 && urui_element_exists("dynamic") == 1)
            pass |= 4;
        if (urui_set_style("dynamic", "opacity", "0.5") == 1 &&
            urui_set_inner_rml("dynamic", "<span id='nested'>hello</span>") == 1 && urui_element_exists("nested") == 1)
            pass |= 8;
        if (urui_remove_element("dynamic") == 1 && urui_element_exists("dynamic") == 0 &&
            urui_element_exists("nested") == 0)
            pass |= 16;
        if (urui_set_attribute("dup", "id", "changed") == 1 && urui_element_exists("changed") == 1 &&
            urui_element_exists("dup") == 1)
            pass |= 32;
        urui_unload_document();
        if (urui_element_exists("root") == 0 && urui_set_style("root", "opacity", "1") == 0)
            pass |= 64;
        return pass;
    }

    int urui_transform_selftest(void)
    {
        if (!urui_load_document("<rml><body><div "
                                "style='position:absolute;left:20px;top:20px;width:60px;height:60px;background-color:#"
                                "fff;transform:translate(12px,4px) rotate(15deg);'></div></body></rml>"))
            return 0;
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
                changed = true;
        }
        urui_unload_document();
        return count > 0 && result == count && changed ? 1 : 0;
    }
}

#endif
