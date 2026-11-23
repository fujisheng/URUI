#include "urui.h"
#include "test_document.h"
static URUISurfaceHandle g_test_surface = 0;
#include <URUI/Core.h>
#include <cassert>
#include <filesystem>
#include <iostream>
#include "../Engine/Source/Core/FontEngineDefault/FontFaceHandleDefault.h"

int main(int argc, char** argv)
{
    assert(argc == 2 && urui_surface_create(400, 200, &g_test_surface) == 1);
    const auto assets = (std::filesystem::path(__FILE__).parent_path() / "fixtures/fonts").string();
    const auto normal = URUI::Style::FontStyle::Normal;
    const auto weight = URUI::Style::FontWeight::Normal;
    assert(URUI::LoadFontFace(assets + "/LatoLatin-Regular.ttf", "reference-a", normal, weight));
    assert(URUI::LoadFontFace(assets + "/RobotoMono-Regular.ttf", "reference-b", normal, weight));
    auto* engine = URUI::GetFontEngineInterface();
    const URUI::String language = "en";
    const URUI::TextShapingContext shaping{language};
    auto measure = [&](URUI::FontFaceHandle handle)
    {
        assert(handle);
        return engine->GetStringWidth(handle, "iiiiWWWW", shaping);
    };
    const auto a = measure(engine->GetFontFaceHandle("reference-a", normal, weight, 20));
    const auto b = measure(engine->GetFontFaceHandle("reference-b", normal, weight, 20));
    assert(a != b);
    const URUI::String mode = argv[1];
    if (mode == "fallback")
    {
        const auto packaged =
            (std::filesystem::path(__FILE__).parent_path().parent_path().parent_path() / "Runtime/Resources/URUI/Fonts")
                .string();
        const auto sans = packaged + "/LiberationSans-Regular.ttf.bytes";
        const auto mono = packaged + "/LiberationMono-Regular.ttf.bytes";
        assert(URUI::LoadFontFace(sans, "reference-sans", normal, weight));
        assert(URUI::LoadFontFace(mono, "reference-mono", normal, weight));
        const auto latin_handle = engine->GetFontFaceHandle("reference-a", normal, weight, 30);
        const auto sans_handle = engine->GetFontFaceHandle("reference-sans", normal, weight, 30);
        const auto mono_handle = engine->GetFontFaceHandle("reference-mono", normal, weight, 30);
        // U+05D0，Latin 子集没有此字形。
        const URUI::String character = "\xd7\x90";
        auto glyph_width = [&](URUI::FontFaceHandle handle)
        { return engine->GetStringWidth(handle, character, shaping); };
        const auto sans_width = glyph_width(sans_handle);
        const auto mono_width = glyph_width(mono_handle);
        glyph_width(latin_handle);
        const auto codepoint = static_cast<URUI::Character>(0x05d0);
        assert(!reinterpret_cast<URUI::FontFaceHandleDefault*>(latin_handle)->GetGlyphs().count(codepoint));
        assert(reinterpret_cast<URUI::FontFaceHandleDefault*>(sans_handle)->GetGlyphs().count(codepoint));
        assert(reinterpret_cast<URUI::FontFaceHandleDefault*>(mono_handle)->GetGlyphs().count(codepoint));
        assert(sans_width != mono_width);
        auto* context = URUI::GetContext(0);
        auto page = [&](const URUI::String& fallback)
        {
            return context->LoadDocumentFromMemory(
                "<!doctype html><html><head><style>"
                "@font-face{font-family:Latin;src:url('LatoLatin-Regular.ttf');}"
                "@font-face{font-family:Fallback;src:url('" +
                    fallback +
                    "');}"
                    "body{font:30px Latin,Fallback;}</style></head><body><span id='same'>" +
                    character + "</span></body></html>",
                assets + "/font-fallback.html");
        };
        auto* first = page(sans);
        assert(first);
        first->Show();
        context->Update();
        const auto first_handle = first->GetElementById("same")->GetComputedValues().font_face_handle();
        assert(glyph_width(first_handle) == sans_width);
        auto* second = page(mono);
        assert(second);
        second->Show();
        context->Update();
        const auto second_handle = second->GetElementById("same")->GetComputedValues().font_face_handle();
        assert(glyph_width(second_handle) == mono_width);
        assert(glyph_width(first_handle) == sans_width);
        second->Close();
        context->Update();
        assert(glyph_width(first_handle) == sans_width);
        urui_surface_destroy(g_test_surface);
        std::cout << "Document family-list glyph fallback and cache isolation passed\n";
        return 0;
    }
    URUI::String css = "@font-face{font-family:Shared;src:url('LatoLatin-Regular.ttf');}body{font:20px Shared;}";
    if (mode == "style")
    {
        css += "#same{font-style:italic;}";
    }
    else if (mode == "media")
    {
        css += "@media(min-width:500px){@font-face{font-family:Shared;src:url('RobotoMono-Regular.ttf');}}";
    }
    else
    {
        assert(false);
    }
    auto* context = URUI::GetContext(0);
    auto* doc = context->LoadDocumentFromMemory("<!doctype html><html><head><style>" + css +
                                                    "</style></head><body><div id='same'>iiiiWWWW</div></body></html>",
                                                assets + "/font-matching.html");
    assert(doc);
    doc->Show();
    context->Update();
    auto* text = doc->GetElementById("same");
    assert(measure(text->GetComputedValues().font_face_handle()) == a);
    if (mode == "media")
    {
        context->SetDimensions({600, 200});
        context->Update();
        assert(measure(text->GetComputedValues().font_face_handle()) == b);
        context->SetDimensions({400, 200});
        context->Update();
        assert(measure(text->GetComputedValues().font_face_handle()) == a);
    }
    urui_surface_destroy(g_test_surface);
    std::cout << "Document font " << mode << " contracts passed\n";
}
