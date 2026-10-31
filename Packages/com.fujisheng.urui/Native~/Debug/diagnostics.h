#pragma once
#if defined(URUI_DEBUG)
#include "urui_debug.h"
#include "lookup_probe.h"
#include "style_probe.h"
#include <chrono>

namespace urui_debug
{
    static_assert(sizeof(UruiProfile) == 104, "diagnostic profile ABI");
    using ProfileClock = std::chrono::steady_clock;
    extern UruiProfile profile;
    extern UruiApiProfile api_profile;
    extern bool api_profile_enabled;
    extern UruiTypedProbe typed_probe;
    extern int typed_probe_mode;

    // Scope ownership prevents a failed/early-returning API call from losing its time.
    class ProfileScope
    {
        uint64_t& total;
        ProfileClock::time_point start;
        bool enabled;

    public:
        explicit ProfileScope(uint64_t& destination, bool active = true) : total(destination), enabled(active)
        {
            if (enabled)
            {
                start = ProfileClock::now();
            }
        }

        ~ProfileScope()
        {
            if (enabled)
            {
                total += static_cast<uint64_t>(
                    std::chrono::duration_cast<std::chrono::nanoseconds>(ProfileClock::now() - start).count());
            }
        }
    };
} // namespace urui_debug

#endif
