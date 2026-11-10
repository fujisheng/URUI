#include "TextureDatabase.h"
#include "../../Include/URUI/Core/ResourceContext.h"
#include "../../Include/URUI/Core/Log.h"
#include "../../Include/URUI/Core/RenderInterface.h"

namespace URUI {

CallbackTextureDatabase::CallbackTextureDatabase()
{
	constexpr size_t reserve_callback_textures = 30;
	texture_list.reserve(reserve_callback_textures);
}

CallbackTextureDatabase::~CallbackTextureDatabase()
{
	if (!texture_list.empty())
	{
		Log::Message(Log::LT_ERROR, "TextureDatabase destroyed with outstanding callback textures. Will likely result in memory corruption.");
		URUI_ERROR;
	}
}

StableVectorIndex CallbackTextureDatabase::CreateTexture(CallbackTextureFunction&& callback)
{
	URUI_ASSERT(callback);
	return texture_list.insert(CallbackTextureEntry{std::move(callback), TextureHandle(), Vector2i()});
}

void CallbackTextureDatabase::ReleaseTexture(RenderInterface* render_interface, StableVectorIndex callback_index)
{
	CallbackTextureEntry& data = texture_list[callback_index];
	if (data.texture_handle)
		render_interface->ReleaseTexture(data.texture_handle);
	texture_list.erase(callback_index);
}

Vector2i CallbackTextureDatabase::GetDimensions(RenderManager* render_manager, RenderInterface* render_interface, StableVectorIndex callback_index)
{
	return EnsureLoaded(render_manager, render_interface, callback_index).dimensions;
}

TextureHandle CallbackTextureDatabase::GetHandle(RenderManager* render_manager, RenderInterface* render_interface, StableVectorIndex callback_index)
{
	return EnsureLoaded(render_manager, render_interface, callback_index).texture_handle;
}

auto CallbackTextureDatabase::EnsureLoaded(RenderManager* render_manager, RenderInterface* render_interface, StableVectorIndex callback_index)
	-> CallbackTextureEntry&
{
	CallbackTextureEntry& data = texture_list[callback_index];
	if (!data.texture_handle && !data.load_failed)
	{
		if (!data.callback(CallbackTextureInterface(*render_manager, *render_interface, data.texture_handle, data.dimensions)))
		{
			data.load_failed = true;
			data.texture_handle = {};
			data.dimensions = {};
		}
	}
	return data;
}

size_t CallbackTextureDatabase::size() const
{
	return texture_list.size();
}

void CallbackTextureDatabase::ReleaseAllTextures(RenderInterface* render_interface)
{
	texture_list.for_each([render_interface](CallbackTextureEntry& texture) {
		if (texture.texture_handle)
		{
			render_interface->ReleaseTexture(texture.texture_handle);
			texture.texture_handle = {};
			texture.dimensions = {};
		}
	});
}

FileTextureDatabase::FileTextureDatabase() {}

FileTextureDatabase::~FileTextureDatabase()
{
#ifdef URUI_ENGINE_DEBUG
	for (const FileTextureEntry& texture : texture_list)
	{
		URUI_ASSERTMSG(!texture.texture_handle,
			"TextureDatabase destroyed without releasing all file textures first. Ensure that 'ReleaseAllTextures' is called before destruction.");
	}
#endif
}

String FileTextureDatabase::Key(const String& source, const SharedPtr<ResourceContext>& resources)
{
    return std::to_string(resources ? resources->GetIdentity() : 0) + ":" + source;
}

TextureFileIndex FileTextureDatabase::InsertTexture(const String& source, const SharedPtr<ResourceContext>& resources)
{
    const auto key = Key(source, resources);
    auto found = texture_map.find(key);
    if (found != texture_map.end()) return found->second;
    const auto index = TextureFileIndex(texture_list.size());
    FileTextureEntry entry; entry.source = source; entry.resources = resources;
    texture_list.push_back(std::move(entry));
    texture_map.emplace(key, index);
    return index;
}

FileTextureDatabase::FileTextureEntry& FileTextureDatabase::EnsureLoaded(RenderInterface* render_interface, TextureFileIndex index)
{
    auto& entry = texture_list[size_t(index)];
    if (!entry.texture_handle && !entry.load_texture_failed && !entry.retired) {
        // Callbacks may insert more file textures; retain values and reacquire
        // the vector entry after calling the host.
        const auto source = entry.source;
        const auto resources = entry.resources;
        Vector2i dimensions;
        const auto handle = render_interface->LoadTexture(dimensions, source, resources);
        auto& loaded = texture_list[size_t(index)];
        loaded.texture_handle = handle; loaded.dimensions = dimensions;
        loaded.load_texture_failed = !handle;
        if (!handle) Log::Message(Log::LT_WARNING, "Could not load texture: %s", source.c_str());
    }
    return texture_list[size_t(index)];
}

TextureHandle FileTextureDatabase::GetHandle(RenderInterface* render_interface, TextureFileIndex index)
{
	URUI_ASSERT(size_t(index) < texture_list.size());
	return EnsureLoaded(render_interface, index).texture_handle;
}

Vector2i FileTextureDatabase::GetDimensions(RenderInterface* render_interface, TextureFileIndex index)
{
	URUI_ASSERT(size_t(index) < texture_list.size());
	return EnsureLoaded(render_interface, index).dimensions;
}

void FileTextureDatabase::GetSourceList(StringList& sources) const
{
    for (const auto& entry : texture_list) if (!entry.retired) sources.push_back(entry.source);
}

bool FileTextureDatabase::ReleaseTexture(RenderInterface* render, const String& source, const SharedPtr<ResourceContext>& resources)
{
    const auto found = texture_map.find(Key(source, resources));
    if (found == texture_map.end()) return false;
    auto& entry = texture_list[size_t(found->second)];
    const auto handle = entry.texture_handle;
    entry.texture_handle = {}; entry.dimensions = {}; entry.load_texture_failed = false;
    if (handle) render->ReleaseTexture(handle);
    return handle != 0;
}

void FileTextureDatabase::RetireContext(RenderInterface* render, uint64_t identity)
{
    for (auto& entry : texture_list) if (entry.resources && entry.resources->GetIdentity() == identity && !entry.retired) {
        texture_map.erase(Key(entry.source, entry.resources));
        const auto handle = entry.texture_handle;
        entry.texture_handle = {}; entry.dimensions = {}; entry.retired = true; entry.resources.reset();
        if (handle) render->ReleaseTexture(handle);
    }
}

void FileTextureDatabase::ReleaseAllTextures(RenderInterface* render)
{
    for (auto& entry : texture_list) {
        const auto handle = entry.texture_handle;
        entry.texture_handle = {}; entry.dimensions = {}; entry.load_texture_failed = false;
        if (handle) render->ReleaseTexture(handle);
    }
}

} // namespace URUI
