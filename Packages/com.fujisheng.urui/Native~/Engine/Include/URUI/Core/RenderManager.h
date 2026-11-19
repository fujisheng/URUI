#pragma once

#include "CallbackTexture.h"
#include "Mesh.h"
#include "RenderInterface.h"
#include "StableVector.h"
#include "Types.h"

namespace URUI
{

    class ResourceContext;

    class Geometry;
    class CompiledFilter;
    class CompiledShader;
    class TextureDatabase;
    class Texture;
    class RenderManagerAccess;

    struct ClipMaskGeometry
    {
        ClipMaskOperation operation;
        Geometry* geometry;
        Vector2f absolute_offset;
        const Matrix4f* transform;
    };
    inline bool operator==(const ClipMaskGeometry& a, const ClipMaskGeometry& b)
    {
        return a.operation == b.operation && a.geometry == b.geometry && a.absolute_offset == b.absolute_offset &&
               a.transform == b.transform;
    }
    inline bool operator!=(const ClipMaskGeometry& a, const ClipMaskGeometry& b)
    {
        return !(a == b);
    }
    using ClipMaskGeometryList = Vector<ClipMaskGeometry>;

    struct RenderState
    {
        Rectanglei scissor_region = Rectanglei::MakeInvalid();
        ClipMaskGeometryList clip_mask_list;
        Matrix4f transform = Matrix4f::Identity();
    };

    /**
    A wrapper over the render interface, which tracks its state and resources.

    All operations to be submitted to the render interface should go through this class.
 */
    class URUI_CORE_API RenderManager : NonCopyMoveable
    {
    public:
        RenderManager(RenderInterface* render_interface);
        ~RenderManager();

        void PrepareRender(Vector2i dimensions);
        void SetViewport(Vector2i dimensions);
        Vector2i GetViewport() const;

        void DisableScissorRegion();
        void SetScissorRegion(Rectanglei region);
        Rectanglei GetScissorRegion() const;

        void DisableClipMask();
        void SetClipMask(ClipMaskGeometryList clip_elements);
        void SetClipMask(ClipMaskOperation operation, Geometry* geometry, Vector2f translation);

        void SetTransform(const Matrix4f* new_transform);

        // Retrieves the cached render state. If setting this state again, ensure the lifetimes of referenced objects are
        // still valid. Possibly invalidating actions include destroying an element, or altering its transform property.
        const RenderState& GetState() const
        {
            return state;
        }
        void SetState(const RenderState& next);
        void ResetState();
        // Discard CPU recording state without invoking an allocating render callback.
        void AbortRender() noexcept;

        Geometry MakeGeometry(Mesh&& mesh);

        Texture LoadTexture(const String& source, const String& document_path = String(),
                            const SharedPtr<ResourceContext>& resources = {});
        CallbackTexture MakeCallbackTexture(CallbackTextureFunction callback);
        bool ReleaseTexture(const String& texture_source, const SharedPtr<ResourceContext>& resources = {});
        void RetireResourceContext(uint64_t identity);

        CompiledFilter CompileFilter(const String& name, const Dictionary& parameters,
                                     const SharedPtr<ResourceContext>& resources = {});
        CompiledShader CompileShader(const String& name, const Dictionary& parameters);

        void BeginDocument(bool isolate_backdrop);
        void EndDocument();
        void CompositeSurfaceBackdrop(LayerHandle destination);
        LayerHandle PushLayer();
        void CompositeLayers(LayerHandle source, LayerHandle destination, BlendMode blend_mode,
                             Span<const CompiledFilterHandle> filters);
        void PopLayer();

        LayerHandle GetTopLayer() const;
        LayerHandle GetNextLayer() const;

        CompiledFilter SaveLayerAsMaskImage();

    private:
        Vector<Pair<LayerHandle, bool>> document_layers;
        void ApplyClipMask(const ClipMaskGeometryList& clip_elements);

        StableVectorIndex InsertGeometry(Mesh&& mesh);
        CompiledGeometryHandle GetCompiledGeometryHandle(StableVectorIndex index);

        void Render(const Geometry& geometry, Vector2f translation, Texture texture, const CompiledShader& shader,
                    const DrawParameters& parameters);

        void GetTextureSourceList(StringList& source_list) const;
        const Mesh& GetMesh(const Geometry& geometry) const;

        void ReleaseAllTextures();
        void ReleaseAllCompiledGeometry();

        void ReleaseResource(const CallbackTexture& texture);
        Mesh ReleaseResource(const Geometry& geometry);
        void ReleaseResource(const CompiledFilter& filter);
        void ReleaseResource(const CompiledShader& shader);

        struct GeometryData
        {
            Mesh mesh;
            CompiledGeometryHandle handle = {};
        };

        RenderInterface* render_interface = nullptr;

        StableVector<GeometryData> geometry_list;
        UniquePtr<TextureDatabase> texture_database;

        int compiled_filter_count = 0;
        int compiled_shader_count = 0;

        RenderState state;
        // 只标记本次渲染中当前裁剪状态的来源，不保存逐节点结果或对象所有权。
        const Element* clipping_parent = nullptr;
        int clipping_ignored = 0;
        bool clipping_ignores_all = false;
        bool clipping_input_valid = false;
        bool clipping_reuse_active = false;
        uint64_t clipping_mutation_revision = 0;
        Vector2i viewport_dimensions;

        Vector<LayerHandle> render_stack;

        friend class RenderManagerAccess;
        friend class ElementUtilities;
        friend class Context;
    };

}
