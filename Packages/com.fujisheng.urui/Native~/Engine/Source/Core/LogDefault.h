#pragma once

#include "../../Include/URUI/Core/Log.h"
#include "../../Include/URUI/Core/Types.h"

namespace URUI
{

    /**
    Provides a platform-dependent default implementation for message logging.
 */

    class LogDefault
    {
    public:
        static bool LogMessage(Log::Type type, const String& message);
    };

}
