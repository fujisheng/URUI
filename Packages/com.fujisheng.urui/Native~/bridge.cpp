#include "Engine/Source/Core/HtmlBuilder.h"
#include "Engine/Source/Core/CompiledDocument.h"
#include "Engine/Source/Core/DocumentFontContext.h"
#include "Engine/Source/Core/HtmlParser.h"
#include "Engine/Source/Core/CssParser.h"
#include "urui.h"
#include "style_properties.h"
namespace urui_elements {
    URUI::ElementDocument* Document(UruiDocumentHandle owner);
    URUI::Element* Get(UruiDocumentHandle owner, uint64_t token);
    int ReplaceHtml(URUI::Element&, const char*);
}
#include "element_references.h"
#include "document_registry.h"
#include "resource_store.h"
#include "resource_files.h"
#include "render_plan.h"
#include "render_plan_compiler.h"
#include <URUI/Core.h>
#include <URUI/Core/ElementText.h>
#include <URUI/Core/RenderManager.h>
#include <URUI/Core/Texture.h>
#include <URUI/Core/URL.h>
#include <URUI/Core/ElementInstancer.h>
#include <URUI/Core/Elements/ElementFormControl.h>
#include <URUI/Core/Elements/ElementFormControlInput.h>
#include <URUI/Core/Transform.h>
#include <URUI/Core/StyleSheetContainer.h>
#include <URUI/Core/StreamMemory.h>
#include <URUI/Core/StyleSheetSpecification.h>
#include "image_geometry.h"
#include <URUI/Core/PropertyDefinition.h>
#include "Engine/Source/Core/ElementStyle.h"
#include "Engine/Source/Core/DirectColorFilters.h"
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <deque>
#include <memory>
#include <limits>
#include <string>
#include <unordered_map>
#include <vector>
#include <mutex>
#if defined(URUI_DEBUG)
#include "Debug/diagnostics.h"
using namespace urui_debug;
#endif

namespace urui_interaction
{
    extern int callback_depth;
}
namespace
{
    struct ImageCallbackScope
    {
        ImageCallbackScope() { ++urui_interaction::callback_depth; }
        ~ImageCallbackScope() { --urui_interaction::callback_depth; }
    };
    static_assert(sizeof(UruiVertex) == 20, "UruiVertex ABI changed");
    static_assert(sizeof(UruiCommand) == 48, "UruiCommand ABI changed");
    static_assert(sizeof(UruiLayerCommand) == 56, "UruiLayerCommand ABI changed");
    struct Geometry
    {
        std::vector<UruiVertex> vertices;
        std::vector<int> indices;
        URUI::TextureSampling texture_sampling = URUI::TextureSampling::Clamp;
        std::array<float,4> bounds{};
        bool bounds_valid = false;
    };
    struct Texture
    {
        int width = 0, height = 0;
        bool borrowed = false;
        bool resolved = false;
        size_t aliases = 0;
        size_t loads = 0;
        bool scoped = false;
        URUI::TextureHandle render_handle = 0;
        std::vector<unsigned char> pixels;
    };
    constexpr uint64_t borrowed_texture_bit = uint64_t{1} << 63;
    std::string error_message;
    void ReportBridgeFailure(const char* message) noexcept
    {
        try { error_message = message; } catch (...) {}
    }

    template <typename Handle> uint64_t AllocateTextureId(uint64_t& next)
    {
        // URUI uses uintptr_t handles, including on 32-bit WebGL. Public IDs
        // remain uint64_t, with the entire high-bit domain reserved for borrowing.
        constexpr uint64_t limit = std::numeric_limits<Handle>::max() < borrowed_texture_bit
                                       ? std::numeric_limits<Handle>::max()
                                       : borrowed_texture_bit - 1;
        if (!next || next > limit)
        {
            error_message = "Texture handle space exhausted";
            return 0;
        }
        return next++;
    }
    std::deque<URUI::ObserverPtr<URUI::Element>> clicks;
    RenderSerial render_serial;
    // 外线程查询会与 Context 重建并发，所有权检查必须是无数据竞争的读取。
    std::atomic<std::thread::id> context_thread;
    class Renderer : public URUI::RenderInterface
    {
    public:
        RenderPlan plan{render_serial};
        urui_plan::Session draw_plan;
        uint64_t document_generation=urui_plan::Identity();
        std::unordered_map<uint64_t, Geometry> meshes;
        std::unordered_map<uint64_t, Texture> textures;
        std::unordered_map<URUI::TextureHandle, uint64_t> borrowed_texture_handles;
        UruiImageResolver image_resolver = nullptr;
        UruiImageRelease image_release = nullptr;
        void ReleaseResolved(uint64_t id)
        {
            if (image_release)
            {
                ImageCallbackScope scope;
                try { image_release(id); }
                catch (...) { error_message = "Image release callback failed"; }
            }
        }
        std::vector<UruiCommand> commands;
        std::vector<UruiPlanUniform> command_uniforms;
        struct PaintShader
        {
            std::string name;
            URUI::ObserverPtr<URUI::Element> element;
            int kind = 0;
            std::vector<UruiColorFilter> color_filters;
        };
        struct PaintEntry { uint64_t shader; int command_index; };
        std::unordered_map<uint64_t, PaintShader> paint_shaders;
        std::vector<PaintEntry> paint_commands;
        std::unordered_map<int,std::array<float,4>> effect_ink_bounds;
        struct EffectRootReference {
            URUI::ObserverPtr<URUI::Element> element, root;
            URUI::String name;
            uint64_t structure = 0;
        };
        std::unordered_map<uint64_t, EffectRootReference> effect_roots;
        static bool EffectAttached(URUI::Element* element) {
            if (!element) return false;
            auto* document=element->GetOwnerDocument();
            for (auto* current=element;current;current=current->GetParentNode()) if (current==document) return true;
            return false;
        }
        void PruneEffectRoots() {
            for (auto iterator=effect_roots.begin();iterator!=effect_roots.end();)
                if (!EffectAttached(iterator->second.element.get())) iterator=effect_roots.erase(iterator); else ++iterator;
        }
        uint64_t next_paint_shader = 1;
        std::vector<UruiLayerCommand> layer_commands;
        std::vector<uint64_t> layer_stack = {0};
        std::unordered_map<uint64_t, uint64_t> mask_filters;
        struct Filter { std::string name; UruiFilter data{}; uint64_t document = 0; URUI::ObserverPtr<URUI::Element> element; std::array<float,4> expansion{}; std::vector<std::array<float,16>> coverage_transforms; };
        std::unordered_map<uint64_t, Filter> filters;
        uint64_t next_layer_resource = 1, next_mask_filter = 1;
        bool layer_valid = true;
        void LayerCommand(int kind, uint64_t source, uint64_t target, uint64_t mask = 0, int blend = 0)
        {
            UruiLayerCommand command{};
            command.command_index = static_cast<int>(commands.size()); command.kind = kind;
            command.source = source; command.target = target; command.mask = mask; command.blend = blend;
            command.flags = clipped ? 1 : 0;
            if (clipped) { command.clip_x = clip.Left(); command.clip_y = clip.Top(); command.clip_w = clip.Width(); command.clip_h = clip.Height(); }
            layer_commands.push_back(command);
        }
        std::vector<std::array<float, 16>> command_transforms;
        URUI::Matrix4f active_transform = URUI::Matrix4f::Identity();
        uint64_t next_mesh = 1, next_texture = 1;
        bool clipped = false;
        URUI::Rectanglei clip = {};
        URUI::Context* recorded_context=nullptr;
        uint64_t recorded_revision=0,recorded_document=0,recorded_resources=0,recorded_epoch=0;
        size_t recorded_command_count=0;
        uint64_t AllocateTextureHandle()
        {
            uint64_t id = AllocateTextureId<URUI::TextureHandle>(next_texture);
            if (id && (textures.count(id) || borrowed_texture_handles.count(static_cast<URUI::TextureHandle>(id))))
            {
                error_message = "Texture handle collision";
                return 0;
            }
            return id;
        }
        uint64_t LogicalTextureId(URUI::TextureHandle handle) const
        {
            auto found = borrowed_texture_handles.find(handle);
            return found == borrowed_texture_handles.end() ? static_cast<uint64_t>(handle) : found->second;
        }
        URUI::CompiledGeometryHandle CompileGeometry(URUI::Span<const URUI::Vertex> vs, URUI::Span<const int> ids) override
        {
            URUI_DEBUG_CODE(ProfileScope timer(profile.geometry_compile_ns);)
            URUI_DEBUG_CODE(++profile.geometry_compiles;)
            URUI_DEBUG_CODE(profile.geometry_vertices += static_cast<uint32_t>(vs.size());)
            URUI_DEBUG_CODE(profile.geometry_indices += static_cast<uint32_t>(ids.size());)
            constexpr uint64_t geometry_limit=sizeof(URUI::CompiledGeometryHandle)==4?UINT32_MAX:((uint64_t{1}<<63)-1);
            if(!next_mesh||next_mesh>geometry_limit)throw std::overflow_error("URUI geometry identity exhausted");
            uint64_t handle = next_mesh++;
            Geometry g;
            g.vertices.reserve(vs.size());
            for (auto& v : vs) {
                g.vertices.push_back({v.position.x, v.position.y, v.tex_coord.x, v.tex_coord.y, v.colour.red,
                                      v.colour.green, v.colour.blue, v.colour.alpha});
                if (!g.bounds_valid) { g.bounds={v.position.x,v.position.y,v.position.x,v.position.y};g.bounds_valid=true; }
                else { g.bounds[0]=std::min(g.bounds[0],v.position.x);g.bounds[1]=std::min(g.bounds[1],v.position.y);
                    g.bounds[2]=std::max(g.bounds[2],v.position.x);g.bounds[3]=std::max(g.bounds[3],v.position.y); }
            }
            g.indices.assign(ids.begin(), ids.end());
            meshes.emplace(handle, std::move(g));
            plan.ResourceChanged();
            return static_cast<URUI::CompiledGeometryHandle>(handle);
        }
        void SetGeometryTextureSampling(URUI::CompiledGeometryHandle geometry, URUI::TextureSampling sampling) override
        {
            auto found = meshes.find(static_cast<uint64_t>(geometry));
            if (found != meshes.end() && found->second.texture_sampling != sampling) {
                found->second.texture_sampling = sampling;
                plan.ResourceChanged();
            }
        }
        void RenderGeometry(URUI::CompiledGeometryHandle geo, URUI::Vector2f move, URUI::TextureHandle tex,const URUI::DrawParameters& parameters) override
        {
            UruiCommand c{};
            c.geometry = static_cast<uint64_t>(geo);
            c.texture = LogicalTextureId(tex);
            c.tx = move.x;
            c.ty = move.y;
            const auto geometry = meshes.find(c.geometry);
            const auto sampling = geometry == meshes.end() ? URUI::TextureSampling::Clamp : geometry->second.texture_sampling;
            c.flags = (clipped ? 1 : 0) | (int(sampling) << 1);
            if (clipped)
            {
                c.clip_x = clip.Left();
                c.clip_y = clip.Top();
                c.clip_w = clip.Width();
                c.clip_h = clip.Height();
            }
            commands.push_back(c);
            const auto& color=parameters.tint;
            command_uniforms.push_back({{color.red/255.f,color.green/255.f,color.blue/255.f,color.alpha/255.f},
                {parameters.uv_scale.x,parameters.uv_scale.y,parameters.uv_offset.x,parameters.uv_offset.y}});
            std::array<float, 16> matrix{};
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                    matrix[col * 4 + row] = active_transform[col][row];
            command_transforms.push_back(matrix);
        }
        void ReleaseGeometry(URUI::CompiledGeometryHandle h) override
        {
            if (meshes.erase(static_cast<uint64_t>(h)))
                plan.ResourceChanged();
        }
        void CollectTexture(uint64_t id)
        {
            auto found = textures.find(id);
            if (found == textures.end() || found->second.aliases || found->second.loads) return;
            const bool resolved = found->second.resolved;
            if (found->second.borrowed) borrowed_texture_handles.erase(found->second.render_handle);
            textures.erase(found); plan.ResourceChanged();
            if (resolved) ReleaseResolved(id);
        }
        void ReleaseAlias(uint64_t id)
        {
            const auto found = textures.find(id);
            if (found == textures.end() || !found->second.aliases) return;
            --found->second.aliases; CollectTexture(id);
        }
        uint64_t RegisterScopedImage(const std::shared_ptr<URUI::Bridge::DocumentResources>& resources,
            const std::string& source, int width, int height, const uint8_t* pixels, size_t length, uint64_t external = 0)
        {
            // The URI claim identifies real source content, independently of
            // native cache indices and per-resolver-call lease identities.
            std::vector<uint8_t> identity;
            for (uint64_t value : {uint64_t(width), uint64_t(height), external})
                for (int byte = 0; byte < 8; ++byte) identity.push_back(uint8_t(value >> (8 * byte)));
            if (!external) identity.insert(identity.end(), pixels, pixels + length);
            auto claim = resources->ClaimImage(source, identity.data(), identity.size(), external ? "external-image" : "rgba-image");
            // 来源身份保留原始字节，原生拥有的像素统一按纹理协议预乘透明度。
            std::vector<uint8_t> premultiplied;
            if (!external) {
                premultiplied.resize(length);
                for (size_t index = 0; index < length; index += 4) {
                    const auto alpha = pixels[index + 3];
                    for (size_t channel = 0; channel < 3; ++channel)
                        premultiplied[index + channel] = uint8_t((int(pixels[index + channel]) * alpha + 127) / 255);
                    premultiplied[index + 3] = alpha;
                }
            }
            uint64_t id = external;
            if (!external) {
                for (const auto& item : textures) {
                    const auto& texture = item.second;
                    if (texture.scoped && !texture.borrowed && texture.width == width && texture.height == height &&
                        texture.pixels == premultiplied) { id = item.first; break; }
                }
            }
            auto found = textures.find(id);
            if (found != textures.end() && (found->second.borrowed != bool(external) || found->second.resolved ||
                found->second.width != width || found->second.height != height)) throw std::invalid_argument("Texture ID kind or dimensions conflict: " + source);
            if (found == textures.end()) {
                const auto handle = AllocateTextureHandle();
                if (!handle) throw std::length_error("Texture handle space exhausted");
                if (!id) id = handle;
                Texture texture; texture.width = width; texture.height = height; texture.scoped = true;
                texture.borrowed = external != 0; texture.render_handle = URUI::TextureHandle(handle);
                if (!external) texture.pixels = std::move(premultiplied);
                textures.emplace(id, std::move(texture));
                if (external) borrowed_texture_handles.emplace(URUI::TextureHandle(handle), id);
            }
            auto& texture = textures.at(id);
            if (texture.aliases == SIZE_MAX) throw std::length_error("Texture alias limit reached");
            ++texture.aliases;
            try { resources->BindImage(source, id, std::move(claim), [this, id] { ReleaseAlias(id); }); }
            catch (...) { ReleaseAlias(id); throw; }
            plan.ResourceChanged(); return id;
        }
        URUI::TextureHandle LoadTexture(URUI::Vector2i& dims, const URUI::String& source, const URUI::SharedPtr<URUI::ResourceContext>& resources = {}) override
        {
            dims = {};
            auto owned = std::dynamic_pointer_cast<URUI::Bridge::DocumentResources>(resources);
            if (!owned) { error_message = "Image resolution requires a document owner"; return 0; }
            if (const auto id = owned->FindImage(source)) {
                const auto found = textures.find(id);
                if (found != textures.end()) {
                    dims = {found->second.width, found->second.height};
                    ++found->second.loads; return found->second.render_handle;
                }
            }
            if (!image_resolver) { owned->ImageFailed(source); error_message = "Texture not registered: " + source; return 0; }
            uint64_t id = 0, source_identity = 0;
            int width = 0, height = 0, ok = 0;
            ImageCallbackScope scope;
            try { ok = image_resolver(owned->GetOwner(), source.c_str(), &id, &source_identity, &width, &height); }
            catch (...) { error_message = "Image resolver callback failed"; owned->ImageFailed(source); return 0; }
            if (ok == 1) {
                try {
                    if (!source_identity) throw std::invalid_argument("Resolved image source identity is zero");
                    std::array<uint64_t, 3> descriptor{{source_identity, uint64_t(width), uint64_t(height)}};
                    auto claim = owned->ClaimImage(source, reinterpret_cast<const uint8_t*>(descriptor.data()), sizeof(descriptor), "resolved-image");
                    if (!(id & borrowed_texture_bit) || textures.count(id) || width <= 0 || height <= 0 || width > 8192 || height > 8192)
                        throw std::invalid_argument("Invalid resolved texture metadata or reused resource ID");
                    const auto handle = AllocateTextureHandle();
                    if (!handle) throw std::length_error("Texture handle space exhausted");
                    Texture texture; texture.width = width; texture.height = height; texture.borrowed = texture.resolved = texture.scoped = true;
                    texture.render_handle = URUI::TextureHandle(handle); texture.aliases = 1;
                    textures.emplace(id, std::move(texture));
                    borrowed_texture_handles.emplace(URUI::TextureHandle(handle), id);
                    try { owned->BindImage(source, id, std::move(claim), [this, id] { ReleaseAlias(id); }); }
                    catch (...) { ReleaseAlias(id); id = 0; throw; }
                    ++textures.at(id).loads; dims = {width, height}; plan.ResourceChanged(); return URUI::TextureHandle(handle);
                } catch (const std::exception& error) {
                    if (id && !textures.count(id)) ReleaseResolved(id);
                    owned->ImageFailed(source); error_message = error.what(); return 0;
                }
            }
            owned->ImageFailed(source);
            error_message = "Texture not registered: " + source;
            return 0;
        }
        URUI::TextureHandle GenerateTexture(URUI::Span<const URUI::byte> pixels, URUI::Vector2i dims) override
        {
            if (dims.x <= 0 || dims.y <= 0)
                return 0;
            uint64_t id = AllocateTextureHandle();
            if (!id)
                return 0;
            Texture t;
            t.width = dims.x;
            t.height = dims.y;
            t.render_handle = static_cast<URUI::TextureHandle>(id);
            t.pixels.assign(pixels.begin(), pixels.end());
            textures.emplace(id, std::move(t));
            plan.ResourceChanged();
            return static_cast<URUI::TextureHandle>(id);
        }
        void ReleaseTexture(URUI::TextureHandle id) override
        {
            auto found = textures.find(LogicalTextureId(id));
            if (found != textures.end() && found->second.scoped) {
                if (found->second.loads) --found->second.loads;
                CollectTexture(found->first); return;
            }
            if (found != textures.end() && !found->second.aliases)
            {
                const bool resolved = found->second.resolved;
                const auto logical = found->first;
                if (found->second.borrowed) borrowed_texture_handles.erase(found->second.render_handle);
                textures.erase(found);
                plan.ResourceChanged();
                if (resolved) ReleaseResolved(logical);
            }
        }
        void EnableClipMask(bool enable) override
        {
            recorded_context = nullptr;
            if (enable)
                plan.Unsupported();
            URUI::RenderInterface::EnableClipMask(enable);
        }
        void RenderToClipMask(URUI::ClipMaskOperation op, URUI::CompiledGeometryHandle g, URUI::Vector2f t) override
        {
            recorded_context = nullptr;
            plan.Unsupported();
            URUI::RenderInterface::RenderToClipMask(op, g, t);
        }
        URUI::LayerHandle PushLayer() override
        {
            recorded_context = nullptr;
            plan.Unsupported();
            const auto id = next_layer_resource++;
            if (!id) { layer_valid = false; return 0; }
            layer_stack.push_back(id); LayerCommand(1, 0, id);
            return static_cast<URUI::LayerHandle>(id);
        }
        void CompositeLayers(URUI::LayerHandle a, URUI::LayerHandle b, URUI::BlendMode mode,
                             URUI::Span<const URUI::CompiledFilterHandle> filters) override
        {
            recorded_context = nullptr;
            plan.Unsupported();
            for (auto handle : filters)
            {
                const auto mask = mask_filters.find(static_cast<uint64_t>(handle));
                if (mask != mask_filters.end()) LayerCommand(6, static_cast<uint64_t>(a), static_cast<uint64_t>(a), mask->second, 1);
                else if (this->filters.count(static_cast<uint64_t>(handle)))
                    LayerCommand(6, static_cast<uint64_t>(a), static_cast<uint64_t>(a), static_cast<uint64_t>(handle));
                else { layer_valid = false; return; }
            }
            if (std::find(layer_stack.begin(), layer_stack.end(), static_cast<uint64_t>(a)) == layer_stack.end() ||
                std::find(layer_stack.begin(), layer_stack.end(), static_cast<uint64_t>(b)) == layer_stack.end())
            { layer_valid = false; return; }
            if (a != b) LayerCommand(4, static_cast<uint64_t>(a), static_cast<uint64_t>(b), 0, static_cast<int>(mode));
        }
        void PopLayer() override
        {
            recorded_context = nullptr;
            plan.Unsupported();
            if (layer_stack.size() <= 1) { layer_valid = false; return; }
            const auto previous = layer_stack.back(); layer_stack.pop_back();
            LayerCommand(2, previous, layer_stack.back());
        }
        URUI::TextureHandle SaveLayerAsTexture() override
        {
            recorded_context = nullptr;
            plan.Unsupported();
            return URUI::RenderInterface::SaveLayerAsTexture();
        }
        URUI::CompiledFilterHandle SaveLayerAsMaskImage() override
        {
            recorded_context = nullptr;
            plan.Unsupported();
            if (layer_stack.size() <= 1 || !next_mask_filter || !next_layer_resource) { layer_valid = false; return 0; }
            const auto resource = next_layer_resource++, filter = next_mask_filter++;
            // 两次分配都成功后才发布句柄，失败帧不会留下无法归还的遮罩身份。
            LayerCommand(3, layer_stack.back(), resource); mask_filters.emplace(filter, resource);
            return static_cast<URUI::CompiledFilterHandle>(filter);
        }
        URUI::CompiledFilterHandle CompileFilter(const URUI::String& name, const URUI::Dictionary& p, const URUI::SharedPtr<URUI::ResourceContext>& resources = {}) override
        {
            recorded_context = nullptr;
            plan.Unsupported();
            if (!next_mask_filter || next_mask_filter == std::numeric_limits<uint64_t>::max()) { layer_valid=false; return 0; }
            const auto id=next_mask_filter++;
            Filter filter; filter.name=name; filter.data.handle=id; filter.document=resources ? resources->GetOwner() : 0;
            if (name == "--urui-effect") {
                const auto value = p.find("value"), owner = p.find("element");
                auto* element = owner == p.end() ? nullptr : static_cast<URUI::Element*>(owner->second.Get<void*>());
                if (value == p.end() || !element) { layer_valid=false; return 0; }
                filter.name = value->second.Get<URUI::String>();
                filter.element = element->GetObserverPtr();
                this->filters.emplace(id, std::move(filter));
                return static_cast<URUI::CompiledFilterHandle>(id);
            }
            const auto value=p.find("value"); if(value!=p.end()) filter.data.value=value->second.Get<float>();
            const auto sigma=p.find("sigma"); if(sigma!=p.end()) filter.data.sigma=sigma->second.Get<float>();
            const auto offset=p.find("offset"); if(offset!=p.end()) { auto v=offset->second.Get<URUI::Vector2f>();filter.data.offset_x=v.x;filter.data.offset_y=v.y; }
            const auto color=p.find("color"); if(color!=p.end()) { auto c=color->second.Get<URUI::Colourb>();filter.data.red=c.red/255.f;filter.data.green=c.green/255.f;filter.data.blue=c.blue/255.f;filter.data.alpha=c.alpha/255.f; }
            this->filters.emplace(id,std::move(filter)); return static_cast<URUI::CompiledFilterHandle>(id);
        }
        void ReleaseFilter(URUI::CompiledFilterHandle h) override
        {
            recorded_context = nullptr;
            const auto found = mask_filters.find(static_cast<uint64_t>(h));
            if (found != mask_filters.end())
            {
                // CompiledFilter 的 noexcept 析构也会进入这里；内存不足时撤销整帧，仍归还句柄。
                try { LayerCommand(5, found->second, 0); }
                catch (...) { layer_valid = false; ReportBridgeFailure("Cannot record URUI mask release"); }
                mask_filters.erase(found); return;
            }
            plan.Unsupported();
            if (filters.erase(static_cast<uint64_t>(h))) return;
            URUI::RenderInterface::ReleaseFilter(h);
        }
        URUI::CompiledShaderHandle CompileShader(const URUI::String& name, const URUI::Dictionary& p) override
        {
            recorded_context = nullptr;
            plan.Unsupported();
            const auto value = p.find("value"), owner = p.find("element");
            if ((name == "shader" || name == "--urui-image-effect" || name == "--urui-css-color") && value != p.end() && owner != p.end())
            {
                auto* element = static_cast<URUI::Element*>(owner->second.Get<void*>());
                auto key = value->second.Get<URUI::String>();
                if (element && !key.empty() && next_paint_shader != std::numeric_limits<uint64_t>::max())
                {
                    const auto handle = next_paint_shader++;
                    PaintShader shader{std::move(key), element->GetObserverPtr(), name == "--urui-image-effect" ? 1 : name == "--urui-css-color" ? 2 : 0, {}};
                    if (shader.kind == 2) {
                        const auto list = URUI::DirectColorFilters(element);
                        if (!list) return 0;
                        for (const auto& declaration : list->list) {
                            const auto* parameter = declaration.properties.GetProperty(declaration.instancer->GetPropertySpecification().GetPropertyId("value"));
                            const auto& type = declaration.type;
                            shader.color_filters.push_back({type == "grayscale" ? 0 : type == "saturate" ? 1 : type == "brightness" ? 2 : type == "contrast" ? 3 : 4,
                                parameter->Get<float>() * (parameter->unit == URUI::Unit::PERCENT ? .01f : 1.f)});
                        }
                    }
                    paint_shaders.emplace(handle, std::move(shader));
                    return static_cast<URUI::CompiledShaderHandle>(handle);
                }
            }
            return URUI::RenderInterface::CompileShader(name, p);
        }
        void RenderShader(URUI::CompiledShaderHandle s, URUI::CompiledGeometryHandle g, URUI::Vector2f t,
                          URUI::TextureHandle tex,const URUI::DrawParameters& parameters) override
        {
            recorded_context = nullptr;
            plan.Unsupported();
            const auto found = paint_shaders.find(static_cast<uint64_t>(s));
            if (found != paint_shaders.end() && found->second.element)
            {
                paint_commands.push_back({static_cast<uint64_t>(s), static_cast<int>(commands.size())});
                RenderGeometry(g, t, tex,parameters);
                return;
            }
            URUI::RenderInterface::RenderShader(s, g, t, tex,parameters);
        }
        void ReleaseShader(URUI::CompiledShaderHandle h) override
        {
            recorded_context = nullptr;
            plan.Unsupported();
            if (paint_shaders.erase(static_cast<uint64_t>(h))) return;
            URUI::RenderInterface::ReleaseShader(h);
        }
        void EnableScissorRegion(bool value) override
        {
            recorded_context = nullptr;
            clipped = value;
        }
        void SetScissorRegion(URUI::Rectanglei value) override
        {
            recorded_context = nullptr;
            clip = value;
        }
        void SetTransform(const URUI::Matrix4f* matrix) override
        {
            recorded_context = nullptr;
            active_transform = matrix ? *matrix : URUI::Matrix4f::Identity();
        }
    };
    class Listener : public URUI::EventListener
    {
        void ProcessEvent(URUI::Event& event) override
        {
            auto* target = event.GetTargetElement();
            if (target && clicks.size() < 4096)
                clicks.push_back(target->GetObserverPtr());
        }
    };
    URUI::String ClickId(const URUI::ObserverPtr<URUI::Element>& value)
    {
        for (auto* el = value.get(); el; el = el->GetParentNode())
            if (!el->GetId().empty())
                return el->GetId();
        return {};
    }
    std::unique_ptr<Renderer> render;
    std::unique_ptr<Listener> listener;
    std::unique_ptr<ResourceFiles> resource_files;
    URUI::Context* context = nullptr;
    bool started = false;
    ElementReferences element_references;
    std::unique_ptr<URUI::Bridge::DocumentRegistry> document_registry;
    std::unique_ptr<URUI::Bridge::FileResourceCache> file_resource_cache;
    uint64_t next_resource_scope = 1;
    int count_elements(URUI::Element* root)
    {
        if (!root)
            return 0;
        int n = 1;
        for (int i = 0; i < root->GetNumChildren(); ++i)
            n += count_elements(root->GetChild(i));
        return n;
    }
    std::string safe_html(const char* src)
    {
        std::string out;
        if (!src)
            return out;
        for (const char* p = src; *p; ++p)
        {
            switch (*p)
            {
            case '&':
                out += "&amp;";
                break;
            case '<':
                out += "&lt;";
                break;
            case '>':
                out += "&gt;";
                break;
            default:
                out.push_back(*p);
                break;
            }
        }
        return out;
    }
#if defined(URUI_DEBUG)
#include "Debug/typed_transform.inl"
#endif

    inline int ApplyTypedTransform(URUI::Element* el, float x, float y, float sx, float sy)
    {
        // Production keeps the property temporary scoped to SetProperty.
        URUI_DEBUG_CODE(if (typed_probe_mode == 1) return ApplyTypedTransformTimed(el, x, y, sx, sy);)
        URUI::Transform::PrimitiveList primitives;
        primitives.reserve(2);
        primitives.emplace_back(URUI::Transforms::Translate2D(x, y, URUI::Unit::PX));
        primitives.emplace_back(URUI::Transforms::Scale2D(sx, sy));
        if (!el->SetProperty(URUI::PropertyId::Transform, URUI::Transform::MakeProperty(std::move(primitives))))
        {
            error_message = "URUI rejected typed transform";
            return -2;
        }
        error_message.clear();
        return 1;
    }

} // namespace
namespace urui_interaction
{
    extern int callback_depth;
    extern bool pending_destroy;
    void Reset();
    void Install();
    void ResetClock();
    void CancelDocumentInput(URUI::ElementDocument* document) noexcept;
    void CancelBlockedDocumentInput() noexcept;
    void RetireDocument(URUI::ElementDocument* document) noexcept;
    void FlushCandidateEvents();
}
extern "C"
{
    int urui_bridge_abi_version(void)
    {
        return 11;
    }
    int urui_command_protocol_version(void) { return 2; }
    int urui_surface_set_default_font_face(int family, const uint8_t* bytes, int length, int face_index, int style, int weight)
    try
    {
        if (family < 0 || family > 2 || !bytes || length <= 0 || length > 64 * 1024 * 1024 || face_index < 0 ||
            style < 0 || style > 1 || weight < 1 || weight > 1000) return -1;
        if (!context || !document_registry) return -2;
        if (context_thread != std::this_thread::get_id()) return -3;
        if (urui_interaction::callback_depth || document_registry->Count() != 0 || context->GetNumDocuments() != 0) return -5;
        if (!URUI::DocumentFontContext::LoadDefaultFace(family, {bytes, size_t(length)},
            static_cast<URUI::Style::FontStyle>(style), static_cast<URUI::Style::FontWeight>(weight), face_index)) {
            error_message = "Cannot install the default font face: invalid source or a different face already occupies the slot";
            return -2;
        }
        error_message.clear();
        return 1;
    }
    catch (...) { try { error_message = "Default font bootstrap failed"; } catch (...) {} return -8; }
    int urui_texture_protocol_version(void)
    {
        return 1;
    }
    int urui_image_resolver_version(void) { return 2; }
    int urui_set_image_resolver(UruiImageResolver resolve, UruiImageRelease release)
    {
        if (!render || context_thread != std::this_thread::get_id() || urui_interaction::callback_depth || bool(resolve) != bool(release)) return -1;
        if (context && context->GetNumDocuments() != 0) return -2;
        for (const auto& entry : render->textures) if (entry.second.resolved) return -2;
        render->image_resolver = resolve;
        render->image_release = release;
        return 1;
    }
    int urui_create(int w, int h) try
    {
        if (started)
            return 0;
        context_thread = std::this_thread::get_id();
        URUI_DEBUG_CODE(profile = {};)
        error_message.clear();
        render = std::make_unique<Renderer>();
        listener = std::make_unique<Listener>();
        URUI::SetRenderInterface(render.get());
        urui_interaction::ResetClock();
        urui_interaction::Install();
        if (!URUI::Initialise())
        {
            error_message = "URUI Initialise failed";
            listener.reset();
            render.reset();
            return 0;
        }
        started = true;
        URUI::DirectColorFilterRenderingEnabled = false;
        urui_image::RegisterStyles();
        URUI::StyleSheetSpecification::RegisterProperty("--urui-caret-width", "1px", false, true).AddParser("length");
        URUI::StyleSheetSpecification::RegisterProperty("--urui-caret-blink-rate", "0.85", false).AddParser("number");
        // Preserve the platform file interface created by Initialise; install
        // the resource overlay before loading document resources.
        resource_files = std::make_unique<ResourceFiles>(URUI::GetFileInterface());
        URUI::SetFileInterface(resource_files.get());
        context = URUI::CreateContext("urui", {std::max(1, w), std::max(1, h)});
        if (!context)
        {
            error_message = "Cannot create URUI context";
            urui_destroy();
            return 0;
        }
        element_references.Begin();
        URUI::RegisterPlugin(&element_references);
        try {
            file_resource_cache = std::make_unique<URUI::Bridge::FileResourceCache>();
            document_registry = std::make_unique<URUI::Bridge::DocumentRegistry>(*context, element_references, urui_interaction::RetireDocument);
        }
        catch (...) { error_message = "Cannot create document registry"; urui_destroy(); return 0; }
        urui_styles::Begin(urui_elements::Get);
        return 1;
    }
    catch (...)
    {
        urui_destroy();
        // Reporting an allocation failure must not allocate across the C ABI.
        try { error_message = "Cannot initialize URUI Surface"; } catch (...) {}
        return 0;
    }
    void urui_destroy(void)
    {
        // Surface 只由创建线程销毁；后台帧租约仍可按自身线程约定归还。
        if (context_thread != std::thread::id{} && context_thread != std::this_thread::get_id()) return;
        if (urui_interaction::callback_depth) { urui_interaction::pending_destroy = true; return; }
        if (document_registry) document_registry->Clear();
        document_registry.reset();
        urui_interaction::Reset();
        element_references.Clear();
        if (render) render->plan.NewDocument();
        urui_styles::End();
        if (context)
        {
            URUI::RemoveContext("urui");
            context = nullptr;
        }
        file_resource_cache.reset();
        if (started)
        {
            if (resource_files)
            {
                URUI::SetFileInterface(resource_files->Original());
                resource_files.reset();
            }
            URUI::Shutdown();
            started = false;
        }
        clicks.clear();
        listener.reset();
        // Initialise 失败时核心会恢复调用前的接口；不要留下即将销毁的 Renderer 指针。
        URUI::SetRenderInterface(nullptr);
        render.reset();
        // teardown 中 release 回调的新请求仍属于已结束的 context，不能污染下一世代。
        urui_interaction::pending_destroy = false;
        URUI_DEBUG_CODE(typed_probe = {}; typed_probe_mode = 0;)
#if defined(URUI_DEBUG)
        urui_lookup::inline_enabled = false;
        urui_lookup::profile_mode = 0;
        urui_lookup::style_scope = false;
        urui_lookup::profile = {};
#endif
    }
    int urui_document_add_stylesheet(UruiDocumentHandle document_handle, const char* css, const char* source_url, int before_author)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!document || !css || !*css || !source_url || !*source_url || (before_author != 0 && before_author != 1) ||
            !element_references.Owner() || urui_interaction::callback_depth)
            return -1;
        auto sheet = URUI::MakeShared<URUI::StyleSheetContainer>();
        URUI::StreamMemory css_stream(reinterpret_cast<const URUI::byte*>(css), std::strlen(css));
        css_stream.SetSourceURL(source_url);
        if (!sheet->LoadStyleSheetContainer(&css_stream, 1, document))
        {
            error_message = sheet->GetResourceError().empty() ? "CSS resource could not be parsed" : sheet->GetResourceError();
            return -2;
        }
        if (const auto* current = document->GetStyleSheetContainer())
            sheet = before_author ? sheet->CombineStyleSheetContainer(*current) : current->CombineStyleSheetContainer(*sheet);
        if (!sheet)
            return -2;
        document->SetStyleSheetContainer(std::move(sheet));
        return 1;
    }
    catch (...)
    {
        try { error_message = "Native stylesheet resource failed"; } catch (...) {}
        return -2;
    }

    void urui_resize(int w, int h)
    try
    {
        if (context)
            context->SetDimensions({std::max(1, w), std::max(1, h)});
    }
    catch (...)
    {
        ReportBridgeFailure("urui_resize failed");
    }
    int urui_hit_test_id(UruiDocumentHandle document_handle, float x, float y, char* out, int capacity)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!context || !document || !std::isfinite(x) || !std::isfinite(y) || capacity < 0)
            return -1;
        if (out && capacity > 0)
            out[0] = 0;
        auto* el = context->GetElementAtPoint({x, y});
        if (el && el->GetOwnerDocument() != document) return 0;
        while (el && el->GetId().empty())
            el = el->GetParentNode();
        if (!el)
            return 0;
        const auto& id = el->GetId();
        if (id.size() + 1 > static_cast<size_t>(capacity) || !out)
            return -static_cast<int>(id.size() + 1);
        std::memcpy(out, id.c_str(), id.size() + 1);
        return static_cast<int>(id.size());
    }
    catch (...)
    {
        ReportBridgeFailure("urui_hit_test_id failed"); return -8;
    }
    int urui_element_exists(UruiDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!document || !id || !*id)
            return 0;
        return document->GetElementById(id) ? 1 : 0;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_element_exists failed"); return -8;
    }
    int urui_get_element_box(UruiDocumentHandle document_handle, const char* id, float* out_six)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!document || !id || !*id || !out_six)
            return -1;
        auto* el = document->GetElementById(id);
        if (!el)
            return 0;
        auto offset = el->GetAbsoluteOffset(URUI::BoxArea::Border);
        auto size = el->GetBox().GetSize(URUI::BoxArea::Border);
        out_six[0] = offset.x;
        out_six[1] = offset.y;
        out_six[2] = size.x;
        out_six[3] = size.y;
        out_six[4] = el->GetClientWidth();
        out_six[5] = el->GetScrollWidth();
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_element_box failed"); return -8;
    }
    int urui_set_attribute(UruiDocumentHandle document_handle, const char* id, const char* name, const char* value)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        URUI_DEBUG_CODE(ProfileScope timer(api_profile.attribute_ns, api_profile_enabled);)
        URUI_DEBUG_CODE(if (api_profile_enabled)++ api_profile.attribute_calls;)
        if (!id || !*id || !name || !*name || !value)
        {
            error_message = "Invalid attribute mutation";
            return -1;
        }
        if (!document)
            return 0;
        auto* el = document->GetElementById(id);
        if (!el)
            return 0;
        el->SetAttribute(name, URUI::String(value));
        error_message.clear();
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_set_attribute failed"); return -8;
    }
    int urui_set_style(UruiDocumentHandle document_handle, const char* id, const char* name, const char* value)
    try
    {
        auto* document = urui_elements::Document(document_handle);
#if defined(URUI_DEBUG)
        urui_lookup::StyleScope lookup_style;
#endif
        URUI_DEBUG_CODE(ProfileScope timer(api_profile.style_ns, api_profile_enabled);)
        URUI_DEBUG_CODE(if (api_profile_enabled)++ api_profile.style_calls;)
        if (!id || !*id || !name || !*name || !value)
        {
            error_message = "Invalid style mutation";
            return -1;
        }
        if (!document)
            return 0;
#if defined(URUI_DEBUG)
        auto* sample = urui_style_probe::mode ? &urui_style_probe::total : nullptr;
        if (sample)
            ++sample->calls;
        URUI::Element* el = nullptr;
        {
            urui_style_probe::Scope lookup(sample ? &sample->lookup_ns : nullptr);
            el = document->GetElementById(id);
        }
        if (!el)
            return 0;
        struct ActiveGuard
        {
            UruiStyleProbe* previous;
            ActiveGuard(UruiStyleProbe* p) : previous(urui_style_probe::active)
            {
                urui_style_probe::active = p;
            }
            ~ActiveGuard()
            {
                urui_style_probe::active = previous;
            }
        } guard(sample);
#else
        auto* el = document->GetElementById(id);
        if (!el)
            return 0;
#endif
        if (!el->SetProperty(name, value))
        {
            error_message = "URUI rejected property: " + std::string(name);
            return -2;
        }
        error_message.clear();
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_set_style failed"); return -8;
    }
    int urui_set_translate_scale_2d(UruiDocumentHandle document_handle, const char* id, float x, float y, float sx, float sy)
    try
    {
        auto* document = urui_elements::Document(document_handle);
#if defined(URUI_DEBUG)
        urui_lookup::StyleScope lookup_style;
#endif
        URUI_DEBUG_CODE(ProfileScope timer(api_profile.style_ns, api_profile_enabled);)
        URUI_DEBUG_CODE(if (api_profile_enabled)++ api_profile.style_calls;)
        URUI_DEBUG_CODE(const bool measure = typed_probe_mode == 1;)
        URUI_DEBUG_CODE(ProfileScope total(typed_probe.total_ns, measure);)
        URUI_DEBUG_CODE(if (typed_probe_mode) {
            ++typed_probe.calls;
            if (measure)
                ++typed_probe.clock_pairs;
        })
        if (!id || !*id || !std::isfinite(x) || !std::isfinite(y) || !std::isfinite(sx) || !std::isfinite(sy))
        {
            URUI_DEBUG_CODE(if (typed_probe_mode)++ typed_probe.rejects;)
            error_message = "Invalid typed translate-scale mutation";
            return -1;
        }
        if (!document)
            return 0;
        URUI::Element* el;
        {
            URUI_DEBUG_CODE(ProfileScope scope(typed_probe.lookup_ns, measure);)
            URUI_DEBUG_CODE(if (measure)++ typed_probe.clock_pairs;)
            el = document->GetElementById(id);
        }
        if (!el)
        {
            URUI_DEBUG_CODE(if (typed_probe_mode)++ typed_probe.misses;)
            return 0;
        }
        return ApplyTypedTransform(el, x, y, sx, sy);
    }
    catch (...)
    {
        ReportBridgeFailure("urui_set_translate_scale_2d failed"); return -8;
    }

    uint64_t urui_element_ref_acquire(UruiDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        return element_references.Acquire(document, id);
    }
    catch (...)
    {
        ReportBridgeFailure("urui_element_ref_acquire failed"); return 0;
    }
    int urui_element_ref_valid(UruiDocumentHandle document_handle, uint64_t token)
    {
        auto* document = urui_elements::Document(document_handle);
        if (!element_references.Owner())
            return -3;
        return element_references.Resolve(token, document) ? 1 : 0;
    }
    int urui_element_ref_release(UruiDocumentHandle document_handle, uint64_t token)
    {
        if (!element_references.Owner()) return -3;
        auto* owner = urui_elements::Document(document_handle);
        if (!owner) return -2;
        if (element_references.OwnerDocument(token) != owner) return -1;
        return element_references.Release(token);
    }
    int urui_element_ref_count(UruiDocumentHandle document_handle)
    {
        auto* owner = urui_elements::Document(document_handle);
        return owner ? element_references.Count(owner) : -2;
    }
    int urui_element_ref_set_translate_scale_2d(UruiDocumentHandle document_handle, uint64_t token, float x, float y, float sx, float sy)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!element_references.Owner())
            return -3;
        URUI_DEBUG_CODE(ProfileScope api(api_profile.style_ns, api_profile_enabled);)
        URUI_DEBUG_CODE(if (api_profile_enabled)++ api_profile.style_calls;)
        URUI_DEBUG_CODE(const bool measure = typed_probe_mode == 1;)
        URUI_DEBUG_CODE(ProfileScope total(typed_probe.total_ns, measure);)
        URUI_DEBUG_CODE(if (typed_probe_mode) {
            ++typed_probe.calls;
            if (measure)
                ++typed_probe.clock_pairs;
        })
        if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(sx) || !std::isfinite(sy))
        {
            URUI_DEBUG_CODE(if (typed_probe_mode)++ typed_probe.rejects;)
            error_message = "Invalid typed translate-scale mutation";
            return -1;
        }
        // Resolve validates weak-reference lifetime and the owning context thread.
        URUI::Element* el;
        {
            URUI_DEBUG_CODE(ProfileScope scope(typed_probe.lookup_ns, measure);)
            URUI_DEBUG_CODE(if (measure)++ typed_probe.clock_pairs;)
            el = element_references.Resolve(token, document);
        }
        if (!el)
        {
            URUI_DEBUG_CODE(if (typed_probe_mode)++ typed_probe.misses;)
            return 0;
        }
        return ApplyTypedTransform(el, x, y, sx, sy);
    }
    catch (...)
    {
        ReportBridgeFailure("urui_element_ref_set_translate_scale_2d failed"); return -8;
    }

    int urui_set_inner_html(UruiDocumentHandle document_handle, const char* id, const char* markup)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!id || !*id || !markup || !element_references.Owner() || urui_interaction::callback_depth) return -1;
        if (!document) return 0;
        auto* el = document->GetElementById(id);
        if (!el) return 0;
        const int result = urui_elements::ReplaceHtml(*el, markup);
        if (result == 1) element_references.Prune();
        return result;
    }
    catch (...) { return -2; }
    int urui_append_element(UruiDocumentHandle document_handle, const char* parent_id, const char* tag, const char* id)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!parent_id || !*parent_id || !tag || !*tag || !id)
        {
            error_message = "Invalid append";
            return -1;
        }
        if (!document)
            return 0;
        auto* parent = document->GetElementById(parent_id);
        if (!parent)
            return 0;
        auto child = document->CreateElement(tag);
        if (!child)
        {
            error_message = "URUI rejected element tag";
            return -2;
        }
        if (*id)
            child->SetId(id);
        if (!parent->AppendChild(std::move(child)))
        {
            error_message = "URUI rejected append";
            return -2;
        }
        error_message.clear();
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_append_element failed"); return -8;
    }
    // General DOM sibling ordering API used by atomic UI and ordinary managed clients.
    // Ownership stays in URUI; moving the same Element preserves its observer identity.
    int urui_get_child_count(UruiDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!document || !id || !*id)
            return -1;
        auto* el = document->GetElementById(id);
        return el ? el->GetNumChildren() : -1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_child_count failed"); return -8;
    }
    int urui_get_child_id_utf8(UruiDocumentHandle document_handle, const char* id, int index, char* out, int capacity)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!document || !id || !*id || index < 0 || capacity < 0)
            return -1;
        auto* el = document->GetElementById(id);
        if (!el || index >= el->GetNumChildren())
            return -1;
        const auto& name = el->GetChild(index)->GetId();
        if (name.size() >= size_t(INT32_MAX))
            return -1;
        if (!out || size_t(capacity) < name.size() + 1)
            return -int(name.size() + 1);
        std::memcpy(out, name.c_str(), name.size() + 1);
        return int(name.size());
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_child_id_utf8 failed"); return -8;
    }
    int urui_reorder_element_to_end(UruiDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!id || !*id)
        {
            error_message = "Invalid reorder";
            return -1;
        }
        if (!document)
            return 0;
        auto* el = document->GetElementById(id);
        if (!el)
            return 0;
        auto* parent = el->GetParentNode();
        if (!parent || el == document)
        {
            error_message = "Cannot reorder document root";
            return -2;
        }
        if (parent->GetChild(parent->GetNumChildren() - 1) == el)
        {
            error_message.clear();
            return 1;
        }
        auto owned = parent->RemoveChild(el);
        if (!owned)
        {
            error_message = "URUI rejected reorder detach";
            return -2;
        }
        if (!parent->AppendChild(std::move(owned)))
        {
            error_message = "URUI rejected reorder append";
            return -2;
        }
        error_message.clear();
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_reorder_element_to_end failed"); return -8;
    }
    int urui_remove_element(UruiDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!id || !*id)
        {
            error_message = "Invalid remove";
            return -1;
        }
        if (!document)
            return 0;
        auto* el = document->GetElementById(id);
        if (!el)
            return 0;
        auto* parent = el->GetParentNode();
        if (!parent || el == document)
        {
            error_message = "Cannot remove document root";
            return -2;
        }
        auto detached = parent->RemoveChild(el);
        if (!detached)
        {
            error_message = "URUI rejected remove";
            return -2;
        }
        error_message.clear();
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_remove_element failed"); return -8;
    }
    void urui_update(void)
    try
    {
        if (urui_interaction::callback_depth) return;
        URUI_DEBUG_CODE(ProfileScope timer(profile.update_ns);)
        if (context) { urui_interaction::FlushCandidateEvents(); context->Update(); urui_styles::Prune(); }
    }
    catch (...) { try { error_message = "URUI update failed"; } catch (...) {} }
    int urui_synchronize_geometry(void)
    try
    {
        if (context_thread != std::this_thread::get_id() || urui_interaction::callback_depth) return -3;
        if (!context) return -1;
        return context->SynchronizeGeometry() ? 1 : -1;
    }
    catch (...) { ReportBridgeFailure("URUI geometry synchronization failed"); return -4; }
    void urui_render(void)
    try
    {
        if (urui_interaction::callback_depth) return;
        URUI_DEBUG_CODE(ProfileScope timer(profile.render_ns);)
        if (!context || !render)
            return;
        const auto revision=context->GetNativeRenderRevision();
        const auto stamp=render->plan.Snapshot(render->commands.size());
        const bool reusable=!render_serial.exhausted&&render->document_generation&&context->AllowsNativeRecordingReuse();
        if(reusable&&render->recorded_context==context&&render->recorded_revision==revision&&
            render->recorded_document==render->document_generation&&render->recorded_resources==stamp.resources&&
            render->recorded_epoch==stamp.epoch&&render->recorded_command_count==render->commands.size()&&render->layer_valid&&render->mask_filters.empty())
        {
            render->plan.ReuseRecording();return;
        }
        render->recorded_context=nullptr;
        render->plan.Begin(render->commands, render->command_transforms);
        render->command_uniforms.clear();
        render->paint_commands.clear();render->effect_ink_bounds.clear();
        render->PruneEffectRoots();
        render->layer_commands.clear(); render->layer_stack.assign(1, 0); render->next_layer_resource = 1;
        render->layer_valid = render->mask_filters.empty();
        render->clipped = false;
        render->active_transform = URUI::Matrix4f::Identity();
        context->Render();
        render->layer_valid = render->layer_valid && render->layer_stack.size() == 1 && render->mask_filters.empty();
        render->plan.Finish(render->commands, render->command_transforms);
        if(reusable&&revision==context->GetNativeRenderRevision()&&render->layer_valid&&context->AllowsNativeRecordingReuse())
        {
            const auto finished=render->plan.Snapshot(render->commands.size());
            render->recorded_context=context;render->recorded_revision=revision;render->recorded_document=render->document_generation;
            render->recorded_resources=finished.resources;render->recorded_epoch=finished.epoch;render->recorded_command_count=render->commands.size();
        }
    }
    catch (...)
    {
        if (render) {
            render->recorded_context=nullptr;
            render->commands.clear(); render->command_transforms.clear();render->command_uniforms.clear();
            render->paint_commands.clear(); render->layer_commands.clear();
            render->layer_stack.clear(); render->layer_valid = false;
            render->plan.NewDocument();
        }

        ReportBridgeFailure("urui_render failed");
    }
    int urui_render_plan_version()
    {
        return 8;
    }
    int urui_render_plan_stamp(UruiFrameStamp* out)
    {
        if (!out)
            return 0;
        if (!render)
        {
            *out = {};
            return 0;
        }
        *out = render->plan.Snapshot(render->commands.size());
        return 1;
    }

    int urui_get_command_transforms(float* out, int float_capacity)
    {
        if (!render || float_capacity < 0)
            return -1;
        const size_t count = render->command_transforms.size();
        if (count > static_cast<size_t>(INT32_MAX / 16) || static_cast<size_t>(float_capacity) < count * 16 ||
            (!out && count))
            return -static_cast<int>(count * 16);
        for (size_t i = 0; i < count; i++)
            std::memcpy(out + i * 16, render->command_transforms[i].data(), 16 * sizeof(float));
        return static_cast<int>(count);
    }
    int urui_get_attribute_utf8(UruiDocumentHandle document_handle, const char* id, const char* name, char* out, int capacity, int* required)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!id || !*id || !name || !*name || capacity < 0 || !required || (!out && capacity > 0))
            return -1;
        *required = 0;
        if (!document)
            return 0;
        auto* el = document->GetElementById(id);
        if (!el || !el->HasAttribute(name))
            return 0;
        const auto value = el->GetAttribute<URUI::String>(name, "");
        if (value.size() >= size_t(INT32_MAX))
            return -1;
        *required = int(value.size() + 1);
        if (!out || capacity < *required)
            return -3;
        std::memcpy(out, value.c_str(), value.size() + 1);
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_attribute_utf8 failed"); return -8;
    }
    int urui_get_text_utf8(UruiDocumentHandle document_handle, const char* id, char* out, int capacity, int* required)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!id || !*id || capacity < 0 || !required || (!out && capacity > 0))
            return -1;
        *required = 0;
        if (!document)
            return 0;
        auto* el = document->GetElementById(id);
        if (!el)
            return 0;
        URUI::String value;
        if (el->GetNumChildren() == 1 && el->GetChild(0)->GetTagName() == "#text")
            value = static_cast<URUI::ElementText*>(el->GetChild(0))->GetText();
        else if (el->GetNumChildren() != 0)
            return -2;
        if (value.size() >= static_cast<size_t>(INT32_MAX))
            return -1;
        *required = static_cast<int>(value.size() + 1);
        if (capacity < *required || !out)
            return -3;
        std::memcpy(out, value.c_str(), value.size() + 1);
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_text_utf8 failed"); return -8;
    }
    void urui_set_text(UruiDocumentHandle document_handle, const char* id, const char* value)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!document || !id || !element_references.Owner() || urui_interaction::callback_depth) return;
        auto* el = document->GetElementById(id);
        if (!el) return;
        const char* text = value ? value : "";
        if (el->GetNumChildren() == 1 && el->GetChild(0)->GetTagName() == "#text") {
            static_cast<URUI::ElementText*>(el->GetChild(0))->SetText(text);
            return;
        }
        auto node = document->CreateTextNode(text);
        if (!node) return;
        while (auto* child = el->GetFirstChild()) el->RemoveChild(child);
        if (*text) el->AppendChild(std::move(node));
        element_references.Prune();
    }
    catch (...) {}
    float urui_get_scroll_top(UruiDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!document || !id)
            return -1.f;
        if (auto* el = document->GetElementById(id))
            return el->GetScrollTop();
        return -1.f;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_scroll_top failed"); return 0;
    }
    int urui_set_pixel_style(UruiDocumentHandle document_handle, const char* id, const char* property, float pixels)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!document || !id || !property || !std::isfinite(pixels))
            return 0;
        if (auto* el = document->GetElementById(id))
            return el->SetProperty(property, std::to_string(pixels) + "px") ? 1 : 0;
        return 0;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_set_pixel_style failed"); return -8;
    }
    int urui_element_count(UruiDocumentHandle document_handle)
    {
        auto* document = urui_elements::Document(document_handle);
        return count_elements(document);
    }
    int urui_command_count(void)
    {
        return render ? static_cast<int>(render->commands.size()) : 0;
    }
    int urui_get_commands(UruiCommand* out, int cap)
    {
        URUI_DEBUG_CODE(ProfileScope timer(profile.command_copy_ns);)
        if (!render || !out || cap < 0)
            return 0;
        int n = std::min(cap, (int)render->commands.size());
        std::copy_n(render->commands.begin(), n, out);
        return n;
    }
    int urui_get_paint_commands(UruiPaintCommand* out, int capacity)
    {
        if (context_thread != std::this_thread::get_id() || urui_interaction::callback_depth || capacity < 0 || (!out && capacity))
            return -1;
        if (!render) return 0;
        if (!out) return static_cast<int>(render->paint_commands.size());
        int count = 0;
        for (const auto& command : render->paint_commands)
        {
            if (count == capacity) break;
            const auto shader = render->paint_shaders.find(command.shader);
            if (shader == render->paint_shaders.end() || !shader->second.element) continue;
            const auto token = element_references.Track(shader->second.element.get(), shader->second.element->GetOwnerDocument());
            if (token) out[count++] = {command.shader, token, command.command_index, shader->second.kind};
        }
        return count;
    }
    int urui_paint_color_filters(uint64_t shader, UruiColorFilter* out, int capacity)
    {
        if (context_thread != std::this_thread::get_id() || urui_interaction::callback_depth || capacity < 0 || (!out && capacity) || !render) return -1;
        const auto found = render->paint_shaders.find(shader);
        if (found == render->paint_shaders.end() || found->second.kind != 2 || !found->second.element ||
            !document_registry || !document_registry->HandleOf(found->second.element->GetOwnerDocument())) return -2;
        const auto& filters = found->second.color_filters;
        if (!out) return static_cast<int>(filters.size());
        if (capacity < static_cast<int>(filters.size())) return -3;
        std::copy(filters.begin(), filters.end(), out); return static_cast<int>(filters.size());
    }
    int urui_set_direct_color_filters(int enabled)
    {
        if (enabled < 0 || enabled > 1 || context_thread != std::this_thread::get_id() || urui_interaction::callback_depth) return -1;
        if (!context || !document_registry) return -2;
        if (document_registry->Count() != 0) return -5;
        URUI::DirectColorFilterRenderingEnabled = enabled != 0; return 1;
    }
    int urui_get_layer_commands(UruiLayerCommand* out, int capacity)
    {
        if (context_thread != std::this_thread::get_id() || urui_interaction::callback_depth || capacity < 0 || (!out && capacity)) return -1;
        if (!render) return 0;
        if (!render->layer_valid) { error_message = "Invalid or unsupported layer/filter command stream"; return -2; }
        if (!out) return static_cast<int>(render->layer_commands.size());
        const int count = std::min(capacity, static_cast<int>(render->layer_commands.size()));
        std::copy_n(render->layer_commands.begin(), count, out); return count;
    }
    int urui_paint_shader_name(uint64_t shader, char* out, int capacity)
    {
        if (context_thread != std::this_thread::get_id() || urui_interaction::callback_depth || capacity < 0 || (!out && capacity))
            return -1;
        if (out && capacity) out[0] = '\0';
        if (!render) return 0;
        const auto found = render->paint_shaders.find(shader);
        if (found == render->paint_shaders.end() || !found->second.element) return 0;
        const auto& name = found->second.name;
        const int required = static_cast<int>(name.size() + 1);
        if (out && capacity >= required) std::memcpy(out, name.c_str(), required);
        return required;
    }
    int urui_geometry_exists(uint64_t id)
    {
        return render && render->meshes.count(id) ? 1 : 0;
    }
    int urui_texture_exists(uint64_t id)
    {
        return render && render->textures.count(id) ? 1 : 0;
    }
    int urui_get_geometry_info(uint64_t id, int* vc, int* ic)
    {
        if (!render || !render->meshes.count(id))
            return 0;
        auto& g = render->meshes.at(id);
        if (vc)
            *vc = (int)g.vertices.size();
        if (ic)
            *ic = (int)g.indices.size();
        return 1;
    }
    int urui_copy_geometry(uint64_t id, UruiVertex* v, int vc, int* i, int ic)
    {
        URUI_DEBUG_CODE(ProfileScope timer(profile.geometry_copy_ns);)
        if (!render || !render->meshes.count(id) || !v || !i)
            return 0;
        auto& g = render->meshes.at(id);
        if (vc < (int)g.vertices.size() || ic < (int)g.indices.size())
            return 0;
        std::copy(g.vertices.begin(), g.vertices.end(), v);
        std::copy(g.indices.begin(), g.indices.end(), i);
        return 1;
    }
    int urui_get_texture_kind(uint64_t id)
    {
        if (!render)
            return 0;
        auto found = render->textures.find(id);
        return found == render->textures.end() ? 0 : found->second.borrowed ? 2 : 1;
    }
    int urui_get_texture_info(uint64_t id, int* w, int* h, int* bytes)
    {
        if (!render || !render->textures.count(id))
            return 0;
        auto& t = render->textures.at(id);
        if (w)
            *w = t.width;
        if (h)
            *h = t.height;
        if (bytes)
            *bytes = (int)t.pixels.size();
        return 1;
    }
    int urui_copy_texture(uint64_t id, uint8_t* out, int cap)
    {
        if (!render || !render->textures.count(id))
            return 0;
        auto& texture = render->textures.at(id);
        if (texture.borrowed)
        {
            error_message = "Cannot copy pixels from a borrowed external texture";
            return -2;
        }
        if (!out)
            return 0;
        auto& p = texture.pixels;
        if (cap < (int)p.size())
            return 0;
        std::copy(p.begin(), p.end(), out);
        return 1;
    }
    int urui_pop_event(UruiDocumentHandle document_handle, char* out, int cap)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!document || !out) return 0;
        for (auto it = clicks.begin(); it != clicks.end();)
        {
            if (!*it) { it = clicks.erase(it); continue; }
            if ((*it)->GetOwnerDocument() != document) { ++it; continue; }
            auto value = ClickId(*it);
            if (cap < (int)value.size() + 1) return -(int)value.size() - 1;
            std::memcpy(out, value.c_str(), value.size() + 1);
            clicks.erase(it);
            return (int)value.size();
        }
        return 0;
    }
    catch (...) { ReportBridgeFailure("urui_pop_event failed"); return -8; }
    const char* urui_last_error(void)
    {
        return error_message.c_str();
    }
}

#if defined(URUI_DEBUG)
#include "Debug/bridge_debug.inl"
#endif

#include "elements.inl"
#include "element_runtime.inl"
#include "element_events.inl"
#include "sequence_sampling.inl"
#include "documents.inl"


#include "effect_layers.inl"
#include "render_plan_bridge.inl"
#include "effect_layout.inl"

#include "authoring.inl"
#include "compiled_assets.inl"
