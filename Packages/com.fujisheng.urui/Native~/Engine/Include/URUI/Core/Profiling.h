#pragma once

#ifdef URUI_TRACY_PROFILING

	#include <tracy/Tracy.hpp>

	#define URUI_ZoneNamed(varname, active) ZoneNamed(varname, active)
	#define URUI_ZoneNamedN(varname, name, active) ZoneNamedN(varname, name, active)
	#define URUI_ZoneNamedC(varname, color, active) ZoneNamedC(varname, color, active)
	#define URUI_ZoneNamedNC(varname, name, color, active) ZoneNamedNC(varname, name, color, active)

	#define URUI_ZoneScoped ZoneScoped
	#define URUI_ZoneScopedN(name) ZoneScopedN(name)
	#define URUI_ZoneScopedC(color) ZoneScopedC(color)
	#define URUI_ZoneScopedNC(name, color) ZoneScopedNC(name, color)

	#define URUI_ZoneText(txt, size) ZoneText(txt, size)
	#define URUI_ZoneName(txt, size) ZoneName(txt, size)

	#define URUI_TracyPlot(name, val) TracyPlot(name, val)

	#define URUI_FrameMark FrameMark
	#define URUI_FrameMarkNamed(name) FrameMarkNamed(name)
	#define URUI_FrameMarkStart(name) FrameMarkStart(name)
	#define URUI_FrameMarkEnd(name) FrameMarkEnd(name)

#else

	#define URUI_ZoneNamed(varname, active)
	#define URUI_ZoneNamedN(varname, name, active)
	#define URUI_ZoneNamedC(varname, color, active)
	#define URUI_ZoneNamedNC(varname, name, color, active)

	#define URUI_ZoneScoped
	#define URUI_ZoneScopedN(name)
	#define URUI_ZoneScopedC(color)
	#define URUI_ZoneScopedNC(name, color)

	#define URUI_ZoneText(txt, size)
	#define URUI_ZoneName(txt, size)

	#define URUI_TracyPlot(name, val)

	#define URUI_FrameMark
	#define URUI_FrameMarkNamed(name)
	#define URUI_FrameMarkStart(name)
	#define URUI_FrameMarkEnd(name)

#endif
