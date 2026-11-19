#pragma once

#include "../../Include/URUI/Core/FontEffect.h"
#include "../../Include/URUI/Core/FontEffectInstancer.h"

namespace URUI
{

    /**
    A concrete font effect for rendering text shadows.
 */

    class FontEffectShadow : public FontEffect
    {
    public:
        FontEffectShadow();
        virtual ~FontEffectShadow();

        bool Initialise(Vector2i offset);

        bool HasUniqueTexture() const override;

        bool GetGlyphMetrics(Vector2i& origin, Vector2i& dimensions, const FontGlyph& glyph) const override;

    private:
        Vector2i offset;
    };

    /**
    A concrete font effect instancer for the shadow effect.
 */

    class FontEffectShadowInstancer : public FontEffectInstancer
    {
    public:
        FontEffectShadowInstancer();
        virtual ~FontEffectShadowInstancer();

        SharedPtr<FontEffect> InstanceFontEffect(const String& name, const PropertyDictionary& properties) override;

    private:
        PropertyId id_offset_x;
        PropertyId id_offset_y;
        PropertyId id_color;
    };

}
