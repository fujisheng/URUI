#include "../../Include/URUI/Core/RenderManager.h"
#include "../../Include/URUI/Core/Core.h"
#include "../../Include/URUI/Core/Geometry.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/RenderInterface.h"
#include "../../Include/URUI/Core/SystemInterface.h"
#include "TextureDatabase.h"

namespace URUI
{

    RenderManager::RenderManager(RenderInterface* render_interface)
        : render_interface(render_interface), texture_database(MakeUnique<TextureDatabase>())
    {
        URUI_ASSERT(render_interface);

        constexpr size_t reserve_geometry = 256;
        geometry_list.reserve(reserve_geometry);
    }

    RenderManager::~RenderManager()
    {
        struct ResourceCount
        {
            const char* name;
            int count;
        };
        ResourceCount elements[] = {
            {"Geometry", (int)geometry_list.size()},
            {"CompiledFilter", compiled_filter_count},
            {"CompiledShader", compiled_shader_count},
            {"CallbackTexture", (int)texture_database->callback_database.size()},
        };

        for (auto& element : elements)
        {
            if (element.count != 0)
            {
                Log::Message(
                    Log::LT_ERROR,
                    "Leaking %s detected (%d). Ensure that all URUI resources have been released by the end of URUI::Shutdown.",
                    element.name, element.count);
            }
        }

        ReleaseAllTextures();
    }

    void RenderManager::PrepareRender(Vector2i dimensions)
    {
        clipping_reuse_active = true;
        clipping_input_valid = false;
        clipping_mutation_revision = 0;
#ifdef URUI_ENGINE_DEBUG
        const RenderState default_state;
        URUI_ASSERT(state.clip_mask_list == default_state.clip_mask_list);
        URUI_ASSERT(state.scissor_region == default_state.scissor_region);
        URUI_ASSERT(state.transform == default_state.transform);
        URUI_ASSERTMSG(
            render_stack.empty(),
            "Unbalanced render stack detected, ensure every PushLayer call has a corresponding call to PopLayer.");
#endif

        SetViewport(dimensions);
    }

    void RenderManager::SetViewport(Vector2i dimensions)
    {
        clipping_input_valid = false;
        viewport_dimensions = dimensions;
    }

    Vector2i RenderManager::GetViewport() const
    {
        return viewport_dimensions;
    }

    Geometry RenderManager::MakeGeometry(Mesh&& mesh)
    {
        if (!mesh)
        {
            return Geometry();
        }
        return Geometry(this, InsertGeometry(std::move(mesh)));
    }

    Texture RenderManager::LoadTexture(const String& source, const String& document_path,
                                       const SharedPtr<ResourceContext>& resources)
    {
        String path;
        GetSystemInterface()->JoinPath(path, document_path, source);

        Texture texture(this, texture_database->file_database.InsertTexture(path, resources));
        // A document candidate validates dependencies before publication, including
        // fixed-size and currently hidden images. Backend identity stays scoped.
        if (resources)
        {
            texture.GetDimensions();
        }
        return texture;
    }

    CallbackTexture RenderManager::MakeCallbackTexture(CallbackTextureFunction callback)
    {
        return CallbackTexture(this, texture_database->callback_database.CreateTexture(std::move(callback)));
    }

    void RenderManager::DisableScissorRegion()
    {
        SetScissorRegion(Rectanglei::MakeInvalid());
    }

    void RenderManager::SetScissorRegion(Rectanglei new_region)
    {
        clipping_input_valid = false;
        const bool old_scissor_enable = state.scissor_region.Valid();
        const bool new_scissor_enable = new_region.Valid();

        if (new_scissor_enable != old_scissor_enable)
        {
            render_interface->EnableScissorRegion(new_scissor_enable);
        }

        if (new_scissor_enable)
        {
            new_region = new_region.Intersect(Rectanglei::FromSize(viewport_dimensions));

            if (new_region != state.scissor_region)
            {
                render_interface->SetScissorRegion(new_region);
            }
        }

        state.scissor_region = new_region;
    }

    Rectanglei RenderManager::GetScissorRegion() const
    {
        return state.scissor_region;
    }

    void RenderManager::DisableClipMask()
    {
        clipping_input_valid = false;
        if (!state.clip_mask_list.empty())
        {
            state.clip_mask_list.clear();
            ApplyClipMask(state.clip_mask_list);
        }
    }

    void RenderManager::SetClipMask(ClipMaskOperation operation, Geometry* geometry, Vector2f translation)
    {
        clipping_input_valid = false;
        URUI_ASSERT(geometry && geometry->render_manager == this);
        state.clip_mask_list = {ClipMaskGeometry{operation, geometry, translation, nullptr}};
        ApplyClipMask(state.clip_mask_list);
    }

    void RenderManager::SetClipMask(ClipMaskGeometryList in_clip_elements)
    {
        clipping_input_valid = false;
        if (state.clip_mask_list != in_clip_elements)
        {
            state.clip_mask_list = std::move(in_clip_elements);
            ApplyClipMask(state.clip_mask_list);
        }
    }

    void RenderManager::SetTransform(const Matrix4f* p_new_transform)
    {
        static const Matrix4f identity_transform = Matrix4f::Identity();
        const Matrix4f& new_transform = (p_new_transform ? *p_new_transform : identity_transform);

        if (state.transform != new_transform)
        {
            render_interface->SetTransform(p_new_transform);
            state.transform = new_transform;
        }
    }

    void RenderManager::ApplyClipMask(const ClipMaskGeometryList& clip_elements)
    {
        const bool clip_mask_enabled = !clip_elements.empty();
        render_interface->EnableClipMask(clip_mask_enabled);

        if (clip_mask_enabled)
        {
            const Matrix4f initial_transform = state.transform;

            for (const ClipMaskGeometry& element_clip : clip_elements)
            {
                URUI_ASSERT(element_clip.geometry->render_manager == this);
                SetTransform(element_clip.transform);
                if (CompiledGeometryHandle handle = GetCompiledGeometryHandle(element_clip.geometry->resource_handle))
                {
                    render_interface->RenderToClipMask(element_clip.operation, handle, element_clip.absolute_offset);
                }
            }

            // Apply the initially set transform in case it was changed.
            SetTransform(&initial_transform);
        }
    }

    void RenderManager::SetState(const RenderState& next)
    {
        SetScissorRegion(next.scissor_region);

        SetClipMask(next.clip_mask_list);

        SetTransform(&next.transform);
    }

    void RenderManager::ResetState()
    {
        clipping_reuse_active = false;
        clipping_input_valid = false;
        SetState(RenderState{});
    }

    void RenderManager::AbortRender() noexcept
    {
        clipping_reuse_active = false;
        clipping_input_valid = false;
        state = RenderState{};
        render_stack.clear();
        document_layers.clear();
    }

    StableVectorIndex RenderManager::InsertGeometry(Mesh&& mesh)
    {
        return geometry_list.insert(GeometryData{std::move(mesh), CompiledGeometryHandle{}});
    }

    CompiledGeometryHandle RenderManager::GetCompiledGeometryHandle(StableVectorIndex index)
    {
        if (index == StableVectorIndex::Invalid)
        {
            return {};
        }

        GeometryData& geometry = geometry_list[index];
        if (!geometry.handle && !geometry.mesh.indices.empty())
        {
            URUI_ZoneScopedNC("CompileGeometry", 0x1E60D2);
            geometry.handle = render_interface->CompileGeometry(geometry.mesh.vertices, geometry.mesh.indices);
            if (geometry.handle && geometry.mesh.texture_sampling != TextureSampling::Clamp)
            {
                render_interface->SetGeometryTextureSampling(geometry.handle, geometry.mesh.texture_sampling);
            }

            if (!geometry.handle)
            {
                Log::Message(Log::LT_ERROR, "Got empty compiled geometry.");
            }
        }
        return geometry.handle;
    }

    void RenderManager::Render(const Geometry& geometry, Vector2f translation, Texture texture,
                               const CompiledShader& shader, const DrawParameters& parameters)
    {
        URUI_ASSERT(geometry);
        URUI_ASSERTMSG(translation == translation.Round(), "RenderManager::Render expects translation to be rounded");

        if (geometry.render_manager != this || (shader && shader.render_manager != this) ||
            (texture && texture.render_manager != this))
        {
            URUI_ERRORMSG("Trying to render geometry with resources constructed in different render managers.");
            return;
        }

        if (CompiledGeometryHandle geometry_handle = GetCompiledGeometryHandle(geometry.resource_handle))
        {
            TextureHandle texture_handle = {};
            if (texture.file_index != TextureFileIndex::Invalid)
            {
                texture_handle = texture_database->file_database.GetHandle(render_interface, texture.file_index);
            }
            else if (texture.callback_index != StableVectorIndex::Invalid)
            {
                texture_handle =
                    texture_database->callback_database.GetHandle(this, render_interface, texture.callback_index);
            }

            if (texture && !texture_handle)
            {
                return;
            }

            URUI_ZoneScopedNC("RenderGeometry", 0x3E60B2);
            if (shader)
            {
                render_interface->RenderShader(shader.resource_handle, geometry_handle, translation, texture_handle,
                                               parameters);
            }
            else
            {
                render_interface->RenderGeometry(geometry_handle, translation, texture_handle, parameters);
            }
        }
    }

    void RenderManager::GetTextureSourceList(StringList& source_list) const
    {
        texture_database->file_database.GetSourceList(source_list);
    }

    const Mesh& RenderManager::GetMesh(const Geometry& geometry) const
    {
        URUI_ASSERT(geometry.render_manager == this && geometry.resource_handle != geometry.InvalidHandle());
        return geometry_list[geometry.resource_handle].mesh;
    }

    bool RenderManager::ReleaseTexture(const String& texture_source, const SharedPtr<ResourceContext>& resources)
    {
        return texture_database->file_database.ReleaseTexture(render_interface, texture_source, resources);
    }

    void RenderManager::RetireResourceContext(uint64_t identity)
    {
        texture_database->file_database.RetireContext(render_interface, identity);
    }

    void RenderManager::ReleaseAllTextures()
    {
        texture_database->callback_database.ReleaseAllTextures(render_interface);
        texture_database->file_database.ReleaseAllTextures(render_interface);
    }

    void RenderManager::ReleaseAllCompiledGeometry()
    {
        geometry_list.for_each(
            [this](GeometryData& data)
            {
                if (data.handle)
                {
                    render_interface->ReleaseGeometry(data.handle);
                    data.handle = {};
                }
            });
    }

    CompiledFilter RenderManager::CompileFilter(const String& name, const Dictionary& parameters,
                                                const SharedPtr<ResourceContext>& resources)
    {
        if (CompiledFilterHandle handle = render_interface->CompileFilter(name, parameters, resources))
        {
            compiled_filter_count += 1;
            return CompiledFilter(this, handle);
        }

        return CompiledFilter();
    }

    CompiledShader RenderManager::CompileShader(const String& name, const Dictionary& parameters)
    {
        if (CompiledShaderHandle handle = render_interface->CompileShader(name, parameters))
        {
            compiled_shader_count += 1;
            return CompiledShader(this, handle);
        }

        return CompiledShader();
    }

    void RenderManager::BeginDocument(bool isolate)
    {
        document_layers.emplace_back(GetTopLayer(), isolate);
        if (isolate)
        {
            PushLayer();
        }
    }
    void RenderManager::EndDocument()
    {
        URUI_ASSERT(!document_layers.empty());
        if (document_layers.back().second)
        {
            CompositeLayers(GetTopLayer(), GetNextLayer(), BlendMode::Blend, {});
            PopLayer();
        }
        document_layers.pop_back();
    }
    void RenderManager::CompositeSurfaceBackdrop(LayerHandle destination)
    {
        const auto surface = document_layers.empty() ? LayerHandle{} : document_layers.back().first;
        CompositeLayers(surface, destination, BlendMode::Replace, {});
        const auto baseline = std::find(render_stack.begin(), render_stack.end(), surface);
        const auto begin = baseline == render_stack.end() ? render_stack.begin() : baseline + 1;
        // Include this document's earlier pixels, including any currently open
        // isolated groups. The destination is a separate transparent snapshot.
        for (auto layer = begin; layer != render_stack.end() && *layer != destination; ++layer)
        {
            CompositeLayers(*layer, destination, BlendMode::Blend, {});
        }
    }

    LayerHandle RenderManager::PushLayer()
    {
        const LayerHandle layer = render_interface->PushLayer();
        render_stack.push_back(layer);
        return layer;
    }

    void RenderManager::CompositeLayers(LayerHandle source, LayerHandle destination, BlendMode blend_mode,
                                        Span<const CompiledFilterHandle> filters)
    {
        URUI_ASSERT(source == 0 || std::find(render_stack.begin(), render_stack.end(), source) != render_stack.end());
        URUI_ASSERT(destination == 0 ||
                    std::find(render_stack.begin(), render_stack.end(), destination) != render_stack.end());
        render_interface->CompositeLayers(source, destination, blend_mode, filters);
    }

    void RenderManager::PopLayer()
    {
        URUI_ASSERT(!render_stack.empty());
        render_interface->PopLayer();
        render_stack.pop_back();
    }

    LayerHandle RenderManager::GetTopLayer() const
    {
        return render_stack.empty() ? LayerHandle{} : render_stack.back();
    }

    LayerHandle RenderManager::GetNextLayer() const
    {
        URUI_ASSERT(!render_stack.empty());
        return render_stack.size() < 2 ? LayerHandle{} : render_stack[render_stack.size() - 2];
    }

    CompiledFilter RenderManager::SaveLayerAsMaskImage()
    {
        if (CompiledFilterHandle handle = render_interface->SaveLayerAsMaskImage())
        {
            compiled_filter_count += 1;
            return CompiledFilter(this, handle);
        }
        return CompiledFilter();
    }

    void RenderManager::ReleaseResource(const CallbackTexture& texture)
    {
        URUI_ASSERT(texture.render_manager == this && texture.resource_handle != texture.InvalidHandle());

        texture_database->callback_database.ReleaseTexture(render_interface, texture.resource_handle);
    }

    Mesh RenderManager::ReleaseResource(const Geometry& geometry)
    {
        URUI_ASSERT(geometry.render_manager == this && geometry.resource_handle != geometry.InvalidHandle());
        URUI_ZoneScopedNC("ReleaseGeometry", 0x1E60D2);

        GeometryData data = geometry_list.erase(geometry.resource_handle);
        if (data.handle)
        {
            render_interface->ReleaseGeometry(data.handle);
        }
        return std::move(data.mesh);
    }

    void RenderManager::ReleaseResource(const CompiledFilter& filter)
    {
        URUI_ASSERT(filter.render_manager == this && filter.resource_handle != filter.InvalidHandle());

        render_interface->ReleaseFilter(filter.resource_handle);
        compiled_filter_count -= 1;
    }

    void RenderManager::ReleaseResource(const CompiledShader& shader)
    {
        URUI_ASSERT(shader.render_manager == this && shader.resource_handle != shader.InvalidHandle());

        render_interface->ReleaseShader(shader.resource_handle);
        compiled_shader_count -= 1;
    }

}
