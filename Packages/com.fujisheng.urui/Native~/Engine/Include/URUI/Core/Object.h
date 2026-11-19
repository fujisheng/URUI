#pragma once

#include "Header.h"
#include "Traits.h"
#include "Types.h"

namespace URUI
{

    /**
    Common runtime object with type information and controlled release.
 */

    class URUI_CORE_API Object : public Releasable
    {
    public:
        URUI_RTTI_Declare(Object)

        virtual ~Object()
        {
        }
    };

}
