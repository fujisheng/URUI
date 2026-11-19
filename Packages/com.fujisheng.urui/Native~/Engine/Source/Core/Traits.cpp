#include "../../Include/URUI/Core/Traits.h"

namespace URUI
{

    int FamilyBase::GetNewId()
    {
        static int id = 0;
        return id++;
    }

}
