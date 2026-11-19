#include "LayoutBox.h"
#include "LayoutPools.h"

namespace URUI
{

    const Box* LayoutBox::GetIfBox() const
    {
        return nullptr;
    }

    bool LayoutBox::GetBaselineOfLastLine(float& /*out_baseline*/) const
    {
        return false;
    }

    float LayoutBox::GetShrinkToFitWidth(bool /*ignore_own_width*/) const
    {
        return 0.f;
    }
    void* LayoutBox::operator new(size_t size)
    {
        void* memory = LayoutPools::AllocateLayoutChunk(size);
        return memory;
    }

    void LayoutBox::operator delete(void* chunk, size_t size)
    {
        LayoutPools::DeallocateLayoutChunk(chunk, size);
    }

}
