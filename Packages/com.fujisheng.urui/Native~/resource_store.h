#pragma once
#include "resource_files.h"
#include <URUI/Core/ResourceContext.h>
#include <URUI/Core/Spritesheet.h>
#include <stdexcept>
#include <functional>
#include <map>

namespace URUI::Bridge
{

    class FileResourceCache
    {
    public:
        struct Claim
        {
            uint64_t group;
            ResourceFiles::Data bytes;
            URUI::String kind;
        };
        using Reference = std::shared_ptr<const Claim>;

    private:
        struct Entry
        {
            std::vector<std::weak_ptr<const Claim>> claims;
            std::vector<std::weak_ptr<const std::vector<uint8_t>>> contents;
        };
        std::unordered_map<URUI::String, Entry> entries;

    public:
        Reference Acquire(uint64_t group, const URUI::String& source, const uint8_t* bytes, size_t length,
                          const URUI::String& kind = "file")
        {
            auto& entry = entries[source];
            auto equal = [bytes, length](const auto& value)
            { return value.size() == length && (!length || std::memcmp(value.data(), bytes, length) == 0); };
            entry.claims.erase(std::remove_if(entry.claims.begin(), entry.claims.end(),
                                              [](const auto& weak) { return weak.expired(); }),
                               entry.claims.end());
            entry.contents.erase(std::remove_if(entry.contents.begin(), entry.contents.end(),
                                                [](const auto& weak) { return weak.expired(); }),
                                 entry.contents.end());
            ResourceFiles::Data shared;
            for (const auto& weak : entry.claims)
            {
                if (auto claim = weak.lock(); claim && claim->group != group)
                {
                    if (claim->kind != kind || (claim->bytes != shared && !equal(*claim->bytes)))
                    {
                        throw std::invalid_argument("Resource URI has a different source: " + source);
                    }
                    // 活动声明通常共享同一份不可变数据；相同 backing 只比较一次。
                    shared = claim->bytes;
                }
            }
            if (!shared)
            {
                for (const auto& weak : entry.contents)
                {
                    if (auto content = weak.lock(); content && equal(*content))
                    {
                        shared = std::move(content);
                        break;
                    }
                }
            }
            if (!shared)
            {
                auto copy = std::make_shared<std::vector<uint8_t>>();
                if (length)
                {
                    copy->assign(bytes, bytes + length);
                }
                shared = std::move(copy);
                entry.contents.push_back(shared);
            }
            auto claim = std::make_shared<Claim>(Claim{group, std::move(shared), kind});
            entry.claims.push_back(claim);
            return claim;
        }
    };

    // Pending and active generations hold independent URL bindings. Retiring a
    // namespace drops claims while open streams keep immutable backing bytes alive.
    class DocumentResources final : public URUI::ResourceContext
    {
        FileResourceCache& cache;
        URUI::FileInterface* fallback;
        mutable ResourceFiles files;
        std::unordered_map<URUI::String, FileResourceCache::Reference> bindings;
        uint64_t group;
        bool retired = false;
        struct ImageBinding
        {
            uint64_t id;
            FileResourceCache::Reference claim;
            std::function<void()> release;
            ~ImageBinding()
            {
                if (release)
                {
                    release();
                }
            }
        };
        std::unordered_map<URUI::String, std::shared_ptr<ImageBinding>> images;
        struct SpriteBinding
        {
            URUI::SharedPtr<const URUI::Spritesheet> sheet;
            URUI::Sprite sprite;
        };
        std::unordered_map<URUI::String, std::shared_ptr<const SpriteBinding>> sprites;
        std::unordered_map<URUI::String, URUI::EffectImageCondition> effectImageConditions;
        std::map<std::pair<URUI::String, float>, URUI::SharedPtr<const URUI::Spritesheet>> spriteSheets;
        std::function<void(uint64_t)> retireTextures;
        URUI::String failedImage;

    public:
        DocumentResources(uint64_t identity, uint64_t owner, uint64_t group, FileResourceCache& cache,
                          URUI::FileInterface* fallback)
            : ResourceContext(identity, owner), cache(cache), fallback(fallback), files(fallback), group(group)
        {
        }
        URUI::FileInterface* GetFileInterface() const override
        {
            return &files;
        }
        uint64_t GetGroup() const
        {
            return group;
        }
        bool IsRetired() const
        {
            return retired;
        }
        const URUI::EffectImageCondition* GetEffectImageCondition(const URUI::String& name) const override
        {
            const auto found = effectImageConditions.find(name);
            return found == effectImageConditions.end() ? nullptr : &found->second;
        }
        void RegisterEffectImageCondition(const URUI::String& name, URUI::EffectImageCondition condition)
        {
            if (retired || !effectImageConditions.emplace(name, std::move(condition)).second)
            {
                throw std::invalid_argument("Duplicate or retired effect image condition");
            }
        }
        void SetTextureRetirement(std::function<void(uint64_t)> retire)
        {
            retireTextures = std::move(retire);
        }
        FileResourceCache::Reference ClaimImage(const URUI::String& source, const uint8_t* bytes, size_t length,
                                                const URUI::String& kind)
        {
            if (retired)
            {
                throw std::logic_error("Resource generation is retired");
            }
            return cache.Acquire(group, source, bytes, length, kind);
        }
        uint64_t FindImage(const URUI::String& source) const
        {
            const auto found = images.find(source);
            return found == images.end() ? 0 : found->second->id;
        }
        void BindImage(const URUI::String& source, uint64_t id, FileResourceCache::Reference claim,
                       std::function<void()> release)
        {
            auto binding = std::make_shared<ImageBinding>();
            binding->id = id;
            binding->claim = std::move(claim);
            binding->release = std::move(release);
            images.insert_or_assign(source, std::move(binding));
        }
        const URUI::Sprite* GetSprite(const URUI::String& name) const override
        {
            const auto found = sprites.find(name);
            return found == sprites.end() ? nullptr : &found->second->sprite;
        }
        void RegisterSprite(const URUI::String& name, const URUI::String& source, URUI::Rectanglef rectangle,
                            float scale)
        {
            if (retired)
            {
                throw std::logic_error("Resource generation is retired");
            }
            auto binding = std::make_shared<SpriteBinding>();
            const auto key = std::make_pair(source, scale);
            auto found = spriteSheets.find(key);
            if (found == spriteSheets.end())
            {
                found =
                    spriteSheets.emplace(key, URUI::MakeShared<URUI::Spritesheet>(source, source, "", 0, scale)).first;
            }
            binding->sheet = found->second;
            binding->sprite = URUI::Sprite{rectangle, binding->sheet.get()};
            sprites.insert_or_assign(name, std::move(binding));
        }
        bool RemoveImage(const URUI::String& source)
        {
            return images.erase(source) != 0;
        }
        void ImageFailed(const URUI::String& source)
        {
            if (failedImage.empty())
            {
                failedImage = source;
            }
        }
        const URUI::String& FailedImage() const
        {
            return failedImage;
        }
        void Register(const URUI::String& source, const uint8_t* bytes, size_t length)
        {
            if (retired)
            {
                throw std::logic_error("Resource generation is retired");
            }
            auto binding = cache.Acquire(group, source, bytes, length);
            files.SetShared(source, binding->bytes);
            bindings.insert_or_assign(source, std::move(binding));
        }
        std::shared_ptr<DocumentResources> Clone(uint64_t identity, uint64_t owner) const
        {
            auto candidate = std::make_shared<DocumentResources>(identity, owner, group, cache, fallback);
            candidate->bindings = bindings;
            candidate->images = images;
            candidate->sprites = sprites;
            candidate->spriteSheets = spriteSheets;
            candidate->effectImageConditions = effectImageConditions;
            candidate->retireTextures = retireTextures;
            for (const auto& binding : bindings)
            {
                candidate->files.SetShared(binding.first, binding.second->bytes);
            }
            return candidate;
        }
        void Retire() noexcept override
        {
            if (retired)
            {
                return;
            }
            retired = true;
            if (retireTextures)
            {
                try
                {
                    retireTextures(GetIdentity());
                }
                catch (...)
                {
                }
            }
            sprites.clear();
            spriteSheets.clear();
            images.clear();
            bindings.clear();
            effectImageConditions.clear();
        }
        ~DocumentResources() override
        {
            Retire();
        }
    };

}
