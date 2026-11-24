#pragma once

#include "../../Include/URUI/Core/CallbackTexture.h"
#include "../../Include/URUI/Core/StableVector.h"
#include "../../Include/URUI/Core/Types.h"

namespace URUI
{

    class RenderInterface;
    class ResourceContext;

    class CallbackTextureDatabase : NonCopyMoveable
    {
    public:
        CallbackTextureDatabase();
        ~CallbackTextureDatabase();

        StableVectorIndex CreateTexture(CallbackTextureFunction&& callback);
        void ReleaseTexture(RenderInterface* render_interface, StableVectorIndex callback_index);

        Vector2i GetDimensions(RenderManager* render_manager, RenderInterface* render_interface,
                               StableVectorIndex callback_index);
        TextureHandle GetHandle(RenderManager* render_manager, RenderInterface* render_interface,
                                StableVectorIndex callback_index);

        size_t size() const;

        void ReleaseAllTextures(RenderInterface* render_interface);

    private:
        struct CallbackTextureEntry
        {
            CallbackTextureFunction callback;
            TextureHandle texture_handle = {};
            Vector2i dimensions;
            bool load_failed = false;
        };

        CallbackTextureEntry& EnsureLoaded(RenderManager* render_manager, RenderInterface* render_interface,
                                           StableVectorIndex callback_index);

        StableVector<CallbackTextureEntry> texture_list;
    };

    class FileTextureDatabase : NonCopyMoveable
    {
    public:
        FileTextureDatabase();
        ~FileTextureDatabase();

        TextureFileIndex InsertTexture(const String& source, const SharedPtr<ResourceContext>& resources);

        TextureHandle GetHandle(RenderInterface* render_interface, TextureFileIndex index);
        Vector2i GetDimensions(RenderInterface* render_interface, TextureFileIndex index);

        void GetSourceList(StringList& source_list) const;

        bool ReleaseTexture(RenderInterface* render_interface, const String& source,
                            const SharedPtr<ResourceContext>& resources);
        void RetireContext(RenderInterface* render_interface, uint64_t identity);
        void ReleaseAllTextures(RenderInterface* render_interface);

    private:
        struct FileTextureEntry
        {
            String source;
            SharedPtr<ResourceContext> resources;
            bool retired = false;
            TextureHandle texture_handle = {};
            Vector2i dimensions;
            bool load_texture_failed = false;
        };

        static String Key(const String& source, const SharedPtr<ResourceContext>& resources);
        FileTextureEntry& EnsureLoaded(RenderInterface* render_interface, TextureFileIndex index);

        Vector<FileTextureEntry> texture_list;
        Vector<TextureFileIndex> free_slots;
        // key: source, value: index into 'texture_list'
        UnorderedMap<String, TextureFileIndex> texture_map;
    };

    class TextureDatabase
    {
    public:
        FileTextureDatabase file_database;
        CallbackTextureDatabase callback_database;
    };

}
