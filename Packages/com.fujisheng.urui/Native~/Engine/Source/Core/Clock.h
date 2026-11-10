#pragma once

#include "../../Include/URUI/Core/Header.h"

namespace URUI {

/**
    URUI's Interface to Time.
 */
class Clock {
public:
	/// Get the elapsed time since application startup
	/// @return Seconds elapsed since application startup.
	URUI_CORE_API static double GetElapsedTime();
};

} // namespace URUI
