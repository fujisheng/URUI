#pragma once
#include "document_registry.h"
#include "element_references.h"
#include "render_plan_compiler.h"
#include "resource_files.h"
#include "resource_store.h"
#include "urui.h"
#include <URUI/Core.h>
#include <URUI/Core/ElementText.h>
#include <URUI/Core/RenderManager.h>
#include <URUI/Core/TextInputContext.h>
#include <URUI/Core/TextInputHandler.h>
#include <array>
#include <atomic>
#include <cstdint>
#include <memory>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>

namespace URUI::Bridge
{
    constexpr uint64_t BorrowedTextureBit = uint64_t{1} << 63;

    struct StyleStore;
    struct ProgramLayoutStore;
    struct SurfaceContext;

    void DestroyStyleStore(StyleStore* store) noexcept;
    void DestroyProgramLayoutStore(ProgramLayoutStore* store) noexcept;

    struct StyleStoreDeleter
    {
        void operator()(StyleStore* p) const noexcept
        {
            DestroyStyleStore(p);
        }
    };
    struct ProgramLayoutStoreDeleter
    {
        void operator()(ProgramLayoutStore* p) const noexcept
        {
            DestroyProgramLayoutStore(p);
        }
    };

    std::unique_ptr<StyleStore, StyleStoreDeleter> CreateStyleStore();
    void ClearStyleStoreDocument(StyleStore* store, URUIDocumentHandle owner, URUI::ElementDocument* doc) noexcept;
    void EvictStyleStoreToken(StyleStore* store, uint64_t token) noexcept;
    void PruneStyleStore(StyleStore* store, SurfaceContext& surface) noexcept;
    void RecordStyleStringRead(StyleStore* store) noexcept;
    bool ComputedStyleKeyword(StyleStore* store, URUI::Element* element, const char* name, std::string& value);

    std::unique_ptr<ProgramLayoutStore, ProgramLayoutStoreDeleter> CreateProgramLayoutStore();
    void ClearProgramLayoutsForDocument(ProgramLayoutStore* store, URUIDocumentHandle owner) noexcept;
    int ValidateProgramLayoutBinding(SurfaceContext& surface, URUIDocumentHandle owner, uint64_t elementToken,
                                     uint64_t program, uint32_t float4_count);
    int FillProgramLayoutValues(SurfaceContext& surface, URUIDocumentHandle owner, uint64_t elementToken,
                                uint64_t program, float* values, uint32_t float4_count);

    struct ContentDigest128
    {
        uint64_t h1 = 0;
        uint64_t h2 = 0;
        size_t size = 0;
        bool operator==(const ContentDigest128& other) const noexcept
        {
            return h1 == other.h1 && h2 == other.h2 && size == other.size;
        }
    };

    ContentDigest128 ComputeDigest128(const uint8_t* data, size_t size) noexcept;

    template <typename Handle> inline uint64_t AllocateHandleId(uint64_t& next, uint64_t upperExclusive) noexcept
    {
        constexpr uint64_t maxHandle = std::numeric_limits<Handle>::max();
        const uint64_t limit = maxHandle < upperExclusive ? maxHandle : (upperExclusive - 1);
        if (!next || next > limit)
        {
            return 0;
        }
        return next++;
    }

    template <typename Handle> inline uint64_t AllocateTextureId(uint64_t& next) noexcept
    {
        return AllocateHandleId<Handle>(next, BorrowedTextureBit);
    }

    struct GeometryRecord
    {
        std::vector<URUIVertex> vertices;
        std::vector<int> indices;
        URUI::TextureSampling texture_sampling = URUI::TextureSampling::Clamp;
        std::array<float, 4> bounds{};
        bool boundsValid = false;
    };

    struct TextureRecord
    {
        int width = 0;
        int height = 0;
        bool borrowed = false;
        bool resolved = false;
        bool scoped = false;
        size_t aliases = 0;
        size_t loads = 0;
        URUI::TextureHandle renderHandle = 0;
        ContentDigest128 pixelDigest{};
        std::vector<unsigned char> pixels;
    };

    struct PaintShaderRecord
    {
        std::string name;
        URUI::ObserverPtr<URUI::Element> element;
        int kind = 0;
        std::vector<URUIColorFilter> colorFilters;
    };

    struct PaintCommandEntry
    {
        uint64_t shader = 0;
        int command_index = 0;
    };

    struct EffectRootReference
    {
        URUI::ObserverPtr<URUI::Element> element;
        URUI::ObserverPtr<URUI::Element> root;
        URUI::String name;
        uint64_t structure = 0;
    };

    struct FilterRecord
    {
        std::string name;
        URUIFilter data{};
        uint64_t document = 0;
        URUI::ObserverPtr<URUI::Element> element;
    };

    class SurfaceRenderer : public URUI::RenderInterface
    {
    public:
        SurfaceContext* ownerSurface = nullptr;
        URUI::RenderPlan::Session drawPlan;
        uint64_t geometryEpoch = URUI::RenderPlan::Identity();
        uint64_t documentGeneration = URUI::RenderPlan::Identity();
        uint64_t resourceRevision = 1;
        std::unordered_map<uint64_t, GeometryRecord> meshes;
        std::unordered_map<uint64_t, TextureRecord> textures;
        std::unordered_map<uint64_t, std::vector<uint64_t>> scopedPixelBuckets;
        std::unordered_map<URUI::TextureHandle, uint64_t> borrowedTextureHandles;
        URUIImageResolver imageResolver = nullptr;
        URUIImageRelease imageRelease = nullptr;
        std::vector<URUICommand> commands;
        std::vector<URUIPlanUniform> commandUniforms;
        std::vector<std::array<float, 16>> commandTransforms;
        std::unordered_map<uint64_t, PaintShaderRecord> paintShaders;
        std::vector<PaintCommandEntry> paintCommands;
        std::unordered_map<uint64_t, EffectRootReference> effectRoots;
        std::vector<URUILayerCommand> layerCommands;
        std::vector<uint64_t> layerStack = {0};
        std::unordered_map<uint64_t, uint64_t> maskFilters;
        std::unordered_map<uint64_t, FilterRecord> filters;
        uint64_t nextPaintShader = 1;
        uint64_t nextLayerResource = 1;
        uint64_t nextMaskFilter = 1;
        uint64_t nextMesh = 1;
        uint64_t nextTexture = 1;
        bool layerValid = true;
        bool clipped = false;
        URUI::Rectanglei clip{};
        URUI::Matrix4f activeTransform = URUI::Matrix4f::Identity();
        URUI::Context* recordedContext = nullptr;
        uint64_t recordedRevision = 0;
        uint64_t recordedDocument = 0;
        uint64_t recordedResources = 0;
        size_t recordedCommandCount = 0;

        explicit SurfaceRenderer(SurfaceContext* owner = nullptr) : ownerSurface(owner)
        {
        }

        void MarkResourceChanged() noexcept
        {
            if (++resourceRevision == 0)
            {
                resourceRevision = 1;
            }
            recordedContext = nullptr;
        }

        void MarkDocumentsChanged() noexcept
        {
            recordedContext = nullptr;
            documentGeneration = URUI::RenderPlan::Identity();
            geometryEpoch = URUI::RenderPlan::Identity();
            drawPlan.Invalidate(geometryEpoch);
        }

        static uint64_t PixelBucketKey(int width, int height, uint64_t h1) noexcept
        {
            return h1 ^ (uint64_t(uint32_t(width)) << 32) ^ uint64_t(uint32_t(height));
        }

        static bool EffectAttached(URUI::Element* element) noexcept;
        void EvictEffectRoot(uint64_t token) noexcept
        {
            effectRoots.erase(token);
        }
        void PruneEffectRoots();
        void LayerCommand(int kind, uint64_t source, uint64_t target, uint64_t mask = 0, int blend = 0);
        uint64_t AllocateTextureHandle();
        uint64_t LogicalTextureId(URUI::TextureHandle handle) const noexcept;
        void ReleaseResolved(uint64_t id) noexcept;
        void RemoveFromPixelBucket(uint64_t id, const TextureRecord& tex) noexcept;
        void CollectTexture(uint64_t id);
        void ReleaseAlias(uint64_t id);
        uint64_t RegisterScopedImage(const std::shared_ptr<DocumentResources>& resources, const std::string& source,
                                     int width, int height, const uint8_t* pixels, size_t length,
                                     uint64_t external = 0);

        URUI::CompiledGeometryHandle CompileGeometry(URUI::Span<const URUI::Vertex> vs,
                                                     URUI::Span<const int> ids) override;
        void SetGeometryTextureSampling(URUI::CompiledGeometryHandle geometry,
                                        URUI::TextureSampling sampling) override;
        void RenderGeometry(URUI::CompiledGeometryHandle geo, URUI::Vector2f move, URUI::TextureHandle tex,
                            const URUI::DrawParameters& parameters) override;
        void ReleaseGeometry(URUI::CompiledGeometryHandle h) override;
        URUI::TextureHandle LoadTexture(URUI::Vector2i& dims, const URUI::String& source,
                                        const URUI::SharedPtr<URUI::ResourceContext>& resources = {}) override;
        URUI::TextureHandle GenerateTexture(URUI::Span<const URUI::byte> pixels, URUI::Vector2i dims) override;
        void ReleaseTexture(URUI::TextureHandle id) override;
        void EnableClipMask(bool enable) override;
        void RenderToClipMask(URUI::ClipMaskOperation op, URUI::CompiledGeometryHandle g, URUI::Vector2f t) override;
        URUI::LayerHandle PushLayer() override;
        void CompositeLayers(URUI::LayerHandle a, URUI::LayerHandle b, URUI::BlendMode mode,
                             URUI::Span<const URUI::CompiledFilterHandle> appliedFilters) override;
        void PopLayer() override;
        URUI::TextureHandle SaveLayerAsTexture() override;
        URUI::CompiledFilterHandle SaveLayerAsMaskImage() override;
        URUI::CompiledFilterHandle CompileFilter(const URUI::String& name, const URUI::Dictionary& p,
                                                 const URUI::SharedPtr<URUI::ResourceContext>& resources = {}) override;
        void ReleaseFilter(URUI::CompiledFilterHandle h) override;
        URUI::CompiledShaderHandle CompileShader(const URUI::String& name, const URUI::Dictionary& p) override;
        void RenderShader(URUI::CompiledShaderHandle s, URUI::CompiledGeometryHandle g, URUI::Vector2f t,
                          URUI::TextureHandle tex, const URUI::DrawParameters& parameters) override;
        void ReleaseShader(URUI::CompiledShaderHandle h) override;
        void EnableScissorRegion(bool value) override;
        void SetScissorRegion(URUI::Rectanglei value) override;
        void SetTransform(const URUI::Matrix4f* matrix) override;
    };

    class SurfaceTextHandler : public URUI::TextInputHandler
    {
    public:
        SurfaceContext* surface = nullptr;
        URUI::TextInputContext* active = nullptr;
        URUI::ObserverPtr<URUI::Element> owner;
        bool composing = false;
        int start = 0;
        int end = 0;
        int originalEnd = 0;
        URUI::String original;

        explicit SurfaceTextHandler(SurfaceContext* s = nullptr) : surface(s)
        {
        }
        void OnActivate(URUI::TextInputContext* value) override;
        void OnDeactivate(URUI::TextInputContext* value) override;
        void OnDestroy(URUI::TextInputContext* value) override;
    };

    class SurfaceClickListener : public URUI::EventListener
    {
    public:
        SurfaceContext* surface = nullptr;
        explicit SurfaceClickListener(SurfaceContext* s = nullptr) : surface(s)
        {
        }
        void ProcessEvent(URUI::Event& event) override;
    };

    struct PointerState
    {
        int id = 0;
        URUI::ObserverPtr<URUI::Element> pressed;
        URUI::ObserverPtr<URUI::Element> hover;
        URUI::ObserverPtr<URUI::Element> capture;
        URUI::ObserverPtr<URUI::Element> gestureDocument;
        float x = 0;
        float y = 0;
        int kind = 0;
        int buttons = 0;
        bool nativeTouch = false;
        bool active = false;
        bool pollReady = false;
        int pollModifiers = 0;
        uint64_t pollRevision = 0;
        URUI::ObserverPtr<URUI::Element> pollCapture;
    };

    struct SubscriptionKey
    {
        uint64_t token = 0;
        int type = 0;
        int capture = 0;
        bool operator==(const SubscriptionKey& other) const noexcept
        {
            return token == other.token && type == other.type && capture == other.capture;
        }
    };

    struct SubscriptionKeyHash
    {
        size_t operator()(const SubscriptionKey& k) const noexcept
        {
            uint64_t h = k.token ^ (uint64_t(uint32_t(k.type)) << 32) ^ uint64_t(uint32_t(k.capture) * 2654435761u);
            h ^= h >> 30;
            h *= 0xbf58476d1ce4e5b9ULL;
            h ^= h >> 27;
            return size_t(h);
        }
    };

    struct EventSubscription;
    struct CandidateEvents;

    void DestroyEventSubscription(EventSubscription* p) noexcept;
    void DestroyCandidateEvents(CandidateEvents* p) noexcept;

    struct EventSubscriptionDeleter
    {
        void operator()(EventSubscription* p) const noexcept
        {
            DestroyEventSubscription(p);
        }
    };
    struct CandidateEventsDeleter
    {
        void operator()(CandidateEvents* p) const noexcept
        {
            DestroyCandidateEvents(p);
        }
    };

    struct SurfaceContext
    {
        URUISurfaceHandle handle = 0;
        std::atomic<std::thread::id> ownerThread{};
        std::string contextName;
        URUI::Context* context = nullptr;
        std::string lastError;
        bool directColorFiltersEnabled = false;
        bool engineAcquired = false;

        // Per-surface clock, clipboard & diagnostics.
        double externalTime = 0;
        bool externalTimeEnabled = false;
        URUI::String clipboard;
        uint64_t clipboardVersion = 1;
        bool captureDiagnostics = false;
        std::vector<URUI::String> diagnostics;

        // Per-surface interaction & callbacks.
        int callbackDepth = 0;
        bool pendingDestroy = false;
        const URUI::Event* animationEvent = nullptr;
        URUIElementCallback eventCallback = nullptr;
        URUIElementEvent currentInput{};
        std::vector<PointerState> pointers;
        int primaryTouch = -1;
        std::unordered_map<SubscriptionKey, std::unique_ptr<EventSubscription, EventSubscriptionDeleter>,
                           SubscriptionKeyHash>
            subscriptions;
        std::vector<std::unique_ptr<CandidateEvents, CandidateEventsDeleter>> pendingDocuments;

        // Per-surface subsystems.
        SurfaceRenderer renderer;
        SurfaceTextHandler textHandler;
        SurfaceClickListener clickListener;
        ElementReferences elementReferences;
        std::unique_ptr<ResourceFiles> resourceFiles;
        std::unique_ptr<FileResourceCache> fileResourceCache;
        std::unique_ptr<DocumentRegistry> documentRegistry;
        uint64_t nextResourceScope = 1;
        std::unique_ptr<StyleStore, StyleStoreDeleter> styles;
        std::unique_ptr<ProgramLayoutStore, ProgramLayoutStoreDeleter> programLayouts;

        SurfaceContext();
        ~SurfaceContext();

        bool IsOwnerThread() const noexcept
        {
            return ownerThread.load(std::memory_order_acquire) == std::this_thread::get_id();
        }
        void SetError(const char* message) noexcept;
        void SetError(const std::string& message) noexcept;
        void ClearError() noexcept
        {
            lastError.clear();
        }
        PointerState* FindPointer(int pointer) noexcept;
        PointerState& GetOrCreatePointer(int pointer);
        void ErasePointer(int pointer) noexcept;
    };

    // RAII scopes for active surface routing, callback depth & nested input state.
    struct ActiveSurfaceScope
    {
        SurfaceContext* previousSurface;
        bool previousDirectColor;
        explicit ActiveSurfaceScope(SurfaceContext* s) noexcept;
        ~ActiveSurfaceScope() noexcept;
        ActiveSurfaceScope(const ActiveSurfaceScope&) = delete;
        ActiveSurfaceScope& operator=(const ActiveSurfaceScope&) = delete;
    };

    struct CallbackDepthGuard
    {
        SurfaceContext& surface;
        explicit CallbackDepthGuard(SurfaceContext& s) noexcept : surface(s)
        {
            ++surface.callbackDepth;
        }
        ~CallbackDepthGuard() noexcept
        {
            --surface.callbackDepth;
        }
        CallbackDepthGuard(const CallbackDepthGuard&) = delete;
        CallbackDepthGuard& operator=(const CallbackDepthGuard&) = delete;
    };

    struct InputScopeGuard
    {
        SurfaceContext& surface;
        URUIElementEvent previous;
        explicit InputScopeGuard(SurfaceContext& s) noexcept : surface(s), previous(s.currentInput)
        {
        }
        ~InputScopeGuard() noexcept
        {
            surface.currentInput = previous;
        }
        InputScopeGuard(const InputScopeGuard&) = delete;
        InputScopeGuard& operator=(const InputScopeGuard&) = delete;
    };

    struct AnimationEventScope
    {
        SurfaceContext& surface;
        const URUI::Event* previous;
        AnimationEventScope(SurfaceContext& s, const URUI::Event& event) noexcept
            : surface(s), previous(s.animationEvent)
        {
            surface.animationEvent = &event;
        }
        ~AnimationEventScope() noexcept
        {
            surface.animationEvent = previous;
        }
    };

    // Process-wide engine lifetime manager (refcounted RmlUi core + thin multiplexers).
    class EngineRuntime
    {
    public:
        static bool Acquire() noexcept;
        static void Release() noexcept;
        static URUI::FileInterface* OriginalFileInterface() noexcept;
        static SurfaceContext* ActiveSurface() noexcept;
        static void SetThreadError(const char* message) noexcept;
        static void SetThreadError(const std::string& message) noexcept;
        static const std::string& ThreadError() noexcept;
        static int CopyString(const std::string& value, char* out, int capacity, int* required) noexcept;

        // Safe RAII scope for stateless compile/validate calls (never touches active Surfaces).
        struct IsolatedScope
        {
            bool ok = false;
            ActiveSurfaceScope suspendSurface;
            IsolatedScope() noexcept : suspendSurface(nullptr)
            {
                ok = EngineRuntime::Acquire();
            }
            ~IsolatedScope() noexcept
            {
                if (ok)
                {
                    EngineRuntime::Release();
                }
            }
        };
    };

    // Surface lookup & lifecycle helpers.
    int LookupSurface(URUISurfaceHandle handle, SurfaceContext*& out, bool requireOwnerThread = true,
                      bool forbidCallback = false) noexcept;
    int LookupDocument(SurfaceContext& surface, URUIDocumentHandle owner, DocumentState*& state, bool requireLoaded,
                       bool mutation) noexcept;
    URUI::ElementDocument* ResolveDocument(SurfaceContext& surface, URUIDocumentHandle owner) noexcept;
    URUI::Element* ResolveElement(SurfaceContext& surface, URUIDocumentHandle owner, uint64_t token) noexcept;
    int DestroySurfaceInternal(URUISurfaceHandle handle, bool forceFromDeferred = false) noexcept;

    // Cross-module hooks (implemented in bridge_events.cpp & bridge_elements.cpp).
    void ResetInteraction(SurfaceContext& surface) noexcept;
    void CancelDocumentInput(SurfaceContext& surface, URUI::ElementDocument* document) noexcept;
    void CancelBlockedDocumentInput(SurfaceContext& surface) noexcept;
    void RetireDocumentInteraction(SurfaceContext& surface, URUIDocumentHandle owner,
                                   URUI::ElementDocument* document) noexcept;
    void PruneDeadSubscriptions(SurfaceContext& surface) noexcept;
    void FlushCandidateEvents(SurfaceContext& surface);
    void CancelCompositionFor(SurfaceContext& surface, URUI::Element* element) noexcept;
    std::unique_ptr<CandidateEvents, CandidateEventsDeleter> CreateCandidateEvents(SurfaceContext& surface,
                                                                                   URUIDocumentHandle owner);
    void AttachCandidateEvents(CandidateEvents& candidate, URUI::ElementDocument& document);
    bool CandidatePending(const SurfaceContext& surface, URUI::Element* root) noexcept;
    int ReplaceElementHtml(SurfaceContext& surface, URUI::Element& element, const char* source);
    std::string SafeHtmlEscape(const char* src);
    void RecordElementTextProfile(uint64_t ns, bool fast) noexcept;
    int ApplyTypedTransform(SurfaceContext& surface, URUI::Element* el, float x, float y, float sx, float sy);
    int EffectLayoutImpl(SurfaceContext& surface, URUIDocumentHandle owner, uint64_t token, float* output, int capacity,
                         uint64_t* root);
    std::shared_ptr<DocumentResources> EnsurePendingResources(SurfaceContext& surface, DocumentState& state);
    void DiscardPendingResources(DocumentState& state) noexcept;
    URUI::String CanonicalResourceUrl(const char* source);
    int RenderSurfaceImpl(SurfaceContext& surface) noexcept;
    int DocumentListenImpl(SurfaceContext& surface, URUIDocumentHandle owner);

    // Internal test/selftest accessor (not exported from shared library).
    SurfaceContext* FindSurfaceInternal(URUISurfaceHandle handle) noexcept;
    size_t ActiveSurfaceCountInternal() noexcept;
}
