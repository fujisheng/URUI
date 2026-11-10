#pragma once

#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/NumericValue.h"
#include "../../Include/URUI/Core/RenderBox.h"
#include "../../Include/URUI/Core/Types.h"
#include "../../Include/URUI/Core/Unit.h"
#include "../../Include/URUI/Core/Utilities.h"
#include "BoxShadowCache.h"
#include "GeometryBoxShadow.h"

namespace std {

template <>
struct hash<::URUI::Unit> {
	using utype = underlying_type_t<::URUI::Unit>;
	size_t operator()(const ::URUI::Unit& t) const noexcept
	{
		hash<utype> h;
		return h(static_cast<utype>(t));
	}
};

template <>
struct hash<::URUI::Vector2i> {
	size_t operator()(const ::URUI::Vector2i& v) const noexcept
	{
		using namespace ::URUI::Utilities;
		size_t seed = hash<int>{}(v.x);
		HashCombine(seed, v.y);
		return seed;
	}
};
template <>
struct hash<::URUI::Vector2f> {
	size_t operator()(const ::URUI::Vector2f& v) const noexcept
	{
		using namespace ::URUI::Utilities;
		size_t seed = hash<float>{}(v.x);
		HashCombine(seed, v.y);
		return seed;
	}
};
template <>
struct hash<::URUI::Colourb> {
	size_t operator()(const ::URUI::Colourb& v) const noexcept { return static_cast<size_t>(hash<uint32_t>{}(reinterpret_cast<const uint32_t&>(v))); }
};
template <>
struct hash<::URUI::ColourbPremultiplied> {
	size_t operator()(const ::URUI::ColourbPremultiplied& v) const noexcept
	{
		return static_cast<size_t>(hash<uint32_t>{}(reinterpret_cast<const uint32_t&>(v)));
	}
};

template <>
struct hash<::URUI::NumericValue> {
	size_t operator()(const ::URUI::NumericValue& v) const noexcept
	{
		using namespace ::URUI::Utilities;
		size_t seed = hash<float>{}(v.number);
		HashCombine(seed, v.unit);
		return seed;
	}
};

template <>
struct hash<::URUI::BoxShadow> {
	size_t operator()(const ::URUI::BoxShadow& s) const noexcept
	{
		using namespace ::URUI;
		using namespace ::URUI::Utilities;
		size_t seed = std::hash<ColourbPremultiplied>{}(s.color);

		HashCombine(seed, s.offset_x);
		HashCombine(seed, s.offset_y);
		HashCombine(seed, s.blur_radius);
		HashCombine(seed, s.spread_distance);
		HashCombine(seed, s.inset);
		return seed;
	}
};

template <>
struct hash<::URUI::RenderBox> {
	size_t operator()(const ::URUI::RenderBox& box) const noexcept
	{
		using namespace ::URUI::Utilities;
		static auto HashArray4 = [](const ::URUI::Array<float, 4>& arr) -> size_t {
			size_t seed = 0;
			for (const auto& v : arr)
				HashCombine(seed, v);
			return seed;
		};

		size_t seed = 0;
		HashCombine(seed, box.GetFillSize());
		HashCombine(seed, box.GetBorderOffset());
		HashCombine(seed, HashArray4(box.GetBorderRadius()));
		HashCombine(seed, HashArray4(box.GetBorderWidths()));
		return seed;
	}
};

template <>
struct hash<::URUI::BoxShadowGeometryInfo> {
	size_t operator()(const ::URUI::BoxShadowGeometryInfo& in) const noexcept
	{
		using namespace ::URUI::Utilities;
		size_t seed = std::hash<void*>{}(in.resources.get());
		HashCombine(seed, static_cast<void*>(in.render_manager));

		HashCombine(seed, in.background_color);
		for (const auto& v : in.border_colors)
		{
			HashCombine(seed, v);
		}

		for (const auto& v : in.border_radius)
		{
			HashCombine(seed, v);
		}

		HashCombine(seed, in.texture_dimensions);
		HashCombine(seed, in.element_offset_in_texture);

		for (const auto& v : in.padding_render_boxes)
		{
			HashCombine(seed, v);
		}
		for (const auto& v : in.border_render_boxes)
		{
			HashCombine(seed, v);
		}
		for (const ::URUI::BoxShadow& v : in.shadow_list)
		{
			HashCombine(seed, v);
		}
		HashCombine(seed, in.opacity);
		return seed;
	}
};

} // namespace std
