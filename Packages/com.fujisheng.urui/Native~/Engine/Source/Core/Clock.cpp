#include "Clock.h"
#include "../../Include/URUI/Core/Core.h"
#include "../../Include/URUI/Core/SystemInterface.h"

namespace URUI {

URUI_CORE_API double Clock::GetElapsedTime()
{
	SystemInterface* system_interface = GetSystemInterface();
	if (system_interface != nullptr)
		return system_interface->GetElapsedTime();
	else
		return 0;
}

} // namespace URUI
