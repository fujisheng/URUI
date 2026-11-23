#include "BoxShadowCache.h"
#include "../../Include/URUI/Core/ElementDocument.h"
#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/MeshUtilities.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/RenderManager.h"
#include "../Core/ControlledLifetimeResource.h"
#include "BoxShadowHash.h"
#include "GeometryBoxShadow.h"
#include <algorithm>

namespace URUI
{

    struct BoxShadowCacheData
    {
        StableUnorderedMap<BoxShadowGeometryInfo, WeakPtr<BoxShadowRenderable>> handles;
    };

    static void ReleaseHandle(BoxShadowRenderable* handle);

    BoxShadowRenderable::BoxShadowRenderable(const BoxShadowGeometryInfo& geometry_info) : cache_key(geometry_info)
    {
    }

    BoxShadowRenderable::~BoxShadowRenderable()
    {
        ReleaseHandle(this);
    }

    static ControlledLifetimeResource<BoxShadowCacheData> shadow_cache_data;

    void BoxShadowCache::Initialize()
    {
        shadow_cache_data.Initialize();
    }

    void BoxShadowCache::Shutdown()
    {
        shadow_cache_data.Shutdown();
    }

    static SharedPtr<BoxShadowRenderable> GetOrCreateBoxShadow(RenderManager& render_manager,
                                                               const BoxShadowGeometryInfo& info)
    {
        URUI_ZoneScoped;
        auto it_handle = shadow_cache_data->handles.find(info);
        if (it_handle != shadow_cache_data->handles.end())
        {
            SharedPtr<BoxShadowRenderable> result = it_handle->second.lock();
            URUI_ASSERTMSG(result, "Failed to lock handle in Box Shadow cache");
            return result;
        }

        const auto iterator_inserted = shadow_cache_data->handles.emplace(info, WeakPtr<BoxShadowRenderable>());
        URUI_ASSERTMSG(iterator_inserted.second,
                       "Could not insert entry into the Box Shadow cache handle map, duplicate key.");
        const BoxShadowGeometryInfo& inserted_key = iterator_inserted.first->first;
        WeakPtr<BoxShadowRenderable>& inserted_weak_data_pointer = iterator_inserted.first->second;

        auto shadow_handle = MakeShared<BoxShadowRenderable>(inserted_key);
        GeometryBoxShadow::GenerateShadow(shadow_handle->texture, shadow_handle->geometry,
                                          shadow_handle->background_border_geometry, render_manager, inserted_key);

        inserted_weak_data_pointer = shadow_handle;
        return shadow_handle;
    }

    static void ReleaseHandle(BoxShadowRenderable* handle)
    {
        // There are no longer any users of the cache entry uniquely identified by the handle address. Start from the
        // tip (i.e. per-color data) and remove that entry from its parent. Move up the cache ancestry and erase any
        // entries that no longer have any children.
        auto& handles = shadow_cache_data->handles;
        const BoxShadowGeometryInfo& key = handle->cache_key;

        auto it_handle = handles.find(key);
        URUI_ASSERT(it_handle != handles.cend());

        handles.erase(it_handle);
    }

    SharedPtr<BoxShadowRenderable> BoxShadowCache::GetHandle(Element* element, const ComputedValues& computed,
                                                             bool inset)
    {
        RenderManager* render_manager = element->GetRenderManager();
        if (!render_manager)
        {
            return {};
        }

        // 外阴影与内阴影分别缓存，底色/图像/边框由元素按层次绘制。
        const ColourbPremultiplied transparent(0, 0);
        BoxShadowGeometryInfo geom_info = GeometryBoxShadow::Resolve(
            element, computed.border_radius(), transparent, {transparent, transparent, transparent, transparent}, 1.f);
        auto& shadows = geom_info.shadow_list;
        shadows.erase(std::remove_if(shadows.begin(), shadows.end(),
                                     [inset](const BoxShadow& shadow) { return shadow.inset != inset; }),
                      shadows.end());
        if (shadows.empty())
        {
            return {};
        }
        geom_info.resources = element->GetOwnerDocument() ? element->GetOwnerDocument()->GetResourceContext() : nullptr;
        geom_info.render_manager = render_manager;
        return GetOrCreateBoxShadow(*render_manager, geom_info);
    }

}
