#pragma once

#include "../../../Include/URUI/Core/FontGlyph.h"
#include "../../../Include/URUI/Core/StyleTypes.h"
#include "../../../Include/URUI/Core/Types.h"

namespace URUI {

using FontFaceHandleFreetype = uintptr_t;

struct FaceVariation {
	Style::FontWeight weight;
	uint16_t width;
	int named_instance_index;
};

inline bool operator<(const FaceVariation& a, const FaceVariation& b)
{
	if (a.weight == b.weight)
		return a.width < b.width;
	return a.weight < b.weight;
}

} // namespace URUI
