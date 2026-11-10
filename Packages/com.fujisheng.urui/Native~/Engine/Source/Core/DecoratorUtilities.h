#pragma once

#include "../../Include/URUI/Core/NumericValue.h"
#include "../../Include/URUI/Core/Types.h"

namespace URUI {

using Vector2Numeric = Vector2<NumericValue>;

// Compute a 2d-position property value into a percentage-length vector.
Vector2Numeric ComputePosition(Array<const Property*, 2> p_position);

} // namespace URUI
