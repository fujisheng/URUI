#include "bridge_runtime.h"
#include "Engine/Source/Core/DirectColorFilters.h"
#include "Engine/Source/Core/DocumentFontContext.h"
#include <URUI/Core/Plugin.h>
#include <URUI/Core/Transform.h>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstring>
#include <limits>
#include <mutex>
#include <stdexcept>

#if defined(URUI_DEBUG)
#include "Debug/diagnostics.h"
using namespace URUI::Debug;
#endif

namespace URUI::Bridge
{
    namespace
    {
        thread_local SurfaceContext* tlsActiveSurface = nullptr;
        thread_local URUISurfaceHandle tlsCachedHandle = 0;
        thread_local SurfaceContext* tlsCachedSurface = nullptr;
        thread_local std::string tlsLastError;

        std::mutex gRuntimeMutex;
        size_t gEngineUsers = 0;
        URUI::FileInterface* gOriginalFileInterface = nullptr;

        std::mutex gSurfaceMutex;
        std::atomic<uint64_t> gNextSurfaceHandle{1};
        std::unordered_map<URUISurfaceHandle, std::unique_ptr<SurfaceContext>> gSurfaces;
        std::unordered_map<URUI::Context*, SurfaceContext*> gContextToSurface;

        class MultiplexSystemInterface final : public URUI::SystemInterface
        {
        public:
            bool LogMessage(URUI::Log::Type type, const URUI::String& message) override
            {
                auto* surface = tlsActiveSurface;
                if (!surface || !surface->captureDiagnostics)
                {
                    return URUI::SystemInterface::LogMessage(type, message);
                }
                if (type <= URUI::Log::LT_WARNING)
                {
                    surface->diagnostics.push_back(message);
                }
                return true;
            }
            double GetElapsedTime() override
            {
                auto* surface = tlsActiveSurface ? tlsActiveSurface : tlsCachedSurface;
                if (surface && surface->externalTimeEnabled)
                {
                    return surface->externalTime;
                }
                return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
            }
            void SetClipboardText(const URUI::String& value) override
            {
                auto* surface = tlsActiveSurface;
                if (!surface)
                {
                    return;
                }
                if (surface->clipboardVersion == UINT64_MAX)
                {
                    throw std::overflow_error("clipboard version exhausted");
                }
                surface->clipboard = value;
                ++surface->clipboardVersion;
            }
            void GetClipboardText(URUI::String& value) override
            {
                auto* surface = tlsActiveSurface;
                value = surface ? surface->clipboard : URUI::String{};
            }
        };

        class MultiplexFileInterface final : public URUI::FileInterface
        {
            URUI::FileInterface* Active() const noexcept
            {
                auto* surface = tlsActiveSurface;
                if (surface && surface->resourceFiles)
                {
                    return surface->resourceFiles.get();
                }
                return gOriginalFileInterface;
            }

        public:
            URUI::FileHandle Open(const URUI::String& path) override
            {
                auto* target = Active();
                return target ? target->Open(path) : 0;
            }
            void Close(URUI::FileHandle file) override
            {
                if (auto* target = Active())
                {
                    target->Close(file);
                }
            }
            size_t Read(void* buffer, size_t size, URUI::FileHandle file) override
            {
                auto* target = Active();
                return target ? target->Read(buffer, size, file) : 0;
            }
            bool Seek(URUI::FileHandle file, long offset, int origin) override
            {
                auto* target = Active();
                return target ? target->Seek(file, offset, origin) : false;
            }
            size_t Tell(URUI::FileHandle file) override
            {
                auto* target = Active();
                return target ? target->Tell(file) : 0;
            }
            size_t Length(URUI::FileHandle file) override
            {
                auto* target = Active();
                return target ? target->Length(file) : 0;
            }
        };

        class MultiplexPlugin final : public URUI::Plugin
        {
        public:
            int GetEventClasses() override
            {
                return EVT_BASIC | EVT_ELEMENT;
            }
            void OnElementDestroy(URUI::Element* element) override
            {
                if (auto* surface = tlsActiveSurface)
                {
                    surface->elementReferences.OnElementDestroy(element);
                    return;
                }
                if (element)
                {
                    if (auto* ctx = element->GetContext())
                    {
                        std::lock_guard<std::mutex> lock(gSurfaceMutex);
                        const auto found = gContextToSurface.find(ctx);
                        if (found != gContextToSurface.end() && found->second)
                        {
                            found->second->elementReferences.OnElementDestroy(element);
                        }
                    }
                }
            }
        };

        MultiplexSystemInterface gSystemInterface;
        MultiplexFileInterface gFileInterface;
        MultiplexPlugin gPlugin;
        URUI::TextInputHandler gFallbackTextHandler;

        void OnElementTokenTeardown(void* user, URUI::ElementDocument* /*doc*/, uint64_t token) noexcept
        {
            auto* surface = static_cast<SurfaceContext*>(user);
            if (!surface || !token)
            {
                return;
            }
            surface->renderer.EvictEffectRoot(token);
            EvictStyleStoreToken(surface->styles.get(), token);
        }

        void OnDocumentRetire(void* user, uint64_t handle, URUI::ElementDocument* document) noexcept
        {
            auto* surface = static_cast<SurfaceContext*>(user);
            if (!surface)
            {
                return;
            }
            RetireDocumentInteraction(*surface, handle, document);
            ClearStyleStoreDocument(surface->styles.get(), handle, document);
            ClearProgramLayoutsForDocument(surface->programLayouts.get(), handle);
        }
    }

    ContentDigest128 ComputeDigest128(const uint8_t* data, size_t size) noexcept
    {
        uint64_t h1 = 0xcbf29ce484222325ULL;
        uint64_t h2 = 0x100000001b3ULL ^ (uint64_t(size) * 0x9e3779b97f4a7c15ULL);
        for (size_t i = 0; i < size; ++i)
        {
            const uint8_t byte = data[i];
            h1 = (h1 ^ byte) * 0x100000001b3ULL;
            h2 = (h2 ^ uint64_t(byte + 17u)) * 0x9e3779b185ebca87ULL;
            h2 ^= h2 >> 29;
        }
        h1 ^= h1 >> 33;
        h1 *= 0xff51afd7ed558ccdULL;
        h1 ^= h1 >> 33;
        h2 ^= h2 >> 30;
        h2 *= 0xbf58476d1ce4e5b9ULL;
        h2 ^= h2 >> 31;
        return {h1, h2, size};
    }

    ActiveSurfaceScope::ActiveSurfaceScope(SurfaceContext* s) noexcept
        : previousSurface(tlsActiveSurface), previousDirectColor(URUI::DirectColorFilterRenderingEnabled)
    {
        tlsActiveSurface = s;
        URUI::DirectColorFilterRenderingEnabled = s ? s->directColorFiltersEnabled : false;
    }

    ActiveSurfaceScope::~ActiveSurfaceScope() noexcept
    {
        tlsActiveSurface = previousSurface;
        URUI::DirectColorFilterRenderingEnabled = previousDirectColor;
    }

    bool EngineRuntime::Acquire() noexcept
    {
        std::lock_guard<std::mutex> lock(gRuntimeMutex);
        if (gEngineUsers > 0)
        {
            ++gEngineUsers;
            return true;
        }
        bool initialised = false;
        try
        {
            URUI::SetSystemInterface(&gSystemInterface);
            URUI::SetTextInputHandler(&gFallbackTextHandler);
            if (!URUI::Initialise())
            {
                URUI::SetRenderInterface(nullptr);
                URUI::SetFileInterface(nullptr);
                URUI::SetSystemInterface(nullptr);
                URUI::SetTextInputHandler(nullptr);
                SetThreadError("URUI Initialise failed");
                return false;
            }
            initialised = true;
            gOriginalFileInterface = URUI::GetFileInterface();
            URUI::SetFileInterface(&gFileInterface);
            URUI::RegisterPlugin(&gPlugin);
            gEngineUsers = 1;
            return true;
        }
        catch (...)
        {
            if (initialised)
            {
                try
                {
                    URUI::UnregisterPlugin(&gPlugin);
                    URUI::SetFileInterface(gOriginalFileInterface);
                    gOriginalFileInterface = nullptr;
                    URUI::Shutdown();
                }
                catch (...)
                {
                }
            }
            URUI::SetRenderInterface(nullptr);
            URUI::SetFileInterface(nullptr);
            URUI::SetSystemInterface(nullptr);
            URUI::SetTextInputHandler(nullptr);
            SetThreadError("URUI Initialise threw an exception");
            return false;
        }
    }

    void EngineRuntime::Release() noexcept
    {
        std::lock_guard<std::mutex> lock(gRuntimeMutex);
        if (gEngineUsers == 0)
        {
            return;
        }
        --gEngineUsers;
        if (gEngineUsers == 0)
        {
            try
            {
                URUI::UnregisterPlugin(&gPlugin);
                URUI::SetFileInterface(gOriginalFileInterface);
                gOriginalFileInterface = nullptr;
                URUI::Shutdown();
                URUI::SetRenderInterface(nullptr);
                URUI::SetSystemInterface(nullptr);
                URUI::SetFileInterface(nullptr);
                URUI::SetTextInputHandler(nullptr);
            }
            catch (...)
            {
            }
        }
    }

    URUI::FileInterface* EngineRuntime::OriginalFileInterface() noexcept
    {
        return gOriginalFileInterface;
    }

    SurfaceContext* EngineRuntime::ActiveSurface() noexcept
    {
        return tlsActiveSurface;
    }

    void EngineRuntime::SetThreadError(const char* message) noexcept
    {
        try
        {
            tlsLastError = message ? message : "";
        }
        catch (...)
        {
        }
    }

    void EngineRuntime::SetThreadError(const std::string& message) noexcept
    {
        try
        {
            tlsLastError = message;
        }
        catch (...)
        {
        }
    }

    const std::string& EngineRuntime::ThreadError() noexcept
    {
        return tlsLastError;
    }

    int EngineRuntime::CopyString(const std::string& value, char* out, int capacity, int* required) noexcept
    {
        if (!required || capacity < 0 || (!out && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (value.size() >= size_t(INT32_MAX))
        {
            *required = 0;
            return URUI_ERR_BUDGET;
        }
        *required = static_cast<int>(value.size()) + 1;
        if (!out || capacity < *required)
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        std::memcpy(out, value.c_str(), size_t(*required));
        return URUI_OK;
    }

    SurfaceContext::SurfaceContext() : renderer(this), textHandler(this), clickListener(this)
    {
    }

    SurfaceContext::~SurfaceContext() = default;

    void SurfaceContext::SetError(const char* message) noexcept
    {
        try
        {
            lastError = message ? message : "";
            tlsLastError = lastError;
        }
        catch (...)
        {
        }
    }

    void SurfaceContext::SetError(const std::string& message) noexcept
    {
        try
        {
            lastError = message;
            tlsLastError = lastError;
        }
        catch (...)
        {
        }
    }

    PointerState* SurfaceContext::FindPointer(int pointer) noexcept
    {
        for (auto& slot : pointers)
        {
            if (slot.id == pointer)
            {
                return &slot;
            }
        }
        return nullptr;
    }

    PointerState& SurfaceContext::GetOrCreatePointer(int pointer)
    {
        if (auto* existing = FindPointer(pointer))
        {
            return *existing;
        }
        PointerState created{};
        created.id = pointer;
        pointers.push_back(std::move(created));
        return pointers.back();
    }

    void SurfaceContext::ErasePointer(int pointer) noexcept
    {
        for (size_t i = 0; i < pointers.size(); ++i)
        {
            if (pointers[i].id == pointer)
            {
                if (i + 1 < pointers.size())
                {
                    pointers[i] = std::move(pointers.back());
                }
                pointers.pop_back();
                return;
            }
        }
    }

    int LookupSurface(URUISurfaceHandle handle, SurfaceContext*& out, bool requireOwnerThread,
                      bool forbidCallback) noexcept
    {
        out = nullptr;
        if (!handle)
        {
            return URUI_ERR_NOT_READY;
        }
        SurfaceContext* surface = nullptr;
        if (tlsCachedHandle == handle && tlsCachedSurface != nullptr)
        {
            surface = tlsCachedSurface;
        }
        else
        {
            std::lock_guard<std::mutex> lock(gSurfaceMutex);
            const auto found = gSurfaces.find(handle);
            if (found == gSurfaces.end() || !found->second)
            {
                return URUI_ERR_NOT_READY;
            }
            surface = found->second.get();
            if (surface->IsOwnerThread())
            {
                tlsCachedHandle = handle;
                tlsCachedSurface = surface;
            }
        }
        if (requireOwnerThread && !surface->IsOwnerThread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        if (!surface->context || !surface->documentRegistry)
        {
            return URUI_ERR_NOT_READY;
        }
        if (forbidCallback && surface->callbackDepth > 0)
        {
            return URUI_ERR_REENTRANCY;
        }
        out = surface;
        return URUI_OK;
    }

    int LookupDocument(SurfaceContext& surface, URUIDocumentHandle owner, DocumentState*& state, bool requireLoaded,
                       bool mutation) noexcept
    {
        state = nullptr;
        if (!surface.IsOwnerThread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        if (!surface.context || !surface.documentRegistry)
        {
            return URUI_ERR_NOT_READY;
        }
        if (mutation && surface.callbackDepth > 0)
        {
            return URUI_ERR_REENTRANCY;
        }
        if (!owner)
        {
            return URUI_ERR_INVALID_ARG;
        }
        state = surface.documentRegistry->Find(owner);
        if (state && mutation && (state->retiring || state->loading))
        {
            return URUI_ERR_REENTRANCY;
        }
        return state && (!requireLoaded || state->Element()) ? URUI_OK : URUI_ERR_NOT_READY;
    }

    URUI::ElementDocument* ResolveDocument(SurfaceContext& surface, URUIDocumentHandle owner) noexcept
    {
        if (!surface.IsOwnerThread() || !surface.documentRegistry || !owner)
        {
            return nullptr;
        }
        auto* state = surface.documentRegistry->Find(owner);
        return state ? state->Element() : nullptr;
    }

    URUI::Element* ResolveElement(SurfaceContext& surface, URUIDocumentHandle owner, uint64_t token) noexcept
    {
        auto* document = ResolveDocument(surface, owner);
        auto* element = surface.elementReferences.Resolve(token, document);
        if (!element)
        {
            surface.SetError("Invalid element identity or surface thread");
        }
        return element;
    }

    SurfaceContext* FindSurfaceInternal(URUISurfaceHandle handle) noexcept
    {
        std::lock_guard<std::mutex> lock(gSurfaceMutex);
        const auto found = gSurfaces.find(handle);
        return found == gSurfaces.end() ? nullptr : found->second.get();
    }

    size_t ActiveSurfaceCountInternal() noexcept
    {
        std::lock_guard<std::mutex> lock(gSurfaceMutex);
        return gSurfaces.size();
    }

    // ---- SurfaceRenderer implementation ----

    bool SurfaceRenderer::EffectAttached(URUI::Element* element) noexcept
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

    void SurfaceRenderer::PruneEffectRoots()
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

    void SurfaceRenderer::LayerCommand(int kind, uint64_t source, uint64_t target, uint64_t mask, int blend)
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

    uint64_t SurfaceRenderer::AllocateTextureHandle()
    {
        const uint64_t id = AllocateTextureId<URUI::TextureHandle>(nextTexture);
        if (!id)
        {
            if (ownerSurface)
            {
                ownerSurface->SetError("Texture handle space exhausted");
            }
            return 0;
        }
        if (textures.count(id) || borrowedTextureHandles.count(static_cast<URUI::TextureHandle>(id)))
        {
            if (ownerSurface)
            {
                ownerSurface->SetError("Texture handle collision");
            }
            return 0;
        }
        return id;
    }

    uint64_t SurfaceRenderer::LogicalTextureId(URUI::TextureHandle handle) const noexcept
    {
        if (!handle || borrowedTextureHandles.empty())
        {
            return static_cast<uint64_t>(handle);
        }
        const auto found = borrowedTextureHandles.find(handle);
        return found == borrowedTextureHandles.end() ? static_cast<uint64_t>(handle) : found->second;
    }

    void SurfaceRenderer::ReleaseResolved(uint64_t id) noexcept
    {
        if (!imageRelease)
        {
            return;
        }
        const URUISurfaceHandle surfaceHandle = ownerSurface ? ownerSurface->handle : 0;
        if (ownerSurface)
        {
            CallbackDepthGuard depth(*ownerSurface);
            try
            {
                imageRelease(surfaceHandle, id);
            }
            catch (...)
            {
                ownerSurface->SetError("Image release callback failed");
            }
        }
        else
        {
            try
            {
                imageRelease(0, id);
            }
            catch (...)
            {
            }
        }
    }

    void SurfaceRenderer::RemoveFromPixelBucket(uint64_t id, const TextureRecord& tex) noexcept
    {
        if (!tex.scoped || tex.borrowed)
        {
            return;
        }
        const uint64_t key = PixelBucketKey(tex.width, tex.height, tex.pixelDigest.h1);
        auto it = scopedPixelBuckets.find(key);
        if (it == scopedPixelBuckets.end())
        {
            return;
        }
        auto& ids = it->second;
        ids.erase(std::remove(ids.begin(), ids.end(), id), ids.end());
        if (ids.empty())
        {
            scopedPixelBuckets.erase(it);
        }
    }

    void SurfaceRenderer::CollectTexture(uint64_t id)
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
        RemoveFromPixelBucket(id, found->second);
        textures.erase(found);
        MarkResourceChanged();
        if (resolved)
        {
            ReleaseResolved(id);
        }
    }

    void SurfaceRenderer::ReleaseAlias(uint64_t id)
    {
        const auto found = textures.find(id);
        if (found == textures.end() || !found->second.aliases)
        {
            return;
        }
        --found->second.aliases;
        CollectTexture(id);
    }

    uint64_t SurfaceRenderer::RegisterScopedImage(const std::shared_ptr<DocumentResources>& resources,
                                                  const std::string& source, int width, int height,
                                                  const uint8_t* pixels, size_t length, uint64_t external)
    {
        // Hash raw identity without duplicating the full pixel buffer for ClaimImage.
        std::array<uint64_t, 5> header{};
        header[0] = uint64_t(width);
        header[1] = uint64_t(height);
        header[2] = external;
        if (!external)
        {
            const auto rawDigest = ComputeDigest128(pixels, length);
            header[3] = rawDigest.h1;
            header[4] = rawDigest.h2;
        }
        auto claim = resources->ClaimImage(source, reinterpret_cast<const uint8_t*>(header.data()), sizeof(header),
                                           external ? "external-image" : "rgba-image");

        std::vector<uint8_t> premultiplied;
        ContentDigest128 premulDigest{};
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
            premulDigest = ComputeDigest128(premultiplied.data(), premultiplied.size());
        }

        uint64_t id = external;
        if (!external)
        {
            const uint64_t bucketKey = PixelBucketKey(width, height, premulDigest.h1);
            const auto bucket = scopedPixelBuckets.find(bucketKey);
            if (bucket != scopedPixelBuckets.end())
            {
                for (uint64_t candidateId : bucket->second)
                {
                    const auto it = textures.find(candidateId);
                    if (it != textures.end() && it->second.scoped && !it->second.borrowed &&
                        it->second.width == width && it->second.height == height &&
                        it->second.pixelDigest == premulDigest && it->second.pixels == premultiplied)
                    {
                        id = candidateId;
                        break;
                    }
                }
            }
        }

        auto found = textures.find(id);
        if (found != textures.end() && (found->second.borrowed != bool(external) || found->second.resolved ||
                                        found->second.width != width || found->second.height != height))
        {
            throw std::invalid_argument("Texture ID kind or dimensions conflict: " + source);
        }

        bool insertedNew = false;
        URUI::TextureHandle newRenderHandle = 0;
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
            newRenderHandle = static_cast<URUI::TextureHandle>(handle);
            TextureRecord texture;
            texture.width = width;
            texture.height = height;
            texture.scoped = true;
            texture.borrowed = external != 0;
            texture.renderHandle = newRenderHandle;
            texture.pixelDigest = premulDigest;
            if (!external)
            {
                texture.pixels = std::move(premultiplied);
            }
            auto [it, ok] = textures.emplace(id, std::move(texture));
            (void)ok;
            try
            {
                if (external)
                {
                    borrowedTextureHandles.emplace(newRenderHandle, id);
                }
                else
                {
                    scopedPixelBuckets[PixelBucketKey(width, height, premulDigest.h1)].push_back(id);
                }
            }
            catch (...)
            {
                if (external)
                {
                    borrowedTextureHandles.erase(newRenderHandle);
                }
                textures.erase(it);
                throw;
            }
            insertedNew = true;
        }
        auto& texture = textures.at(id);
        if (texture.aliases == SIZE_MAX)
        {
            if (insertedNew)
            {
                CollectTexture(id);
            }
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
        MarkResourceChanged();
        return id;
    }

    URUI::CompiledGeometryHandle SurfaceRenderer::CompileGeometry(URUI::Span<const URUI::Vertex> vs,
                                                                  URUI::Span<const int> ids)
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
        const uint64_t handle = nextMesh++;
        GeometryRecord g;
        g.vertices.reserve(vs.size());
        for (const auto& v : vs)
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
        MarkResourceChanged();
        return static_cast<URUI::CompiledGeometryHandle>(handle);
    }

    void SurfaceRenderer::SetGeometryTextureSampling(URUI::CompiledGeometryHandle geometry,
                                                     URUI::TextureSampling sampling)
    {
        auto found = meshes.find(static_cast<uint64_t>(geometry));
        if (found != meshes.end() && found->second.texture_sampling != sampling)
        {
            found->second.texture_sampling = sampling;
            MarkResourceChanged();
        }
    }

    void SurfaceRenderer::RenderGeometry(URUI::CompiledGeometryHandle geo, URUI::Vector2f move, URUI::TextureHandle tex,
                                         const URUI::DrawParameters& parameters)
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

    void SurfaceRenderer::ReleaseGeometry(URUI::CompiledGeometryHandle h)
    {
        const auto id = static_cast<uint64_t>(h);
        if (meshes.erase(id))
        {
            drawPlan.RetireGeometry(id);
            MarkResourceChanged();
        }
    }

    URUI::TextureHandle SurfaceRenderer::LoadTexture(URUI::Vector2i& dims, const URUI::String& source,
                                                     const URUI::SharedPtr<URUI::ResourceContext>& resources)
    {
        dims = {};
        auto owned = std::dynamic_pointer_cast<DocumentResources>(resources);
        if (!owned)
        {
            if (ownerSurface)
            {
                ownerSurface->SetError("Image resolution requires a document owner");
            }
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
            if (ownerSurface)
            {
                ownerSurface->SetError("Texture not registered: " + source);
            }
            return 0;
        }
        uint64_t id = 0;
        uint64_t source_identity = 0;
        int width = 0;
        int height = 0;
        int ok = 0;
        {
            const URUISurfaceHandle surfaceHandle = ownerSurface ? ownerSurface->handle : 0;
            if (ownerSurface)
            {
                CallbackDepthGuard depth(*ownerSurface);
                try
                {
                    ok = imageResolver(surfaceHandle, owned->GetOwner(), source.c_str(), &id, &source_identity, &width,
                                       &height);
                }
                catch (...)
                {
                    ownerSurface->SetError("Image resolver callback failed");
                    owned->ImageFailed(source);
                    return 0;
                }
            }
            else
            {
                try
                {
                    ok = imageResolver(0, owned->GetOwner(), source.c_str(), &id, &source_identity, &width, &height);
                }
                catch (...)
                {
                    owned->ImageFailed(source);
                    return 0;
                }
            }
        }
        if (ok == URUI_OK)
        {
            URUI::TextureHandle installedHandle = 0;
            bool insertedTexture = false;
            try
            {
                if (!source_identity)
                {
                    throw std::invalid_argument("Resolved image source identity is zero");
                }
                std::array<uint64_t, 3> descriptor{{source_identity, uint64_t(width), uint64_t(height)}};
                auto claim = owned->ClaimImage(source, reinterpret_cast<const uint8_t*>(descriptor.data()),
                                               sizeof(descriptor), "resolved-image");
                if (!(id & BorrowedTextureBit) || textures.count(id) || width <= 0 || height <= 0 || width > 8192 ||
                    height > 8192)
                {
                    throw std::invalid_argument("Invalid resolved texture metadata or reused resource ID");
                }
                const auto handle = AllocateTextureHandle();
                if (!handle)
                {
                    throw std::length_error("Texture handle space exhausted");
                }
                installedHandle = static_cast<URUI::TextureHandle>(handle);
                TextureRecord texture;
                texture.width = width;
                texture.height = height;
                texture.borrowed = texture.resolved = texture.scoped = true;
                texture.renderHandle = installedHandle;
                texture.aliases = 1;
                textures.emplace(id, std::move(texture));
                insertedTexture = true;
                borrowedTextureHandles.emplace(installedHandle, id);
                owned->BindImage(source, id, std::move(claim), [this, id] { ReleaseAlias(id); });
                ++textures.at(id).loads;
                dims = {width, height};
                MarkResourceChanged();
                return installedHandle;
            }
            catch (const std::exception& error)
            {
                if (insertedTexture)
                {
                    borrowedTextureHandles.erase(installedHandle);
                    textures.erase(id);
                }
                if (id)
                {
                    ReleaseResolved(id);
                }
                owned->ImageFailed(source);
                if (ownerSurface)
                {
                    ownerSurface->SetError(error.what());
                }
                return 0;
            }
            catch (...)
            {
                if (insertedTexture)
                {
                    borrowedTextureHandles.erase(installedHandle);
                    textures.erase(id);
                }
                if (id)
                {
                    ReleaseResolved(id);
                }
                owned->ImageFailed(source);
                if (ownerSurface)
                {
                    ownerSurface->SetError("Resolved texture registration failed");
                }
                return 0;
            }
        }
        owned->ImageFailed(source);
        if (ownerSurface)
        {
            ownerSurface->SetError("Texture not registered: " + source);
        }
        return 0;
    }

    URUI::TextureHandle SurfaceRenderer::GenerateTexture(URUI::Span<const URUI::byte> pixels, URUI::Vector2i dims)
    {
        if (dims.x <= 0 || dims.y <= 0)
        {
            return 0;
        }
        const uint64_t id = AllocateTextureHandle();
        if (!id)
        {
            return 0;
        }
        TextureRecord t;
        t.width = dims.x;
        t.height = dims.y;
        t.renderHandle = static_cast<URUI::TextureHandle>(id);
        t.pixels.assign(pixels.begin(), pixels.end());
        textures.emplace(id, std::move(t));
        MarkResourceChanged();
        return static_cast<URUI::TextureHandle>(id);
    }

    void SurfaceRenderer::ReleaseTexture(URUI::TextureHandle id)
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
            MarkResourceChanged();
            if (resolved)
            {
                ReleaseResolved(logical);
            }
        }
    }

    void SurfaceRenderer::EnableClipMask(bool enable)
    {
        recordedContext = nullptr;
        URUI::RenderInterface::EnableClipMask(enable);
    }

    void SurfaceRenderer::RenderToClipMask(URUI::ClipMaskOperation op, URUI::CompiledGeometryHandle g, URUI::Vector2f t)
    {
        recordedContext = nullptr;
        URUI::RenderInterface::RenderToClipMask(op, g, t);
    }

    URUI::LayerHandle SurfaceRenderer::PushLayer()
    {
        recordedContext = nullptr;
        const auto id = AllocateHandleId<URUI::LayerHandle>(nextLayerResource, std::numeric_limits<uint64_t>::max());
        if (!id)
        {
            layerValid = false;
            return 0;
        }
        layerStack.push_back(id);
        LayerCommand(URUI_LAYER_PUSH, 0, id);
        return static_cast<URUI::LayerHandle>(id);
    }

    void SurfaceRenderer::CompositeLayers(URUI::LayerHandle a, URUI::LayerHandle b, URUI::BlendMode mode,
                                          URUI::Span<const URUI::CompiledFilterHandle> appliedFilters)
    {
        recordedContext = nullptr;
        for (auto handle : appliedFilters)
        {
            const auto mask = maskFilters.find(static_cast<uint64_t>(handle));
            if (mask != maskFilters.end())
            {
                LayerCommand(URUI_LAYER_APPLY_MASK, static_cast<uint64_t>(a), static_cast<uint64_t>(a), mask->second,
                             1);
            }
            else if (filters.count(static_cast<uint64_t>(handle)))
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

    void SurfaceRenderer::PopLayer()
    {
        recordedContext = nullptr;
        if (layerStack.size() <= 1)
        {
            layerValid = false;
            return;
        }
        const auto previous = layerStack.back();
        layerStack.pop_back();
        LayerCommand(URUI_LAYER_POP, previous, layerStack.back());
    }

    URUI::TextureHandle SurfaceRenderer::SaveLayerAsTexture()
    {
        recordedContext = nullptr;
        return URUI::RenderInterface::SaveLayerAsTexture();
    }

    URUI::CompiledFilterHandle SurfaceRenderer::SaveLayerAsMaskImage()
    {
        recordedContext = nullptr;
        if (layerStack.size() <= 1)
        {
            layerValid = false;
            return 0;
        }
        const uint64_t savedNextLayer = nextLayerResource;
        const uint64_t savedNextFilter = nextMaskFilter;
        const auto resource =
            AllocateHandleId<URUI::LayerHandle>(nextLayerResource, std::numeric_limits<uint64_t>::max());
        const auto filter =
            AllocateHandleId<URUI::CompiledFilterHandle>(nextMaskFilter, std::numeric_limits<uint64_t>::max());
        if (!resource || !filter)
        {
            nextLayerResource = savedNextLayer;
            nextMaskFilter = savedNextFilter;
            layerValid = false;
            return 0;
        }
        LayerCommand(URUI_LAYER_SAVE_MASK, layerStack.back(), resource);
        maskFilters.emplace(filter, resource);
        return static_cast<URUI::CompiledFilterHandle>(filter);
    }

    URUI::CompiledFilterHandle SurfaceRenderer::CompileFilter(const URUI::String& name, const URUI::Dictionary& p,
                                                              const URUI::SharedPtr<URUI::ResourceContext>& resources)
    {
        recordedContext = nullptr;
        const auto id =
            AllocateHandleId<URUI::CompiledFilterHandle>(nextMaskFilter, std::numeric_limits<uint64_t>::max());
        if (!id)
        {
            layerValid = false;
            return 0;
        }
        FilterRecord filter;
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
            filters.emplace(id, std::move(filter));
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
            const auto v = offset->second.Get<URUI::Vector2f>();
            filter.data.offset_x = v.x;
            filter.data.offset_y = v.y;
        }
        const auto color = p.find("color");
        if (color != p.end())
        {
            const auto c = color->second.Get<URUI::Colourb>();
            filter.data.red = c.red / 255.f;
            filter.data.green = c.green / 255.f;
            filter.data.blue = c.blue / 255.f;
            filter.data.alpha = c.alpha / 255.f;
        }
        filters.emplace(id, std::move(filter));
        return static_cast<URUI::CompiledFilterHandle>(id);
    }

    void SurfaceRenderer::ReleaseFilter(URUI::CompiledFilterHandle h)
    {
        recordedContext = nullptr;
        const auto found = maskFilters.find(static_cast<uint64_t>(h));
        if (found != maskFilters.end())
        {
            try
            {
                LayerCommand(URUI_LAYER_RELEASE_MASK, found->second, 0);
            }
            catch (...)
            {
                layerValid = false;
                if (ownerSurface)
                {
                    ownerSurface->SetError("Cannot record URUI mask release");
                }
            }
            maskFilters.erase(found);
            return;
        }
        if (filters.erase(static_cast<uint64_t>(h)))
        {
            return;
        }
        URUI::RenderInterface::ReleaseFilter(h);
    }

    URUI::CompiledShaderHandle SurfaceRenderer::CompileShader(const URUI::String& name, const URUI::Dictionary& p)
    {
        recordedContext = nullptr;
        const auto value = p.find("value");
        const auto owner = p.find("element");
        if ((name == "shader" || name == "--urui-image-effect" || name == "--urui-css-color") && value != p.end() &&
            owner != p.end())
        {
            auto* element = static_cast<URUI::Element*>(owner->second.Get<void*>());
            auto key = value->second.Get<URUI::String>();
            if (element && !key.empty())
            {
                const auto handle =
                    AllocateHandleId<URUI::CompiledShaderHandle>(nextPaintShader, std::numeric_limits<uint64_t>::max());
                if (!handle)
                {
                    return 0;
                }
                PaintShaderRecord shader{std::move(key),
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

    void SurfaceRenderer::RenderShader(URUI::CompiledShaderHandle s, URUI::CompiledGeometryHandle g, URUI::Vector2f t,
                                       URUI::TextureHandle tex, const URUI::DrawParameters& parameters)
    {
        recordedContext = nullptr;
        const auto found = paintShaders.find(static_cast<uint64_t>(s));
        if (found != paintShaders.end() && found->second.element)
        {
            paintCommands.push_back({static_cast<uint64_t>(s), static_cast<int>(commands.size())});
            RenderGeometry(g, t, tex, parameters);
            return;
        }
        URUI::RenderInterface::RenderShader(s, g, t, tex, parameters);
    }

    void SurfaceRenderer::ReleaseShader(URUI::CompiledShaderHandle h)
    {
        recordedContext = nullptr;
        if (paintShaders.erase(static_cast<uint64_t>(h)))
        {
            return;
        }
        URUI::RenderInterface::ReleaseShader(h);
    }

    void SurfaceRenderer::EnableScissorRegion(bool value)
    {
        recordedContext = nullptr;
        clipped = value;
    }

    void SurfaceRenderer::SetScissorRegion(URUI::Rectanglei value)
    {
        recordedContext = nullptr;
        clip = value;
    }

    void SurfaceRenderer::SetTransform(const URUI::Matrix4f* matrix)
    {
        recordedContext = nullptr;
        activeTransform = matrix ? *matrix : URUI::Matrix4f::Identity();
    }

    void SurfaceClickListener::ProcessEvent(URUI::Event& event)
    {
        if (!surface || !surface->documentRegistry)
        {
            return;
        }
        auto* target = event.GetTargetElement();
        if (!target)
        {
            return;
        }
        auto* doc = target->GetOwnerDocument();
        const auto owner = surface->documentRegistry->HandleOf(doc);
        auto* state = owner ? surface->documentRegistry->Find(owner) : nullptr;
        if (state && state->clicks.size() < 4096)
        {
            state->clicks.push_back(target->GetObserverPtr());
        }
    }

    int DestroySurfaceInternal(URUISurfaceHandle handle, bool forceFromDeferred) noexcept
    {
        if (!handle)
        {
            return URUI_ERR_NOT_READY;
        }
        std::unique_ptr<SurfaceContext> owned;
        {
            std::lock_guard<std::mutex> lock(gSurfaceMutex);
            const auto found = gSurfaces.find(handle);
            if (found == gSurfaces.end() || !found->second)
            {
                return URUI_ERR_NOT_READY;
            }
            auto* surface = found->second.get();
            if (!surface->IsOwnerThread())
            {
                return URUI_ERR_WRONG_THREAD;
            }
            if (!forceFromDeferred && surface->callbackDepth > 0)
            {
                surface->pendingDestroy = true;
                return URUI_OK;
            }
            if (tlsCachedHandle == handle || tlsCachedSurface == surface)
            {
                tlsCachedHandle = 0;
                tlsCachedSurface = nullptr;
            }
            if (surface->context)
            {
                gContextToSurface.erase(surface->context);
            }
            owned = std::move(found->second);
            gSurfaces.erase(found);
        }
        if (owned)
        {
            try
            {
                ActiveSurfaceScope scope(owned.get());
                if (owned->documentRegistry)
                {
                    owned->documentRegistry->Clear();
                }
                owned->documentRegistry.reset();
                ResetInteraction(*owned);
                owned->elementReferences.Clear();
                owned->styles.reset();
                owned->programLayouts.reset();
                if (owned->context)
                {
                    URUI::RemoveContext(owned->contextName);
                    owned->context = nullptr;
                }
                owned->fileResourceCache.reset();
                owned->resourceFiles.reset();
                owned->pendingDestroy = false;
                owned->ownerThread.store(std::thread::id{}, std::memory_order_release);
            }
            catch (...)
            {
            }
            const bool acquired = owned->engineAcquired;
            owned->engineAcquired = false;
            owned.reset();
            if (acquired)
            {
                EngineRuntime::Release();
            }
#if defined(URUI_DEBUG)
            typed_probe = {};
            typed_probe_mode = 0;
            URUI::LookupProbe::inline_enabled = false;
            URUI::LookupProbe::profile_mode = 0;
            URUI::LookupProbe::style_scope = false;
            URUI::LookupProbe::profile = {};
#endif
        }
        return URUI_OK;
    }

    int RenderSurfaceImpl(SurfaceContext& surface) noexcept
    try
    {
        if (surface.callbackDepth > 0)
        {
            return URUI_ERR_REENTRANCY;
        }
        ActiveSurfaceScope scope(&surface);
        URUI_DEBUG_CODE(ProfileScope timer(profile.render_ns);)
        auto* context = surface.context;
        auto& render = surface.renderer;
        if (!context)
        {
            return URUI_ERR_NOT_READY;
        }
        const auto revision = context->GetNativeRenderRevision();
        const bool reusable = render.documentGeneration != 0 && context->AllowsNativeRecordingReuse();
        if (reusable && render.recordedContext == context && render.recordedRevision == revision &&
            render.recordedDocument == render.documentGeneration && render.recordedResources == render.resourceRevision &&
            render.recordedCommandCount == render.commands.size() && render.layerValid && render.maskFilters.empty())
        {
            return URUI_OK;
        }
        render.recordedContext = nullptr;
        render.commands.clear();
        render.commandTransforms.clear();
        render.commandUniforms.clear();
        render.paintCommands.clear();
        render.PruneEffectRoots();
        render.layerCommands.clear();
        render.layerStack.assign(1, 0);
        render.nextLayerResource = 1;
        render.layerValid = render.maskFilters.empty();
        render.clipped = false;
        render.activeTransform = URUI::Matrix4f::Identity();
        context->Render();
        render.layerValid = render.layerValid && render.layerStack.size() == 1 && render.maskFilters.empty();
        if (reusable && revision == context->GetNativeRenderRevision() && render.layerValid &&
            context->AllowsNativeRecordingReuse())
        {
            render.recordedContext = context;
            render.recordedRevision = revision;
            render.recordedDocument = render.documentGeneration;
            render.recordedResources = render.resourceRevision;
            render.recordedCommandCount = render.commands.size();
        }
        return URUI_OK;
    }
    catch (...)
    {
        auto& render = surface.renderer;
        render.recordedContext = nullptr;
        render.commands.clear();
        render.commandTransforms.clear();
        render.commandUniforms.clear();
        render.paintCommands.clear();
        render.layerCommands.clear();
        render.layerStack.clear();
        render.layerValid = false;
        surface.SetError("urui_surface_render failed");
        return URUI_ERR_INTERNAL;
    }
}

using namespace URUI::Bridge;

extern "C"
{
    int urui_bridge_abi_version(void) { return 13; }
    int urui_render_plan_version(void) { return 9; }
    int urui_command_protocol_version(void) { return 3; }
    int urui_texture_protocol_version(void) { return 1; }
    int urui_image_resolver_version(void) { return 3; }
    int urui_ui_clock_api_version(void) { return 2; }

    int urui_surface_create(int width, int height, URUISurfaceHandle* out_surface)
    try
    {
        if (!out_surface)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *out_surface = 0;
        EngineRuntime::SetThreadError("");
        URUI_DEBUG_CODE(profile = {};)

        auto surface = std::make_unique<SurfaceContext>();
        surface->ownerThread.store(std::this_thread::get_id(), std::memory_order_release);
        const uint64_t handle = gNextSurfaceHandle.fetch_add(1, std::memory_order_relaxed);
        if (!handle || handle == UINT64_MAX)
        {
            EngineRuntime::SetThreadError("Surface handle space exhausted");
            return URUI_ERR_BUDGET;
        }
        surface->handle = handle;
        surface->contextName = "urui_" + std::to_string(handle);

        if (!EngineRuntime::Acquire())
        {
            return URUI_ERR_INTERNAL;
        }
        surface->engineAcquired = true;

        struct CreateRollback
        {
            std::unique_ptr<SurfaceContext>& surface;
            bool committed = false;
            ~CreateRollback()
            {
                if (!committed && surface)
                {
                    try
                    {
                        ActiveSurfaceScope scope(surface.get());
                        if (surface->documentRegistry)
                        {
                            surface->documentRegistry->Clear();
                        }
                        surface->documentRegistry.reset();
                        surface->elementReferences.Clear();
                        surface->styles.reset();
                        surface->programLayouts.reset();
                        if (surface->context)
                        {
                            URUI::RemoveContext(surface->contextName);
                            surface->context = nullptr;
                        }
                        surface->fileResourceCache.reset();
                        surface->resourceFiles.reset();
                    }
                    catch (...)
                    {
                    }
                    const bool acquired = surface->engineAcquired;
                    surface->engineAcquired = false;
                    if (acquired)
                    {
                        EngineRuntime::Release();
                    }
                    surface.reset();
                }
            }
        } rollback{surface};

        ActiveSurfaceScope scope(surface.get());
        surface->resourceFiles = std::make_unique<ResourceFiles>(EngineRuntime::OriginalFileInterface());
        surface->context = URUI::CreateContext(surface->contextName, {std::max(1, width), std::max(1, height)},
                                               &surface->renderer, &surface->textHandler);
        if (!surface->context)
        {
            EngineRuntime::SetThreadError("Cannot create URUI context");
            return URUI_ERR_INTERNAL;
        }
        surface->elementReferences.Begin();
        surface->elementReferences.SetTeardownHook(OnElementTokenTeardown, surface.get());
        surface->fileResourceCache = std::make_unique<FileResourceCache>();
        surface->documentRegistry = std::make_unique<DocumentRegistry>(*surface->context, surface->elementReferences,
                                                                       OnDocumentRetire, surface.get());
        surface->styles = CreateStyleStore();
        surface->programLayouts = CreateProgramLayoutStore();

        SurfaceContext* raw = surface.get();
        {
            std::lock_guard<std::mutex> lock(gSurfaceMutex);
            gContextToSurface.emplace(raw->context, raw);
            try
            {
                gSurfaces.emplace(handle, std::move(surface));
            }
            catch (...)
            {
                gContextToSurface.erase(raw->context);
                throw;
            }
        }
        tlsCachedHandle = handle;
        tlsCachedSurface = raw;
        rollback.committed = true;
        *out_surface = handle;
        return URUI_OK;
    }
    catch (const std::bad_alloc&)
    {
        EngineRuntime::SetThreadError("Cannot initialize URUI Surface (out of memory)");
        return URUI_ERR_BUDGET;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Cannot initialize URUI Surface");
        return URUI_ERR_INTERNAL;
    }

    int urui_surface_destroy(URUISurfaceHandle surface)
    {
        return DestroySurfaceInternal(surface, false);
    }

    int urui_surface_last_error(URUISurfaceHandle handle, char* out, int capacity, int* required)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, false, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!surface->IsOwnerThread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        return EngineRuntime::CopyString(surface->lastError, out, capacity, required);
    }

    int urui_last_error_copy(char* out, int capacity, int* required)
    {
        return EngineRuntime::CopyString(EngineRuntime::ThreadError(), out, capacity, required);
    }

    int urui_surface_set_default_font_face(URUISurfaceHandle handle, int family, const uint8_t* bytes, int length,
                                           int face_index, int style, int weight)
    try
    {
        if (family < 0 || family > 2 || !bytes || length <= 0 || length > 64 * 1024 * 1024 || face_index < 0 ||
            style < 0 || style > 1 || weight < 1 || weight > 1000)
        {
            return URUI_ERR_INVALID_ARG;
        }
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (surface->documentRegistry->Count() != 0 || surface->context->GetNumDocuments() != 0)
        {
            return URUI_ERR_REENTRANCY;
        }
        ActiveSurfaceScope scope(surface);
        if (!URUI::DocumentFontContext::LoadDefaultFace(family, {bytes, size_t(length)},
                                                        static_cast<URUI::Style::FontStyle>(style),
                                                        static_cast<URUI::Style::FontWeight>(weight), face_index))
        {
            surface->SetError(
                "Cannot install the default font face: invalid source or a different face already occupies the slot");
            return URUI_ERR_NOT_READY;
        }
        surface->ClearError();
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Default font bootstrap failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_surface_set_image_resolver(URUISurfaceHandle handle, URUIImageResolver resolve, URUIImageRelease release)
    {
        if (bool(resolve) != bool(release))
        {
            return URUI_ERR_INVALID_ARG;
        }
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (surface->context->GetNumDocuments() != 0)
        {
            return URUI_ERR_NOT_READY;
        }
        for (const auto& entry : surface->renderer.textures)
        {
            if (entry.second.resolved)
            {
                return URUI_ERR_NOT_READY;
            }
        }
        surface->renderer.imageResolver = resolve;
        surface->renderer.imageRelease = release;
        return URUI_OK;
    }

    int urui_surface_set_ui_time(URUISurfaceHandle handle, double now)
    {
        if (!std::isfinite(now) || now < 0)
        {
            return URUI_ERR_INVALID_ARG;
        }
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        surface->externalTime = now;
        surface->externalTimeEnabled = true;
        return URUI_OK;
    }

    int urui_surface_set_direct_color_filters(URUISurfaceHandle handle, int enabled)
    {
        if (enabled < 0 || enabled > 1)
        {
            return URUI_ERR_INVALID_ARG;
        }
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (surface->documentRegistry->Count() != 0)
        {
            return URUI_ERR_REENTRANCY;
        }
        surface->directColorFiltersEnabled = enabled != 0;
        return URUI_OK;
    }

    int urui_surface_resize(URUISurfaceHandle handle, int width, int height)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        ActiveSurfaceScope scope(surface);
        surface->context->SetDimensions({std::max(1, width), std::max(1, height)});
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("urui_surface_resize failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_surface_update(URUISurfaceHandle handle)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        ActiveSurfaceScope scope(surface);
        URUI_DEBUG_CODE(ProfileScope timer(profile.update_ns);)
        FlushCandidateEvents(*surface);
        surface->context->Update();
        PruneStyleStore(surface->styles.get(), *surface);
        PruneDeadSubscriptions(*surface);
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("URUI update failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_surface_synchronize_geometry(URUISurfaceHandle handle)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        ActiveSurfaceScope scope(surface);
        return surface->context->SynchronizeGeometry() ? URUI_OK : URUI_ERR_NOT_READY;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("URUI geometry synchronization failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_surface_render(URUISurfaceHandle handle)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        return RenderSurfaceImpl(*surface);
    }

    uint64_t urui_surface_layout_version(URUISurfaceHandle handle)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(handle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        return surface->context->GetHitTestRevision();
    }

    int urui_command_count(URUISurfaceHandle handle)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        return static_cast<int>(surface->renderer.commands.size());
    }

    int urui_get_commands(URUISurfaceHandle handle, URUICommand* out, int cap)
    {
        URUI_DEBUG_CODE(ProfileScope timer(profile.command_copy_ns);)
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (cap < 0 || (!out && cap > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (!out)
        {
            return static_cast<int>(surface->renderer.commands.size());
        }
        const int n = std::min(cap, static_cast<int>(surface->renderer.commands.size()));
        if (n > 0)
        {
            std::copy_n(surface->renderer.commands.begin(), n, out);
        }
        return n;
    }

    int urui_get_paint_commands(URUISurfaceHandle handle, URUIPaintCommand* out, int capacity)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (capacity < 0 || (!out && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        const auto& render = surface->renderer;
        if (!out)
        {
            return static_cast<int>(render.paintCommands.size());
        }
        int count = 0;
        for (const auto& command : render.paintCommands)
        {
            if (count == capacity)
            {
                break;
            }
            const auto shader = render.paintShaders.find(command.shader);
            if (shader == render.paintShaders.end() || !shader->second.element)
            {
                continue;
            }
            const auto token =
                surface->elementReferences.Track(shader->second.element.get(), shader->second.element->GetOwnerDocument());
            if (token)
            {
                out[count++] = {command.shader, token, command.command_index, shader->second.kind};
            }
        }
        return count;
    }

    int urui_paint_color_filters(URUISurfaceHandle handle, uint64_t shader, URUIColorFilter* out, int capacity)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (capacity < 0 || (!out && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        const auto found = surface->renderer.paintShaders.find(shader);
        if (found == surface->renderer.paintShaders.end() || found->second.kind != 2 || !found->second.element ||
            !surface->documentRegistry->HandleOf(found->second.element->GetOwnerDocument()))
        {
            return URUI_ERR_NOT_READY;
        }
        const auto& filters = found->second.colorFilters;
        if (!out)
        {
            return static_cast<int>(filters.size());
        }
        if (capacity < static_cast<int>(filters.size()))
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        std::copy(filters.begin(), filters.end(), out);
        return static_cast<int>(filters.size());
    }

    int urui_paint_shader_name(URUISurfaceHandle handle, uint64_t shader, char* out, int capacity, int* required)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!required || capacity < 0 || (!out && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        if (out && capacity > 0)
        {
            out[0] = '\0';
        }
        const auto found = surface->renderer.paintShaders.find(shader);
        if (found == surface->renderer.paintShaders.end() || !found->second.element)
        {
            return URUI_ABSENT;
        }
        return EngineRuntime::CopyString(found->second.name, out, capacity, required);
    }

    int urui_get_layer_commands(URUISurfaceHandle handle, URUILayerCommand* out, int capacity)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (capacity < 0 || (!out && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        const auto& render = surface->renderer;
        if (!render.layerValid)
        {
            surface->SetError("Invalid or unsupported layer/filter command stream");
            return URUI_ERR_NOT_READY;
        }
        if (!out)
        {
            return static_cast<int>(render.layerCommands.size());
        }
        const int count = std::min(capacity, static_cast<int>(render.layerCommands.size()));
        if (count > 0)
        {
            std::copy_n(render.layerCommands.begin(), count, out);
        }
        return count;
    }

    int urui_geometry_exists(URUISurfaceHandle handle, uint64_t id)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(handle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        return surface->renderer.meshes.count(id) ? URUI_OK : URUI_ABSENT;
    }

    int urui_get_geometry_info(URUISurfaceHandle handle, uint64_t id, int* vc, int* ic)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        const auto found = surface->renderer.meshes.find(id);
        if (found == surface->renderer.meshes.end())
        {
            return URUI_ABSENT;
        }
        if (vc)
        {
            *vc = static_cast<int>(found->second.vertices.size());
        }
        if (ic)
        {
            *ic = static_cast<int>(found->second.indices.size());
        }
        return URUI_OK;
    }

    int urui_copy_geometry(URUISurfaceHandle handle, uint64_t id, URUIVertex* v, int vc, int* i, int ic)
    {
        URUI_DEBUG_CODE(ProfileScope timer(profile.geometry_copy_ns);)
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        const auto found = surface->renderer.meshes.find(id);
        if (found == surface->renderer.meshes.end())
        {
            return URUI_ABSENT;
        }
        const auto& g = found->second;
        if (!v || !i)
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (vc < static_cast<int>(g.vertices.size()) || ic < static_cast<int>(g.indices.size()))
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        std::copy(g.vertices.begin(), g.vertices.end(), v);
        std::copy(g.indices.begin(), g.indices.end(), i);
        return URUI_OK;
    }

    int urui_texture_exists(URUISurfaceHandle handle, uint64_t id)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(handle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        return surface->renderer.textures.count(id) ? URUI_OK : URUI_ABSENT;
    }

    int urui_get_texture_kind(URUISurfaceHandle handle, uint64_t id)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(handle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        const auto found = surface->renderer.textures.find(id);
        return found == surface->renderer.textures.end() ? 0 : found->second.borrowed ? 2 : 1;
    }

    int urui_get_texture_info(URUISurfaceHandle handle, uint64_t id, int* w, int* h, int* bytes)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        const auto found = surface->renderer.textures.find(id);
        if (found == surface->renderer.textures.end())
        {
            return URUI_ABSENT;
        }
        const auto& t = found->second;
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
            *bytes = static_cast<int>(t.pixels.size());
        }
        return URUI_OK;
    }

    int urui_copy_texture(URUISurfaceHandle handle, uint64_t id, uint8_t* out, int cap)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(handle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        const auto found = surface->renderer.textures.find(id);
        if (found == surface->renderer.textures.end())
        {
            return URUI_ABSENT;
        }
        const auto& texture = found->second;
        if (texture.borrowed)
        {
            surface->SetError("Cannot copy pixels from a borrowed external texture");
            return URUI_ERR_NOT_READY;
        }
        if (!out)
        {
            return URUI_ERR_INVALID_ARG;
        }
        const auto& p = texture.pixels;
        if (cap < static_cast<int>(p.size()))
        {
            return URUI_ERR_BUFFER_TOO_SMALL;
        }
        std::copy(p.begin(), p.end(), out);
        return URUI_OK;
    }
}
