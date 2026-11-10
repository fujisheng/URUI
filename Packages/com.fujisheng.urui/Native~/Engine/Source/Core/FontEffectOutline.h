#pragma once

#include "../../Include/URUI/Core/ConvolutionFilter.h"
#include "../../Include/URUI/Core/FontEffect.h"
#include "../../Include/URUI/Core/FontEffectInstancer.h"

namespace URUI {

/**
    A concrete font effect for rendering outlines around text.
 */

class FontEffectOutline : public FontEffect {
public:
	FontEffectOutline();
	virtual ~FontEffectOutline();

	bool Initialise(float width);
	bool InitialiseRelative(float width_em);
	SharedPtr<const FontEffect> ResolveForFontSize(float font_size) const override;

	bool HasUniqueTexture() const override;

	bool GetGlyphMetrics(Vector2i& origin, Vector2i& dimensions, const FontGlyph& glyph) const override;

	void GenerateGlyphTexture(byte* destination_data, Vector2i destination_dimensions, int destination_stride, const FontGlyph& glyph) const override;

private:
	int width;
	float relative_width = 0;
	ConvolutionFilter filter;
};

/**
    A concrete font effect instancer for the outline effect.
 */

class FontEffectOutlineInstancer : public FontEffectInstancer {
public:
	FontEffectOutlineInstancer();
	virtual ~FontEffectOutlineInstancer();

	SharedPtr<FontEffect> InstanceFontEffect(const String& name, const PropertyDictionary& properties) override;

private:
	PropertyId id_width, id_color;
};

} // namespace URUI
