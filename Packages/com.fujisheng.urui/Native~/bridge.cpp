#include "Engine/Source/Core/HtmlBuilder.h"
#include "Engine/Source/Core/CompiledDocument.h"
#include "Engine/Source/Core/DocumentFontContext.h"
#include "Engine/Source/Core/HtmlParser.h"
#include "Engine/Source/Core/CssParser.h"
#include "urui.h"
#include "style_properties.h"
namespace URUI::Elements
{
    URUI::ElementDocument* Document(URUIDocumentHandle owner);
    URUI::Element* Get(URUIDocumentHandle owner, uint64_t token);
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
using namespace URUI::Debug;
#endif

namespace URUI::Interaction
{
    extern int callbackDepth;
}
namespace
{
    struct ImageCallbackScope
    {
        ImageCallbackScope()
        {
            ++URUI::Interaction::callbackDepth;
        }
        ~ImageCallbackScope()
        {
            --URUI::Interaction::callbackDepth;
        }
    };
    static_assert(sizeof(URUIVertex) == 20, "UruiVertex ABI changed");
    static_assert(sizeof(URUICommand) == 48, "UruiCommand ABI changed");
    static_assert(sizeof(URUILayerCommand) == 56, "UruiLayerCommand ABI changed");
    struct Geometry
    {
        std::vector<URUIVertex> vertices;
        std::vector<int> indices;
        URUI::TextureSampling texture_sampling = URUI::TextureSampling::Clamp;
        std::array<float, 4> bounds{};
        bool boundsValid = false;
    };
    struct Texture
    {
        int width = 0;
        int height = 0;
        bool borrowed = false;
        bool resolved = false;
        size_t aliases = 0;
        size_t loads = 0;
        bool scoped = false;
        URUI::TextureHandle renderHandle = 0;
        std::vector<unsigned char> pixels;
    };
    constexpr uint64_t borrowedTextureBit = uint64_t{1} << 63;
    std::string error_message;
    void ReportBridgeFailure(const char* message) noexcept
    {
        try
        {
            error_message = message;
        }
        catch (...)
        {
        }
    }

    template <typename Handle> uint64_t AllocateTextureId(uint64_t& next)
    {
        // URUI uses uintptr_t handles, including on 32-bit WebGL. Public IDs
        // remain uint64_t, with the entire high-bit domain reserved for borrowing.
        constexpr uint64_t limit = std::numeric_limits<Handle>::max() < borrowedTextureBit
                                       ? std::numeric_limits<Handle>::max()
                                       : borrowedTextureBit - 1;
        if (!next || next > limit)
        {
            error_message = "Texture handle space exhausted";
            return 0;
        }
        return next++;
    }
    std::deque<URUI::ObserverPtr<URUI::Element>> clicks;
    RenderSerial renderSerial;
    // 外线程查询会与 Context 重建并发，所有权检查必须是无数据竞争的读取。
    std::atomic<std::thread::id> contextThread;
    class Renderer : public URUI::RenderInterface
    {
    public:
        RenderPlan plan{renderSerial};
        URUI::RenderPlan::Session drawPlan;
        uint64_t documentGeneration = URUI::RenderPlan::Identity();
        std::unordered_map<uint64_t, Geometry> meshes;
        std::unordered_map<uint64_t, Texture> textures;
        std::unordered_map<URUI::TextureHandle, uint64_t> borrowedTextureHandles;
        URUIImageResolver imageResolver = nullptr;
        URUIImageRelease imageRelease = nullptr;
        void ReleaseResolved(uint64_t id)
        {
            if (imageRelease)
            {
                ImageCallbackScope scope;
                try
                {
                    imageRelease(id);
                }
                catch (...)
                {
                    error_message = "Image release callback failed";
                }
            }
        }
        std::vector<URUICommand> commands;
        std::vector<URUIPlanUniform> commandUniforms;
        struct PaintShader
        {
            std::string name;
            URUI::ObserverPtr<URUI::Element> element;
            int kind = 0;
            std::vector<URUIColorFilter> colorFilters;
        };
        struct PaintEntry
        {
            uint64_t shader;
            int command_index;
        };
        std::unordered_map<uint64_t, PaintShader> paintShaders;
        std::vector<PaintEntry> paintCommands;
        std::unordered_map<int, std::array<float, 4>> effectInkBounds;
        struct EffectRootReference
        {
            URUI::ObserverPtr<URUI::Element> element;
            URUI::ObserverPtr<URUI::Element> root;
            URUI::String name;
            uint64_t structure = 0;
        };
        std::unordered_map<uint64_t, EffectRootReference> effectRoots;
        static bool EffectAttached(URUI::Element* element)
        {
            if (!element)
            {
                return false;
            }
            auto* document = element->GetOwnerDocument();
            for (auto* current = element; current; current = current->GetParentNode())
            {
                if (current == document)
                {
                    return true;
                }
            }
            return false;
        }
        void PruneEffectRoots()
        {
            for (auto iterator = effectRoots.begin(); iterator != effectRoots.end();)
            {
                if (!EffectAttached(iterator->second.element.get()))
                {
                    iterator = effectRoots.erase(iterator);
                }
                else
                {
                    ++iterator;
                }
            }
        }
        uint64_t nextPaintShader = 1;
        std::vector<URUILayerCommand> layerCommands;
        std::vector<uint64_t> layerStack = {0};
        std::unordered_map<uint64_t, uint64_t> maskFilters;
        struct Filter
        {
            std::string name;
            URUIFilter data{};
            uint64_t document = 0;
            URUI::ObserverPtr<URUI::Element> element;
            std::array<float, 4> expansion{};
            std::vector<std::array<float, 16>> coverageTransforms;
        };
        std::unordered_map<uint64_t, Filter> filters;
        uint64_t nextLayerResource = 1;
        uint64_t nextMaskFilter = 1;
        bool layerValid = true;
        void LayerCommand(int kind, uint64_t source, uint64_t target, uint64_t mask = 0, int blend = 0)
        {
            URUILayerCommand command{};
            command.command_index = static_cast<int>(commands.size());
            command.kind = kind;
            command.source = source;
            command.target = target;
            command.mask = mask;
            command.blend = blend;
            command.flags = clipped ? 1 : 0;
            if (clipped)
            {
                command.clip_x = clip.Left();
                command.clip_y = clip.Top();
                command.clip_w = clip.Width();
                command.clip_h = clip.Height();
            }
            layerCommands.push_back(command);
        }
        std::vector<std::array<float, 16>> commandTransforms;
        URUI::Matrix4f activeTransform = URUI::Matrix4f::Identity();
        uint64_t nextMesh = 1;
        uint64_t nextTexture = 1;
        bool clipped = false;
        URUI::Rectanglei clip = {};
        URUI::Context* recordedContext = nullptr;
        uint64_t recordedRevision = 0;
        uint64_t recordedDocument = 0;
        uint64_t recordedResources = 0;
        uint64_t recordedEpoch = 0;
        size_t recordedCommandCount = 0;
        uint64_t AllocateTextureHandle()
        {
            uint64_t id = AllocateTextureId<URUI::TextureHandle>(nextTexture);
            if (id && (textures.count(id) || borrowedTextureHandles.count(static_cast<URUI::TextureHandle>(id))))
            {
                error_message = "Texture handle collision";
                return 0;
            }
            return id;
        }
        uint64_t LogicalTextureId(URUI::TextureHandle handle) const
        {
            auto found = borrowedTextureHandles.find(handle);
            return found == borrowedTextureHandles.end() ? static_cast<uint64_t>(handle) : found->second;
        }
        URUI::CompiledGeometryHandle CompileGeometry(URUI::Span<const URUI::Vertex> vs,
                                                     URUI::Span<const int> ids) override
        {
            URUI_DEBUG_CODE(ProfileScope timer(profile.geometry_compile_ns);)
            URUI_DEBUG_CODE(++profile.geometry_compiles;)
            URUI_DEBUG_CODE(profile.geometry_vertices += static_cast<uint32_t>(vs.size());)
            URUI_DEBUG_CODE(profile.geometry_indices += static_cast<uint32_t>(ids.size());)
            constexpr uint64_t geometryLimit =
                sizeof(URUI::CompiledGeometryHandle) == 4 ? UINT32_MAX : ((uint64_t{1} << 63) - 1);
            if (!nextMesh || nextMesh > geometryLimit)
            {
                throw std::overflow_error("URUI geometry identity exhausted");
            }
            uint64_t handle = nextMesh++;
            Geometry g;
            g.vertices.reserve(vs.size());
            for (auto& v : vs)
            {
                g.vertices.push_back({v.position.x, v.position.y, v.tex_coord.x, v.tex_coord.y, v.colour.red,
                                      v.colour.green, v.colour.blue, v.colour.alpha});
                if (!g.boundsValid)
                {
                    g.bounds = {v.position.x, v.position.y, v.position.x, v.position.y};
                    g.boundsValid = true;
                }
                else
                {
                    g.bounds[0] = std::min(g.bounds[0], v.position.x);
                    g.bounds[1] = std::min(g.bounds[1], v.position.y);
                    g.bounds[2] = std::max(g.bounds[2], v.position.x);
                    g.bounds[3] = std::max(g.bounds[3], v.position.y);
                }
            }
            g.indices.assign(ids.begin(), ids.end());
            meshes.emplace(handle, std::move(g));
            plan.ResourceChanged();
            return static_cast<URUI::CompiledGeometryHandle>(handle);
        }
        void SetGeometryTextureSampling(URUI::CompiledGeometryHandle geometry, URUI::TextureSampling sampling) override
        {
            auto found = meshes.find(static_cast<uint64_t>(geometry));
            if (found != meshes.end() && found->second.texture_sampling != sampling)
            {
                found->second.texture_sampling = sampling;
                plan.ResourceChanged();
            }
        }
        void RenderGeometry(URUI::CompiledGeometryHandle geo, URUI::Vector2f move, URUI::TextureHandle tex,
                            const URUI::DrawParameters& parameters) override
        {
            URUICommand c{};
            c.geometry = static_cast<uint64_t>(geo);
            c.texture = LogicalTextureId(tex);
            c.tx = move.x;
            c.ty = move.y;
            const auto geometry = meshes.find(c.geometry);
            const auto sampling =
                geometry == meshes.end() ? URUI::TextureSampling::Clamp : geometry->second.texture_sampling;
            c.flags = (clipped ? 1 : 0) | (int(sampling) << 1);
            if (clipped)
            {
                c.clip_x = clip.Left();
                c.clip_y = clip.Top();
                c.clip_w = clip.Width();
                c.clip_h = clip.Height();
            }
            commands.push_back(c);
            const auto& color = parameters.tint;
            commandUniforms.push_back(
                {{color.red / 255.f, color.green / 255.f, color.blue / 255.f, color.alpha / 255.f},
                 {parameters.uv_scale.x, parameters.uv_scale.y, parameters.uv_offset.x, parameters.uv_offset.y}});
            std::array<float, 16> matrix{};
            for (int col = 0; col < 4; col++)
            {
                for (int row = 0; row < 4; row++)
                {
                    matrix[col * 4 + row] = activeTransform[col][row];
                }
            }
            commandTransforms.push_back(matrix);
        }
        void ReleaseGeometry(URUI::CompiledGeometryHandle h) override
        {
            if (meshes.erase(static_cast<uint64_t>(h)))
            {
                plan.ResourceChanged();
            }
        }
        void CollectTexture(uint64_t id)
        {
            auto found = textures.find(id);
            if (found == textures.end() || found->second.aliases || found->second.loads)
            {
                return;
            }
            const bool resolved = found->second.resolved;
            if (found->second.borrowed)
            {
                borrowedTextureHandles.erase(found->second.renderHandle);
            }
            textures.erase(found);
            plan.ResourceChanged();
            if (resolved)
            {
                ReleaseResolved(id);
            }
        }
        void ReleaseAlias(uint64_t id)
        {
            const auto found = textures.find(id);
            if (found == textures.end() || !found->second.aliases)
            {
                return;
            }
            --found->second.aliases;
            CollectTexture(id);
        }
        uint64_t RegisterScopedImage(const std::shared_ptr<URUI::Bridge::DocumentResources>& resources,
                                     const std::string& source, int width, int height, const uint8_t* pixels,
                                     size_t length, uint64_t external = 0)
        {
            // The URI claim identifies real source content, independently of
            // native cache indices and per-resolver-call lease identities.
            std::vector<uint8_t> identity;
            for (uint64_t value : {uint64_t(width), uint64_t(height), external})
            {
                for (int byte = 0; byte < 8; ++byte)
                {
                    identity.push_back(uint8_t(value >> (8 * byte)));
                }
            }
            if (!external)
            {
                identity.insert(identity.end(), pixels, pixels + length);
            }
            auto claim = resources->ClaimImage(source, identity.data(), identity.size(),
                                               external ? "external-image" : "rgba-image");
            // 来源身份保留原始字节，原生拥有的像素统一按纹理协议预乘透明度。
            std::vector<uint8_t> premultiplied;
            if (!external)
            {
                premultiplied.resize(length);
                for (size_t index = 0; index < length; index += 4)
                {
                    const auto alpha = pixels[index + 3];
                    for (size_t channel = 0; channel < 3; ++channel)
                    {
                        premultiplied[index + channel] = uint8_t((int(pixels[index + channel]) * alpha + 127) / 255);
                    }
                    premultiplied[index + 3] = alpha;
                }
            }
            uint64_t id = external;
            if (!external)
            {
                for (const auto& item : textures)
                {
                    const auto& texture = item.second;
                    if (texture.scoped && !texture.borrowed && texture.width == width && texture.height == height &&
                        texture.pixels == premultiplied)
                    {
                        id = item.first;
                        break;
                    }
                }
            }
            auto found = textures.find(id);
            if (found != textures.end() && (found->second.borrowed != bool(external) || found->second.resolved ||
                                            found->second.width != width || found->second.height != height))
            {
                throw std::invalid_argument("Texture ID kind or dimensions conflict: " + source);
            }
            if (found == textures.end())
            {
                const auto handle = AllocateTextureHandle();
                if (!handle)
                {
                    throw std::length_error("Texture handle space exhausted");
                }
                if (!id)
                {
                    id = handle;
                }
                Texture texture;
                texture.width = width;
                texture.height = height;
                texture.scoped = true;
                texture.borrowed = external != 0;
                texture.renderHandle = URUI::TextureHandle(handle);
                if (!external)
                {
                    texture.pixels = std::move(premultiplied);
                }
                textures.emplace(id, std::move(texture));
                if (external)
                {
                    borrowedTextureHandles.emplace(URUI::TextureHandle(handle), id);
                }
            }
            auto& texture = textures.at(id);
            if (texture.aliases == SIZE_MAX)
            {
                throw std::length_error("Texture alias limit reached");
            }
            ++texture.aliases;
            try
            {
                resources->BindImage(source, id, std::move(claim), [this, id] { ReleaseAlias(id); });
            }
            catch (...)
            {
                ReleaseAlias(id);
                throw;
            }
            plan.ResourceChanged();
            return id;
        }
        URUI::TextureHandle LoadTexture(URUI::Vector2i& dims, const URUI::String& source,
                                        const URUI::SharedPtr<URUI::ResourceContext>& resources = {}) override
        {
            dims = {};
            auto owned = std::dynamic_pointer_cast<URUI::Bridge::DocumentResources>(resources);
            if (!owned)
            {
                error_message = "Image resolution requires a document owner";
                return 0;
            }
            if (const auto id = owned->FindImage(source))
            {
                const auto found = textures.find(id);
                if (found != textures.end())
                {
                    dims = {found->second.width, found->second.height};
                    ++found->second.loads;
                    return found->second.renderHandle;
                }
            }
            if (!imageResolver)
            {
                owned->ImageFailed(source);
                error_message = "Texture not registered: " + source;
                return 0;
            }
            uint64_t id = 0;
            uint64_t source_identity = 0;
            int width = 0;
            int height = 0;
            int ok = 0;
            ImageCallbackScope scope;
            try
            {
                ok = imageResolver(owned->GetOwner(), source.c_str(), &id, &source_identity, &width, &height);
            }
            catch (...)
            {
                error_message = "Image resolver callback failed";
                owned->ImageFailed(source);
                return 0;
            }
            if (ok == 1)
            {
                try
                {
                    if (!source_identity)
                    {
                        throw std::invalid_argument("Resolved image source identity is zero");
                    }
                    std::array<uint64_t, 3> descriptor{{source_identity, uint64_t(width), uint64_t(height)}};
                    auto claim = owned->ClaimImage(source, reinterpret_cast<const uint8_t*>(descriptor.data()),
                                                   sizeof(descriptor), "resolved-image");
                    if (!(id & borrowedTextureBit) || textures.count(id) || width <= 0 || height <= 0 || width > 8192 ||
                        height > 8192)
                    {
                        throw std::invalid_argument("Invalid resolved texture metadata or reused resource ID");
                    }
                    const auto handle = AllocateTextureHandle();
                    if (!handle)
                    {
                        throw std::length_error("Texture handle space exhausted");
                    }
                    Texture texture;
                    texture.width = width;
                    texture.height = height;
                    texture.borrowed = texture.resolved = texture.scoped = true;
                    texture.renderHandle = URUI::TextureHandle(handle);
                    texture.aliases = 1;
                    textures.emplace(id, std::move(texture));
                    borrowedTextureHandles.emplace(URUI::TextureHandle(handle), id);
                    try
                    {
                        owned->BindImage(source, id, std::move(claim), [this, id] { ReleaseAlias(id); });
                    }
                    catch (...)
                    {
                        ReleaseAlias(id);
                        id = 0;
                        throw;
                    }
                    ++textures.at(id).loads;
                    dims = {width, height};
                    plan.ResourceChanged();
                    return URUI::TextureHandle(handle);
                }
                catch (const std::exception& error)
                {
                    if (id && !textures.count(id))
                    {
                        ReleaseResolved(id);
                    }
                    owned->ImageFailed(source);
                    error_message = error.what();
                    return 0;
                }
            }
            owned->ImageFailed(source);
            error_message = "Texture not registered: " + source;
            return 0;
        }
        URUI::TextureHandle GenerateTexture(URUI::Span<const URUI::byte> pixels, URUI::Vector2i dims) override
        {
            if (dims.x <= 0 || dims.y <= 0)
            {
                return 0;
            }
            uint64_t id = AllocateTextureHandle();
            if (!id)
            {
                return 0;
            }
            Texture t;
            t.width = dims.x;
            t.height = dims.y;
            t.renderHandle = static_cast<URUI::TextureHandle>(id);
            t.pixels.assign(pixels.begin(), pixels.end());
            textures.emplace(id, std::move(t));
            plan.ResourceChanged();
            return static_cast<URUI::TextureHandle>(id);
        }
        void ReleaseTexture(URUI::TextureHandle id) override
        {
            auto found = textures.find(LogicalTextureId(id));
            if (found != textures.end() && found->second.scoped)
            {
                if (found->second.loads)
                {
                    --found->second.loads;
                }
                CollectTexture(found->first);
                return;
            }
            if (found != textures.end() && !found->second.aliases)
            {
                const bool resolved = found->second.resolved;
                const auto logical = found->first;
                if (found->second.borrowed)
                {
                    borrowedTextureHandles.erase(found->second.renderHandle);
                }
                textures.erase(found);
                plan.ResourceChanged();
                if (resolved)
                {
                    ReleaseResolved(logical);
                }
            }
        }
        void EnableClipMask(bool enable) override
        {
            recordedContext = nullptr;
            if (enable)
            {
                plan.Unsupported();
            }
            URUI::RenderInterface::EnableClipMask(enable);
        }
        void RenderToClipMask(URUI::ClipMaskOperation op, URUI::CompiledGeometryHandle g, URUI::Vector2f t) override
        {
            recordedContext = nullptr;
            plan.Unsupported();
            URUI::RenderInterface::RenderToClipMask(op, g, t);
        }
        URUI::LayerHandle PushLayer() override
        {
            recordedContext = nullptr;
            plan.Unsupported();
            const auto id = nextLayerResource++;
            if (!id)
            {
                layerValid = false;
                return 0;
            }
            layerStack.push_back(id);
            LayerCommand(URUI_LAYER_PUSH, 0, id);
            return static_cast<URUI::LayerHandle>(id);
        }
        void CompositeLayers(URUI::LayerHandle a, URUI::LayerHandle b, URUI::BlendMode mode,
                             URUI::Span<const URUI::CompiledFilterHandle> filters) override
        {
            recordedContext = nullptr;
            plan.Unsupported();
            for (auto handle : filters)
            {
                const auto mask = maskFilters.find(static_cast<uint64_t>(handle));
                if (mask != maskFilters.end())
                {
                    LayerCommand(URUI_LAYER_APPLY_MASK, static_cast<uint64_t>(a), static_cast<uint64_t>(a),
                                 mask->second, 1);
                }
                else if (this->filters.count(static_cast<uint64_t>(handle)))
                {
                    LayerCommand(URUI_LAYER_APPLY_MASK, static_cast<uint64_t>(a), static_cast<uint64_t>(a),
                                 static_cast<uint64_t>(handle));
                }
                else
                {
                    layerValid = false;
                    return;
                }
            }
            if (std::find(layerStack.begin(), layerStack.end(), static_cast<uint64_t>(a)) == layerStack.end() ||
                std::find(layerStack.begin(), layerStack.end(), static_cast<uint64_t>(b)) == layerStack.end())
            {
                layerValid = false;
                return;
            }
            if (a != b)
            {
                LayerCommand(URUI_LAYER_COMPOSITE, static_cast<uint64_t>(a), static_cast<uint64_t>(b), 0,
                             static_cast<int>(mode));
            }
        }
        void PopLayer() override
        {
            recordedContext = nullptr;
            plan.Unsupported();
            if (layerStack.size() <= 1)
            {
                layerValid = false;
                return;
            }
            const auto previous = layerStack.back();
            layerStack.pop_back();
            LayerCommand(URUI_LAYER_POP, previous, layerStack.back());
        }
        URUI::TextureHandle SaveLayerAsTexture() override
        {
            recordedContext = nullptr;
            plan.Unsupported();
            return URUI::RenderInterface::SaveLayerAsTexture();
        }
        URUI::CompiledFilterHandle SaveLayerAsMaskImage() override
        {
            recordedContext = nullptr;
            plan.Unsupported();
            if (layerStack.size() <= 1 || !nextMaskFilter || !nextLayerResource)
            {
                layerValid = false;
                return 0;
            }
            const auto resource = nextLayerResource++;
            const auto filter = nextMaskFilter++;
            // 两次分配都成功后才发布句柄，失败帧不会留下无法归还的遮罩身份。
            LayerCommand(URUI_LAYER_SAVE_MASK, layerStack.back(), resource);
            maskFilters.emplace(filter, resource);
            return static_cast<URUI::CompiledFilterHandle>(filter);
        }
        URUI::CompiledFilterHandle CompileFilter(const URUI::String& name, const URUI::Dictionary& p,
                                                 const URUI::SharedPtr<URUI::ResourceContext>& resources = {}) override
        {
            recordedContext = nullptr;
            plan.Unsupported();
            if (!nextMaskFilter || nextMaskFilter == std::numeric_limits<uint64_t>::max())
            {
                layerValid = false;
                return 0;
            }
            const auto id = nextMaskFilter++;
            Filter filter;
            filter.name = name;
            filter.data.handle = id;
            filter.document = resources ? resources->GetOwner() : 0;
            if (name == "--urui-effect")
            {
                const auto value = p.find("value");
                const auto owner = p.find("element");
                auto* element = owner == p.end() ? nullptr : static_cast<URUI::Element*>(owner->second.Get<void*>());
                if (value == p.end() || !element)
                {
                    layerValid = false;
                    return 0;
                }
                filter.name = value->second.Get<URUI::String>();
                filter.element = element->GetObserverPtr();
                this->filters.emplace(id, std::move(filter));
                return static_cast<URUI::CompiledFilterHandle>(id);
            }
            const auto value = p.find("value");
            if (value != p.end())
            {
                filter.data.value = value->second.Get<float>();
            }
            const auto sigma = p.find("sigma");
            if (sigma != p.end())
            {
                filter.data.sigma = sigma->second.Get<float>();
            }
            const auto offset = p.find("offset");
            if (offset != p.end())
            {
                auto v = offset->second.Get<URUI::Vector2f>();
                filter.data.offset_x = v.x;
                filter.data.offset_y = v.y;
            }
            const auto color = p.find("color");
            if (color != p.end())
            {
                auto c = color->second.Get<URUI::Colourb>();
                filter.data.red = c.red / 255.f;
                filter.data.green = c.green / 255.f;
                filter.data.blue = c.blue / 255.f;
                filter.data.alpha = c.alpha / 255.f;
            }
            this->filters.emplace(id, std::move(filter));
            return static_cast<URUI::CompiledFilterHandle>(id);
        }
        void ReleaseFilter(URUI::CompiledFilterHandle h) override
        {
            recordedContext = nullptr;
            const auto found = maskFilters.find(static_cast<uint64_t>(h));
            if (found != maskFilters.end())
            {
                // CompiledFilter 的 noexcept 析构也会进入这里；内存不足时撤销整帧，仍归还句柄。
                try
                {
                    LayerCommand(URUI_LAYER_RELEASE_MASK, found->second, 0);
                }
                catch (...)
                {
                    layerValid = false;
                    ReportBridgeFailure("Cannot record URUI mask release");
                }
                maskFilters.erase(found);
                return;
            }
            plan.Unsupported();
            if (filters.erase(static_cast<uint64_t>(h)))
            {
                return;
            }
            URUI::RenderInterface::ReleaseFilter(h);
        }
        URUI::CompiledShaderHandle CompileShader(const URUI::String& name, const URUI::Dictionary& p) override
        {
            recordedContext = nullptr;
            plan.Unsupported();
            const auto value = p.find("value");
            const auto owner = p.find("element");
            if ((name == "shader" || name == "--urui-image-effect" || name == "--urui-css-color") && value != p.end() &&
                owner != p.end())
            {
                auto* element = static_cast<URUI::Element*>(owner->second.Get<void*>());
                auto key = value->second.Get<URUI::String>();
                if (element && !key.empty() && nextPaintShader != std::numeric_limits<uint64_t>::max())
                {
                    const auto handle = nextPaintShader++;
                    PaintShader shader{std::move(key),
                                       element->GetObserverPtr(),
                                       name == "--urui-image-effect" ? 1
                                       : name == "--urui-css-color"  ? 2
                                                                     : 0,
                                       {}};
                    if (shader.kind == 2)
                    {
                        const auto list = URUI::DirectColorFilters(element);
                        if (!list)
                        {
                            return 0;
                        }
                        for (const auto& declaration : list->list)
                        {
                            const auto* parameter = declaration.properties.GetProperty(
                                declaration.instancer->GetPropertySpecification().GetPropertyId("value"));
                            const auto& type = declaration.type;
                            shader.colorFilters.push_back(
                                {type == "grayscale"    ? 0
                                 : type == "saturate"   ? 1
                                 : type == "brightness" ? 2
                                 : type == "contrast"   ? 3
                                                        : 4,
                                 parameter->Get<float>() * (parameter->unit == URUI::Unit::PERCENT ? .01f : 1.f)});
                        }
                    }
                    paintShaders.emplace(handle, std::move(shader));
                    return static_cast<URUI::CompiledShaderHandle>(handle);
                }
            }
            return URUI::RenderInterface::CompileShader(name, p);
        }
        void RenderShader(URUI::CompiledShaderHandle s, URUI::CompiledGeometryHandle g, URUI::Vector2f t,
                          URUI::TextureHandle tex, const URUI::DrawParameters& parameters) override
        {
            recordedContext = nullptr;
            plan.Unsupported();
            const auto found = paintShaders.find(static_cast<uint64_t>(s));
            if (found != paintShaders.end() && found->second.element)
            {
                paintCommands.push_back({static_cast<uint64_t>(s), static_cast<int>(commands.size())});
                RenderGeometry(g, t, tex, parameters);
                return;
            }
            URUI::RenderInterface::RenderShader(s, g, t, tex, parameters);
        }
        void ReleaseShader(URUI::CompiledShaderHandle h) override
        {
            recordedContext = nullptr;
            plan.Unsupported();
            if (paintShaders.erase(static_cast<uint64_t>(h)))
            {
                return;
            }
            URUI::RenderInterface::ReleaseShader(h);
        }
        void EnableScissorRegion(bool value) override
        {
            recordedContext = nullptr;
            clipped = value;
        }
        void SetScissorRegion(URUI::Rectanglei value) override
        {
            recordedContext = nullptr;
            clip = value;
        }
        void SetTransform(const URUI::Matrix4f* matrix) override
        {
            recordedContext = nullptr;
            activeTransform = matrix ? *matrix : URUI::Matrix4f::Identity();
        }
    };
    class Listener : public URUI::EventListener
    {
        void ProcessEvent(URUI::Event& event) override
        {
            auto* target = event.GetTargetElement();
            if (target && clicks.size() < 4096)
            {
                clicks.push_back(target->GetObserverPtr());
            }
        }
    };
    URUI::String ClickId(const URUI::ObserverPtr<URUI::Element>& value)
    {
        for (auto* el = value.get(); el; el = el->GetParentNode())
        {
            if (!el->GetId().empty())
            {
                return el->GetId();
            }
        }
        return {};
    }
    std::unique_ptr<Renderer> render;
    std::unique_ptr<Listener> listener;
    std::unique_ptr<ResourceFiles> resourceFiles;
    URUI::Context* context = nullptr;
    bool started = false;
    ElementReferences elementReferences;
    std::unique_ptr<URUI::Bridge::DocumentRegistry> documentRegistry;
    std::unique_ptr<URUI::Bridge::FileResourceCache> fileResourceCache;
    uint64_t nextResourceScope = 1;
    int CountElements(URUI::Element* root)
    {
        if (!root)
        {
            return 0;
        }
        int n = 1;
        for (int i = 0; i < root->GetNumChildren(); ++i)
        {
            n += CountElements(root->GetChild(i));
        }
        return n;
    }
    std::string SafeHtml(const char* src)
    {
        std::string out;
        if (!src)
        {
            return out;
        }
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

}
namespace URUI::Interaction
{
    extern int callbackDepth;
    extern bool pendingDestroy;
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
    int urui_command_protocol_version(void)
    {
        return 2;
    }
    int urui_surface_set_default_font_face(int family, const uint8_t* bytes, int length, int face_index, int style,
                                           int weight)
    try
    {
        if (family < 0 || family > 2 || !bytes || length <= 0 || length > 64 * 1024 * 1024 || face_index < 0 ||
            style < 0 || style > 1 || weight < 1 || weight > 1000)
        {
            return -1;
        }
        if (!context || !documentRegistry)
        {
            return -2;
        }
        if (contextThread != std::this_thread::get_id())
        {
            return -3;
        }
        if (URUI::Interaction::callbackDepth || documentRegistry->Count() != 0 || context->GetNumDocuments() != 0)
        {
            return -5;
        }
        if (!URUI::DocumentFontContext::LoadDefaultFace(family, {bytes, size_t(length)},
                                                        static_cast<URUI::Style::FontStyle>(style),
                                                        static_cast<URUI::Style::FontWeight>(weight), face_index))
        {
            error_message =
                "Cannot install the default font face: invalid source or a different face already occupies the slot";
            return -2;
        }
        error_message.clear();
        return 1;
    }
    catch (...)
    {
        try
        {
            error_message = "Default font bootstrap failed";
        }
        catch (...)
        {
        }
        return -8;
    }
    int urui_texture_protocol_version(void)
    {
        return 1;
    }
    int urui_image_resolver_version(void)
    {
        return 2;
    }
    int urui_set_image_resolver(URUIImageResolver resolve, URUIImageRelease release)
    {
        if (!render || contextThread != std::this_thread::get_id() || URUI::Interaction::callbackDepth ||
            bool(resolve) != bool(release))
        {
            return -1;
        }
        if (context && context->GetNumDocuments() != 0)
        {
            return -2;
        }
        for (const auto& entry : render->textures)
        {
            if (entry.second.resolved)
            {
                return -2;
            }
        }
        render->imageResolver = resolve;
        render->imageRelease = release;
        return 1;
    }
    int urui_create(int w, int h)
    try
    {
        if (started)
        {
            return 0;
        }
        contextThread = std::this_thread::get_id();
        URUI_DEBUG_CODE(profile = {};)
        error_message.clear();
        render = std::make_unique<Renderer>();
        listener = std::make_unique<Listener>();
        URUI::SetRenderInterface(render.get());
        URUI::Interaction::ResetClock();
        URUI::Interaction::Install();
        if (!URUI::Initialise())
        {
            error_message = "URUI Initialise failed";
            listener.reset();
            render.reset();
            return 0;
        }
        started = true;
        URUI::DirectColorFilterRenderingEnabled = false;
        URUI::Images::RegisterStyles();
        URUI::StyleSheetSpecification::RegisterProperty("--urui-caret-width", "1px", false, true).AddParser("length");
        URUI::StyleSheetSpecification::RegisterProperty("--urui-caret-blink-rate", "0.85", false).AddParser("number");
        // Preserve the platform file interface created by Initialise; install
        // the resource overlay before loading document resources.
        resourceFiles = std::make_unique<ResourceFiles>(URUI::GetFileInterface());
        URUI::SetFileInterface(resourceFiles.get());
        context = URUI::CreateContext("urui", {std::max(1, w), std::max(1, h)});
        if (!context)
        {
            error_message = "Cannot create URUI context";
            urui_destroy();
            return 0;
        }
        elementReferences.Begin();
        URUI::RegisterPlugin(&elementReferences);
        try
        {
            fileResourceCache = std::make_unique<URUI::Bridge::FileResourceCache>();
            documentRegistry = std::make_unique<URUI::Bridge::DocumentRegistry>(*context, elementReferences,
                                                                                URUI::Interaction::RetireDocument);
        }
        catch (...)
        {
            error_message = "Cannot create document registry";
            urui_destroy();
            return 0;
        }
        URUI::Styles::Begin(URUI::Elements::Get);
        return 1;
    }
    catch (...)
    {
        urui_destroy();
        // Reporting an allocation failure must not allocate across the C ABI.
        try
        {
            error_message = "Cannot initialize URUI Surface";
        }
        catch (...)
        {
        }
        return 0;
    }
    void urui_destroy(void)
    {
        // Surface 只由创建线程销毁；后台帧租约仍可按自身线程约定归还。
        if (contextThread != std::thread::id{} && contextThread != std::this_thread::get_id())
        {
            return;
        }
        if (URUI::Interaction::callbackDepth)
        {
            URUI::Interaction::pendingDestroy = true;
            return;
        }
        if (documentRegistry)
        {
            documentRegistry->Clear();
        }
        documentRegistry.reset();
        URUI::Interaction::Reset();
        elementReferences.Clear();
        if (render)
        {
            render->plan.NewDocument();
        }
        URUI::Styles::End();
        if (context)
        {
            URUI::RemoveContext("urui");
            context = nullptr;
        }
        fileResourceCache.reset();
        if (started)
        {
            if (resourceFiles)
            {
                URUI::SetFileInterface(resourceFiles->Original());
                resourceFiles.reset();
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
        URUI::Interaction::pendingDestroy = false;
        URUI_DEBUG_CODE(typed_probe = {}; typed_probe_mode = 0;)
#if defined(URUI_DEBUG)
        URUI::LookupProbe::inline_enabled = false;
        URUI::LookupProbe::profile_mode = 0;
        URUI::LookupProbe::style_scope = false;
        URUI::LookupProbe::profile = {};
#endif
    }
    int urui_document_add_stylesheet(URUIDocumentHandle document_handle, const char* css, const char* source_url,
                                     int before_author)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!document || !css || !*css || !source_url || !*source_url || (before_author != 0 && before_author != 1) ||
            !elementReferences.Owner() || URUI::Interaction::callbackDepth)
        {
            return -1;
        }
        auto sheet = URUI::MakeShared<URUI::StyleSheetContainer>();
        URUI::StreamMemory cssStream(reinterpret_cast<const URUI::byte*>(css), std::strlen(css));
        cssStream.SetSourceURL(source_url);
        if (!sheet->LoadStyleSheetContainer(&cssStream, 1, document))
        {
            error_message =
                sheet->GetResourceError().empty() ? "CSS resource could not be parsed" : sheet->GetResourceError();
            return -2;
        }
        if (const auto* current = document->GetStyleSheetContainer())
        {
            sheet = before_author ? sheet->CombineStyleSheetContainer(*current)
                                  : current->CombineStyleSheetContainer(*sheet);
        }
        if (!sheet)
        {
            return -2;
        }
        document->SetStyleSheetContainer(std::move(sheet));
        return 1;
    }
    catch (...)
    {
        try
        {
            error_message = "Native stylesheet resource failed";
        }
        catch (...)
        {
        }
        return -2;
    }

    void urui_resize(int w, int h)
    try
    {
        if (context)
        {
            context->SetDimensions({std::max(1, w), std::max(1, h)});
        }
    }
    catch (...)
    {
        ReportBridgeFailure("urui_resize failed");
    }
    int urui_hit_test_id(URUIDocumentHandle document_handle, float x, float y, char* out, int capacity)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!context || !document || !std::isfinite(x) || !std::isfinite(y) || capacity < 0)
        {
            return -1;
        }
        if (out && capacity > 0)
        {
            out[0] = 0;
        }
        auto* el = context->GetElementAtPoint({x, y});
        if (el && el->GetOwnerDocument() != document)
        {
            return 0;
        }
        while (el && el->GetId().empty())
        {
            el = el->GetParentNode();
        }
        if (!el)
        {
            return 0;
        }
        const auto& id = el->GetId();
        if (id.size() + 1 > static_cast<size_t>(capacity) || !out)
        {
            return -static_cast<int>(id.size() + 1);
        }
        std::memcpy(out, id.c_str(), id.size() + 1);
        return static_cast<int>(id.size());
    }
    catch (...)
    {
        ReportBridgeFailure("urui_hit_test_id failed");
        return -8;
    }
    int urui_element_exists(URUIDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!document || !id || !*id)
        {
            return 0;
        }
        return document->GetElementById(id) ? 1 : 0;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_element_exists failed");
        return -8;
    }
    int urui_get_element_box(URUIDocumentHandle document_handle, const char* id, float* out_six)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!document || !id || !*id || !out_six)
        {
            return -1;
        }
        auto* el = document->GetElementById(id);
        if (!el)
        {
            return 0;
        }
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
        ReportBridgeFailure("urui_get_element_box failed");
        return -8;
    }
    int urui_set_attribute(URUIDocumentHandle document_handle, const char* id, const char* name, const char* value)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        URUI_DEBUG_CODE(ProfileScope timer(api_profile.attribute_ns, api_profile_enabled);)
        URUI_DEBUG_CODE(if (api_profile_enabled)++ api_profile.attribute_calls;)
        if (!id || !*id || !name || !*name || !value)
        {
            error_message = "Invalid attribute mutation";
            return -1;
        }
        if (!document)
        {
            return 0;
        }
        auto* el = document->GetElementById(id);
        if (!el)
        {
            return 0;
        }
        el->SetAttribute(name, URUI::String(value));
        error_message.clear();
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_set_attribute failed");
        return -8;
    }
    int urui_set_style(URUIDocumentHandle document_handle, const char* id, const char* name, const char* value)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
#if defined(URUI_DEBUG)
        URUI::LookupProbe::StyleScope lookup_style;
#endif
        URUI_DEBUG_CODE(ProfileScope timer(api_profile.style_ns, api_profile_enabled);)
        URUI_DEBUG_CODE(if (api_profile_enabled)++ api_profile.style_calls;)
        if (!id || !*id || !name || !*name || !value)
        {
            error_message = "Invalid style mutation";
            return -1;
        }
        if (!document)
        {
            return 0;
        }
#if defined(URUI_DEBUG)
        auto* sample = URUI::StyleProbe::mode ? &URUI::StyleProbe::total : nullptr;
        if (sample)
        {
            ++sample->calls;
        }
        URUI::Element* el = nullptr;
        {
            URUI::StyleProbe::Scope lookup(sample ? &sample->lookup_ns : nullptr);
            el = document->GetElementById(id);
        }
        if (!el)
        {
            return 0;
        }
        struct ActiveGuard
        {
            URUIStyleProbe* previous;
            ActiveGuard(URUIStyleProbe* p) : previous(URUI::StyleProbe::active)
            {
                URUI::StyleProbe::active = p;
            }
            ~ActiveGuard()
            {
                URUI::StyleProbe::active = previous;
            }
        } guard(sample);
#else
        auto* el = document->GetElementById(id);
        if (!el)
        {
            return 0;
        }
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
        ReportBridgeFailure("urui_set_style failed");
        return -8;
    }
    int urui_set_translate_scale_2d(URUIDocumentHandle document_handle, const char* id, float x, float y, float sx,
                                    float sy)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
#if defined(URUI_DEBUG)
        URUI::LookupProbe::StyleScope lookup_style;
#endif
        URUI_DEBUG_CODE(ProfileScope timer(api_profile.style_ns, api_profile_enabled);)
        URUI_DEBUG_CODE(if (api_profile_enabled)++ api_profile.style_calls;)
        URUI_DEBUG_CODE(const bool measure = typed_probe_mode == 1;)
        URUI_DEBUG_CODE(ProfileScope total(typed_probe.total_ns, measure);)
        URUI_DEBUG_CODE(if (typed_probe_mode) {
            ++typed_probe.calls;
            if (measure)
            {
                ++typed_probe.clock_pairs;
            }
        })
        if (!id || !*id || !std::isfinite(x) || !std::isfinite(y) || !std::isfinite(sx) || !std::isfinite(sy))
        {
            URUI_DEBUG_CODE(if (typed_probe_mode)++ typed_probe.rejects;)
            error_message = "Invalid typed translate-scale mutation";
            return -1;
        }
        if (!document)
        {
            return 0;
        }
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
        ReportBridgeFailure("urui_set_translate_scale_2d failed");
        return -8;
    }

    uint64_t urui_element_ref_acquire(URUIDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        return elementReferences.Acquire(document, id);
    }
    catch (...)
    {
        ReportBridgeFailure("urui_element_ref_acquire failed");
        return 0;
    }
    int urui_element_ref_valid(URUIDocumentHandle document_handle, uint64_t token)
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!elementReferences.Owner())
        {
            return -3;
        }
        return elementReferences.Resolve(token, document) ? 1 : 0;
    }
    int urui_element_ref_release(URUIDocumentHandle document_handle, uint64_t token)
    {
        if (!elementReferences.Owner())
        {
            return -3;
        }
        auto* owner = URUI::Elements::Document(document_handle);
        if (!owner)
        {
            return -2;
        }
        if (elementReferences.OwnerDocument(token) != owner)
        {
            return -1;
        }
        return elementReferences.Release(token);
    }
    int urui_element_ref_count(URUIDocumentHandle document_handle)
    {
        auto* owner = URUI::Elements::Document(document_handle);
        return owner ? elementReferences.Count(owner) : -2;
    }
    int urui_element_ref_set_translate_scale_2d(URUIDocumentHandle document_handle, uint64_t token, float x, float y,
                                                float sx, float sy)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!elementReferences.Owner())
        {
            return -3;
        }
        URUI_DEBUG_CODE(ProfileScope api(api_profile.style_ns, api_profile_enabled);)
        URUI_DEBUG_CODE(if (api_profile_enabled)++ api_profile.style_calls;)
        URUI_DEBUG_CODE(const bool measure = typed_probe_mode == 1;)
        URUI_DEBUG_CODE(ProfileScope total(typed_probe.total_ns, measure);)
        URUI_DEBUG_CODE(if (typed_probe_mode) {
            ++typed_probe.calls;
            if (measure)
            {
                ++typed_probe.clock_pairs;
            }
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
            el = elementReferences.Resolve(token, document);
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
        ReportBridgeFailure("urui_element_ref_set_translate_scale_2d failed");
        return -8;
    }

    int urui_set_inner_html(URUIDocumentHandle document_handle, const char* id, const char* markup)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!id || !*id || !markup || !elementReferences.Owner() || URUI::Interaction::callbackDepth)
        {
            return -1;
        }
        if (!document)
        {
            return 0;
        }
        auto* el = document->GetElementById(id);
        if (!el)
        {
            return 0;
        }
        const int result = URUI::Elements::ReplaceHtml(*el, markup);
        if (result == 1)
        {
            elementReferences.Prune();
        }
        return result;
    }
    catch (...)
    {
        return -2;
    }
    int urui_append_element(URUIDocumentHandle document_handle, const char* parent_id, const char* tag, const char* id)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!parent_id || !*parent_id || !tag || !*tag || !id)
        {
            error_message = "Invalid append";
            return -1;
        }
        if (!document)
        {
            return 0;
        }
        auto* parent = document->GetElementById(parent_id);
        if (!parent)
        {
            return 0;
        }
        auto child = document->CreateElement(tag);
        if (!child)
        {
            error_message = "URUI rejected element tag";
            return -2;
        }
        if (*id)
        {
            child->SetId(id);
        }
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
        ReportBridgeFailure("urui_append_element failed");
        return -8;
    }
    // General DOM sibling ordering API used by atomic UI and ordinary managed clients.
    // Ownership stays in URUI; moving the same Element preserves its observer identity.
    int urui_get_child_count(URUIDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!document || !id || !*id)
        {
            return -1;
        }
        auto* el = document->GetElementById(id);
        return el ? el->GetNumChildren() : -1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_child_count failed");
        return -8;
    }
    int urui_get_child_id_utf8(URUIDocumentHandle document_handle, const char* id, int index, char* out, int capacity)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!document || !id || !*id || index < 0 || capacity < 0)
        {
            return -1;
        }
        auto* el = document->GetElementById(id);
        if (!el || index >= el->GetNumChildren())
        {
            return -1;
        }
        const auto& name = el->GetChild(index)->GetId();
        if (name.size() >= size_t(INT32_MAX))
        {
            return -1;
        }
        if (!out || size_t(capacity) < name.size() + 1)
        {
            return -int(name.size() + 1);
        }
        std::memcpy(out, name.c_str(), name.size() + 1);
        return int(name.size());
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_child_id_utf8 failed");
        return -8;
    }
    int urui_reorder_element_to_end(URUIDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!id || !*id)
        {
            error_message = "Invalid reorder";
            return -1;
        }
        if (!document)
        {
            return 0;
        }
        auto* el = document->GetElementById(id);
        if (!el)
        {
            return 0;
        }
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
        ReportBridgeFailure("urui_reorder_element_to_end failed");
        return -8;
    }
    int urui_remove_element(URUIDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!id || !*id)
        {
            error_message = "Invalid remove";
            return -1;
        }
        if (!document)
        {
            return 0;
        }
        auto* el = document->GetElementById(id);
        if (!el)
        {
            return 0;
        }
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
        ReportBridgeFailure("urui_remove_element failed");
        return -8;
    }
    void urui_update(void)
    try
    {
        if (URUI::Interaction::callbackDepth)
        {
            return;
        }
        URUI_DEBUG_CODE(ProfileScope timer(profile.update_ns);)
        if (context)
        {
            URUI::Interaction::FlushCandidateEvents();
            context->Update();
            URUI::Styles::Prune();
        }
    }
    catch (...)
    {
        try
        {
            error_message = "URUI update failed";
        }
        catch (...)
        {
        }
    }
    int urui_synchronize_geometry(void)
    try
    {
        if (contextThread != std::this_thread::get_id() || URUI::Interaction::callbackDepth)
        {
            return -3;
        }
        if (!context)
        {
            return -1;
        }
        return context->SynchronizeGeometry() ? 1 : -1;
    }
    catch (...)
    {
        ReportBridgeFailure("URUI geometry synchronization failed");
        return -4;
    }
    void urui_render(void)
    try
    {
        if (URUI::Interaction::callbackDepth)
        {
            return;
        }
        URUI_DEBUG_CODE(ProfileScope timer(profile.render_ns);)
        if (!context || !render)
        {
            return;
        }
        const auto revision = context->GetNativeRenderRevision();
        const auto stamp = render->plan.Snapshot(render->commands.size());
        const bool reusable =
            !renderSerial.exhausted && render->documentGeneration && context->AllowsNativeRecordingReuse();
        if (reusable && render->recordedContext == context && render->recordedRevision == revision &&
            render->recordedDocument == render->documentGeneration && render->recordedResources == stamp.resources &&
            render->recordedEpoch == stamp.epoch && render->recordedCommandCount == render->commands.size() &&
            render->layerValid && render->maskFilters.empty())
        {
            render->plan.ReuseRecording();
            return;
        }
        render->recordedContext = nullptr;
        render->plan.Begin(render->commands, render->commandTransforms);
        render->commandUniforms.clear();
        render->paintCommands.clear();
        render->effectInkBounds.clear();
        render->PruneEffectRoots();
        render->layerCommands.clear();
        render->layerStack.assign(1, 0);
        render->nextLayerResource = 1;
        render->layerValid = render->maskFilters.empty();
        render->clipped = false;
        render->activeTransform = URUI::Matrix4f::Identity();
        context->Render();
        render->layerValid = render->layerValid && render->layerStack.size() == 1 && render->maskFilters.empty();
        render->plan.Finish(render->commands, render->commandTransforms);
        if (reusable && revision == context->GetNativeRenderRevision() && render->layerValid &&
            context->AllowsNativeRecordingReuse())
        {
            const auto finished = render->plan.Snapshot(render->commands.size());
            render->recordedContext = context;
            render->recordedRevision = revision;
            render->recordedDocument = render->documentGeneration;
            render->recordedResources = finished.resources;
            render->recordedEpoch = finished.epoch;
            render->recordedCommandCount = render->commands.size();
        }
    }
    catch (...)
    {
        if (render)
        {
            render->recordedContext = nullptr;
            render->commands.clear();
            render->commandTransforms.clear();
            render->commandUniforms.clear();
            render->paintCommands.clear();
            render->layerCommands.clear();
            render->layerStack.clear();
            render->layerValid = false;
            render->plan.NewDocument();
        }

        ReportBridgeFailure("urui_render failed");
    }
    int urui_render_plan_version()
    {
        return 8;
    }
    int urui_render_plan_stamp(URUIFrameStamp* out)
    {
        if (!out)
        {
            return 0;
        }
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
        {
            return -1;
        }
        const size_t count = render->commandTransforms.size();
        if (count > static_cast<size_t>(INT32_MAX / 16) || static_cast<size_t>(float_capacity) < count * 16 ||
            (!out && count))
        {
            return -static_cast<int>(count * 16);
        }
        for (size_t i = 0; i < count; i++)
        {
            std::memcpy(out + i * 16, render->commandTransforms[i].data(), 16 * sizeof(float));
        }
        return static_cast<int>(count);
    }
    int urui_get_attribute_utf8(URUIDocumentHandle document_handle, const char* id, const char* name, char* out,
                                int capacity, int* required)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!id || !*id || !name || !*name || capacity < 0 || !required || (!out && capacity > 0))
        {
            return -1;
        }
        *required = 0;
        if (!document)
        {
            return 0;
        }
        auto* el = document->GetElementById(id);
        if (!el || !el->HasAttribute(name))
        {
            return 0;
        }
        const auto value = el->GetAttribute<URUI::String>(name, "");
        if (value.size() >= size_t(INT32_MAX))
        {
            return -1;
        }
        *required = int(value.size() + 1);
        if (!out || capacity < *required)
        {
            return -3;
        }
        std::memcpy(out, value.c_str(), value.size() + 1);
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_attribute_utf8 failed");
        return -8;
    }
    int urui_get_text_utf8(URUIDocumentHandle document_handle, const char* id, char* out, int capacity, int* required)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!id || !*id || capacity < 0 || !required || (!out && capacity > 0))
        {
            return -1;
        }
        *required = 0;
        if (!document)
        {
            return 0;
        }
        auto* el = document->GetElementById(id);
        if (!el)
        {
            return 0;
        }
        URUI::String value;
        if (el->GetNumChildren() == 1 && el->GetChild(0)->GetTagName() == "#text")
        {
            value = static_cast<URUI::ElementText*>(el->GetChild(0))->GetText();
        }
        else if (el->GetNumChildren() != 0)
        {
            return -2;
        }
        if (value.size() >= static_cast<size_t>(INT32_MAX))
        {
            return -1;
        }
        *required = static_cast<int>(value.size() + 1);
        if (capacity < *required || !out)
        {
            return -3;
        }
        std::memcpy(out, value.c_str(), value.size() + 1);
        return 1;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_text_utf8 failed");
        return -8;
    }
    void urui_set_text(URUIDocumentHandle document_handle, const char* id, const char* value)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!document || !id || !elementReferences.Owner() || URUI::Interaction::callbackDepth)
        {
            return;
        }
        auto* el = document->GetElementById(id);
        if (!el)
        {
            return;
        }
        const char* text = value ? value : "";
        if (el->GetNumChildren() == 1 && el->GetChild(0)->GetTagName() == "#text")
        {
            static_cast<URUI::ElementText*>(el->GetChild(0))->SetText(text);
            return;
        }
        auto node = document->CreateTextNode(text);
        if (!node)
        {
            return;
        }
        while (auto* child = el->GetFirstChild())
        {
            el->RemoveChild(child);
        }
        if (*text)
        {
            el->AppendChild(std::move(node));
        }
        elementReferences.Prune();
    }
    catch (...)
    {
    }
    float urui_get_scroll_top(URUIDocumentHandle document_handle, const char* id)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!document || !id)
        {
            return -1.f;
        }
        if (auto* el = document->GetElementById(id))
        {
            return el->GetScrollTop();
        }
        return -1.f;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_get_scroll_top failed");
        return 0;
    }
    int urui_set_pixel_style(URUIDocumentHandle document_handle, const char* id, const char* property, float pixels)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!document || !id || !property || !std::isfinite(pixels))
        {
            return 0;
        }
        if (auto* el = document->GetElementById(id))
        {
            return el->SetProperty(property, std::to_string(pixels) + "px") ? 1 : 0;
        }
        return 0;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_set_pixel_style failed");
        return -8;
    }
    int urui_element_count(URUIDocumentHandle document_handle)
    {
        auto* document = URUI::Elements::Document(document_handle);
        return CountElements(document);
    }
    int urui_command_count(void)
    {
        return render ? static_cast<int>(render->commands.size()) : 0;
    }
    int urui_get_commands(URUICommand* out, int cap)
    {
        URUI_DEBUG_CODE(ProfileScope timer(profile.command_copy_ns);)
        if (!render || !out || cap < 0)
        {
            return 0;
        }
        int n = std::min(cap, (int)render->commands.size());
        std::copy_n(render->commands.begin(), n, out);
        return n;
    }
    int urui_get_paint_commands(URUIPaintCommand* out, int capacity)
    {
        if (contextThread != std::this_thread::get_id() || URUI::Interaction::callbackDepth || capacity < 0 ||
            (!out && capacity))
        {
            return -1;
        }
        if (!render)
        {
            return 0;
        }
        if (!out)
        {
            return static_cast<int>(render->paintCommands.size());
        }
        int count = 0;
        for (const auto& command : render->paintCommands)
        {
            if (count == capacity)
            {
                break;
            }
            const auto shader = render->paintShaders.find(command.shader);
            if (shader == render->paintShaders.end() || !shader->second.element)
            {
                continue;
            }
            const auto token =
                elementReferences.Track(shader->second.element.get(), shader->second.element->GetOwnerDocument());
            if (token)
            {
                out[count++] = {command.shader, token, command.command_index, shader->second.kind};
            }
        }
        return count;
    }
    int urui_paint_color_filters(uint64_t shader, URUIColorFilter* out, int capacity)
    {
        if (contextThread != std::this_thread::get_id() || URUI::Interaction::callbackDepth || capacity < 0 ||
            (!out && capacity) || !render)
        {
            return -1;
        }
        const auto found = render->paintShaders.find(shader);
        if (found == render->paintShaders.end() || found->second.kind != 2 || !found->second.element ||
            !documentRegistry || !documentRegistry->HandleOf(found->second.element->GetOwnerDocument()))
        {
            return -2;
        }
        const auto& filters = found->second.colorFilters;
        if (!out)
        {
            return static_cast<int>(filters.size());
        }
        if (capacity < static_cast<int>(filters.size()))
        {
            return -3;
        }
        std::copy(filters.begin(), filters.end(), out);
        return static_cast<int>(filters.size());
    }
    int urui_set_direct_color_filters(int enabled)
    {
        if (enabled < 0 || enabled > 1 || contextThread != std::this_thread::get_id() ||
            URUI::Interaction::callbackDepth)
        {
            return -1;
        }
        if (!context || !documentRegistry)
        {
            return -2;
        }
        if (documentRegistry->Count() != 0)
        {
            return -5;
        }
        URUI::DirectColorFilterRenderingEnabled = enabled != 0;
        return 1;
    }
    int urui_get_layer_commands(URUILayerCommand* out, int capacity)
    {
        if (contextThread != std::this_thread::get_id() || URUI::Interaction::callbackDepth || capacity < 0 ||
            (!out && capacity))
        {
            return -1;
        }
        if (!render)
        {
            return 0;
        }
        if (!render->layerValid)
        {
            error_message = "Invalid or unsupported layer/filter command stream";
            return -2;
        }
        if (!out)
        {
            return static_cast<int>(render->layerCommands.size());
        }
        const int count = std::min(capacity, static_cast<int>(render->layerCommands.size()));
        std::copy_n(render->layerCommands.begin(), count, out);
        return count;
    }
    int urui_paint_shader_name(uint64_t shader, char* out, int capacity)
    {
        if (contextThread != std::this_thread::get_id() || URUI::Interaction::callbackDepth || capacity < 0 ||
            (!out && capacity))
        {
            return -1;
        }
        if (out && capacity)
        {
            out[0] = '\0';
        }
        if (!render)
        {
            return 0;
        }
        const auto found = render->paintShaders.find(shader);
        if (found == render->paintShaders.end() || !found->second.element)
        {
            return 0;
        }
        const auto& name = found->second.name;
        const int required = static_cast<int>(name.size() + 1);
        if (out && capacity >= required)
        {
            std::memcpy(out, name.c_str(), required);
        }
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
        {
            return 0;
        }
        auto& g = render->meshes.at(id);
        if (vc)
        {
            *vc = (int)g.vertices.size();
        }
        if (ic)
        {
            *ic = (int)g.indices.size();
        }
        return 1;
    }
    int urui_copy_geometry(uint64_t id, URUIVertex* v, int vc, int* i, int ic)
    {
        URUI_DEBUG_CODE(ProfileScope timer(profile.geometry_copy_ns);)
        if (!render || !render->meshes.count(id) || !v || !i)
        {
            return 0;
        }
        auto& g = render->meshes.at(id);
        if (vc < (int)g.vertices.size() || ic < (int)g.indices.size())
        {
            return 0;
        }
        std::copy(g.vertices.begin(), g.vertices.end(), v);
        std::copy(g.indices.begin(), g.indices.end(), i);
        return 1;
    }
    int urui_get_texture_kind(uint64_t id)
    {
        if (!render)
        {
            return 0;
        }
        auto found = render->textures.find(id);
        return found == render->textures.end() ? 0 : found->second.borrowed ? 2 : 1;
    }
    int urui_get_texture_info(uint64_t id, int* w, int* h, int* bytes)
    {
        if (!render || !render->textures.count(id))
        {
            return 0;
        }
        auto& t = render->textures.at(id);
        if (w)
        {
            *w = t.width;
        }
        if (h)
        {
            *h = t.height;
        }
        if (bytes)
        {
            *bytes = (int)t.pixels.size();
        }
        return 1;
    }
    int urui_copy_texture(uint64_t id, uint8_t* out, int cap)
    {
        if (!render || !render->textures.count(id))
        {
            return 0;
        }
        auto& texture = render->textures.at(id);
        if (texture.borrowed)
        {
            error_message = "Cannot copy pixels from a borrowed external texture";
            return -2;
        }
        if (!out)
        {
            return 0;
        }
        auto& p = texture.pixels;
        if (cap < (int)p.size())
        {
            return 0;
        }
        std::copy(p.begin(), p.end(), out);
        return 1;
    }
    int urui_pop_event(URUIDocumentHandle document_handle, char* out, int cap)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!document || !out)
        {
            return 0;
        }
        for (auto it = clicks.begin(); it != clicks.end();)
        {
            if (!*it)
            {
                it = clicks.erase(it);
                continue;
            }
            if ((*it)->GetOwnerDocument() != document)
            {
                ++it;
                continue;
            }
            auto value = ClickId(*it);
            if (cap < (int)value.size() + 1)
            {
                return -(int)value.size() - 1;
            }
            std::memcpy(out, value.c_str(), value.size() + 1);
            clicks.erase(it);
            return (int)value.size();
        }
        return 0;
    }
    catch (...)
    {
        ReportBridgeFailure("urui_pop_event failed");
        return -8;
    }
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
