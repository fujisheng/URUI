#pragma once

#if defined __WIN32__ || defined _WIN32
	#define URUI_PLATFORM_WIN32
	#define URUI_PLATFORM_NAME "win32"
#elif defined __APPLE_CC__
	#define URUI_PLATFORM_UNIX
	#define URUI_PLATFORM_MACOSX
	#define URUI_PLATFORM_NAME "macosx"
#elif defined __EMSCRIPTEN__
	#define URUI_PLATFORM_UNIX
	#define URUI_PLATFORM_EMSCRIPTEN
	#define URUI_PLATFORM_NAME "emscripten"
#else
	#define URUI_PLATFORM_UNIX
	#define URUI_PLATFORM_LINUX
	#define URUI_PLATFORM_NAME "linux"
#endif

#if !defined NDEBUG && !defined URUI_ENGINE_DEBUG
	#define URUI_ENGINE_DEBUG
#endif

#if defined __LP64__ || defined _M_X64 || defined _WIN64 || defined __MINGW64__ || defined _LP64
	#define URUI_ARCH_64
#else
	#define URUI_ARCH_32
#endif

#if defined(URUI_PLATFORM_WIN32) && !defined(__MINGW32__)
	#define URUI_PLATFORM_WIN32_NATIVE

	// declaration of 'identifier' hides class member
	#pragma warning(disable : 4458)

	// <type> needs to have dll-interface to be used by clients
	#pragma warning(disable : 4251)

	// <function> was declared deprecated
	#pragma warning(disable : 4996)
#endif

// Tell the compiler of printf-like functions, warns on incorrect usage.
#if defined __MINGW32__
	#define URUI_ATTRIBUTE_FORMAT_PRINTF(i, f) __attribute__((format(__MINGW_PRINTF_FORMAT, i, f)))
#elif defined __GNUC__ || defined __clang__
	#define URUI_ATTRIBUTE_FORMAT_PRINTF(i, f) __attribute__((format(printf, i, f)))
#else
	#define URUI_ATTRIBUTE_FORMAT_PRINTF(i, f)
#endif

#ifdef __has_feature
	#if __has_feature(cxx_rtti)
		#define URUI_HAS_RTTI
	#endif
#elif defined(__GXX_RTTI) || defined(_CPPRTTI)
	#define URUI_HAS_RTTI
#endif
