#if defined(URUI_DEBUG)
#include "diagnostics.h"

// One strong definition provides consistent TLS access from the bridge and
// the generated RmlUi instrumentation translation units.
namespace urui_lookup
{
    thread_local bool inline_enabled = false;
    thread_local int profile_mode = 0;
    thread_local bool style_scope = false;
    thread_local UruiLookupProfile profile{};
} // namespace urui_lookup

namespace urui_style_probe
{
    thread_local UruiStyleProbe total{};
    thread_local int mode = 0;
    thread_local UruiStyleProbe* active = nullptr;
} // namespace urui_style_probe

namespace urui_debug
{
    UruiProfile profile{};
    UruiApiProfile api_profile{};
    bool api_profile_enabled = false;
    UruiTypedProbe typed_probe{};
    int typed_probe_mode = 0;
} // namespace urui_debug

#endif
