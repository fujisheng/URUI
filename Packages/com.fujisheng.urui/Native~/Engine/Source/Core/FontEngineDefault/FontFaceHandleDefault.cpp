#include "FontFaceHandleDefault.h"
#include "../../../Include/URUI/Core/Profiling.h"
#include "../../../Include/URUI/Core/StringUtilities.h"
#include "../../../Include/URUI/Core/StyleTypes.h"
#include "../TextureLayout.h"
#include "FontFaceLayer.h"
#include "FontProvider.h"
#include "FreeTypeInterface.h"
#include <algorithm>
#include <numeric>

namespace URUI {

static constexpr char32_t KerningCache_AsciiSubsetBegin = 32;
static constexpr char32_t KerningCache_AsciiSubsetLast = 126;

FontFaceHandleDefault::FontFaceHandleDefault()
{
	base_layer = nullptr;
	metrics = {};
	ft_face = 0;
}

FontFaceHandleDefault::~FontFaceHandleDefault()
{
	glyphs.clear();
	layers.clear();
}

bool FontFaceHandleDefault::Initialize(FontFaceHandleFreetype face, float font_size, bool load_default_glyphs)
{
	ft_face = face;

	URUI_ASSERTMSG(layer_configurations.empty(), "Initialize must only be called once.");

	if (!FreeType::InitialiseFaceHandle(ft_face, font_size, glyphs, metrics, load_default_glyphs))
		return false;

	has_kerning = FreeType::HasKerning(ft_face);
	FillKerningPairCache();

	// Generate the default layer and layer configuration.
	base_layer = GetOrCreateLayer(nullptr);
	layer_configurations.push_back(LayerConfiguration{base_layer});

	return true;
}

UniquePtr<FontFaceHandleDefault> FontFaceHandleDefault::CreateFontChain(Span<const FontFaceHandle> handles) const
{
	auto result = MakeUnique<FontFaceHandleDefault>();
	if (!result->Initialize(ft_face, metrics.size, false)) return nullptr;
	for (size_t i = 1; i < handles.size(); ++i)
		result->explicit_fallbacks.push_back(reinterpret_cast<FontFaceHandleDefault*>(handles[i]));
	return result;
}

const FontMetrics& FontFaceHandleDefault::GetFontMetrics() const
{
	return metrics;
}

const FontGlyphMap& FontFaceHandleDefault::GetGlyphs() const
{
	return glyphs;
}

float FontFaceHandleDefault::GetStringWidth(StringView string, const TextShapingContext& text_shaping_context, Character prior_character)
{
	URUI_ZoneScoped;

	bool has_set_size = false;
	bool is_kerning_enabled = IsKerningEnabled(text_shaping_context);
	float width = 0;
	for (auto it_string = StringIteratorU8(string); it_string; ++it_string)
	{
		Character character = *it_string;

		const FontGlyph* glyph = GetOrAppendGlyph(character);
		if (!glyph)
			continue;

		// Adjust the cursor for the kerning between this character and the previous one.
		if (is_kerning_enabled)
			width += GetKerning(prior_character, character, has_set_size);

		// Adjust the cursor for this character's advance.
		width += glyph->advance;
		width += text_shaping_context.letter_spacing;

		prior_character = character;
	}

	return Math::Max(width, 0.f);
}

int FontFaceHandleDefault::GenerateLayerConfiguration(const FontEffectList& font_effects)
{
	if (font_effects.empty())
		return 0;

	// Check each existing configuration for a match with this arrangement of effects.
	int configuration_index = 1;
	for (; configuration_index < (int)layer_configurations.size(); ++configuration_index)
	{
		const LayerConfiguration& configuration = layer_configurations[configuration_index];

		// Check the size is correct. For a match, there should be one layer in the configuration
		// plus an extra for the base layer.
		if (configuration.size() != font_effects.size() + 1)
			continue;

		// Check through each layer, checking it was created by the same effect as the one we're
		// checking.
		size_t effect_index = 0;
		for (size_t i = 0; i < configuration.size(); ++i)
		{
			// Skip the base layer ...
			if (configuration[i]->GetFontEffect() == nullptr)
				continue;

			// If the ith layer's effect doesn't match the equivalent effect, then this
			// configuration can't match.
			if (configuration[i]->GetFontEffect() != font_effects[effect_index].get())
				break;

			// Check the next one ...
			++effect_index;
		}

		if (effect_index == font_effects.size())
			return configuration_index;
	}

	// No match, so we have to generate a new layer configuration.
	layer_configurations.push_back(LayerConfiguration());
	LayerConfiguration& layer_configuration = layer_configurations.back();

	bool added_base_layer = false;

	for (size_t i = 0; i < font_effects.size(); ++i)
	{
		if (!added_base_layer && font_effects[i]->GetLayer() == FontEffect::Layer::Front)
		{
			layer_configuration.push_back(base_layer);
			added_base_layer = true;
		}

		FontFaceLayer* new_layer = GetOrCreateLayer(font_effects[i]);
		layer_configuration.push_back(new_layer);
	}

	// Add the base layer now if we still haven't added it.
	if (!added_base_layer)
		layer_configuration.push_back(base_layer);

	return (int)(layer_configurations.size() - 1);
}

bool FontFaceHandleDefault::GenerateLayerTexture(Vector<byte>& texture_data, Vector2i& texture_dimensions, const FontEffect* font_effect,
	int texture_id, int handle_version) const
{
	if (handle_version != version)
	{
		URUI_ERRORMSG("While generating font layer texture: Handle version mismatch in texture vs font-face.");
		return false;
	}

	auto it = std::find_if(layers.begin(), layers.end(), [font_effect](const EffectLayerPair& pair) { return pair.font_effect == font_effect; });

	if (it == layers.end())
	{
		URUI_ERRORMSG("While generating font layer texture: Layer id not found.");
		return false;
	}

	return it->layer->GenerateTexture(texture_data, texture_dimensions, texture_id, glyphs);
}

float FontFaceHandleDefault::GenerateString(RenderManager& render_manager, TexturedMeshList& mesh_list, StringView string, const Vector2f position,
	const ColourbPremultiplied colour, const float opacity, const TextShapingContext& text_shaping_context, const int layer_configuration_index)
{
	URUI_ASSERT(layer_configuration_index >= 0);
	URUI_ASSERT(layer_configuration_index < (int)layer_configurations.size());

	int geometry_index = 0;
	float line_width = 0;
	bool has_set_size = false;
	bool is_kerning_enabled = IsKerningEnabled(text_shaping_context);

	UpdateLayersOnDirty();

	// Fetch the requested configuration and generate the geometry for each one.
	const LayerConfiguration& layer_configuration = layer_configurations[layer_configuration_index];
    const bool replaces_face = std::any_of(layer_configuration.begin(), layer_configuration.end(), [](const FontFaceLayer* layer) {
        const auto* effect = layer->GetFontEffect();
        return effect && effect->GetLayer() == FontEffect::Layer::Face;
    });

	// Each texture represents one geometry.
	const int num_geometries = std::accumulate(layer_configuration.begin(), layer_configuration.end(), 0,
		[](int sum, const FontFaceLayer* layer) { return sum + layer->GetNumTextures(); });

	mesh_list.resize(num_geometries);

	for (size_t layer_index = 0; layer_index < layer_configuration.size(); ++layer_index)
	{
		FontFaceLayer* layer = layer_configuration[layer_index];

		ColourbPremultiplied layer_colour;
        const auto* layer_effect = layer->GetFontEffect();
        const bool face_layer = layer_effect && layer_effect->GetLayer() == FontEffect::Layer::Face;
		if (layer == base_layer || face_layer)
			layer_colour = colour;
		else
			layer_colour = layer->GetColour(opacity);

		const int num_textures = layer->GetNumTextures();
		if (num_textures == 0)
			continue;

		URUI_ASSERT(geometry_index + num_textures <= (int)mesh_list.size());

		line_width = 0;
		Character prior_character = Character::Null;

		// Set the mesh and textures to the geometries.
		for (int tex_index = 0; tex_index < num_textures; ++tex_index)
			mesh_list[geometry_index + tex_index].texture = layer->GetTexture(render_manager, tex_index);

		mesh_list[geometry_index].mesh.indices.reserve(string.size() * 6);
		mesh_list[geometry_index].mesh.vertices.reserve(string.size() * 4);

		for (auto it_string = StringIteratorU8(string); it_string; ++it_string)
		{
			Character character = *it_string;

			const FontGlyph* glyph = GetOrAppendGlyph(character);
			if (!glyph)
				continue;

			// Adjust the cursor for the kerning between this character and the previous one.
			if (is_kerning_enabled)
				line_width += GetKerning(prior_character, character, has_set_size);

			ColourbPremultiplied glyph_color = layer_colour;
			// Use white vertex colors on RGB glyphs.
			if (layer == base_layer && glyph->color_format == ColorFormat::RGBA8)
				glyph_color = ColourbPremultiplied(layer_colour.alpha, layer_colour.alpha);

            // 膨胀层接管单色字形，保留彩色字形原始RGB；空基础网格不产生draw。
            if (!(replaces_face && layer == base_layer && glyph->color_format != ColorFormat::RGBA8))
                layer->GenerateGeometry(&mesh_list[geometry_index], character, Vector2f(position.x + line_width, position.y), glyph_color);

			line_width += glyph->advance;
			line_width += text_shaping_context.letter_spacing;
			prior_character = character;
		}

		geometry_index += num_textures;
	}

	return Math::Max(line_width, 0.f);
}

bool FontFaceHandleDefault::UpdateLayersOnDirty()
{
	bool result = false;

	// If we are dirty, regenerate all the layers and increment the version
	if (is_layers_dirty && base_layer)
	{
		is_layers_dirty = false;
		++version;

		// Regenerate all the layers.
		// Note: The layer regeneration needs to happen in the order in which the layers were created,
		// otherwise we may end up cloning a layer which has not yet been regenerated. This means trouble!
		for (auto& pair : layers)
		{
			GenerateLayer(pair.layer.get());
		}

		result = true;
	}

	return result;
}

int FontFaceHandleDefault::GetVersion() const
{
	return version;
}

bool FontFaceHandleDefault::AppendGlyph(Character character)
{
	bool result = FreeType::AppendGlyph(ft_face, metrics.size, character, glyphs);
	return result;
}

void FontFaceHandleDefault::FillKerningPairCache()
{
	if (!has_kerning)
		return;

	for (char32_t i = KerningCache_AsciiSubsetBegin; i <= KerningCache_AsciiSubsetLast; i++)
	{
		for (char32_t j = KerningCache_AsciiSubsetBegin; j <= KerningCache_AsciiSubsetLast; j++)
		{
			const bool first_iteration = (i == KerningCache_AsciiSubsetBegin && j == KerningCache_AsciiSubsetBegin);

			// Fetch the kerning from the font face. Submit zero font size on subsequent iterations for performance reasons.
			const float kerning = FreeType::GetKerning(ft_face, first_iteration ? metrics.size : 0, Character(i), Character(j));
			if (kerning != 0)
			{
				kerning_pair_cache.emplace(AsciiPair((i << 8) | j), kerning);
			}
		}
	}
}

float FontFaceHandleDefault::GetKerning(Character lhs, Character rhs, bool& has_set_size) const
{
	static_assert(' ' == 32, "Only ASCII/UTF8 character set supported.");

	// Check if we have no kerning, or if we have a control character.
	if (!has_kerning || char32_t(lhs) < ' ' || char32_t(rhs) < ' ')
		return 0;

	// See if the kerning pair has been cached.
	const bool lhs_in_cache = (char32_t(lhs) >= KerningCache_AsciiSubsetBegin && char32_t(lhs) <= KerningCache_AsciiSubsetLast);
	const bool rhs_in_cache = (char32_t(rhs) >= KerningCache_AsciiSubsetBegin && char32_t(rhs) <= KerningCache_AsciiSubsetLast);

	if (lhs_in_cache && rhs_in_cache)
	{
		const auto it = kerning_pair_cache.find(AsciiPair((int(lhs) << 8) | int(rhs)));

		if (it != kerning_pair_cache.end())
		{
			return it->second;
		}

		return 0;
	}

	// Fetch it from the font face instead.
	const float result = FreeType::GetKerning(ft_face, has_set_size ? 0 : metrics.size, lhs, rhs);

	// This is purely an optimization to avoid repeatedly setting the font size in FreeType, which can be a measurable performance hit.
	has_set_size = true;

	return result;
}

bool FontFaceHandleDefault::IsKerningEnabled(const TextShapingContext& text_shaping_context) const
{
	return text_shaping_context.font_kerning != Style::FontKerning::None;
}

const FontGlyph* FontFaceHandleDefault::GetOrAppendGlyph(Character& character, bool look_in_fallback_fonts)
{
	// Don't try to render control characters
	if ((char32_t)character < (char32_t)' ')
		return nullptr;

	auto it_glyph = glyphs.find(character);
	if (it_glyph == glyphs.end())
	{
		bool result = AppendGlyph(character);

		if (result)
		{
			it_glyph = glyphs.find(character);
			if (it_glyph == glyphs.end())
			{
				URUI_ERROR;
				return nullptr;
			}

			is_layers_dirty = true;
		}
		else if (look_in_fallback_fonts)
		{
			// 组合句柄独占字形选择缓存，不能将文档 A 的回退结果写进共享主字体。
			for (auto* fallback_face : explicit_fallbacks)
			{
				const FontGlyph* glyph = fallback_face->GetOrAppendGlyph(character, false);
				if (!glyph) continue;
				it_glyph = glyphs.emplace(character, glyph->WeakCopy()).first;
				is_layers_dirty = true;
				break;
			}
			const int num_fallback_faces = FontProvider::CountFallbackFontFaces();
			for (int i = 0; it_glyph == glyphs.end() && i < num_fallback_faces; i++)
			{
				FontFaceHandleDefault* fallback_face = FontProvider::GetFallbackFontFace(i, metrics.size);
				if (!fallback_face || fallback_face == this)
					continue;

				const FontGlyph* glyph = fallback_face->GetOrAppendGlyph(character, false);
				if (glyph)
				{
					// Insert the new glyph into our own set of glyphs
					auto pair = glyphs.emplace(character, glyph->WeakCopy());
					it_glyph = pair.first;
					if (pair.second)
						is_layers_dirty = true;
					break;
				}
			}

			// If we still have not found a glyph, use the replacement character.
			if (it_glyph == glyphs.end())
			{
				character = Character::Replacement;
				it_glyph = glyphs.find(character);
				if (it_glyph == glyphs.end())
					return nullptr;
			}
		}
		else
		{
			return nullptr;
		}
	}

	const FontGlyph* glyph = &it_glyph->second;
	return glyph;
}

FontFaceLayer* FontFaceHandleDefault::GetOrCreateLayer(const SharedPtr<const FontEffect>& font_effect)
{
	// Search for the font effect layer first, it may have been instanced before as part of a different configuration.
	const FontEffect* font_effect_ptr = font_effect.get();
	auto it =
		std::find_if(layers.begin(), layers.end(), [font_effect_ptr](const EffectLayerPair& pair) { return pair.font_effect == font_effect_ptr; });

	if (it != layers.end())
		return it->layer.get();

	// No existing effect matches, generate a new layer for the effect.
	layers.push_back(EffectLayerPair{font_effect_ptr, nullptr});
	auto& layer = layers.back().layer;

	layer = MakeUnique<FontFaceLayer>(font_effect, metrics.size);
	GenerateLayer(layer.get());

	return layer.get();
}

bool FontFaceHandleDefault::GenerateLayer(FontFaceLayer* layer)
{
	URUI_ASSERT(layer);
	const FontEffect* font_effect = layer->GetFontEffect();
	bool result = false;

	if (!font_effect)
	{
		result = layer->Generate(this);
	}
	else
	{
		// Determine which, if any, layer the new layer should copy its geometry and textures from.
		FontFaceLayer* clone = nullptr;
		bool clone_glyph_origins = true;
		String generation_key;
		size_t fingerprint = font_effect->GetFingerprint();

		if (!font_effect->HasUniqueTexture())
		{
			clone = base_layer;
			clone_glyph_origins = false;
		}
		else
		{
			auto cache_iterator = layer_cache.find(fingerprint);
			if (cache_iterator != layer_cache.end() && cache_iterator->second != layer)
				clone = cache_iterator->second;
		}

		// Create a new layer.
		result = layer->Generate(this, clone, clone_glyph_origins);

		// Cache the layer in the layer cache if it generated its own textures (ie, didn't clone).
		if (!clone)
			layer_cache[fingerprint] = layer;
	}

	return result;
}

} // namespace URUI
