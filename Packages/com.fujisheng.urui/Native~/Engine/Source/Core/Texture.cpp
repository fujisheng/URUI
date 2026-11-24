#include "../../Include/URUI/Core/Texture.h"
#include "RenderManagerAccess.h"
#include "../../Include/URUI/Core/ResourceContext.h"

namespace URUI
{

    Texture::Texture(RenderManager* render_manager, TextureFileIndex file_index)
        : render_manager(render_manager), file_index(file_index)
    {
    }

    Texture::Texture(RenderManager* render_manager, StableVectorIndex callback_index)
        : render_manager(render_manager), callback_index(callback_index)
    {
    }

    Vector2i Texture::GetDimensions() const
    {
        if (file_index != TextureFileIndex::Invalid)
        {
            return RenderManagerAccess::GetDimensions(render_manager, file_index);
        }
        if (callback_index != StableVectorIndex::Invalid)
        {
            return RenderManagerAccess::GetDimensions(render_manager, callback_index);
        }
        return {};
    }

    Texture::operator bool() const
    {
        return callback_index != StableVectorIndex::Invalid || file_index != TextureFileIndex::Invalid;
    }

    bool Texture::operator==(const Texture& other) const
    {
        return render_manager == other.render_manager && file_index == other.file_index &&
               callback_index == other.callback_index;
    }

    TextureSource::TextureSource(String source, String document_path)
        : source(std::move(source)), document_path(std::move(document_path))
    {
    }

    Texture TextureSource::GetTexture(RenderManager& render_manager, const SharedPtr<ResourceContext>& resources) const
    {
        constexpr size_t MaxRenderManagers = 16;
        constexpr size_t MaxResourceContextsPerManager = 64;
        if (textures.size() >= MaxRenderManagers && textures.find(&render_manager) == textures.end())
        {
            textures.clear();
        }
        auto& by_context = textures[&render_manager];
        const uint64_t identity = resources ? resources->GetIdentity() : 0;
        if (by_context.size() >= MaxResourceContextsPerManager && by_context.find(identity) == by_context.end())
        {
            by_context.clear();
        }
        Texture& texture = by_context[identity];
        if (!texture)
        {
            texture = render_manager.LoadTexture(source, document_path, resources);
        }
        return texture;
    }

    const String& TextureSource::GetSource() const
    {
        return source;
    }

    const String& TextureSource::GetDefinitionSource() const
    {
        return document_path;
    }

}
