#pragma once

#include "../../../Include/URUI/Core/Types.h"

namespace URUI
{

    namespace LayoutPools
    {

        void Initialize();
        void Shutdown();

        void* AllocateLayoutChunk(size_t size);
        void DeallocateLayoutChunk(void* chunk, size_t size);

    }

}
