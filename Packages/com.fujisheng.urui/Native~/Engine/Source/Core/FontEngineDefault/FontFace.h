#pragma once

#include "../../../Include/URUI/Core/StyleTypes.h"
#include "FontTypes.h"

namespace URUI {

class FontFaceHandleDefault;

class FontFace {
public:
	FontFace(FontFaceHandleFreetype face, Style::FontStyle style, Style::FontWeight weight);
	~FontFace();

	Style::FontStyle GetStyle() const;
	Style::FontWeight GetWeight() const;

	/// Returns a handle for positioning and rendering this face at the given size.
	/// @param[in] size The size of the desired handle, in points.
	/// @param[in] load_default_glyphs True to load the default set of glyph (ASCII range).
	/// @return The font handle.
	FontFaceHandleDefault* GetHandle(float size, bool load_default_glyphs);

	/// Releases resources owned by sized font faces, including their textures and rendered glyphs.
	void ReleaseFontResources();

private:
	Style::FontStyle style;
	Style::FontWeight weight;

	// Key is font size in 26.6 CSS pixels.
	using HandleMap = UnorderedMap<int, UniquePtr<FontFaceHandleDefault>>;
	HandleMap handles;

	FontFaceHandleFreetype face;
};

} // namespace URUI
