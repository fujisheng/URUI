#include "../../Include/URUI/Core/FontEngineInterface.h"
#include "../../Include/URUI/Core/StringUtilities.h"

namespace URUI
{

    FontEngineInterface::FontEngineInterface()
    {
    }

    FontEngineInterface::~FontEngineInterface()
    {
    }

    void FontEngineInterface::Initialize()
    {
    }

    void FontEngineInterface::Shutdown()
    {
    }

    bool FontEngineInterface::LoadFontFace(const String& /*file_path*/, int /*face_index*/, bool /*fallback_face*/,
                                           Style::FontWeight /*weight*/)
    {
        return false;
    }

    bool FontEngineInterface::LoadFontFace(const String& /*file_path*/, int /*face_index*/, const String& /*family*/,
                                           Style::FontStyle /*style*/, Style::FontWeight /*weight*/,
                                           bool /*fallback_face*/)
    {
        return false;
    }

    bool FontEngineInterface::LoadFontFace(Span<const byte> /*data*/, int /*face_index*/, const String& /*family*/,
                                           Style::FontStyle /*style*/, Style::FontWeight /*weight*/,
                                           bool /*fallback_face*/)
    {
        return false;
    }

    FontFaceHandle FontEngineInterface::GetFontFaceHandle(const String& /*family*/, Style::FontStyle /*style*/,
                                                          Style::FontWeight /*weight*/, float /*size*/)
    {
        return 0;
    }

    FontFaceHandle FontEngineInterface::GetFontFaceHandleChain(Span<const FontFaceHandle> handles)
    {
        return handles.empty() ? 0 : handles[0];
    }

    FontEffectsHandle FontEngineInterface::PrepareFontEffects(FontFaceHandle /*handle*/,
                                                              const FontEffectList& /*font_effects*/)
    {
        return 0;
    }

    const FontMetrics& FontEngineInterface::GetFontMetrics(FontFaceHandle /*handle*/)
    {
        static const FontMetrics metrics = {};
        return metrics;
    }

    float FontEngineInterface::GetStringWidth(FontFaceHandle /*handle*/, StringView /*string*/,
                                              const TextShapingContext& /*text_shaping_context*/,
                                              Character /*prior_character*/)
    {
        return 0;
    }

    float FontEngineInterface::GenerateString(RenderManager& /*render_manager*/, FontFaceHandle /*face_handle*/,
                                              FontEffectsHandle /*font_effects_handle*/, StringView /*string*/,
                                              Vector2f /*position*/, ColourbPremultiplied /*colour*/, float /*opacity*/,
                                              const TextShapingContext& /*text_shaping_context*/,
                                              TexturedMeshList& /*mesh_list*/)
    {
        return 0;
    }

    int FontEngineInterface::GetVersion(FontFaceHandle /*handle*/)
    {
        return 0;
    }

    void FontEngineInterface::ReleaseFontResources()
    {
    }

}
