#include "urui.h"
#include "element_references.h"
#include "render_plan.h"
#include "prepared_batch.h"
#include "core_frame.h"
#include "native_frame.h"
#include <RmlUi/Core.h>
#include <RmlUi/Core/ElementText.h>
#include <RmlUi/Core/Transform.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <deque>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>
#include <mutex>
#if defined(URUI_DEBUG)
#include "Debug/diagnostics.h"
using namespace urui_debug;
#endif

namespace
{
    static_assert(sizeof(UruiVertex) == 20, "UruiVertex ABI changed");
    static_assert(sizeof(UruiCommand) == 48, "UruiCommand ABI changed");
    struct Geometry
    {
        std::vector<UruiVertex> vertices;
        std::vector<int> indices;
    };
    struct Texture
    {
        int width = 0, height = 0;
        bool registered = false;
        std::vector<unsigned char> pixels;
    };
    std::string error_message;
    std::deque<Rml::ObserverPtr<Rml::Element>> clicks;
    std::unordered_map<uint64_t, Rml::Vector2f> active_touches;
    RenderSerial render_serial;
    std::thread::id context_thread;
    // Sessions retain immutable CPU payloads until their last lease is released.
    // This mutex protects registry access; it does not grant foreign-thread ownership.
    std::unordered_map<uint64_t, std::unique_ptr<urui_prepared::Session>> prepared_sessions;
    std::unordered_map<uint64_t, std::unique_ptr<urui_frame::Session>> frame_sessions;
    std::mutex frame_sessions_mutex;
    std::thread::id frame_context_owner;
    class Renderer : public Rml::RenderInterface
    {
    public:
        RenderPlan plan{render_serial};
        std::unordered_map<uint64_t, Geometry> meshes;
        std::unordered_map<uint64_t, Texture> textures;
        std::unordered_map<std::string, uint64_t> named_textures;
        std::vector<UruiCommand> commands;
        std::vector<std::array<float, 16>> command_transforms;
        Rml::Matrix4f active_transform = Rml::Matrix4f::Identity();
        uint64_t next_mesh = 1, next_texture = 1;
        bool clipped = false;
        Rml::Rectanglei clip = {};
        Rml::CompiledGeometryHandle CompileGeometry(Rml::Span<const Rml::Vertex> vs, Rml::Span<const int> ids) override
        {
            URUI_DEBUG_CODE(ProfileScope timer(profile.geometry_compile_ns);)
            URUI_DEBUG_CODE(++profile.geometry_compiles;)
            URUI_DEBUG_CODE(profile.geometry_vertices += static_cast<uint32_t>(vs.size());)
            URUI_DEBUG_CODE(profile.geometry_indices += static_cast<uint32_t>(ids.size());)
            uint64_t handle = next_mesh++;
            Geometry g;
            g.vertices.reserve(vs.size());
            for (auto& v : vs)
                g.vertices.push_back({v.position.x, v.position.y, v.tex_coord.x, v.tex_coord.y, v.colour.red,
                                      v.colour.green, v.colour.blue, v.colour.alpha});
            g.indices.assign(ids.begin(), ids.end());
            meshes.emplace(handle, std::move(g));
            plan.ResourceChanged();
            return static_cast<Rml::CompiledGeometryHandle>(handle);
        }
        void RenderGeometry(Rml::CompiledGeometryHandle geo, Rml::Vector2f move, Rml::TextureHandle tex) override
        {
            UruiCommand c{};
            c.geometry = static_cast<uint64_t>(geo);
            c.texture = static_cast<uint64_t>(tex);
            c.tx = move.x;
            c.ty = move.y;
            c.clip_enabled = clipped ? 1 : 0;
            if (clipped)
            {
                c.clip_x = clip.Left();
                c.clip_y = clip.Top();
                c.clip_w = clip.Width();
                c.clip_h = clip.Height();
            }
            commands.push_back(c);
            std::array<float, 16> matrix{};
            for (int col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                    matrix[col * 4 + row] = active_transform[col][row];
            command_transforms.push_back(matrix);
        }
        void ReleaseGeometry(Rml::CompiledGeometryHandle h) override
        {
            if (meshes.erase(static_cast<uint64_t>(h)))
                plan.ResourceChanged();
        }
        Rml::TextureHandle LoadTexture(Rml::Vector2i& dims, const Rml::String& source) override
        {
            auto found = named_textures.find(source);
            if (found == named_textures.end())
            {
                dims = {0, 0};
                error_message = "Texture not registered: " + source;
                return 0;
            }
            auto texture = textures.find(found->second);
            if (texture == textures.end())
            {
                dims = {0, 0};
                error_message = "Registered texture missing: " + source;
                return 0;
            }
            dims = {texture->second.width, texture->second.height};
            return static_cast<Rml::TextureHandle>(found->second);
        }
        Rml::TextureHandle GenerateTexture(Rml::Span<const Rml::byte> pixels, Rml::Vector2i dims) override
        {
            if (dims.x <= 0 || dims.y <= 0)
                return 0;
            uint64_t id = next_texture++;
            Texture t;
            t.width = dims.x;
            t.height = dims.y;
            t.pixels.assign(pixels.begin(), pixels.end());
            textures.emplace(id, std::move(t));
            plan.ResourceChanged();
            return static_cast<Rml::TextureHandle>(id);
        }
        void ReleaseTexture(Rml::TextureHandle id) override
        {
            auto found = textures.find(static_cast<uint64_t>(id));
            if (found != textures.end() && !found->second.registered)
            {
                textures.erase(found);
                plan.ResourceChanged();
            }
        }
        void EnableClipMask(bool enable) override
        {
            if (enable)
                plan.Unsupported();
            Rml::RenderInterface::EnableClipMask(enable);
        }
        void RenderToClipMask(Rml::ClipMaskOperation op, Rml::CompiledGeometryHandle g, Rml::Vector2f t) override
        {
            plan.Unsupported();
            Rml::RenderInterface::RenderToClipMask(op, g, t);
        }
        Rml::LayerHandle PushLayer() override
        {
            plan.Unsupported();
            return Rml::RenderInterface::PushLayer();
        }
        void CompositeLayers(Rml::LayerHandle a, Rml::LayerHandle b, Rml::BlendMode mode,
                             Rml::Span<const Rml::CompiledFilterHandle> filters) override
        {
            plan.Unsupported();
            Rml::RenderInterface::CompositeLayers(a, b, mode, filters);
        }
        void PopLayer() override
        {
            plan.Unsupported();
            Rml::RenderInterface::PopLayer();
        }
        Rml::TextureHandle SaveLayerAsTexture() override
        {
            plan.Unsupported();
            return Rml::RenderInterface::SaveLayerAsTexture();
        }
        Rml::CompiledFilterHandle SaveLayerAsMaskImage() override
        {
            plan.Unsupported();
            return Rml::RenderInterface::SaveLayerAsMaskImage();
        }
        Rml::CompiledFilterHandle CompileFilter(const Rml::String& name, const Rml::Dictionary& p) override
        {
            plan.Unsupported();
            return Rml::RenderInterface::CompileFilter(name, p);
        }
        void ReleaseFilter(Rml::CompiledFilterHandle h) override
        {
            plan.Unsupported();
            Rml::RenderInterface::ReleaseFilter(h);
        }
        Rml::CompiledShaderHandle CompileShader(const Rml::String& name, const Rml::Dictionary& p) override
        {
            plan.Unsupported();
            return Rml::RenderInterface::CompileShader(name, p);
        }
        void RenderShader(Rml::CompiledShaderHandle s, Rml::CompiledGeometryHandle g, Rml::Vector2f t,
                          Rml::TextureHandle tex) override
        {
            plan.Unsupported();
            Rml::RenderInterface::RenderShader(s, g, t, tex);
        }
        void ReleaseShader(Rml::CompiledShaderHandle h) override
        {
            plan.Unsupported();
            Rml::RenderInterface::ReleaseShader(h);
        }
        void EnableScissorRegion(bool value) override
        {
            clipped = value;
        }
        void SetScissorRegion(Rml::Rectanglei value) override
        {
            clip = value;
        }
        void SetTransform(const Rml::Matrix4f* matrix) override
        {
            active_transform = matrix ? *matrix : Rml::Matrix4f::Identity();
        }
    };
    class Listener : public Rml::EventListener
    {
        void ProcessEvent(Rml::Event& event) override
        {
            auto* target = event.GetTargetElement();
            if (target && clicks.size() < 4096)
                clicks.push_back(target->GetObserverPtr());
        }
    };
    Rml::String ClickId(const Rml::ObserverPtr<Rml::Element>& value)
    {
        for (auto* el = value.get(); el; el = el->GetParentNode())
            if (!el->GetId().empty())
                return el->GetId();
        return {};
    }
    std::unique_ptr<Renderer> render;
    std::unique_ptr<Listener> listener;
    Rml::Context* context = nullptr;
    Rml::ElementDocument* document = nullptr;
    bool started = false;
    ElementReferences element_references;
    int count_elements(Rml::Element* root)
    {
        if (!root)
            return 0;
        int n = 1;
        for (int i = 0; i < root->GetNumChildren(); ++i)
            n += count_elements(root->GetChild(i));
        return n;
    }
    std::string safe_rml(const char* src)
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

    inline int ApplyTypedTransform(Rml::Element* el, float x, float y, float sx, float sy)
    {
        // Production keeps the property temporary scoped to SetProperty.
        URUI_DEBUG_CODE(if (typed_probe_mode == 1) return ApplyTypedTransformTimed(el, x, y, sx, sy);)
        Rml::Transform::PrimitiveList primitives;
        primitives.reserve(2);
        primitives.emplace_back(Rml::Transforms::Translate2D(x, y, Rml::Unit::PX));
        primitives.emplace_back(Rml::Transforms::Scale2D(sx, sy));
        if (!el->SetProperty(Rml::PropertyId::Transform, Rml::Transform::MakeProperty(std::move(primitives))))
        {
            error_message = "RmlUi rejected typed transform";
            return -2;
        }
        error_message.clear();
        return 1;
    }

} // namespace
extern "C"
{
    int urui_bridge_abi_version(void)
    {
        return 3;
    }
    int urui_create(int w, int h, const char* font)
    {
        if (started)
            return 0;
        context_thread = std::this_thread::get_id();
        {
            std::lock_guard<std::mutex> guard(frame_sessions_mutex);
            frame_context_owner = context_thread;
        }
        URUI_DEBUG_CODE(profile = {};)
        error_message.clear();
        render = std::make_unique<Renderer>();
        listener = std::make_unique<Listener>();
        Rml::SetRenderInterface(render.get());
        if (!Rml::Initialise())
        {
            error_message = "RmlUi Initialise failed";
            listener.reset();
            render.reset();
            return 0;
        }
        started = true;
        if (font && *font && !Rml::LoadFontFace(font))
            error_message = "Cannot load font file";
        context = Rml::CreateContext("urui", {std::max(1, w), std::max(1, h)});
        if (!context)
        {
            error_message = "Cannot create RmlUi context";
            urui_destroy();
            return 0;
        }
        element_references.Begin();
        return 1;
    }
    void urui_destroy(void)
    {
        urui_unload_document();
        if (context)
        {
            Rml::RemoveContext("urui");
            context = nullptr;
        }
        if (started)
        {
            Rml::Shutdown();
            started = false;
        }
        clicks.clear();
        listener.reset();
        render.reset();
        URUI_DEBUG_CODE(typed_probe = {}; typed_probe_mode = 0;)
#if defined(URUI_DEBUG)
        urui_lookup::inline_enabled = false;
        urui_lookup::profile_mode = 0;
        urui_lookup::style_scope = false;
        urui_lookup::profile = {};
#endif
    }
    void urui_unload_document(void)
    {
        element_references.Clear();
        URUI_DEBUG_CODE(typed_probe = {};)
        if (render)
            render->plan.NewDocument();
        if (context && !active_touches.empty())
        {
            context->ProcessMouseLeave();
            for (const auto& entry : active_touches)
            {
                Rml::TouchList cancelled{{static_cast<Rml::TouchId>(entry.first), entry.second}};
                context->ProcessTouchCancel(cancelled);
            }
        }
        active_touches.clear();
        if (document)
        {
            document->Close();
            document = nullptr;
        }
        if (render)
            render->commands.clear();
        if (render)
            render->command_transforms.clear();
        clicks.clear();
    }
    int urui_load_document(const char* rml)
    {
        if (!context || !rml)
            return 0;
        urui_unload_document();
        document = context->LoadDocumentFromMemory(rml, "demo.rml");
        if (!document)
        {
            error_message = "LoadDocumentFromMemory failed";
            return 0;
        }
        document->AddEventListener("click", listener.get());
        document->Show();
        element_references.DocumentLoaded();
        return 1;
    }
    int urui_register_rgba_texture(const char* source, int width, int height, const uint8_t* rgba, int byte_count,
                                   uint64_t* handle)
    {
        if (!render || !source || !*source || !rgba || !handle || width <= 0 || height <= 0 || width > 8192 ||
            height > 8192 || static_cast<int64_t>(width) * height * 4 != byte_count)
        {
            error_message = "Invalid RGBA texture registration";
            return -1;
        }
        if (document)
        {
            error_message = "Unload the document before registering textures";
            return -2;
        }
        Rml::String normalized;
        Rml::GetSystemInterface()->JoinPath(normalized, "demo.rml", source);
        if (normalized.empty())
        {
            error_message = "Invalid texture source";
            return -1;
        }
        auto old = render->named_textures.find(normalized);
        if (old != render->named_textures.end())
        {
            Rml::ReleaseTexture(normalized, render.get());
            render->textures.erase(old->second);
        }
        uint64_t id = render->next_texture++;
        Texture t;
        t.width = width;
        t.height = height;
        t.registered = true;
        t.pixels.resize(static_cast<size_t>(byte_count));
        for (int i = 0; i < byte_count; i += 4)
        {
            uint8_t a = rgba[i + 3];
            t.pixels[i] = static_cast<uint8_t>((static_cast<int>(rgba[i]) * a + 127) / 255);
            t.pixels[i + 1] = static_cast<uint8_t>((static_cast<int>(rgba[i + 1]) * a + 127) / 255);
            t.pixels[i + 2] = static_cast<uint8_t>((static_cast<int>(rgba[i + 2]) * a + 127) / 255);
            t.pixels[i + 3] = a;
        }
        render->textures.emplace(id, std::move(t));
        render->named_textures[normalized] = id;
        render->plan.ResourceChanged();
        *handle = id;
        error_message.clear();
        return 1;
    }
    int urui_unregister_texture(const char* source)
    {
        if (!render || !source || !*source)
        {
            error_message = "Invalid texture source";
            return -1;
        }
        if (document)
        {
            error_message = "Unload the document before unregistering textures";
            return -2;
        }
        Rml::String normalized;
        Rml::GetSystemInterface()->JoinPath(normalized, "demo.rml", source);
        auto found = render->named_textures.find(normalized);
        if (found == render->named_textures.end())
        {
            error_message = "Texture not registered";
            return 0;
        }
        Rml::ReleaseTexture(normalized, render.get());
        render->textures.erase(found->second);
        render->named_textures.erase(found);
        render->plan.ResourceChanged();
        error_message.clear();
        return 1;
    }

    void urui_resize(int w, int h)
    {
        if (context)
            context->SetDimensions({std::max(1, w), std::max(1, h)});
    }
    void urui_mouse_move(int x, int y)
    {
        if (context)
            context->ProcessMouseMove(x, y, 0);
    }
    void urui_mouse_button(int b, int down)
    {
        if (context)
        {
            if (down)
                context->ProcessMouseButtonDown(b, 0);
            else
                context->ProcessMouseButtonUp(b, 0);
        }
    }
    void urui_mouse_wheel(float d)
    {
        if (context)
            context->ProcessMouseWheel(Rml::Vector2f(0.f, d), 0);
    }
    int urui_touch_event(int phase, uint64_t pointer_id, float x, float y)
    {
        if (!context || !document || phase < 0 || phase > 3 || !std::isfinite(x) || !std::isfinite(y) ||
            pointer_id > static_cast<uint64_t>(UINTPTR_MAX))
            return -1;
        Rml::Touch touch{static_cast<Rml::TouchId>(pointer_id), {x, y}};
        Rml::TouchList list{touch};
        if (phase == 0)
        {
            if (active_touches.count(pointer_id))
                return 0;
            // RmlUi 6.3 maps all touches onto one mouse button. Reject concurrent
            // fingers instead of producing cross-finger clicks or invalid active chains.
            if (!active_touches.empty())
                return -2;
            active_touches.emplace(pointer_id, touch.position);
            context->ProcessTouchStart(list, 0);
            return 1;
        }
        auto found = active_touches.find(pointer_id);
        if (found == active_touches.end())
            return 0;
        if (phase == 1)
        {
            found->second = touch.position;
            context->ProcessTouchMove(list, 0);
            return 1;
        }
        if (phase == 2)
        {
            context->ProcessTouchEnd(list, 0);
            active_touches.erase(found);
            return 1;
        }
        // RmlUi 6.3 implements cancel via mouse-button-up. Clear hover first so
        // cancelling a finger cannot dispatch Click to the previously active target.
        context->ProcessMouseLeave();
        context->ProcessTouchCancel(list);
        active_touches.erase(found);
        return 1;
    }
    int urui_hit_test_id(float x, float y, char* out, int capacity)
    {
        if (!context || !document || !std::isfinite(x) || !std::isfinite(y) || capacity < 0)
            return -1;
        if (out && capacity > 0)
            out[0] = 0;
        auto* el = context->GetElementAtPoint({x, y});
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
    int urui_element_exists(const char* id)
    {
        if (!document || !id || !*id)
            return 0;
        return document->GetElementById(id) ? 1 : 0;
    }
    int urui_get_element_box(const char* id, float* out_six)
    {
        if (!document || !id || !*id || !out_six)
            return -1;
        auto* el = document->GetElementById(id);
        if (!el)
            return 0;
        auto offset = el->GetAbsoluteOffset(Rml::BoxArea::Border);
        auto size = el->GetBox().GetSize(Rml::BoxArea::Border);
        out_six[0] = offset.x;
        out_six[1] = offset.y;
        out_six[2] = size.x;
        out_six[3] = size.y;
        out_six[4] = el->GetClientWidth();
        out_six[5] = el->GetScrollWidth();
        return 1;
    }
    int urui_set_attribute(const char* id, const char* name, const char* value)
    {
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
        el->SetAttribute(name, Rml::String(value));
        error_message.clear();
        return 1;
    }
    int urui_set_style(const char* id, const char* name, const char* value)
    {
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
        Rml::Element* el = nullptr;
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
            error_message = "RmlUi rejected property: " + std::string(name);
            return -2;
        }
        error_message.clear();
        return 1;
    }
    int urui_set_translate_scale_2d(const char* id, float x, float y, float sx, float sy)
    {
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
        Rml::Element* el;
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

    uint64_t urui_element_ref_acquire(const char* id)
    {
        return element_references.Acquire(document, id);
    }
    int urui_element_ref_valid(uint64_t token)
    {
        if (!element_references.Owner())
            return -3;
        return element_references.Resolve(token, document) ? 1 : 0;
    }
    int urui_element_ref_release(uint64_t token)
    {
        return element_references.Release(token);
    }
    int urui_element_ref_count(void)
    {
        return element_references.Count();
    }
    int urui_element_ref_set_translate_scale_2d(uint64_t token, float x, float y, float sx, float sy)
    {
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
        Rml::Element* el;
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

    int urui_set_inner_rml(const char* id, const char* markup)
    {
        if (!id || !*id || !markup)
        {
            error_message = "Invalid inner RML mutation";
            return -1;
        }
        if (!document)
            return 0;
        auto* el = document->GetElementById(id);
        if (!el)
            return 0;
        el->SetInnerRML(markup);
        error_message.clear();
        return 1;
    }
    int urui_append_element(const char* parent_id, const char* tag, const char* id)
    {
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
            error_message = "RmlUi rejected element tag";
            return -2;
        }
        if (*id)
            child->SetId(id);
        if (!parent->AppendChild(std::move(child)))
        {
            error_message = "RmlUi rejected append";
            return -2;
        }
        error_message.clear();
        return 1;
    }
    // General DOM sibling ordering API used by atomic UI and ordinary managed clients.
    // Ownership stays in RmlUi; moving the same Element preserves its observer identity.
    int urui_get_child_count(const char* id)
    {
        if (!document || !id || !*id)
            return -1;
        auto* el = document->GetElementById(id);
        return el ? el->GetNumChildren() : -1;
    }
    int urui_get_child_id_utf8(const char* id, int index, char* out, int capacity)
    {
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
    int urui_reorder_element_to_end(const char* id)
    {
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
            error_message = "RmlUi rejected reorder detach";
            return -2;
        }
        if (!parent->AppendChild(std::move(owned)))
        {
            error_message = "RmlUi rejected reorder append";
            return -2;
        }
        error_message.clear();
        return 1;
    }
    int urui_remove_element(const char* id)
    {
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
            error_message = "RmlUi rejected remove";
            return -2;
        }
        error_message.clear();
        return 1;
    }
    void urui_update(void)
    {
        URUI_DEBUG_CODE(ProfileScope timer(profile.update_ns);)
        if (context)
            context->Update();
    }
    void urui_render(void)
    {
        URUI_DEBUG_CODE(ProfileScope timer(profile.render_ns);)
        if (!context || !render)
            return;
        render->plan.Begin(render->commands, render->command_transforms);
        render->clipped = false;
        render->active_transform = Rml::Matrix4f::Identity();
        context->Render();
        render->plan.Finish(render->commands, render->command_transforms);
    }
    int urui_render_plan_version()
    {
        return 1;
    }
    int urui_render_plan_enable(int enabled)
    {
        if (!render || (enabled != 0 && enabled != 1))
            return 0;
        render->plan.Enable(enabled != 0);
        return 1;
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
    int urui_get_attribute_utf8(const char* id, const char* name, char* out, int capacity, int* required)
    {
        if (!id || !*id || !name || !*name || capacity < 0 || !required || (!out && capacity > 0))
            return -1;
        *required = 0;
        if (!document)
            return 0;
        auto* el = document->GetElementById(id);
        if (!el || !el->HasAttribute(name))
            return 0;
        const auto value = el->GetAttribute<Rml::String>(name, "");
        if (value.size() >= size_t(INT32_MAX))
            return -1;
        *required = int(value.size() + 1);
        if (!out || capacity < *required)
            return -3;
        std::memcpy(out, value.c_str(), value.size() + 1);
        return 1;
    }
    int urui_get_text_utf8(const char* id, char* out, int capacity, int* required)
    {
        if (!id || !*id || capacity < 0 || !required || (!out && capacity > 0))
            return -1;
        *required = 0;
        if (!document)
            return 0;
        auto* el = document->GetElementById(id);
        if (!el)
            return 0;
        Rml::String value;
        if (el->GetNumChildren() == 1 && el->GetChild(0)->GetTagName() == "#text")
            value = static_cast<Rml::ElementText*>(el->GetChild(0))->GetText();
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
    void urui_set_text(const char* id, const char* value)
    {
        URUI_DEBUG_CODE(ProfileScope timer(profile.text_ns);)
        URUI_DEBUG_CODE(++profile.text_calls;)
        if (!document || !id)
            return;
        Rml::Element* el;
        {
            URUI_DEBUG_CODE(ProfileScope lookup(profile.text_lookup_ns);)
            URUI_DEBUG_CODE(++profile.text_lookups;)
            el = document->GetElementById(id);
        }
        if (!el)
            return;
        if (!value || !*value)
        {
            URUI_DEBUG_CODE(ProfileScope operation(profile.text_setinnerrml_ns);)
            URUI_DEBUG_CODE(++profile.text_fallback_calls;)
            el->SetInnerRML("");
            return;
        }
        // Keep the original parser path for entities, markup-like text, data
        // expressions, whitespace-only values, and non-ASCII input. This preserves
        // translation and entity-decoding behavior for arbitrary documents.
        bool plain = false, has_content = false;
        for (const unsigned char* p = reinterpret_cast<const unsigned char*>(value); *p; ++p)
        {
            if (*p >= 128 || std::strchr("&<>{}\r\n\t", *p))
            {
                plain = false;
                break;
            }
            if (*p != ' ')
                has_content = true;
            plain = true;
        }
        if (!plain || !has_content)
        {
            URUI_DEBUG_CODE(ProfileScope operation(profile.text_setinnerrml_ns);)
            URUI_DEBUG_CODE(++profile.text_fallback_calls;)
            el->SetInnerRML(safe_rml(value));
            return;
        }
        // SetInnerRML destroys and recreates the text child on every update. Reuse it
        // when the target contains exactly one plain text node; retain the markup path
        // for mixed or structured content.
        if (el->GetNumChildren() == 1)
        {
            auto* child = el->GetChild(0);
            if (child && child->GetTagName() == "#text")
            {
                URUI_DEBUG_CODE(ProfileScope operation(profile.text_settext_ns);)
                URUI_DEBUG_CODE(++profile.text_fast_calls;)
                static_cast<Rml::ElementText*>(child)->SetText(value);
                return;
            }
        }
        {
            URUI_DEBUG_CODE(ProfileScope operation(profile.text_setinnerrml_ns);)
            URUI_DEBUG_CODE(++profile.text_fallback_calls;)
            el->SetInnerRML(safe_rml(value));
        }
    }
    float urui_get_scroll_top(const char* id)
    {
        if (!document || !id)
            return -1.f;
        if (auto* el = document->GetElementById(id))
            return el->GetScrollTop();
        return -1.f;
    }
    int urui_set_pixel_style(const char* id, const char* property, float pixels)
    {
        if (!document || !id || !property || !std::isfinite(pixels))
            return 0;
        if (auto* el = document->GetElementById(id))
            return el->SetProperty(property, std::to_string(pixels) + "px") ? 1 : 0;
        return 0;
    }
    int urui_element_count(void)
    {
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
        if (!render || !render->textures.count(id) || !out)
            return 0;
        auto& p = render->textures.at(id).pixels;
        if (cap < (int)p.size())
            return 0;
        std::copy(p.begin(), p.end(), out);
        return 1;
    }
    int urui_pop_event(char* out, int cap)
    {
        if (clicks.empty() || !out)
            return 0;
        auto value = ClickId(clicks.front());
        if (cap < (int)value.size() + 1)
            return -(int)value.size() - 1;
        std::memcpy(out, value.c_str(), value.size() + 1);
        int n = (int)value.size();
        clicks.pop_front();
        return n;
    }
    const char* urui_last_error(void)
    {
        return error_message.c_str();
    }
}

extern "C"
{
    int urui_core_frame_api_version()
    {
        return 1;
    }
    int urui_core_frame_copy(const UruiFrameStamp* expected, UruiCoreKey* keys, UruiCoreValue* values, float* worlds,
                             int capacity, UruiCoreSummary* info)
    {
        if (info)
            *info = {};
        if (context_thread != std::this_thread::get_id())
            return -3;
        if (!info || !expected || !render || capacity < 0)
            return -1;
        const auto actual = render->plan.Snapshot(render->commands.size());
        if (actual.flags != 1 || expected->flags != 1 || actual.epoch != expected->epoch ||
            actual.commands != expected->commands || actual.resources != expected->resources ||
            actual.count != expected->count)
            return -2;
        if (actual.count > 65536 || size_t(capacity) < actual.count ||
            render->command_transforms.size() != actual.count || (actual.count && (!keys || !values || !worlds)))
            return -1;
        for (size_t i = 0; i < actual.count; i++)
        {
            urui_core::Prepare(render->commands[i], render->command_transforms[i].data(), keys[i], values[i],
                               worlds + i * 16);
            if (keys[i].mode == 1)
                ++info->ready;
            else
                ++info->fallback;
        }
        info->bytes = uint64_t(actual.count) * (sizeof(UruiCoreKey) + sizeof(UruiCoreValue) + 64);
        return int(actual.count);
    }
    int urui_prepared_api_version()
    {
        return 1;
    }
    uint64_t urui_prepared_create()
    {
        if (!context || context_thread != std::this_thread::get_id() || prepared_sessions.size() >= 8)
            return 0;
        try
        {
            auto value = std::make_unique<urui_prepared::Session>();
            auto id = value->id;
            prepared_sessions.emplace(id, std::move(value));
            return id;
        }
        catch (...)
        {
            return 0;
        }
    }
    int urui_prepared_destroy(uint64_t id)
    {
        if (context_thread != std::this_thread::get_id())
            return -3;
        auto it = prepared_sessions.find(id);
        if (it == prepared_sessions.end())
            return -2;
        int status = it->second->Close();
        if (status == 1)
            prepared_sessions.erase(it);
        return status;
    }
    int urui_prepared_acquire(uint64_t id, const UruiFrameStamp* expected, const uint8_t* mask, int count,
                              UruiPreparedPacket* out)
    {
        if (out)
            *out = {};
        if (context_thread != std::this_thread::get_id())
            return -3;
        if (!out || !expected || !render)
            return -1;
        auto it = prepared_sessions.find(id);
        if (it == prepared_sessions.end())
            return -2;
        auto actual = render->plan.Snapshot(render->commands.size());
        if (actual.flags != 1 || expected->flags != 1 || actual.epoch != expected->epoch ||
            actual.commands != expected->commands || actual.resources != expected->resources ||
            actual.count != expected->count)
            return -2;
        if (count < 0 || uint32_t(count) != actual.count || (count && !mask))
            return -1;
        for (int n = 0; n < count; n++)
        {
            const auto& c = render->commands[n];
            if (c.geometry && !render->meshes.count(c.geometry))
                return -2;
            if (c.texture && !render->textures.count(c.texture))
                return -2;
            if (mask[n])
            {
                const auto& m = render->command_transforms[n];
                for (float f : m)
                    if (!std::isfinite(f))
                        return -1;
                if (!std::isfinite(c.tx) || !std::isfinite(c.ty) || m[8] != 0 || m[9] != 0 || m[2] != 0 || m[6] != 0 ||
                    m[10] != 1 || m[14] != 0 || m[3] != 0 || m[7] != 0 || m[11] != 0 || m[15] != 1)
                    return -1;
            }
        }
        try
        {
            return it->second->Acquire(
                actual, render->commands, mask, count,
                [&](uint64_t handle)
                {
                    auto g = render->meshes.find(handle);
                    if (g == render->meshes.end())
                        return urui_prepared::View{};
                    auto& v = g->second;
                    return urui_prepared::View{v.vertices.data(), v.vertices.size(), v.indices.data(),
                                               v.indices.size()};
                },
                *out);
        }
        catch (...)
        {
            *out = {};
            return -8;
        }
    }
    int urui_prepared_copy_draws(uint64_t id, uint64_t lease, UruiPreparedDraw* out, int capacity)
    {
        if (context_thread != std::this_thread::get_id())
            return -3;
        auto it = prepared_sessions.find(id);
        if (it == prepared_sessions.end())
            return -2;
        auto plan = it->second->Get(lease);
        if (!plan)
            return -2;
        if (capacity < 0 || size_t(capacity) < plan->batches.size() || (!out && !plan->batches.empty()))
            return -1;
        for (size_t i = 0; i < plan->batches.size(); i++)
            out[i] = plan->batches[i]->draw;
        return int(plan->batches.size());
    }
    int urui_prepared_copy_batch(uint64_t id, uint64_t lease, uint64_t batch, UruiPreparedVertex* vertices, int vc,
                                 uint32_t* indices, int ic)
    {
        if (context_thread != std::this_thread::get_id())
            return -3;
        auto it = prepared_sessions.find(id);
        if (it == prepared_sessions.end())
            return -2;
        auto plan = it->second->Get(lease);
        if (!plan)
            return -2;
        for (const auto& b : plan->batches)
            if (b->draw.batch == batch)
            {
                if (vc < 0 || ic < 0 || size_t(vc) < b->vertices.size() || size_t(ic) < b->indices.size() ||
                    !vertices || !indices)
                    return -1;
                std::memcpy(vertices, b->vertices.data(), b->vertices.size() * sizeof(*vertices));
                std::memcpy(indices, b->indices.data(), b->indices.size() * sizeof(*indices));
                return 1;
            }
        return -2;
    }
    int urui_prepared_release(uint64_t id, uint64_t lease)
    {
        if (context_thread != std::this_thread::get_id())
            return -3;
        auto it = prepared_sessions.find(id);
        if (it == prepared_sessions.end())
            return -2;
        int status = it->second->Release(lease);
        if (it->second->Closing() && !it->second->Pending())
            prepared_sessions.erase(it);
        return status;
    }
}

extern "C"
{
    int urui_prepared_delta_api_version()
    {
        return 1;
    }
    int urui_prepared_delta_enable(uint64_t id, int enabled)
    {
        if (context_thread != std::this_thread::get_id())
            return -3;
        auto it = prepared_sessions.find(id);
        if (it == prepared_sessions.end())
            return -2;
        return it->second->EnableDelta(enabled);
    }
    int urui_prepared_copy_incremental(uint64_t id, uint64_t lease, uint64_t batch, uint64_t base,
                                       UruiPreparedVertex* vertices, int vc, uint32_t* indices, int ic,
                                       UruiPreparedDeltaInfo* info)
    {
        if (info)
            *info = {};
        if (context_thread != std::this_thread::get_id())
            return -3;
        if (!info)
            return -1;
        auto it = prepared_sessions.find(id);
        if (it == prepared_sessions.end())
            return -2;
        return it->second->CopyDelta(lease, batch, base, vertices, vc, indices, ic, *info);
    }
}

extern "C"
{
    int urui_native_frame_api_version()
    {
        return 1;
    }
    uint64_t urui_native_frame_create()
    {
        return urui_native_frame_create_mode(1);
    }
    uint64_t urui_native_frame_create_mode(uint32_t arithmetic)
    {
        if (arithmetic != 1 && arithmetic != 2)
            return 0;
        std::lock_guard<std::mutex> guard(frame_sessions_mutex);
        if (frame_context_owner != std::this_thread::get_id() || !context || frame_sessions.size() >= 4)
            return 0;
        try
        {
            auto value = std::make_unique<urui_frame::Session>(48u * 1024 * 1024, arithmetic);
            auto id = value->id;
            frame_sessions.emplace(id, std::move(value));
            return id;
        }
        catch (...)
        {
            return 0;
        }
    }
    int urui_native_frame_acquire(uint64_t id, UruiNativeFramePacket* out)
    {
        std::lock_guard<std::mutex> guard(frame_sessions_mutex);
        if (out)
            *out = {};
        if (!out)
            return -1;
        auto it = frame_sessions.find(id);
        if (it == frame_sessions.end())
            return -2;
        if (!it->second->Thread())
            return -3;
        if (frame_context_owner != std::this_thread::get_id() || !render || !context)
            return -2;
        try
        {
            return it->second->Acquire(
                render->plan.Snapshot(render->commands.size()), render->commands, render->command_transforms,
                [&](uint64_t handle)
                {
                    auto g = render->meshes.find(handle);
                    if (g == render->meshes.end())
                        return urui_prepared::View{};
                    auto& v = g->second;
                    return urui_prepared::View{v.vertices.data(), v.vertices.size(), v.indices.data(),
                                               v.indices.size()};
                },
                [&](uint64_t handle) { return render->textures.count(handle) != 0; }, *out);
        }
        catch (...)
        {
            *out = {};
            return -8;
        }
    }
    int urui_native_frame_mesh(uint64_t id, uint64_t lease, uint64_t batch, UruiNativeMeshView* out)
    {
        std::lock_guard<std::mutex> guard(frame_sessions_mutex);
        if (out)
            *out = {};
        if (!out)
            return -1;
        auto it = frame_sessions.find(id);
        if (it == frame_sessions.end())
            return -2;
        if (!it->second->Thread())
            return -3;
        auto f = it->second->Get(lease);
        if (!f)
            return -2;
        for (const auto& b : f->geometry->batches)
            if (b->draw.batch == batch)
            {
                *out = {b->vertices.data(),
                        b->indices.data(),
                        uint32_t(b->vertices.size()),
                        uint32_t(b->indices.size()),
                        32,
                        4};
                return 1;
            }
        return -2;
    }
    int urui_native_frame_release(uint64_t id, uint64_t lease)
    {
        std::lock_guard<std::mutex> guard(frame_sessions_mutex);
        auto it = frame_sessions.find(id);
        if (it == frame_sessions.end())
            return -2;
        int status = it->second->Release(lease);
        if (status == 1 && it->second->Closing() && !it->second->Pending())
            frame_sessions.erase(it);
        return status;
    }
    int urui_native_frame_destroy(uint64_t id)
    {
        std::lock_guard<std::mutex> guard(frame_sessions_mutex);
        auto it = frame_sessions.find(id);
        if (it == frame_sessions.end())
            return -2;
        int status = it->second->Close();
        if (status == 1)
            frame_sessions.erase(it);
        return status;
    }
}

// Arithmetic preparation is a production capability used to match Unity matrix semantics.
extern "C"
{
    int urui_native_frame_prepare_matrix(const float* input, float tx, float ty, float* world, float* parameter)
    {
        return urui_native_frame_prepare_matrix_mode(input, tx, ty, 1, world, parameter);
    }
    int urui_native_frame_prepare_matrix_mode(const float* input, float tx, float ty, uint32_t arithmetic, float* world,
                                              float* parameter)
    {
        if (arithmetic != 1 && arithmetic != 2)
        {
            if (world)
                std::fill(world, world + 16, 0.f);
            if (parameter)
                std::fill(parameter, parameter + 8, 0.f);
            return -1;
        }
        if (!input || !world || !parameter)
            return -1;
        float m[16];
        std::memcpy(m, input, 64);
        urui_frame::Value value{};
        int status = urui_frame::Prepare(m, tx, ty, world, value, arithmetic);
        std::memcpy(parameter, value.data, 32);
        return status;
    }
}

#if defined(URUI_DEBUG)
#include "Debug/bridge_debug.inl"
#endif

#include "elements.inl"
