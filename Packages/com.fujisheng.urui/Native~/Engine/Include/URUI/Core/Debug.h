#pragma once

#include "Header.h"

// Define for breakpointing.
#if defined(URUI_PLATFORM_WIN32)
#if defined(__MINGW32__)
#define URUI_BREAK                                                                                                     \
    {                                                                                                                  \
        asm("int $0x03");                                                                                              \
    }
#elif defined(_MSC_VER)
#define URUI_BREAK                                                                                                     \
    {                                                                                                                  \
        __debugbreak();                                                                                                \
    }
#else
#define URUI_BREAK
#endif
#elif defined(URUI_PLATFORM_LINUX)
#if defined __GNUC__
#define URUI_BREAK                                                                                                     \
    {                                                                                                                  \
        __builtin_trap();                                                                                              \
    }
#else
#define URUI_BREAK
#endif
#elif defined(URUI_PLATFORM_MACOSX)
#define URUI_BREAK                                                                                                     \
    {                                                                                                                  \
        __builtin_trap();                                                                                              \
    }
#else
#define URUI_BREAK
#endif

namespace URUI
{

    bool URUI_CORE_API Assert(const char* message, const char* file, int line);

}

// Define the URUI assertion macros.
#if !defined URUI_ENGINE_DEBUG

#define URUI_ASSERT(x)
#define URUI_ASSERTMSG(x, m)
#define URUI_ERROR
#define URUI_ERRORMSG(m)
#define URUI_VERIFY(x) x
#define URUI_ASSERT_NONRECURSIVE

#else

#define URUI_ASSERT(x)                                                                                                 \
    if (!(x))                                                                                                          \
    {                                                                                                                  \
        if (!(::URUI::Assert("URUI_ASSERT(" #x ")", __FILE__, __LINE__)))                                              \
        {                                                                                                              \
            URUI_BREAK;                                                                                                \
        }                                                                                                              \
    }
#define URUI_ASSERTMSG(x, m)                                                                                           \
    if (!(x))                                                                                                          \
    {                                                                                                                  \
        if (!(::URUI::Assert(m, __FILE__, __LINE__)))                                                                  \
        {                                                                                                              \
            URUI_BREAK;                                                                                                \
        }                                                                                                              \
    }
#define URUI_ERROR                                                                                                     \
    if (!(::URUI::Assert("URUI_ERROR", __FILE__, __LINE__)))                                                           \
    {                                                                                                                  \
        URUI_BREAK;                                                                                                    \
    }
#define URUI_ERRORMSG(m)                                                                                               \
    if (!(::URUI::Assert(m, __FILE__, __LINE__)))                                                                      \
    {                                                                                                                  \
        URUI_BREAK;                                                                                                    \
    }
#define URUI_VERIFY(x) URUI_ASSERT(x)

struct URUIAssertNonrecursive
{
    bool& entered;
    URUIAssertNonrecursive(bool& entered) : entered(entered)
    {
        URUI_ASSERTMSG(!entered, "A method defined as non-recursive was entered twice!");
        entered = true;
    }
    ~URUIAssertNonrecursive()
    {
        entered = false;
    }
};

#define URUI_ASSERT_NONRECURSIVE                                                                                       \
    static bool urui_nonrecursive_entered = false;                                                                     \
    URUIAssertNonrecursive urui_nonrecursive(urui_nonrecursive_entered)

#endif // URUI_ENGINE_DEBUG
