#pragma once
#include "FileInterface.h"
#include <cstdint>

namespace URUI
{
    struct Sprite;
    struct EffectImageCondition
    {
        String property;
        String direct_value;
    };

    // Explicit resource provenance travels with a document and its cached data.
    // The embedding host supplies file access; no process-wide document is selected.
    class URUI_CORE_API ResourceContext : NonCopyMoveable
    {
    public:
        ResourceContext(uint64_t identity, uint64_t owner) : identity(identity), owner(owner)
        {
        }
        virtual ~ResourceContext() = default;
        uint64_t GetIdentity() const
        {
            return identity;
        }
        uint64_t GetOwner() const
        {
            return owner;
        }
        virtual FileInterface* GetFileInterface() const = 0;
        virtual const Sprite* GetSprite(const String&) const
        {
            return nullptr;
        }
        virtual const EffectImageCondition* GetEffectImageCondition(const String&) const
        {
            return nullptr;
        }
        virtual void Retire() noexcept
        {
        }

    private:
        uint64_t identity;
        uint64_t owner;
    };

}
