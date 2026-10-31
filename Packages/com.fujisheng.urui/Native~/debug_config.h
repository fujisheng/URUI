#pragma once

// Disabled instrumentation disappears during preprocessing, including arguments.
// Correctness checks and resource ownership must never use this macro.
#if defined(URUI_DEBUG)
#define URUI_DEBUG_CODE(...) __VA_ARGS__
#else
#define URUI_DEBUG_CODE(...)
#endif
