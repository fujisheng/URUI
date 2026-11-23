#pragma once
#include "render_direct_mask.h"
#include "render_external_geometry.h"
#include "render_plan_protocol.h"
#include <array>
#include <atomic>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <new>
#include <stdexcept>
#include <thread>
#include <vector>

namespace URUI::RenderPlan
{
    // 单叶直绘仍保留抽象组依赖；二值遮罩删除的是源内容修改，两者不能共用缓存签名语义。
    static constexpr uint8_t OmittedDirectMask = 1;
    static constexpr uint8_t OmittedBinaryMask = 2;
    static constexpr uint8_t OmittedEmptyMask = 3;

    static_assert(sizeof(URUIVertex) == 20, "vertex ABI");
    static_assert(sizeof(URUICommand) == 48, "command ABI");
    static_assert(sizeof(URUILayerCommand) == 56, "layer command ABI");
    static_assert(sizeof(URUIPlanVertex) == 32 && offsetof(URUIPlanVertex, parameter) == 28, "plan vertex ABI");
    static_assert(sizeof(URUIPlanParameter) == 128, "plan parameter ABI");
    static_assert(sizeof(URUIPlanOperation) == 136 && offsetof(URUIPlanOperation, base_parameter) == 128,
                  "operation ABI");
    static_assert(offsetof(URUIPlanInputPacket, command_count) == 16 + 4 * sizeof(void*), "input pointer ABI");
    static_assert(sizeof(URUIPlanInputPacket) == (sizeof(void*) == 8 ? 80 : 56), "input packet ABI");
    static_assert(sizeof(URUIPlanBindings) == (sizeof(void*) == 8 ? 80 : 60) && sizeof(URUIPlanExternalBinding) == 8,
                  "bindings ABI");
    static_assert(sizeof(URUIRenderPlanPacket) == (sizeof(void*) == 8 ? 144 : 128), "output packet ABI");
    static_assert(offsetof(URUIRenderPlanPacket, extra_parameters) == (sizeof(void*) == 8 ? 120 : 108),
                  "extra parameter pointer ABI");
    static_assert(sizeof(URUIPlanPrepareRequest) == 128, "prepare request ABI");
    static_assert(sizeof(URUIPlanFeedback) == 104 && offsetof(URUIPlanFeedback, transforms) == 96,
                  "prepare feedback ABI");
    static_assert(sizeof(URUIGeometryView) == (sizeof(void*) == 8 ? 32 : 24), "geometry view ABI");
    static_assert(sizeof(URUIPlanLayerRegion) == 48 && offsetof(URUIPlanLayerRegion, alias_layer) == 40,
                  "prepared layer region ABI");
    static_assert(sizeof(URUIPlanPreparePacket) == (sizeof(void*) == 8 ? 56 : 48), "prepared packet ABI");

    struct AllocationCounters
    {
        uint64_t allocations = 0;
        uint64_t frees = 0;
        uint64_t bytes = 0;
    };

    struct Budget
    {
        AllocationCounters* counters;
        size_t retained = 0;
        size_t limit;
    };

    // Every heap allocation owned by the compiler, including metadata, uses this allocator.
    template <class T> struct Allocator
    {
        using value_type = T;
        using propagate_on_container_move_assignment = std::true_type;
        Budget* budget = nullptr;
        explicit Allocator(Budget* b = nullptr) : budget(b)
        {
        }
        template <class U> Allocator(const Allocator<U>& other) : budget(other.budget)
        {
        }
        T* allocate(size_t n)
        {
            if (n > SIZE_MAX / sizeof(T))
            {
                throw std::bad_alloc();
            }
            const size_t bytes = n * sizeof(T);
            if (!budget || bytes > budget->limit - budget->retained)
            {
                throw std::bad_alloc();
            }
            auto* out = static_cast<T*>(::operator new(bytes));
            budget->retained += bytes;
            budget->counters->allocations++;
            budget->counters->bytes += bytes;
            return out;
        }
        void deallocate(T* pointer, size_t n) noexcept
        {
            if (!pointer)
            {
                return;
            }
            budget->retained -= n * sizeof(T);
            budget->counters->frees++;
            ::operator delete(pointer);
        }
        template <class U> bool operator==(const Allocator<U>& b) const
        {
            return budget == b.budget;
        }
        template <class U> bool operator!=(const Allocator<U>& b) const
        {
            return !(*this == b);
        }
    };

    template <class T> using Buffer = std::vector<T, Allocator<T>>;

    inline uint64_t Identity()
    {
        static std::atomic<uint64_t> next{0};
        const uint64_t value = next.fetch_add(1, std::memory_order_relaxed);
        if (value == UINT64_MAX)
        {
            throw std::overflow_error("URUI render identity exhausted");
        }
        return value + 1;
    }

    struct GeometrySource
    {
        const URUIVertex* vertices = nullptr;
        size_t vertex_count = 0;
        const int* indices = nullptr;
        size_t index_count = 0;
        const float* bounds = nullptr;
        const URUIPlanExternalGeometry* external = nullptr;
    };

    struct GeometryResolver
    {
        void* user = nullptr;
        GeometrySource (*fn)(void* user, uint64_t id) = nullptr;
        GeometrySource operator()(uint64_t id) const
        {
            return fn(user, id);
        }
    };

    struct ImageProgramFiller
    {
        void* user = nullptr;
        int (*fn)(void* user, const URUIPlanPrepareRequest& request, float* output, uint32_t rows) = nullptr;
        int operator()(const URUIPlanPrepareRequest& request, float* output, uint32_t rows) const
        {
            return fn(user, request, output, rows);
        }
    };

    struct GeometryKey
    {
        uint64_t geometry = 0;
        // Batch-local parameter index (globalIndex - operation.base_parameter).
        uint32_t parameter = 0;
        // Bit 31 marks ordinary layout-space vertices.
        uint32_t texture = 0;
        uint64_t version = 0;
        float originX = 0;
        float originY = 0;
        bool operator==(const GeometryKey& other) const
        {
            return geometry == other.geometry && parameter == other.parameter && texture == other.texture &&
                   version == other.version;
        }
    };

    struct Batch
    {
        uint64_t id = 0;
        uint64_t epoch = 0;
        uint64_t lastUse = 0;
        uint64_t keysHash = 0;
        uint32_t refCount = 0;
        bool compilingPin = false;
        Buffer<GeometryKey> keys;
        Buffer<uint8_t> uniformColors;
        Buffer<URUIPlanVertex> vertices;
        Buffer<uint32_t> indices;
        explicit Batch(Budget* budget)
            : keys(Allocator<GeometryKey>(budget)), uniformColors(Allocator<uint8_t>(budget)),
              vertices(Allocator<URUIPlanVertex>(budget)), indices(Allocator<uint32_t>(budget))
        {
        }
        Batch(Batch&&) = default;
        Batch& operator=(Batch&&) = default;
    };

    struct InkRegion
    {
        double x0 = 0;
        double y0 = 0;
        double x1 = 0;
        double y1 = 0;
        bool valid = false;
        bool TryPoint(double x, double y) noexcept;
        void Point(double x, double y);
        void Join(const InkRegion& b);
        void Rect(const float* b);
        void Capture(float* out) const;
    };

    struct MaskCropUsage
    {
        uint32_t creates = 0;
        uint32_t reads = 0;
        bool opaque = false;
        bool scalar = false;
    };

    struct LayerEffect
    {
        uint32_t layer_index = 0;
        uint64_t document = 0;
        uint64_t element = 0;
        uint64_t program = 0;
        // 1 blur, 2 shadow, 3 managed element, 0 point operation.
        uint32_t kind = 0;
        float sigma = 0;
        float offset_x = 0;
        float offset_y = 0;
        float value = 0;
        std::array<float, 16> projection{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
        bool cacheable = false;
    };

    struct ColorFilterInput
    {
        uint32_t command = 0;
        uint32_t kind = 0;
        float value = 0;
    };

    struct PrepareSlot
    {
        URUIPlanPrepareRequest request{};
        InkRegion ink;
        InkRegion worldInk;
        std::array<float, 4> expansion{};
        uint32_t transformOffset = 0;
        uint32_t transform_count = 0;
        uint32_t layer_index = 0;
        uint32_t source_index = 0;
        uint32_t geometry_index = UINT32_MAX;
        std::array<float, 16> geometryTransform{};
        bool image = false;
        bool layout = false;
        bool prepared = false;
        bool sourceReady = false;
        bool unityBlend = false;
        bool localImage = false;
    };

    struct SourceSnapshot
    {
        uint64_t geometry = 0;
        uint64_t version = 0;
        uint64_t epoch = 0;
        uint64_t composedVersion = 0;
        URUIPlanUniform uniform{};
        Buffer<URUIVertex> baseVertices;
        Buffer<URUIPlanVertex> vertices;
        Buffer<uint32_t> indices;
        explicit SourceSnapshot(Budget* geometryBudget)
            : baseVertices(Allocator<URUIVertex>(geometryBudget)), vertices(Allocator<URUIPlanVertex>(geometryBudget)),
              indices(Allocator<uint32_t>(geometryBudget))
        {
        }
        SourceSnapshot(SourceSnapshot&&) = default;
        SourceSnapshot& operator=(SourceSnapshot&&) = default;
    };

#include "render_layer_dependencies.h"

    struct ImageProgram
    {
        uint64_t program = 0;
        uint32_t offset = 0;
        uint32_t count = 0;
        uint32_t clock = UINT32_MAX;
    };

    struct Frame
    {
        uint64_t token = 0;
        uint64_t epoch = 0;
        uint64_t version = 0;
        uint64_t parameter_version = 0;
        bool input = false;
        bool compiled = false;
        Buffer<URUICommand> commands;
        Buffer<std::array<float, 16>> matrices;
        Buffer<URUIPlanUniform> uniforms;
        Buffer<uint64_t> command_layers;
        Buffer<int32_t> rawFlags;
        Buffer<int32_t> layerComposites;
        Buffer<uint8_t> haloLayers;
        Buffer<URUIPaintCommand> paints;
        Buffer<URUILayerCommand> layers;
        Buffer<URUIPlanOperation> operations;
        Buffer<URUIPlanTargetRequest> targets;
        Buffer<uint32_t> targetLookup;
        Buffer<uint8_t> targetCapabilities;
        Buffer<URUIPlanParameter> parameters;
        Buffer<std::array<float, 4>> extra_parameters;
        Buffer<std::array<float, 4>> clocks;
        Buffer<uint32_t> clockLookup;
        Buffer<ImageProgram> imagePrograms;
        Buffer<ImageProgram> capturedPrograms;
        Buffer<uint32_t> capturedParameters;
        Buffer<std::array<float, 4>> imageParameters;
        Buffer<uint32_t> imagePending;
        Buffer<ColorFilterInput> colorFilters;
        Buffer<int32_t> colorOffsets;
        Buffer<uint32_t> colorCounts;
        Buffer<uint64_t> parameter_pages;
        Buffer<LayerEffect> effects;
        Buffer<PrepareSlot> preparation;
        Buffer<URUIPlanPrepareRequest> pendingRequests;
        Buffer<uint32_t> pendingSlots;
        Buffer<SourceSnapshot> sources;
        Buffer<int32_t> imageSlots;
        Buffer<int32_t> effectIndices;
        Buffer<int32_t> effectSlots;
        Buffer<std::array<float, 16>> coverage;
        Buffer<InkRegion> layerInk;
        Buffer<MaskCropUsage> maskCropUsage;
        Buffer<uint8_t> createdLayers;
        Buffer<URUIPlanLayerRegion> layerRegions;
        Buffer<uint32_t> layerRegionLookup;
        Buffer<uint32_t> layer_indices;
        Buffer<uint8_t> omittedLayers;
        Buffer<uint8_t> omittedOperations;
        Buffer<uint8_t> suppressedCommands;
        Buffer<int32_t> directMaskIndices;
        Buffer<DirectMask> directMasks;
        struct BinaryMaskClip
        {
            uint32_t layer_index;
            std::array<float, 4> bounds;
        };
        Buffer<BinaryMaskClip> binaryMaskClips;
        Buffer<LayerDependencyState> dependencies;
        Buffer<uint8_t> cachedLayers;
        Buffer<uint8_t> knownProviders;
        Buffer<URUIPlanExternalGeometry> external_geometry;
        Buffer<URUIPlanExternalDraw> external_draws;
        Buffer<int32_t> externalOffsets;
        Buffer<uint32_t> externalCounts;
        Buffer<uint32_t> externalLookup;
        Buffer<std::array<float, 6>> externalBounds;
        Buffer<uint32_t> externalPending;
        Buffer<URUIPlanTextureBinding> resources;
        Buffer<uint32_t> resourceLookup;
        uint32_t resourceFlags = 0;
        bool discardedEmptyMask = false;
        Buffer<uint64_t> layerStack;
        Buffer<uint64_t> lastLayerWrite;
        Buffer<uint64_t> maskSaveSequence;
        Buffer<uint32_t> maskCopySources;
        Buffer<uint32_t> maskCopyCounts;
        Buffer<uint8_t> maskCopyState;
        uint32_t imageCursor = 0;
        uint32_t layoutCursor = 0;
        uint32_t elementCursor = 0;
        uint32_t captureCommand = 0;
        uint32_t captureLayer = 0;
        bool captureStarted = false;
        uint32_t width = 8192;
        uint32_t height = 8192;
        size_t geometryQueryCursor = 0;
        bool resolving = false;
        bool ready = false;

        explicit Frame(Budget* budget);
        void Reset() noexcept;
        void ShrinkAndReset() noexcept;
    };

    class Session
    {
        AllocationCounters counters;
        Budget geometryBudget;
        Budget frameBudget;
        LayerDependencyCache dependencyCache;
        std::atomic<std::thread::id> owner{std::this_thread::get_id()};
        uint64_t epoch = 0;
        bool retiredEpochBatches = false;
        uint32_t width = 8192;
        uint32_t height = 8192;
        std::array<Frame, 4> frames;
        Buffer<Batch> batches;
        // Open-addressing hash indices for O(1) batch lookup by (epoch, keysHash) and by batch.id.
        Buffer<uint32_t> batchKeyIndex;
        Buffer<uint32_t> batchIdIndex;
        Buffer<uint32_t> freeBatchSlots;
        uint32_t batchActiveCount = 0;
        uint32_t batchTombstoneCount = 0;
        uint64_t nextLastUse = 0;
        Buffer<GeometryKey> keys;
        Buffer<GeometrySource> batchSources;
        Buffer<size_t> geometryHints;
        size_t geometryHintCursor = 0;
        Buffer<URUIPaintCommand> paintScratch;
        Buffer<LayerEffect> effectScratch;
        Buffer<ColorFilterInput> colorScratch;
        Buffer<URUIPlanOperation> previousOperations;
        Buffer<URUIPlanParameter> previousParameters;
        Buffer<std::array<float, 4>> previousExtraParameters;
        Buffer<uint64_t> previousParameterPages;
        Buffer<URUIPlanTargetRequest> previousTargets;
        uint64_t previousEpoch = 0;
        uint64_t operationVersion = 0;
        uint64_t parameter_version = 0;

        // Full-scene reuse snapshot (covers ordinary AND layer/mask/filter/effect frames).
        struct ExternalGeometryIdentity
        {
            uint64_t geometry = 0;
            uint64_t version = 0;
            uint32_t vertex_count = 0;
            uint32_t index_count = 0;
        };
        Buffer<URUICommand> reuseCommands;
        Buffer<std::array<float, 16>> reuseMatrices;
        Buffer<URUIPlanUniform> reuseUniforms;
        Buffer<URUIPaintCommand> reusePaints;
        Buffer<URUILayerCommand> reuseLayers;
        Buffer<ColorFilterInput> reuseColorFilters;
        Buffer<LayerEffect> reuseEffects;
        Buffer<ImageProgram> reuseImagePrograms;
        Buffer<ImageProgram> reuseCapturedPrograms;
        Buffer<std::array<float, 4>> reuseImageParameters;
        Buffer<std::array<float, 4>> reuseClocks;
        Buffer<URUIPlanLayerRegion> reuseLayerRegions;
        Buffer<ExternalGeometryIdentity> reuseExternalGeo;
        Buffer<URUIPlanExternalDraw> reuseExternalDraws;
        Buffer<URUIPlanTextureBinding> reuseResources;
        Buffer<URUIPlanTextureBinding> reuseSourceResources;
        Buffer<URUIPlanExternalBinding> reuseExternals;
        Buffer<URUIPlanLayerBinding> reuseLayerBindings;
        uint64_t reuseEpoch = 0;
        uint32_t reuseWidth = 0;
        uint32_t reuseHeight = 0;
        uint32_t reuseSourceFlags = 0;
        uint32_t reuseBindingFlags = 0;
        bool reuseValid = false;

        // Per-Compile O(1) lookup table for bindings.textures.
        Buffer<uint32_t> bindingTextureLookup;
        const URUIPlanTextureBinding* bindingTexturePtr = nullptr;
        uint32_t bindingTextureCount = 0;
        bool hasNonZeroBindingDomain = false;

        struct SortNode
        {
            uint32_t command = 0;
            uint32_t state = 0;
            uint32_t depth = 0;
            uint32_t seen = 0;
            InkRegion bounds;
        };
        struct SortState
        {
            uint64_t texture = 0;
            uint64_t mask = 0;
            uint32_t domain = 0;
            uint32_t generation = 0;
            uint32_t value = 0;
        };
        struct Cell
        {
            uint64_t key = 0;
            uint32_t generation = 0;
            uint32_t maxDepth = 0;
            int head = -1;
        };
        struct Link
        {
            uint32_t item;
            int next;
        };
        struct SortMemoKey
        {
            URUICommand command{};
            std::array<float, 16> matrix{};
            uint64_t mask = 0;
            uint32_t domain = 0;
        };
        Buffer<SortMemoKey> sortMemo;
        Buffer<uint32_t> sortMemoOrder;
        uint64_t sortMemoEpoch = 0;
        Buffer<SortNode> sortNodes;
        Buffer<SortState> sortStates;
        Buffer<Cell> sortCells;
        Buffer<Link> sortLinks;
        Buffer<uint32_t> pending;
        Buffer<uint32_t> order;
        Buffer<uint32_t> large;
        uint32_t sortGeneration = 0;
        bool sortBoundsValid = false;
        uint64_t sortBuilds = 0;
        uint64_t planReuses = 0;
        uint64_t geometrySearches = 0;
        Frame* compiling = nullptr;

        Frame* Find(uint64_t token) noexcept;
        Frame* Free() noexcept;
        void UnpinFrame(Frame& frame) noexcept;
        void PinFrame(Frame& frame) noexcept;
        void ClearCompilingPins() noexcept;
        void RebuildBatchIndices();
        void InsertBatchIndex(uint32_t index);
        void RemoveBatchIndex(uint32_t index, uint64_t oldId, uint64_t oldEpoch, uint64_t oldKeysHash) noexcept;
        uint32_t FindBatchById(uint64_t id) const noexcept;
        uint32_t FindBatchByKeys(uint64_t batchEpoch, uint64_t keysHash) const noexcept;
        bool BatchInUse(uint32_t index) const noexcept;
        void CollectRetiredEpochs();
        size_t Reclaimable(size_t except = SIZE_MAX) const noexcept;
        void DropBatch(uint32_t index);
        void BuildResourceLookup(Frame& frame);
        void BuildBindingTextureLookup(const URUIPlanBindings& bindings);
        uint32_t Domain(uint64_t texture) const noexcept;
        bool NormalizedBindingTexture(uint64_t id) const noexcept;
        bool NormalizedFrameTexture(const Frame& frame, uint64_t id) const noexcept;
        const URUIPlanTextureBinding* FindFrameResource(const Frame& frame, uint64_t id) const noexcept;
        void Order(const Frame& source, const Frame& target, GeometryResolver resolve);
        InkRegion Capture(Frame& frame, uint32_t stop, GeometryResolver resolve);
        void LowerDirectMasks(Frame& frame, GeometryResolver resolve);
        void LowerBinaryMaskClips(Frame& frame);
        void PlanMaskCopies(Frame& frame);
        void ConstrainTerminalMasks(Frame& frame);
        void BuildLayerDependencies(Frame& frame);
        void CompleteRegions(Frame& frame, GeometryResolver resolve);
        void BuildTargetRequests(const Frame& source, Frame& target);
        template <class T> void ResizeSource(Buffer<T>& data, size_t size);
        void ComposeSource(SourceSnapshot& snapshot);
        void PrepareSource(Frame& frame, PrepareSlot& slot, GeometryResolver resolve);
        Batch& BuildOrReuseBatch(uint64_t e, uint32_t baseParameter, GeometryResolver resolve,
                                 const Frame* sourceFrame);
        uint32_t TryFindExternal(const Frame& frame, uint64_t id, uint64_t version) const noexcept;
        uint32_t FindExternal(const Frame& frame, uint64_t id, uint64_t version) const;
        void InsertExternalLookup(Frame& frame, uint32_t index);
        void SetExternalInputs(Frame& frame, const URUIPlanBindings& bindings);
        bool MatchesReuseSnapshot(const Frame& source, const URUIPlanBindings& bindings) const noexcept;
        void SaveReuseSnapshot(const Frame& source, const URUIPlanBindings& bindings);
        uint32_t FindPendingSlot(const Frame& frame, uint64_t request) const noexcept;

        int ResolveImpl(uint64_t input, const URUIPlanFeedback* feedbacks, uint32_t feedback_count,
                        GeometryResolver resolve, URUIPlanPreparePacket& out, const URUIPlanBindings* resources);
        int CompileImpl(uint64_t input, const URUIPlanBindings& bindings, GeometryResolver resolve,
                        URUIRenderPlanPacket& out);
        int SetImageProgramImpl(uint64_t input, uint64_t request, uint64_t program, uint32_t rows,
                                ImageProgramFiller fill, uint64_t clock);

    public:
        explicit Session(size_t geometryLimit = 64u * 1024 * 1024, size_t frameLimit = 48u * 1024 * 1024);

        URUIPaintCommand* PaintScratch(size_t count);
        LayerEffect* EffectScratch(size_t count);
        ColorFilterInput* ColorScratch(size_t count);

        AllocationCounters Allocations() const noexcept
        {
            return counters;
        }
        uint64_t SortBuilds() const noexcept
        {
            return sortBuilds;
        }
        uint64_t OrdinaryReuses() const noexcept
        {
            return planReuses;
        }
        uint64_t GeometrySearches() const noexcept
        {
            return geometrySearches;
        }
        bool Thread() const noexcept
        {
            return owner.load(std::memory_order_acquire) == std::this_thread::get_id();
        }
        void Viewport(uint32_t w, uint32_t h) noexcept
        {
            width = w;
            height = h;
        }
        void Invalidate(uint64_t value);

        int Begin(uint64_t value, const URUICommand* commands, const std::array<float, 16>* matrices, uint32_t count,
                  const URUIPaintCommand* paints, uint32_t paint_count, const URUILayerCommand* layers,
                  uint32_t layer_count, URUIPlanInputPacket& out, const URUIPlanUniform* uniforms = nullptr);
        int SetImageDocument(uint64_t input, uint32_t command, uint64_t document);
        int SetColorFilters(uint64_t input, const ColorFilterInput* values, uint32_t count);
        int SetLayerEffects(uint64_t input, const LayerEffect* effects, uint32_t count);
        int SetImageGeometry(uint64_t input, uint64_t request, const URUIPlanExternalGeometry& geometry,
                             const float* transform);

        template <class Fill>
        int SetImageProgram(uint64_t input, uint64_t request, uint64_t program, uint32_t rows, Fill&& fill,
                            uint64_t clock = 0)
        {
            using Fn = std::decay_t<Fill>;
            Fn fn = std::forward<Fill>(fill);
            ImageProgramFiller filler{&fn, [](void* user, const URUIPlanPrepareRequest& req, float* data,
                                              uint32_t r) -> int { return (*static_cast<Fn*>(user))(req, data, r); }};
            return SetImageProgramImpl(input, request, program, rows, filler, clock);
        }

        template <class GeometryResolve>
        int Resolve(uint64_t input, const URUIPlanFeedback* feedbacks, uint32_t feedback_count,
                    GeometryResolve&& resolve, URUIPlanPreparePacket& out,
                    const URUIPlanBindings* resources = nullptr)
        {
            using Fn = std::decay_t<GeometryResolve>;
            Fn fn = std::forward<GeometryResolve>(resolve);
            GeometryResolver resolver{&fn, [](void* user, uint64_t id) -> GeometrySource {
                                          return (*static_cast<Fn*>(user))(id);
                                      }};
            return ResolveImpl(input, feedbacks, feedback_count, resolver, out, resources);
        }

        // Single-feedback convenience overload for tests and callers.
        template <class GeometryResolve>
        int Resolve(uint64_t input, const URUIPlanFeedback* feedback, GeometryResolve&& resolve,
                    URUIPlanPreparePacket& out, const URUIPlanBindings* resources = nullptr)
        {
            return Resolve(input, feedback, feedback ? 1u : 0u, std::forward<GeometryResolve>(resolve), out, resources);
        }

        template <class GeometryResolve>
        int Compile(uint64_t input, const URUIPlanBindings& bindings, GeometryResolve&& resolve,
                    URUIRenderPlanPacket& out)
        {
            using Fn = std::decay_t<GeometryResolve>;
            Fn fn = std::forward<GeometryResolve>(resolve);
            GeometryResolver resolver{&fn, [](void* user, uint64_t id) -> GeometrySource {
                                          return (*static_cast<Fn*>(user))(id);
                                      }};
            return CompileImpl(input, bindings, resolver, out);
        }

        int Geometry(uint64_t lease, uint64_t geometry, URUIGeometryView& out);
        int Release(uint64_t token);
    };
}
