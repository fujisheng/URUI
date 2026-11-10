#include "FontEffectOutline.h"
#include "../../Include/URUI/Core/PropertyDefinition.h"
#include <cmath>

namespace URUI {

FontEffectOutline::FontEffectOutline()
{
	width = 0;
	SetLayer(Layer::Back);
}

FontEffectOutline::~FontEffectOutline() {}

bool FontEffectOutline::HasUniqueTexture() const
{
	return true;
}

bool FontEffectOutline::InitialiseRelative(float width_em)
{
    if (!std::isfinite(width_em) || width_em <= 0) return false;
    relative_width = width_em;
    return true;
}

SharedPtr<const FontEffect> FontEffectOutline::ResolveForFontSize(float font_size) const
{
    if (relative_width == 0) return nullptr;
    auto resolved = MakeShared<FontEffectOutline>();
    if (!resolved->Initialise(relative_width * font_size)) return nullptr;
    resolved->SetLayer(GetLayer());
    resolved->SetColour(GetColour());
    resolved->SetFingerprint(GetFingerprint());
    return resolved;
}

bool FontEffectOutline::Initialise(float _width)
{
	if (!std::isfinite(_width) || _width <= 0)
		return false;

	width = int(std::ceil(_width));

	filter.Initialise(width, FilterOperation::Dilation);
	for (int x = -width; x <= width; ++x)
	{
		for (int y = -width; y <= width; ++y)
		{
			float weight = 1;

			float distance = Math::SquareRoot(float(x * x + y * y));
			if (distance > _width)
			{
				weight = (_width + 1) - distance;
				weight = Math::Max(weight, 0.0f);
			}

			filter[x + width][y + width] = weight;
		}
	}

	return true;
}

bool FontEffectOutline::GetGlyphMetrics(Vector2i& origin, Vector2i& dimensions, const FontGlyph& glyph) const
{
    // 单色字形膨胀不替换彩色字形，后者继续由原始字形层绘制。
    if (GetLayer() == Layer::Face && glyph.color_format == ColorFormat::RGBA8) return false;
	if (dimensions.x * dimensions.y > 0)
	{
		origin.x -= width;
		origin.y -= width;

		dimensions.x += 2 * width;
		dimensions.y += 2 * width;

		return true;
	}

	return false;
}

void FontEffectOutline::GenerateGlyphTexture(byte* destination_data, const Vector2i destination_dimensions, int destination_stride,
	const FontGlyph& glyph) const
{
	filter.Run(destination_data, destination_dimensions, destination_stride, ColorFormat::RGBA8, glyph.bitmap_data, glyph.bitmap_dimensions,
		Vector2i(width), glyph.color_format);

	FillColorValuesFromAlpha(destination_data, destination_dimensions, destination_stride);
}

FontEffectOutlineInstancer::FontEffectOutlineInstancer() : id_width(PropertyId::Invalid), id_color(PropertyId::Invalid)
{
	id_width = RegisterProperty("width", "1px", true).AddParser("length").GetId();
	id_color = RegisterProperty("color", "white", false).AddParser("color").GetId();
	RegisterShorthand("--urui-font-effect", "width, color", ShorthandType::FallThrough);
}

FontEffectOutlineInstancer::~FontEffectOutlineInstancer() {}

SharedPtr<FontEffect> FontEffectOutlineInstancer::InstanceFontEffect(const String& name, const PropertyDictionary& properties)
{
	float width = properties.GetProperty(id_width)->Get<float>();
	Colourb color = properties.GetProperty(id_color)->Get<Colourb>();

	auto font_effect = MakeShared<FontEffectOutline>();
    const bool relative = properties.GetProperty(id_width)->unit == Unit::EM;
	if (relative ? font_effect->InitialiseRelative(width) : font_effect->Initialise(width))
	{
		if (name == "dilate") font_effect->SetLayer(FontEffect::Layer::Face);
		font_effect->SetColour(color);
		return font_effect;
	}

	return nullptr;
}

} // namespace URUI
