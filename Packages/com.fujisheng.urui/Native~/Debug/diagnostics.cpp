#if defined(URUI_DEBUG)
#include "diagnostics.h"

// One strong definition provides consistent TLS access from the bridge and
// the generated URUI instrumentation translation units.
namespace URUI::LookupProbe
{
    thread_local bool inline_enabled = false;
    thread_local int profile_mode = 0;
    thread_local bool style_scope = false;
    thread_local URUILookupProfile profile{};
}

namespace URUI::StyleProbe
{
    thread_local URUIStyleProbe total{};
    thread_local int mode = 0;
    thread_local URUIStyleProbe* active = nullptr;
}

namespace URUI::Debug
{
    URUIProfile profile{};
    URUIApiProfile api_profile{};
    bool api_profile_enabled = false;
    URUITypedProbe typed_probe{};
    int typed_probe_mode = 0;
}

#endif
