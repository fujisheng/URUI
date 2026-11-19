#pragma once
#include "render_plan_protocol.h"
#include "render_direct_mask.h"
#include "render_external_geometry.h"
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstring>
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
    static_assert(sizeof(URUIPlanVertex) == 32 && offsetof(URUIPlanVertex, parameter) == 28, "plan vertex ABI");
    static_assert(sizeof(URUIPlanParameter) == 128, "plan parameter ABI");
    static_assert(sizeof(URUIPlanOperation) == 128, "operation ABI");
    static_assert(offsetof(URUIPlanInputPacket, command_count) == 16 + 4 * sizeof(void*), "input pointer ABI");
    static_assert(sizeof(URUIPlanInputPacket) == (sizeof(void*) == 8 ? 80 : 56), "input packet ABI");
    static_assert(sizeof(URUIPlanBindings) == (sizeof(void*) == 8 ? 80 : 60) && sizeof(URUIPlanExternalBinding) == 8,
                  "bindings ABI");
    static_assert(sizeof(URUIRenderPlanPacket) == (sizeof(void*) == 8 ? 144 : 128), "output packet ABI");
    static_assert(offsetof(URUIRenderPlanPacket, extra_parameters) == (sizeof(void*) == 8 ? 120 : 108),
                  "extra parameter pointer ABI");
    static_assert(sizeof(URUIPlanPrepareRequest) == 128 && sizeof(URUIPlanFeedback) == 104, "preparation ABI");
    static_assert(sizeof(URUIGeometryView) == (sizeof(void*) == 8 ? 32 : 24), "geometry view ABI");
    static_assert(sizeof(URUIPlanLayerRegion) == 40 && sizeof(URUIPlanPreparePacket) == (sizeof(void*) == 8 ? 56 : 48),
                  "prepared layer ABI");
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
        auto value = next.load(std::memory_order_relaxed);
        do
        {
            if (value == UINT64_MAX)
            {
                throw std::overflow_error("URUI render identity exhausted");
            }
        } while (!next.compare_exchange_weak(value, value + 1, std::memory_order_relaxed));
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
    struct GeometryKey
    {
        uint64_t geometry;
        uint32_t parameter;
        // Bit 31 marks ordinary layout-space vertices.
        uint32_t texture;
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
        Buffer<GeometryKey> keys;
        Buffer<URUIPlanVertex> vertices;
        Buffer<uint32_t> indices;
        explicit Batch(Budget* budget)
            : keys(Allocator<GeometryKey>(budget)), vertices(Allocator<URUIPlanVertex>(budget)),
              indices(Allocator<uint32_t>(budget))
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
        void Point(double x, double y)
        {
            if (!std::isfinite(x) || !std::isfinite(y))
            {
                throw std::invalid_argument("nonfinite ink");
            }
            if (!valid)
            {
                x0 = x1 = x;
                y0 = y1 = y;
                valid = true;
            }
            else
            {
                x0 = std::min(x0, x);
                y0 = std::min(y0, y);
                x1 = std::max(x1, x);
                y1 = std::max(y1, y);
            }
        }
        void Join(const InkRegion& b)
        {
            if (b.valid)
            {
                Point(b.x0, b.y0);
                Point(b.x1, b.y1);
            }
        }
        void Rect(const float* b)
        {
            Point(b[0], b[1]);
            Point(double(b[0]) + b[2], double(b[1]) + b[3]);
        }
        void Capture(float* out) const
        {
            double x = valid ? std::floor(x0) : 0;
            double y = valid ? std::floor(y0) : 0;
            double w = valid ? std::max(1., std::ceil(x1) - x) : 1;
            double h = valid ? std::max(1., std::ceil(y1) - y) : 1;
            if (w > 8192 || h > 8192 || std::abs(x) > 1000000 || std::abs(y) > 1000000)
            {
                throw std::invalid_argument("capture bounds");
            }
            out[0] = float(x);
            out[1] = float(y);
            out[2] = float(w);
            out[3] = float(h);
        }
    };
    struct MaskCropUsage
    {
        uint32_t creates = 0;
        uint32_t reads = 0;
        bool opaque = false;
    };
    struct LayerEffect
    {
        uint32_t layer_index;
        uint64_t document;
        uint64_t element;
        uint64_t program;
        // 1 blur, 2 shadow, 3 managed element, 0 point operation.
        uint32_t kind;
        float sigma;
        float offset_x;
        float offset_y;
        float value;
        std::array<float, 16> projection{1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1};
        bool cacheable = false;
    };
    struct ColorFilterInput
    {
        uint32_t command;
        uint32_t kind;
        float value;
    };
    struct PrepareSlot
    {
        URUIPlanPrepareRequest request{};
        InkRegion ink;
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
        Buffer<int32_t> rawFlags, layerComposites;
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
        Buffer<ImageProgram> imagePrograms, capturedPrograms;
        Buffer<uint32_t> capturedParameters;
        Buffer<std::array<float, 4>> imageParameters;
        Buffer<uint32_t> imagePending;
        Buffer<ColorFilterInput> colorFilters;
        Buffer<int32_t> colorOffsets;
        Buffer<uint32_t> colorCounts;
        Buffer<uint64_t> parameter_pages;
        Buffer<LayerEffect> effects;
        Buffer<PrepareSlot> preparation;
        Buffer<SourceSnapshot> sources;
        Buffer<int32_t> imageSlots, effectIndices, effectSlots;
        Buffer<std::array<float, 16>> coverage;
        Buffer<InkRegion> layerInk;
        Buffer<MaskCropUsage> maskCropUsage;
        Buffer<uint8_t> createdLayers;
        Buffer<URUIPlanLayerRegion> layerRegions;
        Buffer<uint32_t> layer_indices;
        Buffer<uint8_t> omittedLayers, omittedOperations, suppressedCommands;
        Buffer<int32_t> directMaskIndices;
        Buffer<DirectMask> directMasks;
        struct BinaryMaskClip
        {
            uint32_t layer_index;
            std::array<float, 4> bounds;
        };
        Buffer<BinaryMaskClip> binaryMaskClips;
        Buffer<LayerDependencyState> dependencies;
        Buffer<uint8_t> cachedLayers, knownProviders;
        Buffer<URUIPlanExternalGeometry> external_geometry;
        Buffer<URUIPlanExternalDraw> external_draws;
        Buffer<int32_t> externalOffsets;
        Buffer<uint32_t> externalCounts, externalLookup;
        Buffer<std::array<float, 6>> externalBounds;
        Buffer<uint32_t> externalPending;
        Buffer<URUIPlanTextureBinding> resources;
        uint32_t resourceFlags = 0;
        Buffer<uint64_t> layerStack, lastLayerWrite, maskSaveSequence;
        Buffer<uint32_t> maskCopySources, maskCopyCounts;
        Buffer<uint8_t> maskCopyState;
        uint32_t pending = UINT32_MAX;
        uint32_t imageCursor = 0;
        uint32_t layoutCursor = 0;
        uint32_t elementCursor = 0;
        uint32_t captureCommand = 0;
        uint32_t captureLayer = 0;
        bool captureStarted = false;
        uint32_t width = 8192;
        uint32_t height = 8192;
        bool resolving = false;
        bool ready = false;
        explicit Frame(Budget* budget)
            : commands(Allocator<URUICommand>(budget)), matrices(Allocator<std::array<float, 16>>(budget)),
              uniforms(Allocator<URUIPlanUniform>(budget)), command_layers(Allocator<uint64_t>(budget)),
              rawFlags(Allocator<int32_t>(budget)), layerComposites(Allocator<int32_t>(budget)),
              haloLayers(Allocator<uint8_t>(budget)), paints(Allocator<URUIPaintCommand>(budget)),
              layers(Allocator<URUILayerCommand>(budget)), operations(Allocator<URUIPlanOperation>(budget)),
              targets(Allocator<URUIPlanTargetRequest>(budget)), targetLookup(Allocator<uint32_t>(budget)),
              targetCapabilities(Allocator<uint8_t>(budget)), parameters(Allocator<URUIPlanParameter>(budget)),
              extra_parameters(Allocator<std::array<float, 4>>(budget)),
              clocks(Allocator<std::array<float, 4>>(budget)), clockLookup(Allocator<uint32_t>(budget)),
              imagePrograms(Allocator<ImageProgram>(budget)), capturedPrograms(Allocator<ImageProgram>(budget)),
              capturedParameters(Allocator<uint32_t>(budget)), imageParameters(Allocator<std::array<float, 4>>(budget)),
              imagePending(Allocator<uint32_t>(budget)), colorFilters(Allocator<ColorFilterInput>(budget)),
              colorOffsets(Allocator<int32_t>(budget)), colorCounts(Allocator<uint32_t>(budget)),
              parameter_pages(Allocator<uint64_t>(budget)), effects(Allocator<LayerEffect>(budget)),
              preparation(Allocator<PrepareSlot>(budget)), sources(Allocator<SourceSnapshot>(budget)),
              imageSlots(Allocator<int32_t>(budget)), effectIndices(Allocator<int32_t>(budget)),
              effectSlots(Allocator<int32_t>(budget)), coverage(Allocator<std::array<float, 16>>(budget)),
              layerInk(Allocator<InkRegion>(budget)), maskCropUsage(Allocator<MaskCropUsage>(budget)),
              createdLayers(Allocator<uint8_t>(budget)), layerRegions(Allocator<URUIPlanLayerRegion>(budget)),
              layer_indices(Allocator<uint32_t>(budget)), omittedLayers(Allocator<uint8_t>(budget)),
              omittedOperations(Allocator<uint8_t>(budget)), suppressedCommands(Allocator<uint8_t>(budget)),
              directMaskIndices(Allocator<int32_t>(budget)), directMasks(Allocator<DirectMask>(budget)),
              binaryMaskClips(Allocator<BinaryMaskClip>(budget)), dependencies(Allocator<LayerDependencyState>(budget)),
              cachedLayers(Allocator<uint8_t>(budget)), knownProviders(Allocator<uint8_t>(budget)),
              external_geometry(Allocator<URUIPlanExternalGeometry>(budget)),
              external_draws(Allocator<URUIPlanExternalDraw>(budget)), externalOffsets(Allocator<int32_t>(budget)),
              externalCounts(Allocator<uint32_t>(budget)), externalLookup(Allocator<uint32_t>(budget)),
              externalBounds(Allocator<std::array<float, 6>>(budget)), externalPending(Allocator<uint32_t>(budget)),
              resources(Allocator<URUIPlanTextureBinding>(budget)), layerStack(Allocator<uint64_t>(budget)),
              lastLayerWrite(Allocator<uint64_t>(budget)), maskSaveSequence(Allocator<uint64_t>(budget)),
              maskCopySources(Allocator<uint32_t>(budget)), maskCopyCounts(Allocator<uint32_t>(budget)),
              maskCopyState(Allocator<uint8_t>(budget))
        {
        }
    };
#include "render_target_requests.h"
    class Session
    {
        AllocationCounters counters;
        Budget geometryBudget;
        Budget frameBudget;
        LayerDependencyCache dependencyCache;
        std::thread::id owner = std::this_thread::get_id();
        uint64_t epoch = 0;
        bool retiredEpochBatches = false;
        uint32_t width = 8192;
        uint32_t height = 8192;
        std::array<Frame, 4> frames;
        Buffer<Batch> batches;
        Buffer<GeometryKey> keys;
        Buffer<size_t> geometryHints;
        size_t geometryHintCursor = 0;
        Buffer<URUIPaintCommand> paintScratch;
        Buffer<LayerEffect> effectScratch;
        Buffer<ColorFilterInput> colorScratch;
        Buffer<URUIPlanOperation> previousOperations;
        Buffer<URUIPlanParameter> previousParameters;
        Buffer<std::array<float, 4>> previousExtraParameters;
        Buffer<uint64_t> previousParameterPages;
        uint64_t previousEpoch = 0;
        uint64_t operationVersion = 0;
        uint64_t parameter_version = 0;
        Buffer<URUICommand> ordinaryCommands;
        Buffer<std::array<float, 16>> ordinaryMatrices;
        Buffer<URUIPlanUniform> ordinaryUniforms;
        Buffer<URUIPlanTextureBinding> ordinaryResources, ordinarySourceResources;
        uint64_t ordinaryEpoch = 0;
        uint32_t ordinaryWidth = 0;
        uint32_t ordinaryHeight = 0;
        uint32_t ordinarySourceFlags = 0;
        uint32_t ordinaryBindingFlags = 0;
        bool ordinaryValid = false;
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
        Buffer<uint32_t> pending, order, large;
        uint32_t sortGeneration = 0;
        uint64_t sortBuilds = 0;
        uint64_t ordinaryReuses = 0;
        uint64_t geometrySearches = 0;
        Frame* compiling = nullptr;
        template <class T> static bool Same(const Buffer<T>& a, const Buffer<T>& b)
        {
            return a.size() == b.size() && (a.empty() || std::memcmp(a.data(), b.data(), a.size() * sizeof(T)) == 0);
        }
        Frame* Find(uint64_t token)
        {
            if (token)
            {
                for (auto& frame : frames)
                {
                    if (frame.token == token)
                    {
                        return &frame;
                    }
                }
            }
            return nullptr;
        }
        Frame* Free()
        {
            for (auto& frame : frames)
            {
                if (!frame.token)
                {
                    return &frame;
                }
            }
            return nullptr;
        }
        static bool RectValid(const float* b)
        {
            return std::isfinite(b[0]) && std::isfinite(b[1]) && std::isfinite(b[2]) && std::isfinite(b[3]) &&
                   b[2] >= 0 && b[3] >= 0;
        }
        static InkRegion LocalBounds(const GeometrySource& mesh)
        {
            InkRegion out;
            if (mesh.bounds)
            {
                out.Point(mesh.bounds[0], mesh.bounds[1]);
                out.Point(mesh.bounds[2], mesh.bounds[3]);
            }
            else
            {
                for (size_t v = 0; v < mesh.vertex_count; v++)
                {
                    out.Point(mesh.vertices[v].x, mesh.vertices[v].y);
                }
            }
            return out;
        }
        static void TransformInk(InkRegion& out, const InkRegion& ink, const float* m, double tx = 0, double ty = 0)
        {
            if (!ink.valid)
            {
                return;
            }
            if (m[3] == 0 && m[7] == 0 && m[15] == 1)
            {
                const double x0 = ink.x0 + tx;
                const double x1 = ink.x1 + tx;
                const double y0 = ink.y0 + ty;
                const double y1 = ink.y1 + ty;
                out.Point(m[0] * (m[0] >= 0 ? x0 : x1) + m[4] * (m[4] >= 0 ? y0 : y1) + m[12],
                          m[1] * (m[1] >= 0 ? x0 : x1) + m[5] * (m[5] >= 0 ? y0 : y1) + m[13]);
                out.Point(m[0] * (m[0] >= 0 ? x1 : x0) + m[4] * (m[4] >= 0 ? y1 : y0) + m[12],
                          m[1] * (m[1] >= 0 ? x1 : x0) + m[5] * (m[5] >= 0 ? y1 : y0) + m[13]);
                return;
            }
            double low = std::numeric_limits<double>::infinity(), high = -low;
            for (int corner = 0; corner < 4; corner++)
            {
                double x = (corner & 1 ? ink.x1 : ink.x0) + tx;
                double y = (corner & 2 ? ink.y1 : ink.y0) + ty;
                double w = m[3] * x + m[7] * y + m[15];
                low = std::min(low, w);
                high = std::max(high, w);
                if (std::abs(w) < 1e-9)
                {
                    throw std::invalid_argument("unbounded projected ink");
                }
                out.Point((m[0] * x + m[4] * y + m[12]) / w, (m[1] * x + m[5] * y + m[13]) / w);
            }
            if (low < 0 && high > 0)
            {
                throw std::invalid_argument("projection crosses infinity");
            }
        }
        static void TransformExternalInk(InkRegion& out, const std::array<float, 6>& bounds, const float* matrix)
        {
            double low = std::numeric_limits<double>::infinity(), high = -low;
            const int corners = bounds[2] == bounds[5] ? 4 : 8;
            for (int corner = 0; corner < corners; corner++)
            {
                const double x = bounds[corner & 1 ? 3 : 0], y = bounds[corner & 2 ? 4 : 1],
                             z = bounds[corner & 4 ? 5 : 2];
                const double w = matrix[3] * x + matrix[7] * y + matrix[11] * z + matrix[15];
                low = std::min(low, w);
                high = std::max(high, w);
                if (std::abs(w) < 1e-9)
                {
                    throw std::invalid_argument("unbounded external ink");
                }
                out.Point((matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12]) / w,
                          (matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13]) / w);
            }
            if (low < 0 && high > 0)
            {
                throw std::invalid_argument("external projection crosses infinity");
            }
        }
        static uint64_t Hash(uint64_t value)
        {
            value ^= value >> 30;
            value *= 0xbf58476d1ce4e5b9ULL;
            value ^= value >> 27;
            value *= 0x94d049bb133111ebULL;
            return value ^ (value >> 31);
        }
        static bool CellBounds(const InkRegion& b, int& x0, int& y0, int& x1, int& y1)
        {
            if (!b.valid || std::abs(b.x0) > 1e7 || std::abs(b.y0) > 1e7 || std::abs(b.x1) > 1e7 ||
                std::abs(b.y1) > 1e7)
            {
                return false;
            }
            x0 = int(std::floor(b.x0 / 32));
            y0 = int(std::floor(b.y0 / 32));
            x1 = int(std::floor(b.x1 / 32));
            y1 = int(std::floor(b.y1 / 32));
            return int64_t(x1 - x0 + 1) * (y1 - y0 + 1) <= 64;
        }
        static bool Disjoint(const InkRegion& a, const InkRegion& b)
        {
            return a.valid && b.valid && (a.x1 <= b.x0 || b.x1 <= a.x0 || a.y1 <= b.y0 || b.y1 <= a.y0);
        }
        template <class Resolve>
        void Order(const Frame& source, const Frame& target, const URUIPlanBindings& bindings, Resolve resolve)
        {
            order.resize(pending.size());
            // A compatible palette needs no spatial graph or per-node graph data.
            std::array<SortState, 8> palette{};
            uint32_t paletteCount = 0;
            uint32_t domain = 0;
            uint64_t mask = 0;
            bool graph = false;
            for (uint32_t n = 0; n < pending.size(); n++)
            {
                order[n] = n;
                const auto command = pending[n];
                const auto& c = source.commands[command];
                const auto d = Domain(c.texture, bindings);
                const auto index = source.directMaskIndices[command];
                const auto m = index >= 0 ? source.directMasks[index].texture : 0;
                if (!n)
                {
                    domain = d;
                }
                if (d != domain || (mask && m && mask != m))
                {
                    graph = true;
                    break;
                }
                if (m)
                {
                    mask = m;
                }
                uint32_t slot = 0;
                while (slot < paletteCount &&
                       (palette[slot].texture != c.texture || palette[slot].domain != d || palette[slot].mask != m))
                {
                    slot++;
                }
                if (slot == paletteCount)
                {
                    if (paletteCount == 8)
                    {
                        graph = true;
                        break;
                    }
                    palette[paletteCount++] = {c.texture, m, d, 0, 0};
                }
            }
            if (!graph)
            {
                return;
            }
            bool same = sortMemoEpoch == source.epoch && sortMemo.size() == pending.size();
            for (uint32_t n = 0; same && n < pending.size(); n++)
            {
                const auto command = pending[n];
                const auto& c = source.commands[command];
                const auto& key = sortMemo[n];
                const auto index = source.directMaskIndices[command];
                const auto m = index >= 0 ? source.directMasks[index].texture : 0;
                same = std::memcmp(&key.command, &c, sizeof(c)) == 0 &&
                       std::memcmp(key.matrix.data(), target.parameters[command].matrix, 16 * sizeof(float)) == 0 &&
                       key.mask == m && key.domain == Domain(c.texture, bindings);
            }
            if (same)
            {
                std::copy(sortMemoOrder.begin(), sortMemoOrder.end(), order.begin());
                return;
            }
            sortNodes.resize(pending.size());
            size_t capacity = 16;
            while (capacity < pending.size() * 2)
            {
                capacity *= 2;
            }
            if (sortStates.size() < capacity)
            {
                sortStates.resize(capacity);
            }
            if (++sortGeneration == 0)
            {
                for (auto& entry : sortStates)
                {
                    entry.generation = 0;
                }
                for (auto& entry : sortCells)
                {
                    entry.generation = 0;
                }
                sortGeneration = 1;
            }
            uint32_t stateCount = 0;
            bool oneDomain = true;
            bool oneMask = true;
            uint32_t firstDomain = 0;
            uint64_t firstMask = 0;
            for (uint32_t n = 0; n < pending.size(); n++)
            {
                const auto command = pending[n];
                order[n] = n;
                auto& node = sortNodes[n];
                node = {};
                node.command = command;
                const auto& c = source.commands[command];
                auto domain = Domain(c.texture, bindings);
                const auto maskIndex = source.directMaskIndices[command];
                const uint64_t mask = maskIndex >= 0 ? source.directMasks[maskIndex].texture : 0;
                if (mask)
                {
                    if (!firstMask)
                    {
                        firstMask = mask;
                    }
                    else if (firstMask != mask)
                    {
                        oneMask = false;
                    }
                }
                if (!n)
                {
                    firstDomain = domain;
                }
                else if (domain != firstDomain)
                {
                    oneDomain = false;
                }
                auto slot = size_t(Hash(c.texture ^ Hash(domain) ^ Hash(mask))) & (sortStates.size() - 1);
                while (sortStates[slot].generation == sortGeneration &&
                       (sortStates[slot].texture != c.texture || sortStates[slot].domain != domain ||
                        sortStates[slot].mask != mask))
                {
                    slot = (slot + 1) & (sortStates.size() - 1);
                }
                auto& state = sortStates[slot];
                if (state.generation != sortGeneration)
                {
                    state = {c.texture, mask, domain, sortGeneration, stateCount++};
                }
                node.state = state.value;
            }
            // Already one compatible palette: keep the original triangle order and
            // avoid sorting or rebuilding indices when only parameters change.
            if (stateCount <= 8 && oneDomain && oneMask)
            {
                return;
            }
            sortBuilds++;
            if (sortCells.size() < 32768)
            {
                sortCells.resize(32768);
            }
            sortLinks.clear();
            large.clear();
            uint32_t comparisons = 0;
            for (uint32_t n = 0; n < pending.size(); n++)
            {
                auto& node = sortNodes[n];
                const auto& c = source.commands[node.command];
                auto mesh = resolve(c.geometry);
                auto local = LocalBounds(mesh);
                try
                {
                    TransformInk(node.bounds, local, target.parameters[node.command].matrix);
                }
                catch (const std::invalid_argument&)
                {
                    node.bounds = {};
                    // Unbounded projection is an ordering barrier, not a culling claim.
                }
                auto dependency = [&](uint32_t previous)
                {
                    if (++comparisons > 262144)
                    {
                        return false;
                    }
                    const auto& prior = sortNodes[previous];
                    if (!Disjoint(node.bounds, prior.bounds))
                    {
                        node.depth = std::max(node.depth, prior.depth + (node.state == prior.state ? 0u : 1u));
                    }
                    return true;
                };
                int x0 = 0;
                int y0 = 0;
                int x1 = 0;
                int y1 = 0;
                bool small = CellBounds(node.bounds, x0, y0, x1, y1);
                if (!small)
                {
                    for (uint32_t previous = 0; previous < n; previous++)
                    {
                        if (!dependency(previous))
                        {
                            return;
                        }
                    }
                }
                else
                {
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            const uint64_t key = (uint64_t(uint32_t(x)) << 32) | uint32_t(y);
                            auto slot = size_t(Hash(key)) & (sortCells.size() - 1);
                            while (sortCells[slot].generation == sortGeneration && sortCells[slot].key != key)
                            {
                                slot = (slot + 1) & (sortCells.size() - 1);
                            }
                            const auto& cell = sortCells[slot];
                            if (cell.generation != sortGeneration)
                            {
                                continue;
                            }
                            for (int link = cell.head; link >= 0; link = sortLinks[link].next)
                            {
                                auto& prior = sortNodes[sortLinks[link].item];
                                if (prior.seen != n + 1)
                                {
                                    prior.seen = n + 1;
                                    if (!dependency(sortLinks[link].item))
                                    {
                                        return;
                                    }
                                }
                            }
                        }
                    }
                    for (auto previous : large)
                    {
                        if (!dependency(previous))
                        {
                            return;
                        }
                    }
                }
                if (!small)
                {
                    large.push_back(n);
                    continue;
                }
                if (sortLinks.size() + size_t(x1 - x0 + 1) * (y1 - y0 + 1) > 16384)
                {
                    return;
                }
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        const uint64_t key = (uint64_t(uint32_t(x)) << 32) | uint32_t(y);
                        auto slot = size_t(Hash(key)) & (sortCells.size() - 1);
                        while (sortCells[slot].generation == sortGeneration && sortCells[slot].key != key)
                        {
                            slot = (slot + 1) & (sortCells.size() - 1);
                        }
                        auto& cell = sortCells[slot];
                        if (cell.generation != sortGeneration)
                        {
                            cell = {key, sortGeneration, -1};
                        }
                        sortLinks.push_back({n, cell.head});
                        cell.head = int(sortLinks.size() - 1);
                    }
                }
            }
            std::sort(
                order.begin(), order.end(),
                [&](uint32_t a, uint32_t b)
                {
                    const auto& x = sortNodes[a];
                    const auto& y = sortNodes[b];
                    return x.depth != y.depth ? x.depth < y.depth : x.state != y.state ? x.state < y.state : a < b;
                });
            // Logical geometry IDs are immutable within an epoch. Keep the exact
            // inputs, not a hash-only dirty approximation or a resource pointer.
            sortMemoEpoch = 0;
            sortMemo.resize(pending.size());
            sortMemoOrder.assign(order.begin(), order.end());
            for (uint32_t n = 0; n < pending.size(); n++)
            {
                const auto command = pending[n];
                auto& key = sortMemo[n];
                key.command = source.commands[command];
                std::copy_n(target.parameters[command].matrix, 16, key.matrix.data());
                key.domain = Domain(key.command.texture, bindings);
                const auto index = source.directMaskIndices[command];
                key.mask = index >= 0 ? source.directMasks[index].texture : 0;
            }
            sortMemoEpoch = source.epoch;
        }
        // Replay the frozen semantic stream only up to the effect being prepared. Child
        // outputs are already frozen, so a parent's input includes their final ink.
        template <class Resolve> InkRegion Capture(Frame& frame, uint32_t stop, Resolve resolve)
        {
            if (!frame.captureStarted)
            {
                uint64_t maximum = 0;
                for (const auto& c : frame.layers)
                {
                    maximum = std::max(maximum, std::max(c.source, c.target));
                }
                if (maximum > 65536)
                {
                    throw std::invalid_argument("layer identity");
                }
                frame.layerInk.assign(size_t(maximum) + 1, InkRegion{});
                frame.createdLayers.assign(size_t(maximum) + 1, 0);
                frame.layerStack.clear();
                frame.layerStack.push_back(0);
                frame.captureStarted = true;
            }
            auto& layer = frame.captureLayer;
            auto paint = std::lower_bound(frame.paints.begin(), frame.paints.end(), frame.captureCommand,
                                          [](const URUIPaintCommand& value, uint32_t command)
                                          { return uint32_t(value.command_index) < command; });
            for (auto& n = frame.captureCommand; n <= frame.commands.size(); n++)
            {
                while (layer < frame.layers.size() && frame.layers[layer].command_index == int(n))
                {
                    const auto& c = frame.layers[layer];
                    if (layer == stop)
                    {
                        return frame.layerInk[c.source];
                    }
                    if (c.kind == 1)
                    {
                        if (!c.target)
                        {
                            throw std::invalid_argument("root layer push");
                        }
                        frame.layerInk[c.target] = {};
                        frame.createdLayers[c.target] = 1;
                        frame.layerStack.push_back(c.target);
                    }
                    else if (c.kind == 2)
                    {
                        if (frame.layerStack.size() < 2 || frame.layerStack.back() != c.source ||
                            frame.layerStack[frame.layerStack.size() - 2] != c.target)
                        {
                            throw std::invalid_argument("unbalanced layer");
                        }
                        frame.layerStack.pop_back();
                    }
                    else if (c.kind == 3)
                    {
                        if (!c.target)
                        {
                            throw std::invalid_argument("root mask");
                        }
                        frame.layerInk[c.target] = frame.layerInk[c.source];
                        frame.createdLayers[c.target] = 1;
                    }
                    else if (c.kind == 4)
                    {
                        if (!c.source)
                        {
                            InkRegion background;
                            const float region[4] = {float(c.flags ? c.clip_x : 0), float(c.flags ? c.clip_y : 0),
                                                     float(c.flags ? c.clip_w : frame.width),
                                                     float(c.flags ? c.clip_h : frame.height)};
                            background.Rect(region);
                            frame.layerInk[c.target].Join(background);
                        }
                        else
                        {
                            frame.layerInk[c.target].Join(frame.layerInk[c.source]);
                        }
                    }
                    else if (c.kind == 6 && !c.blend)
                    {
                        const int effect_index = frame.effectIndices[layer];
                        if (effect_index >= 0)
                        {
                            const auto& effect = frame.effects[effect_index];
                            auto& ink = frame.layerInk[c.source];
                            if (effect.kind == 1 || effect.kind == 2)
                            {
                                auto shifted = ink;
                                double halo = std::max(0.f, effect.sigma) * 3;
                                if (shifted.valid)
                                {
                                    shifted.x0 += effect.offset_x - halo;
                                    shifted.y0 += effect.offset_y - halo;
                                    shifted.x1 += effect.offset_x + halo;
                                    shifted.y1 += effect.offset_y + halo;
                                    ink.Join(shifted);
                                }
                            }
                            else if (effect.kind == 3)
                            {
                                const int index = frame.effectSlots[layer];
                                if (index < 0)
                                {
                                    throw std::invalid_argument("missing effect slot");
                                }
                                const auto& slot = frame.preparation[index];
                                if (!slot.prepared)
                                {
                                    throw std::invalid_argument("effect dependency not ready");
                                }
                                ink.Join(slot.ink);
                            }
                        }
                    }
                    layer++;
                }
                if (n == frame.commands.size())
                {
                    break;
                }
                const auto& command = frame.commands[n];
                while (paint != frame.paints.end() && uint32_t(paint->command_index) < n)
                {
                    ++paint;
                }
                if (frame.knownProviders[n])
                {
                    for (uint32_t item = 0; item < frame.externalCounts[n]; item++)
                    {
                        const auto& draw = frame.external_draws[frame.externalOffsets[n] + item];
                        if (!frame.external_geometry[draw.geometry_index].index_count)
                        {
                            continue;
                        }
                        TransformExternalInk(frame.layerInk[frame.layerStack.back()],
                                             frame.externalBounds[draw.geometry_index], draw.parameter.matrix);
                    }
                    continue;
                }
                if (paint != frame.paints.end() && uint32_t(paint->command_index) == n && paint->reserved == 0)
                {
                    // 任意回调的输出不能用作者底图的 AABB 证明。覆盖目标可见域，
                    // 并保留祖先邻域滤镜可能从屏幕外采入的范围；已知 provider 不走此分支。
                    double px = 0;
                    double py = 0;
                    for (const auto& effect : frame.effects)
                    {
                        if (effect.kind != 1 && effect.kind != 2)
                        {
                            continue;
                        }
                        const auto source = frame.layers[effect.layer_index].source;
                        if (std::find(frame.layerStack.begin(), frame.layerStack.end(), source) ==
                            frame.layerStack.end())
                        {
                            continue;
                        }
                        px += 3 * std::max(0.f, effect.sigma) + std::abs(effect.offset_x);
                        py += 3 * std::max(0.f, effect.sigma) + std::abs(effect.offset_y);
                    }
                    double left = -px;
                    double top = -py;
                    double right = frame.width + px;
                    double bottom = frame.height + py;
                    if (frame.rawFlags[n] & 1)
                    {
                        left = std::max(left, double(command.clip_x));
                        top = std::max(top, double(command.clip_y));
                        right = std::min(right, double(command.clip_x) + command.clip_w);
                        bottom = std::min(bottom, double(command.clip_y) + command.clip_h);
                    }
                    if (right > left && bottom > top)
                    {
                        auto& ink = frame.layerInk[frame.layerStack.back()];
                        ink.Point(left, top);
                        ink.Point(right, bottom);
                    }
                    continue;
                }
                auto geometry = resolve(command.geometry);
                auto local = LocalBounds(geometry);
                const int image = frame.imageSlots[n];
                if (image >= 0 && frame.preparation[image].prepared)
                {
                    local.Join(frame.preparation[image].ink);
                }
                TransformInk(frame.layerInk[frame.layerStack.back()], local, frame.matrices[n].data(), command.tx,
                             command.ty);
            }
            if (stop == UINT32_MAX)
            {
                if (frame.layerStack.size() != 1)
                {
                    throw std::invalid_argument("unbalanced layer end");
                }
                return frame.layerInk[0];
            }
            throw std::invalid_argument("missing capture operation");
        }
        bool NormalizedTexture(const Frame& frame, uint64_t id) const
        {
            if (!id)
            {
                return true;
            }
            for (const auto& resource : frame.resources)
            {
                if (resource.texture == id)
                {
                    return resource.domain == 0 && (resource.flags & 1);
                }
            }
            return false;
        }
        static bool AnalyticUnitQuad(const URUIPlanExternalGeometry& geometry, QuadMapping& mapping)
        {
            if (geometry.vertex_count != 4 || geometry.index_count != 6)
            {
                return false;
            }
            std::array<URUIVertex, 4> vertices{};
            std::array<int, 6> indices{};
            for (uint32_t n = 0; n < 4; n++)
            {
                const auto value = ReadExternalVertex(geometry, n);
                if (value.z != 0)
                {
                    return false;
                }
                auto& vertex = vertices[n];
                vertex.x = value.x;
                vertex.y = value.y;
                vertex.u = value.x;
                vertex.v = value.y;
                vertex.r = vertex.g = vertex.b = vertex.a = 255;
            }
            for (uint32_t n = 0; n < 6; n++)
            {
                indices[n] = int(ExternalIndex(geometry, n));
            }
            return DirectQuad(vertices.data(), 4, indices.data(), 6, mapping, true) &&
                   mapping.rectangle == std::array<float, 4>{0, 0, 1, 1};
        }
        template <class Resolve> void LowerDirectMasks(Frame& frame, Resolve resolve)
        {
            frame.directMasks.clear();
            frame.omittedLayers.assign(frame.createdLayers.size(), 0);
            std::fill(frame.omittedOperations.begin(), frame.omittedOperations.end(), 0);
            std::fill(frame.suppressedCommands.begin(), frame.suppressedCommands.end(), 0);
            std::fill(frame.directMaskIndices.begin(), frame.directMaskIndices.end(), -1);
            for (size_t n = 0; n + 7 < frame.layers.size(); n++)
            {
                const auto& a = frame.layers[n];
                const auto& b = frame.layers[n + 1];
                const auto& c = frame.layers[n + 2];
                const auto& d = frame.layers[n + 3];
                const auto& e = frame.layers[n + 4];
                const auto& f = frame.layers[n + 5];
                const auto& g = frame.layers[n + 6];
                const auto& h = frame.layers[n + 7];
                if (a.kind != 1 || b.kind != 1 || c.kind != 3 || d.kind != 2 || e.kind != 6 || f.kind != 4 ||
                    g.kind != 2 || h.kind != 5)
                {
                    continue;
                }
                const auto source = a.target;
                const auto mask = b.target;
                const auto saved = c.target;
                if (!source || !mask || !saved || source == mask || source == saved || mask == saved)
                {
                    continue;
                }
                if (c.source != mask || d.source != mask || d.target != source || e.source != source ||
                    e.target != source || e.mask != saved || e.blend != 1 || f.source != source || f.mask || f.blend ||
                    g.source != source || g.target != f.target || h.source != saved)
                {
                    continue;
                }
                if (!f.target && !(frame.resourceFlags & URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT))
                {
                    continue;
                }
                const int command = a.command_index;
                const int maskCommand = command + 1;
                const int end = command + 2;
                if (command < 0 || size_t(end) > frame.commands.size() || b.command_index != maskCommand ||
                    c.command_index != end || d.command_index != end || e.command_index != end ||
                    f.command_index != end || g.command_index != end || h.command_index != end)
                {
                    continue;
                }
                if (frame.command_layers[command] != source || frame.command_layers[maskCommand] != mask)
                {
                    continue;
                }
                auto paint =
                    std::lower_bound(frame.paints.begin(), frame.paints.end(), command,
                                     [](const URUIPaintCommand& p, int value) { return p.command_index < value; });
                if (paint != frame.paints.end() && paint->command_index == command && paint->reserved != 2)
                {
                    continue;
                }
                paint = std::lower_bound(frame.paints.begin(), frame.paints.end(), maskCommand,
                                         [](const URUIPaintCommand& p, int value) { return p.command_index < value; });
                const URUIPlanExternalDraw* analytic = nullptr;
                if (paint != frame.paints.end() && paint->command_index == maskCommand)
                {
                    if (!frame.knownProviders[maskCommand] || frame.externalCounts[maskCommand] != 1)
                    {
                        continue;
                    }
                    const auto& draw = frame.external_draws[frame.externalOffsets[maskCommand]];
                    if (draw.flags != 4)
                    {
                        continue;
                    }
                    analytic = &draw;
                }
                const auto& content = frame.commands[command];
                const auto& alpha = frame.commands[maskCommand];
                if (!NormalizedTexture(frame, content.texture) ||
                    (!analytic && !NormalizedTexture(frame, alpha.texture)))
                {
                    continue;
                }
                auto contentMesh = resolve(content.geometry);
                QuadMapping contentMapping;
                QuadMapping maskMapping;
                if (!DirectQuad(contentMesh.vertices, contentMesh.vertex_count, contentMesh.indices,
                                contentMesh.index_count, contentMapping, false))
                {
                    continue;
                }
                if (analytic)
                {
                    if (!AnalyticUnitQuad(frame.external_geometry[analytic->geometry_index], maskMapping))
                    {
                        continue;
                    }
                }
                else
                {
                    const auto maskMesh = resolve(alpha.geometry);
                    if (!DirectQuad(maskMesh.vertices, maskMesh.vertex_count, maskMesh.indices, maskMesh.index_count,
                                    maskMapping, true))
                    {
                        continue;
                    }
                }
                const auto parameter = analytic
                                           ? analytic->parameter
                                           : Parameter(alpha, frame.matrices[maskCommand], frame.uniforms[maskCommand]);
                DirectMask descriptor{};
                std::array<std::array<float, 4>, 4> inverse{};
                if (!DirectMaskRasterGrid(parameter.matrix, maskMapping) ||
                    !InverseMaskProjection(parameter.matrix, inverse))
                {
                    continue;
                }
                descriptor.command = uint32_t(command);
                descriptor.maskCommand = uint32_t(maskCommand);
                descriptor.texture = analytic ? 0 : alpha.texture;
                descriptor.composite = f;
                std::copy(inverse.begin(), inverse.end(), descriptor.values.begin());
                descriptor.values[4] = maskMapping.rectangle;
                if (analytic)
                {
                    // XY 投影只读取逆矩阵列0/1/3；列2存放解析羽化距离，无需扩大8texel契约。
                    std::copy_n(analytic->parameter.tint, 4, descriptor.values[2].data());
                    descriptor.values[5] = {analytic->parameter.uv[0], analytic->parameter.uv[1], 0, 8};
                    descriptor.values[6] = {0, 0, 0, 1};
                }
                else
                {
                    const auto& uniform = frame.uniforms[maskCommand];
                    for (int axis = 0; axis < 2; axis++)
                    {
                        auto& mapping = descriptor.values[5 + axis];
                        mapping = axis == 0 ? maskMapping.u : maskMapping.v;
                        mapping[0] *= uniform.uv[axis];
                        mapping[1] *= uniform.uv[axis];
                        mapping[2] = mapping[2] * uniform.uv[axis] + uniform.uv[2 + axis];
                    }
                    descriptor.values[5][3] = float(((alpha.flags >> 1) & 3) | (alpha.texture ? 0 : 4));
                    descriptor.values[6][3] = maskMapping.alpha * uniform.tint[3];
                }
                descriptor.values[7] = (alpha.flags & 1)
                                           ? std::array<float, 4>{float(alpha.clip_x), float(alpha.clip_y),
                                                                  float(double(alpha.clip_x) + alpha.clip_w),
                                                                  float(double(alpha.clip_y) + alpha.clip_h)}
                                           : std::array<float, 4>{-1e20f, -1e20f, 1e20f, 1e20f};
                frame.directMaskIndices[command] = int32_t(frame.directMasks.size());
                frame.directMasks.push_back(descriptor);
                frame.suppressedCommands[maskCommand] = 1;
                frame.omittedLayers[source] = frame.omittedLayers[mask] = frame.omittedLayers[saved] = 1;
                for (size_t operation = n; operation < n + 8; operation++)
                {
                    frame.omittedOperations[operation] = OmittedDirectMask;
                }
                n += 7;
            }
            frame.layer_indices.clear();
            for (uint32_t n = 0; n < frame.layers.size(); n++)
            {
                if (!frame.omittedOperations[n])
                {
                    frame.layer_indices.push_back(n);
                }
            }
        }
        static const Frame::BinaryMaskClip* BinaryClip(const Frame& frame, uint32_t index)
        {
            const auto found = std::lower_bound(frame.binaryMaskClips.begin(), frame.binaryMaskClips.end(), index,
                                                [](const Frame::BinaryMaskClip& clip, uint32_t layer)
                                                { return clip.layer_index < layer; });
            return found != frame.binaryMaskClips.end() && found->layer_index == index ? &*found : nullptr;
        }
        void LowerBinaryMaskClips(Frame& frame)
        {
            for (uint32_t n = 0; n + 6 < frame.layers.size(); n++)
            {
                bool omitted = false;
                for (uint32_t k = n; k < n + 7; k++)
                {
                    omitted |= frame.omittedOperations[k] != 0;
                }
                if (omitted)
                {
                    continue;
                }
                const auto& b = frame.layers[n];
                const auto& c = frame.layers[n + 1];
                const auto& d = frame.layers[n + 2];
                const auto& e = frame.layers[n + 3];
                const auto& f = frame.layers[n + 4];
                const auto& g = frame.layers[n + 5];
                const auto& h = frame.layers[n + 6];
                if (b.kind != 1 || c.kind != 3 || d.kind != 2 || e.kind != 6 || f.kind != 4 || g.kind != 2 ||
                    h.kind != 5)
                {
                    continue;
                }
                const auto source = e.source;
                const auto mask = b.target;
                const auto saved = c.target;
                if (!source || !mask || !saved || source == mask || source == saved || mask == saved ||
                    c.source != mask || d.source != mask || d.target != source || e.target != source ||
                    e.mask != saved || e.blend != 1 || f.source != source || f.mask || f.blend || g.source != source ||
                    g.target != f.target || h.source != saved)
                {
                    continue;
                }
                const auto command = b.command_index;
                const auto end = c.command_index;
                if (command < 0 || end != command + 1 || size_t(end) > frame.commands.size() ||
                    d.command_index != end || e.command_index != end || f.command_index != end ||
                    g.command_index != end || h.command_index != end)
                {
                    continue;
                }
                // 根输出必须与 UI 像素一一对应；缩放提交会对原遮罩边缘作双线性重采样。
                if (!f.target && !(frame.resourceFlags & URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT))
                {
                    continue;
                }
                if (frame.command_layers[command] != mask || !frame.knownProviders[command] ||
                    frame.externalCounts[command] != 1)
                {
                    continue;
                }
                const auto& maskUsage = frame.maskCropUsage[mask];
                const auto& savedUsage = frame.maskCropUsage[saved];
                if (maskUsage.creates != 1 || maskUsage.reads != 1 || savedUsage.creates != 1 || savedUsage.reads != 1)
                {
                    continue;
                }
                const auto& draw = frame.external_draws[frame.externalOffsets[command]];
                if (draw.flags != 4)
                {
                    continue;
                }
                std::array<float, 4> bounds{};
                if (!BinaryRectangleClip(draw.parameter, f, bounds))
                {
                    continue;
                }
                frame.binaryMaskClips.push_back({n + 4, bounds});
                frame.suppressedCommands[command] = 1;
                frame.omittedLayers[mask] = frame.omittedLayers[saved] = 1;
                for (uint32_t offset : {0u, 1u, 2u, 3u, 6u})
                {
                    frame.omittedOperations[n + offset] = OmittedBinaryMask;
                }
                n += 6;
            }
            frame.layer_indices.clear();
            for (uint32_t n = 0; n < frame.layers.size(); n++)
            {
                if (!frame.omittedOperations[n])
                {
                    frame.layer_indices.push_back(n);
                }
            }
        }
        void PlanMaskCopies(Frame& frame)
        {
            const auto count = frame.createdLayers.size();
            frame.lastLayerWrite.assign(count, 0);
            frame.maskSaveSequence.assign(count, 0);
            frame.maskCopySources.assign(count, 0);
            frame.maskCopyCounts.assign(count, 0);
            frame.maskCopyState.assign(count, 0);
            uint64_t sequence = 0;
            size_t operation = 0;
            for (size_t command = 0; command <= frame.commands.size(); command++)
            {
                while (operation < frame.layers.size() && frame.layers[operation].command_index == int(command))
                {
                    const auto& layer = frame.layers[operation++];
                    sequence++;
                    if (layer.kind == URUI_LAYER_PUSH || layer.kind == URUI_LAYER_SAVE_MASK ||
                        layer.kind == URUI_LAYER_COMPOSITE)
                    {
                        frame.lastLayerWrite[layer.target] = sequence;
                    }
                    else if (layer.kind == URUI_LAYER_APPLY_MASK)
                    {
                        frame.lastLayerWrite[layer.source] = sequence;
                    }
                    if (layer.kind == URUI_LAYER_SAVE_MASK)
                    {
                        frame.maskCopySources[layer.target] = uint32_t(layer.source);
                        frame.maskCopyCounts[layer.target]++;
                        frame.maskSaveSequence[layer.target] = sequence;
                    }
                }
                if (command < frame.commands.size())
                {
                    frame.lastLayerWrite[frame.command_layers[command]] = ++sequence;
                }
            }
            // A mask snapshot uses the physical source allocation, including frozen
            // custom-effect capture padding. This does not feed padding into parent ink.
            for (uint32_t layer = 1; layer < count; layer++)
            {
                frame.layerStack.clear();
                uint32_t current = layer;
                while (frame.maskCopyCounts[current] == 1 && frame.maskCopyState[current] == 0)
                {
                    frame.maskCopyState[current] = 1;
                    frame.layerStack.push_back(current);
                    current = frame.maskCopySources[current];
                }
                if (frame.maskCopyState[current] == 1)
                {
                    throw std::invalid_argument("cyclic mask copy");
                }
                while (!frame.layerStack.empty())
                {
                    const auto target = uint32_t(frame.layerStack.back());
                    frame.layerStack.pop_back();
                    const auto source = frame.maskCopySources[target];
                    frame.layerInk[target] = frame.layerInk[source];
                    frame.maskCopyState[target] = 2;
                }
            }
        }
        void ConstrainTerminalMasks(Frame& frame)
        {
            // 只有最终遮罩之外的像素不再被任何消费者读取时，才能收紧共用的物理目标。
            // 先计数整帧读取，避免对每个遮罩重复扫描全部图层。
            auto& usage = frame.maskCropUsage;
            usage.assign(frame.createdLayers.size(), MaskCropUsage{});
            for (uint32_t n = 0; n < frame.layers.size(); n++)
            {
                if (!frame.omittedOperations[n])
                {
                    const auto& op = frame.layers[n];
                    if (op.kind == 1 || op.kind == 3)
                    {
                        usage[op.target].creates++;
                    }
                    if (op.kind == 3 || op.kind == 4 || op.kind == 6)
                    {
                        usage[op.source].reads++;
                    }
                    if ((op.kind == 4 || (op.kind == 6 && op.blend == 1)) && op.mask)
                    {
                        if (op.mask >= usage.size())
                        {
                            throw std::out_of_range("mask layer");
                        }
                        usage[op.mask].reads++;
                    }
                }
            }
            for (const auto& paint : frame.paints)
            {
                if (paint.reserved == 0 && !frame.knownProviders[paint.command_index])
                {
                    usage[frame.command_layers[paint.command_index]].opaque = true;
                }
            }
            for (const auto& slot : frame.preparation)
            {
                if (!slot.image)
                {
                    usage[slot.request.layer].opaque = true;
                }
            }
            const auto& order = frame.layer_indices;
            for (size_t n = 0; n + 7 < order.size(); n++)
            {
                const auto& a = frame.layers[order[n]];
                const auto& b = frame.layers[order[n + 1]];
                const auto& c = frame.layers[order[n + 2]];
                const auto& d = frame.layers[order[n + 3]];
                const auto& e = frame.layers[order[n + 4]];
                const auto& f = frame.layers[order[n + 5]];
                const auto& g = frame.layers[order[n + 6]];
                const auto& h = frame.layers[order[n + 7]];
                if (a.kind != 1 || b.kind != 1 || c.kind != 3 || d.kind != 2 || e.kind != 6 || f.kind != 4 ||
                    g.kind != 2 || h.kind != 5)
                {
                    continue;
                }
                const auto source = a.target;
                const auto mask = b.target;
                const auto saved = c.target;
                if (!source || !mask || !saved || source == mask || source == saved || mask == saved)
                {
                    continue;
                }
                if (c.source != mask || d.source != mask || d.target != source || e.source != source ||
                    e.target != source || e.mask != saved || e.blend != 1 || f.source != source || f.mask || f.blend ||
                    g.source != source || g.target != f.target || h.source != saved)
                {
                    continue;
                }
                if (a.command_index > b.command_index || b.command_index >= c.command_index ||
                    d.command_index != c.command_index || e.command_index != c.command_index ||
                    f.command_index != c.command_index || g.command_index != c.command_index ||
                    h.command_index != c.command_index)
                {
                    continue;
                }
                if (usage[source].creates != 1 || usage[source].reads != 2 || usage[source].opaque ||
                    usage[mask].creates != 1 || usage[saved].creates != 1)
                {
                    continue;
                }
                // 遮罩矩形外的像素为零，但最终双线性采样仍可能读到相邻透明纹素。
                // 每边保留一个纹素，避免缩放输出时被 Clamp 扩大硬边或截断半像素覆盖。
                // 邻域滤镜、提前保存/读取和任意捕获不满足上述证明。
                float rectangle[4];
                frame.layerInk[saved].Capture(rectangle);
                rectangle[0] -= 1;
                rectangle[1] -= 1;
                rectangle[2] += 2;
                rectangle[3] += 2;
                auto& ink = frame.layerInk[source];
                if (ink.valid)
                {
                    ink.x0 = std::max(ink.x0, double(rectangle[0]));
                    ink.y0 = std::max(ink.y0, double(rectangle[1]));
                    ink.x1 = std::min(ink.x1, double(rectangle[0]) + rectangle[2]);
                    ink.y1 = std::min(ink.y1, double(rectangle[1]) + rectangle[3]);
                    if (ink.x1 <= ink.x0 || ink.y1 <= ink.y0)
                    {
                        ink = {};
                    }
                }
            }
        }
        void BuildLayerDependencies(Frame& frame)
        {
            if (frame.layers.empty())
            {
                return;
            }
            const auto count = frame.createdLayers.size();
            dependencyCache.Begin(count);
            while (frame.dependencies.size() < count)
            {
                frame.dependencies.emplace_back(&frameBudget);
            }
            for (size_t layer = 0; layer < count; layer++)
            {
                auto& state = frame.dependencies[layer];
                state.Reset();
                state.Add(frame.epoch);
                state.Add(frame.resourceFlags);
                state.Add(frame.width);
                state.Add(frame.height);
                if (layer)
                {
                    float bounds[4];
                    frame.layerInk[layer].Capture(bounds);
                    state.Bytes(bounds, sizeof(bounds));
                }
            }
            // A final source cannot replace an earlier snapshot if that layer
            // is written again later. Keep only that dependency chain live.
            uint64_t sequence = 0;
            size_t flow = 0;
            for (uint32_t command = 0; command <= frame.commands.size(); command++)
            {
                while (flow < frame.layers.size() && frame.layers[flow].command_index == int(command))
                {
                    const auto& op = frame.layers[flow++];
                    sequence++;
                    auto forbid = [&](uint64_t layer)
                    {
                        if (layer && frame.lastLayerWrite[layer] > sequence)
                        {
                            frame.dependencies[layer].dynamic = true;
                        }
                    };
                    if (op.kind == 3 || (op.kind == 4 && op.source != op.target))
                    {
                        forbid(op.source);
                    }
                    if (op.kind == 6 && op.blend == 1)
                    {
                        forbid(op.mask);
                    }
                }
                if (command < frame.commands.size())
                {
                    sequence++;
                }
            }
            size_t operation = 0;
            size_t paint = 0;
            auto version = [&](uint64_t id) { return dependencyCache.Version(frame.dependencies[id]); };
            auto descriptor = [](LayerDependencyState& state, const URUILayerCommand& op)
            {
                state.Add(uint64_t(op.kind));
                state.Add(uint64_t(op.blend));
                state.Add(uint64_t(op.flags));
                state.Add(uint64_t(op.clip_x));
                state.Add(uint64_t(op.clip_y));
                state.Add(uint64_t(op.clip_w));
                state.Add(uint64_t(op.clip_h));
            };
            for (uint32_t command = 0; command <= frame.commands.size(); command++)
            {
                while (operation < frame.layers.size() && frame.layers[operation].command_index == int(command))
                {
                    const auto index = operation++;
                    if (frame.omittedOperations[index] == OmittedBinaryMask)
                    {
                        continue;
                    }
                    const auto& op = frame.layers[index];
                    auto& source = frame.dependencies[op.source];
                    auto& target = frame.dependencies[op.target];
                    if (op.kind == 3)
                    {
                        target.words = source.words;
                        target.version = source.version;
                        target.dynamic |= source.dynamic;
                    }
                    else if (op.kind == 4 && op.target)
                    {
                        if (!op.source)
                        {
                            target.dynamic = true;
                        }
                        else
                        {
                            const auto child = version(op.source);
                            target.Add(child);
                            target.dynamic |= source.dynamic;
                        }
                        if (const auto* clip = BinaryClip(frame, uint32_t(index)))
                        {
                            auto effective = op;
                            effective.flags |= 1;
                            effective.clip_x = int(clip->bounds[0]);
                            effective.clip_y = int(clip->bounds[1]);
                            effective.clip_w = int(clip->bounds[2] - clip->bounds[0]);
                            effective.clip_h = int(clip->bounds[3] - clip->bounds[1]);
                            descriptor(target, effective);
                        }
                        else
                        {
                            descriptor(target, op);
                        }
                        if (op.mask)
                        {
                            const auto mask = version(op.mask);
                            target.Add(mask);
                            target.dynamic |= frame.dependencies[op.mask].dynamic;
                        }
                    }
                    else if (op.kind == 6)
                    {
                        descriptor(source, op);
                        if (op.blend == 1)
                        {
                            source.Add(version(op.mask));
                            source.dynamic |= frame.dependencies[op.mask].dynamic;
                        }
                        else
                        {
                            source.Add(op.mask);
                            const auto effect = frame.effectIndices[index];
                            if (effect < 0 || frame.effects[effect].kind == 3 ||
                                (frame.effects[effect].kind == 0 && !frame.effects[effect].cacheable))
                            {
                                source.dynamic = true;
                            }
                            if (effect >= 0)
                            {
                                const auto& value = frame.effects[effect];
                                source.Add(value.kind);
                                source.Bytes(&value.sigma, sizeof(float) * 4);
                            }
                        }
                    }
                }
                if (command == frame.commands.size())
                {
                    break;
                }
                const auto layer = frame.command_layers[command];
                if (!layer)
                {
                    continue;
                }
                auto& state = frame.dependencies[layer];
                const auto& draw = frame.commands[command];
                if (frame.knownProviders[command])
                {
                    state.Add(0x455854);
                    for (uint32_t item = 0; item < frame.externalCounts[command]; item++)
                    {
                        const auto& external = frame.external_draws[frame.externalOffsets[command] + item];
                        const auto& geometry = frame.external_geometry[external.geometry_index];
                        state.Add(geometry.geometry);
                        state.Add(geometry.version);
                        state.Add(external.program);
                        state.Add(external.flags);
                        state.dynamic |= (external.flags & 3) != 0;
                        state.Bytes(&external.parameter, sizeof(external.parameter));
                    }
                    continue;
                }
                state.Add(0x44524157);
                state.Add(draw.geometry);
                state.Add(draw.texture);
                const auto parameter = Parameter(draw, frame.matrices[command], frame.uniforms[command]);
                state.Bytes(&parameter, sizeof(parameter));
                if (draw.texture)
                {
                    auto resource = std::find_if(frame.resources.begin(), frame.resources.end(),
                                                 [&](const auto& value) { return value.texture == draw.texture; });
                    if (resource == frame.resources.end())
                    {
                        state.dynamic = true;
                    }
                    else
                    {
                        state.Add(resource->version);
                        state.Add(resource->domain);
                        state.Add(resource->flags);
                        state.dynamic |= resource->domain != 0;
                    }
                }
                while (paint < frame.paints.size() && uint32_t(frame.paints[paint].command_index) < command)
                {
                    paint++;
                }
                if (paint < frame.paints.size() && uint32_t(frame.paints[paint].command_index) == command)
                {
                    const auto& program = frame.paints[paint];
                    state.Add(uint64_t(program.reserved));
                    if (program.reserved != 2)
                    {
                        state.dynamic = true;
                    }
                    else
                    {
                        for (uint32_t n = 0; n < frame.colorCounts[command]; n++)
                        {
                            const auto& field = frame.colorFilters[frame.colorOffsets[command] + n];
                            state.Add(field.kind);
                            state.Bytes(&field.value, sizeof(float));
                        }
                    }
                }
            }
        }
        template <class Resolve> void CompleteRegions(Frame& frame, Resolve resolve)
        {
            frame.layerRegions.clear();
            if (frame.layers.empty())
            {
                frame.ready = true;
                return;
            }
            Capture(frame, UINT32_MAX, resolve);
            LowerDirectMasks(frame, resolve);
            // Ink propagation above is complete. Allocation padding must stay local
            // to its layer, so union captures only now and never inflate parent ink.
            for (const auto& slot : frame.preparation)
            {
                if (!slot.image)
                {
                    frame.layerInk[slot.request.layer].Rect(slot.request.capture);
                    frame.createdLayers[slot.request.layer] |= 8;
                }
            }
            for (const auto& operation : frame.layers)
            {
                if (operation.kind == 3)
                {
                    frame.createdLayers[operation.source] |= 2;
                }
            }
            for (const auto& effect : frame.effects)
            {
                if (effect.kind == 1 || effect.kind == 2)
                {
                    frame.createdLayers[frame.layers[effect.layer_index].source] |= 4;
                }
            }
            PlanMaskCopies(frame);
            ConstrainTerminalMasks(frame);
            LowerBinaryMaskClips(frame);
            BuildLayerDependencies(frame);
            for (size_t layer = 1; layer < frame.createdLayers.size(); layer++)
            {
                if (frame.createdLayers[layer] && !frame.omittedLayers[layer])
                {
                    URUIPlanLayerRegion result{};
                    result.layer = layer;
                    result.flags = frame.createdLayers[layer] >> 1;
                    frame.layerInk[layer].Capture(result.bounds);
                    const auto source = frame.maskCopySources[layer];
                    const auto saved = frame.maskSaveSequence[layer];
                    if (frame.maskCopyCounts[layer] == 1 && source != layer && frame.lastLayerWrite[source] < saved &&
                        frame.lastLayerWrite[layer] == saved)
                    {
                        result.flags |= 8;
                        result.reserved = source;
                    }
                    result.version = dependencyCache.Version(frame.dependencies[layer]);
                    if (!frame.dependencies[layer].dynamic)
                    {
                        result.flags |= 16;
                    }
                    frame.layerRegions.push_back(result);
                }
            }
            frame.ready = true;
        }
        template <class T> void ResizeSource(Buffer<T>& data, size_t size)
        {
            if (size > data.capacity())
            {
                if (size > geometryBudget.limit / sizeof(T))
                {
                    throw std::bad_alloc();
                }
                const auto bytes = size * sizeof(T);
                while (bytes > geometryBudget.limit - geometryBudget.retained)
                {
                    size_t victim = SIZE_MAX;
                    for (size_t n = 0; n < batches.size(); n++)
                    {
                        const auto& batch = batches[n];
                        if (InUse(batch.id) ||
                            (!batch.keys.capacity() && !batch.vertices.capacity() && !batch.indices.capacity()))
                        {
                            continue;
                        }
                        if (victim == SIZE_MAX || batch.lastUse < batches[victim].lastUse)
                        {
                            victim = n;
                        }
                    }
                    if (victim == SIZE_MAX)
                    {
                        throw std::bad_alloc();
                    }
                    Drop(batches[victim]);
                }
                // 明确扩容大小，避免 vector 的隐式倍增超出已核对的预算。
                data.reserve(size);
            }
            data.resize(size);
        }
        void ComposeSource(SourceSnapshot& snapshot)
        {
            if (snapshot.composedVersion == snapshot.version)
            {
                return;
            }
            ResizeSource(snapshot.vertices, snapshot.baseVertices.size());
            const auto& uniform = snapshot.uniform;
            for (size_t n = 0; n < snapshot.baseVertices.size(); n++)
            {
                const auto& v = snapshot.baseVertices[n];
                auto color = [](uint8_t value, float tint)
                { return uint8_t(std::clamp(std::lround(value * tint), 0l, 255l)); };
                snapshot.vertices[n] = {v.x,
                                        v.y,
                                        0,
                                        color(v.r, uniform.tint[0]),
                                        color(v.g, uniform.tint[1]),
                                        color(v.b, uniform.tint[2]),
                                        color(v.a, uniform.tint[3]),
                                        v.u * uniform.uv[0] + uniform.uv[2],
                                        v.v * uniform.uv[1] + uniform.uv[3],
                                        0,
                                        0};
            }
            snapshot.composedVersion = snapshot.version;
        }
        template <class Resolve> void PrepareSource(Frame& frame, PrepareSlot& slot, Resolve resolve)
        {
            while (frame.sources.size() <= slot.source_index)
            {
                frame.sources.emplace_back(&geometryBudget);
            }
            auto& snapshot = frame.sources[slot.source_index];
            const auto geometry = frame.commands[slot.request.slot].geometry;
            const auto& uniform = frame.uniforms[slot.request.slot];
            for (int n = 0; n < 4; n++)
            {
                if (!std::isfinite(uniform.tint[n]) || !std::isfinite(uniform.uv[n]))
                {
                    throw std::invalid_argument("source uniform");
                }
            }
            if (snapshot.geometry != geometry || snapshot.epoch != frame.epoch)
            {
                const auto source = resolve(geometry);
                if (!source.vertices || !source.indices)
                {
                    throw std::out_of_range("source geometry expired");
                }
                snapshot.version = 0;
                snapshot.composedVersion = 0;
                ResizeSource(snapshot.baseVertices, source.vertex_count);
                ResizeSource(snapshot.indices, source.index_count);
                std::copy_n(source.vertices, source.vertex_count, snapshot.baseVertices.data());
                for (size_t n = 0; n < source.index_count; n++)
                {
                    if (source.indices[n] < 0 || size_t(source.indices[n]) >= source.vertex_count)
                    {
                        throw std::invalid_argument("source geometry index");
                    }
                    snapshot.indices[n] = uint32_t(source.indices[n]);
                }
                snapshot.geometry = geometry;
                snapshot.epoch = frame.epoch;
            }
            if (!snapshot.version || std::memcmp(&snapshot.uniform, &uniform, sizeof(uniform)))
            {
                const URUIPlanUniform identity{{1, 1, 1, 1}, {1, 1, 0, 0}};
                if (std::memcmp(&identity, &uniform, sizeof(uniform)) == 0)
                {
                    snapshot.version = geometry;
                }
                else
                {
                    const auto next = Identity();
                    if (next >= (uint64_t{1} << 63))
                    {
                        throw std::overflow_error("source geometry identity exhausted");
                    }
                    snapshot.version = next | (uint64_t{1} << 63);
                }
                snapshot.uniform = uniform;
                snapshot.composedVersion = 0;
            }
            slot.request.geometry = snapshot.version;
            slot.sourceReady = true;
        }
        uint32_t Domain(uint64_t texture, const URUIPlanBindings& bindings) const
        {
            for (uint32_t i = 0; i < bindings.texture_count; i++)
            {
                if (bindings.textures[i].texture == texture)
                {
                    return bindings.textures[i].domain;
                }
            }
            return 0;
        }
        bool InUse(uint64_t id) const
        {
            if (!id)
            {
                return false;
            }
            for (const auto& frame : frames)
            {
                if ((&frame == compiling) || (frame.token && !frame.input))
                {
                    for (const auto& operation : frame.operations)
                    {
                        if (operation.geometry == id)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        void CollectRetiredEpochs()
        {
            if (!retiredEpochBatches)
            {
                return;
            }
            retiredEpochBatches = false;
            for (auto& batch : batches)
            {
                if (batch.id && batch.epoch != epoch)
                {
                    if (InUse(batch.id))
                    {
                        retiredEpochBatches = true;
                        continue;
                    }
                    // 旧文档世代不会再次命中；仅撤销身份，空闲容量由现有预算和空槽路径复用。
                    // 真实 GPU 租约仍持有的版本不进入此分支。
                    batch.id = 0;
                    batch.lastUse = 0;
                }
            }
        }
        size_t Reclaimable(size_t except = SIZE_MAX) const
        {
            size_t best = SIZE_MAX;
            for (size_t n = 0; n < batches.size(); n++)
            {
                if (n != except && !InUse(batches[n].id) &&
                    (best == SIZE_MAX || batches[n].lastUse < batches[best].lastUse))
                {
                    best = n;
                }
            }
            return best;
        }
        void Drop(Batch& batch)
        {
            batch.id = 0;
            batch.lastUse = 0;
            Buffer<GeometryKey>(Allocator<GeometryKey>(&geometryBudget)).swap(batch.keys);
            Buffer<URUIPlanVertex>(Allocator<URUIPlanVertex>(&geometryBudget)).swap(batch.vertices);
            Buffer<uint32_t>(Allocator<uint32_t>(&geometryBudget)).swap(batch.indices);
        }
        static void ApplyLayoutOrigins(const Batch& batch, const Frame& source, Frame& target)
        {
            for (const auto& key : batch.keys)
            {
                if (key.texture & 0x80000000u)
                {
                    const auto index = key.parameter;
                    auto& parameter = target.parameters[index];
                    const auto& matrix = source.matrices[index];
                    const double dx = double(source.commands[index].tx) - key.originX,
                                 dy = double(source.commands[index].ty) - key.originY;
                    for (int row = 0; row < 4; row++)
                    {
                        const double value = matrix[12 + row] + double(matrix[row]) * dx + double(matrix[4 + row]) * dy;
                        if (!std::isfinite(value) || std::abs(value) > std::numeric_limits<float>::max())
                        {
                            throw std::invalid_argument("layout delta matrix");
                        }
                        parameter.matrix[12 + row] = float(value);
                    }
                }
            }
        }
        template <class Resolve>
        Batch& Geometry(uint64_t e, Resolve resolve, const Frame* sourceFrame = nullptr, Frame* targetFrame = nullptr)
        {
            const auto hint = geometryHintCursor++;
            if (hint == geometryHints.size())
            {
                geometryHints.push_back(SIZE_MAX);
            }
            auto reuse = [&](size_t index) -> Batch&
            {
                auto& batch = batches[index];
                geometryHints[hint] = index;
                batch.lastUse = Identity();
                if (sourceFrame && targetFrame)
                {
                    ApplyLayoutOrigins(batch, *sourceFrame, *targetFrame);
                }
                return batch;
            };
            // 帧内调用顺序通常稳定；提示只避免重复搜索，不作为身份或内容的依据。
            const auto previous = geometryHints[hint];
            if (previous < batches.size())
            {
                const auto& batch = batches[previous];
                geometrySearches++;
                if (batch.id && batch.epoch == e && batch.keys == keys)
                {
                    return reuse(previous);
                }
            }
            for (size_t index = 0; index < batches.size(); index++)
            {
                const auto& batch = batches[index];
                geometrySearches++;
                if (batch.id && batch.epoch == e && batch.keys == keys)
                {
                    return reuse(index);
                }
            }
            size_t nv = 0;
            size_t ni = 0;
            for (const auto& key : keys)
            {
                const auto source = resolve(key.geometry, key.version);
                if ((!source.external && (!source.vertices || !source.indices)) ||
                    source.vertex_count > geometryBudget.limit / sizeof(URUIPlanVertex) ||
                    source.index_count > geometryBudget.limit / sizeof(uint32_t))
                {
                    throw std::invalid_argument("geometry expired");
                }
                nv += source.vertex_count;
                ni += source.index_count;
            }
            size_t selected = SIZE_MAX;
            for (size_t n = 0; n < batches.size(); n++)
            {
                if (!batches[n].id)
                {
                    selected = n;
                    break;
                }
            }
            // Versioned source payloads may change every frame without changing
            // capacity. Recycle only unleased storage for the same source slots.
            if (selected == SIZE_MAX)
            {
                for (size_t n = 0; n < batches.size(); n++)
                {
                    const auto& candidate = batches[n];
                    if (candidate.epoch != e || candidate.keys.size() != keys.size() || InUse(candidate.id))
                    {
                        continue;
                    }
                    bool same = true;
                    bool changed = false;
                    for (size_t key = 0; key < keys.size(); key++)
                    {
                        const auto& old = candidate.keys[key];
                        const auto& next = keys[key];
                        if (old.geometry != next.geometry || old.parameter != next.parameter ||
                            old.texture != next.texture)
                        {
                            same = false;
                            break;
                        }
                        changed |= old.version != next.version;
                    }
                    if (same && changed && (selected == SIZE_MAX || candidate.lastUse < batches[selected].lastUse))
                    {
                        selected = n;
                    }
                }
            }
            const size_t payload =
                keys.size() * sizeof(GeometryKey) + nv * sizeof(URUIPlanVertex) + ni * sizeof(uint32_t);
            if (selected == SIZE_MAX)
            {
                const size_t capacity = batches.size() == batches.capacity()
                                            ? std::max(size_t(1), batches.capacity() * 2)
                                            : batches.capacity();
                const size_t metadata = batches.size() == batches.capacity() ? capacity * sizeof(Batch) : 0;
                if (payload + metadata <= geometryBudget.limit - geometryBudget.retained)
                {
                    if (capacity > batches.capacity())
                    {
                        batches.reserve(capacity);
                    }
                    selected = batches.size();
                    batches.emplace_back(&geometryBudget);
                }
                else
                {
                    selected = Reclaimable();
                }
            }
            if (selected == SIZE_MAX)
            {
                throw std::bad_alloc();
            }
            auto& batch = batches[selected];
            batch.id = 0;
            // Recycle buffers without shrinking when capacity suffices. Only actual
            // budget pressure retires unpublished versions; leased/in-build IDs are pinned.
            if (batch.keys.capacity() < keys.size())
            {
                Buffer<GeometryKey>(Allocator<GeometryKey>(&geometryBudget)).swap(batch.keys);
            }
            if (batch.vertices.capacity() < nv)
            {
                Buffer<URUIPlanVertex>(Allocator<URUIPlanVertex>(&geometryBudget)).swap(batch.vertices);
            }
            if (batch.indices.capacity() < ni)
            {
                Buffer<uint32_t>(Allocator<uint32_t>(&geometryBudget)).swap(batch.indices);
            }
            auto required = [&]()
            {
                return (batch.keys.capacity() < keys.size() ? keys.size() * sizeof(GeometryKey) : 0) +
                       (batch.vertices.capacity() < nv ? nv * sizeof(URUIPlanVertex) : 0) +
                       (batch.indices.capacity() < ni ? ni * sizeof(uint32_t) : 0);
            };
            while (required() > geometryBudget.limit - geometryBudget.retained)
            {
                auto victim = Reclaimable(selected);
                if (victim == SIZE_MAX)
                {
                    throw std::bad_alloc();
                }
                // Empty entries cannot release further storage.
                if (!batches[victim].keys.capacity() && !batches[victim].vertices.capacity() &&
                    !batches[victim].indices.capacity())
                {
                    size_t next = SIZE_MAX;
                    for (size_t n = 0; n < batches.size(); n++)
                    {
                        if (n != selected && !InUse(batches[n].id) &&
                            (batches[n].keys.capacity() || batches[n].vertices.capacity() ||
                             batches[n].indices.capacity()))
                        {
                            next = n;
                            break;
                        }
                    }
                    if (next == SIZE_MAX)
                    {
                        throw std::bad_alloc();
                    }
                    victim = next;
                }
                Drop(batches[victim]);
            }
            try
            {
                batch.keys = keys;
                batch.vertices.resize(nv);
                batch.indices.resize(ni);
                size_t v = 0;
                size_t k = 0;
                for (auto& key : batch.keys)
                {
                    const auto source = resolve(key.geometry, key.version);
                    if ((key.texture & 0x80000000u) && !source.external)
                    {
                        // 只有能无损还原局部坐标的布局原点才进入不可变顶点。
                        for (size_t n = 0; n < source.vertex_count; n++)
                        {
                            const auto& p = source.vertices[n];
                            const float x = p.x + key.originX;
                            const float y = p.y + key.originY;
                            if (!std::isfinite(x) || x - key.originX != p.x)
                            {
                                key.originX = 0;
                            }
                            if (!std::isfinite(y) || y - key.originY != p.y)
                            {
                                key.originY = 0;
                            }
                        }
                    }
                    for (size_t n = 0; n < source.vertex_count; n++)
                    {
                        if (source.external)
                        {
                            auto value = ReadExternalVertex(*source.external, uint32_t(n));
                            value.texture = float(key.texture & 7);
                            value.parameter = float(key.parameter);
                            batch.vertices[v + n] = value;
                        }
                        else
                        {
                            const auto& p = source.vertices[n];
                            const bool layout = (key.texture & 0x80000000u) != 0;
                            const float x = p.x + (layout ? key.originX : 0);
                            const float y = p.y + (layout ? key.originY : 0);
                            if (!std::isfinite(x) || !std::isfinite(y))
                            {
                                throw std::invalid_argument("layout vertex");
                            }
                            batch.vertices[v + n] = {
                                x, y, 0, p.r, p.g, p.b, p.a, p.u, p.v, float(key.texture & 7), float(key.parameter)};
                        }
                    }
                    for (size_t n = 0; n < source.index_count; n++)
                    {
                        const auto index = source.external ? ExternalIndex(*source.external, uint32_t(n))
                                                           : uint32_t(source.indices[n]);
                        if (index >= source.vertex_count || v > UINT32_MAX - index)
                        {
                            throw std::invalid_argument("geometry index");
                        }
                        batch.indices[k + n] = uint32_t(v) + index;
                    }
                    v += source.vertex_count;
                    k += source.index_count;
                }
                const auto id = Identity();
                if (id & (uint64_t{1} << 63))
                {
                    throw std::overflow_error("canonical geometry namespace exhausted");
                }
                batch.id = id;
                batch.epoch = e;
                batch.lastUse = batch.id;
                geometryHints[hint] = selected;
                if (sourceFrame && targetFrame)
                {
                    ApplyLayoutOrigins(batch, *sourceFrame, *targetFrame);
                }
                return batch;
            }
            catch (...)
            {
                batch.id = 0;
                throw;
            }
        }
        static URUIPlanParameter Parameter(const URUICommand& command, const std::array<float, 16>& matrix,
                                           const URUIPlanUniform& uniform)
        {
            URUIPlanParameter p{};
            if (!std::isfinite(command.tx) || !std::isfinite(command.ty))
            {
                throw std::invalid_argument("translation");
            }
            for (int n = 0; n < 16; n++)
            {
                if (!std::isfinite(matrix[n]))
                {
                    throw std::invalid_argument("matrix");
                }
                p.matrix[n] = matrix[n];
            }
            for (int r = 0; r < 4; r++)
            {
                p.matrix[12 + r] = matrix[12 + r] + matrix[r] * command.tx + matrix[4 + r] * command.ty;
            }
            for (float value : p.matrix)
            {
                if (!std::isfinite(value))
                {
                    throw std::invalid_argument("world matrix");
                }
            }
            p.clip[0] = float(command.clip_x);
            p.clip[1] = float(command.clip_y);
            p.clip[2] = float(double(command.clip_x) + command.clip_w);
            p.clip[3] = float(double(command.clip_y) + command.clip_h);
            for (int n = 0; n < 4; n++)
            {
                if (!std::isfinite(uniform.tint[n]) || !std::isfinite(uniform.uv[n]))
                {
                    throw std::invalid_argument("image uniform");
                }
                p.tint[n] = uniform.tint[n];
                p.uv[n] = uniform.uv[n];
            }
            p.metadata[0] = float(command.flags);
            return p;
        }

    public:
        explicit Session(size_t geometryLimit = 64u * 1024 * 1024, size_t frameLimit = 48u * 1024 * 1024)
            : geometryBudget{&counters, 0, geometryLimit}, frameBudget{&counters, 0, frameLimit},
              dependencyCache(&frameBudget),
              frames{Frame(&frameBudget), Frame(&frameBudget), Frame(&frameBudget), Frame(&frameBudget)},
              batches(Allocator<Batch>(&geometryBudget)), keys(Allocator<GeometryKey>(&frameBudget)),
              geometryHints(Allocator<size_t>(&frameBudget)), paintScratch(Allocator<URUIPaintCommand>(&frameBudget)),
              effectScratch(Allocator<LayerEffect>(&frameBudget)),
              colorScratch(Allocator<ColorFilterInput>(&frameBudget)),
              ordinaryCommands(Allocator<URUICommand>(&frameBudget)),
              ordinaryMatrices(Allocator<std::array<float, 16>>(&frameBudget)),
              ordinaryUniforms(Allocator<URUIPlanUniform>(&frameBudget)),
              ordinaryResources(Allocator<URUIPlanTextureBinding>(&frameBudget)),
              ordinarySourceResources(Allocator<URUIPlanTextureBinding>(&frameBudget)),
              previousOperations(Allocator<URUIPlanOperation>(&frameBudget)),
              previousParameters(Allocator<URUIPlanParameter>(&frameBudget)),
              previousExtraParameters(Allocator<std::array<float, 4>>(&frameBudget)),
              previousParameterPages(Allocator<uint64_t>(&frameBudget)), sortMemo(Allocator<SortMemoKey>(&frameBudget)),
              sortMemoOrder(Allocator<uint32_t>(&frameBudget)), sortNodes(Allocator<SortNode>(&frameBudget)),
              sortStates(Allocator<SortState>(&frameBudget)), sortCells(Allocator<Cell>(&frameBudget)),
              sortLinks(Allocator<Link>(&frameBudget)), pending(Allocator<uint32_t>(&frameBudget)),
              order(Allocator<uint32_t>(&frameBudget)), large(Allocator<uint32_t>(&frameBudget))
        {
        }
        URUIPaintCommand* PaintScratch(size_t count)
        {
            paintScratch.resize(count);
            return paintScratch.data();
        }
        LayerEffect* EffectScratch(size_t count)
        {
            effectScratch.resize(count);
            return effectScratch.data();
        }
        int SetImageDocument(uint64_t input, uint32_t command, uint64_t document)
        {
            if (!Thread())
            {
                return -3;
            }
            auto* frame = Find(input);
            if (!frame || !frame->input || frame->epoch != epoch)
            {
                return -2;
            }
            if (frame->resolving || !document)
            {
                return -1;
            }
            for (auto& slot : frame->preparation)
            {
                if (slot.image && slot.request.slot == command)
                {
                    slot.request.document = document;
                    return 1;
                }
            }
            return -2;
        }
        AllocationCounters Allocations() const
        {
            return counters;
        }
        uint64_t SortBuilds() const
        {
            return sortBuilds;
        }
        uint64_t OrdinaryReuses() const
        {
            return ordinaryReuses;
        }
        uint64_t GeometrySearches() const
        {
            return geometrySearches;
        }
        bool Thread() const
        {
            return owner == std::this_thread::get_id();
        }
        void Viewport(uint32_t w, uint32_t h)
        {
            width = w;
            height = h;
        }
        void Invalidate(uint64_t value)
        {
            if (epoch == value)
            {
                return;
            }
            epoch = value;
            retiredEpochBatches = true;
            CollectRetiredEpochs();
        }
        int Begin(uint64_t value, const URUICommand* commands, const std::array<float, 16>* matrices, uint32_t count,
                  const URUIPaintCommand* paints, uint32_t paint_count, const URUILayerCommand* layers,
                  uint32_t layer_count, URUIPlanInputPacket& out, const URUIPlanUniform* uniforms = nullptr)
        {
            out = {};
            if (!Thread())
            {
                return -3;
            }
            if (!value || count > 65536 || (!commands && count) || (!matrices && count) || (!paints && paint_count) ||
                (!layers && layer_count))
            {
                return -1;
            }
            auto* frame = Free();
            if (!frame)
            {
                return -5;
            }
            struct UnpublishedInput
            {
                Frame* frame;
                Budget* budget;
                bool published = false;
                ~UnpublishedInput()
                {
                    if (!published)
                    {
                        *frame = Frame(budget);
                    }
                }
            } build{frame, &frameBudget};
            try
            {
                // Avoid pointer arithmetic on null even for an empty input.
                frame->commands.resize(count);
                frame->matrices.resize(count);
                frame->paints.resize(paint_count);
                frame->layers.resize(layer_count);
                frame->uniforms.resize(count);
                if (uniforms && count)
                {
                    std::copy_n(uniforms, count, frame->uniforms.data());
                }
                else
                {
                    std::fill(frame->uniforms.begin(), frame->uniforms.end(),
                              URUIPlanUniform{{1, 1, 1, 1}, {1, 1, 0, 0}});
                }
                if (count)
                {
                    std::copy_n(commands, count, frame->commands.data());
                    std::copy_n(matrices, count, frame->matrices.data());
                }
                frame->rawFlags.resize(count);
                for (uint32_t n = 0; n < count; n++)
                {
                    frame->rawFlags[n] = commands[n].flags;
                }
                if (paint_count)
                {
                    std::copy_n(paints, paint_count, frame->paints.data());
                }
                if (layer_count)
                {
                    std::copy_n(layers, layer_count, frame->layers.data());
                }
                int previousPaint = -1;
                for (const auto& p : frame->paints)
                {
                    if (p.command_index <= previousPaint || uint32_t(p.command_index) >= count || p.reserved < 0 ||
                        p.reserved > 2)
                    {
                        return -1;
                    }
                    previousPaint = p.command_index;
                }
                int previous = 0;
                for (const auto& l : frame->layers)
                {
                    if (l.command_index < previous || uint32_t(l.command_index) > count)
                    {
                        return -1;
                    }
                    previous = l.command_index;
                }
                frame->command_layers.resize(count);
                size_t layer = 0;
                uint64_t currentLayer = 0;
                for (uint32_t command = 0; command < count; command++)
                {
                    while (layer < layer_count && frame->layers[layer].command_index == int(command))
                    {
                        const auto& operation = frame->layers[layer++];
                        if (operation.kind == 1 || operation.kind == 2)
                        {
                            currentLayer = operation.target;
                        }
                    }
                    frame->command_layers[command] = currentLayer;
                }
                frame->effects.clear();
                frame->preparation.clear();
                frame->coverage.clear();
                frame->layerRegions.clear();
                frame->pending = UINT32_MAX;
                frame->resolving = false;
                frame->ready = false;
                frame->colorFilters.clear();
                frame->colorOffsets.assign(count, -1);
                frame->colorCounts.assign(count, 0);
                frame->resources.clear();
                frame->resourceFlags = 0;
                frame->external_geometry.clear();
                frame->externalLookup.clear();
                frame->external_draws.clear();
                frame->externalBounds.clear();
                frame->externalPending.clear();
                frame->knownProviders.assign(count, 0);
                frame->externalOffsets.assign(count, -1);
                frame->externalCounts.assign(count, 0);
                frame->directMasks.clear();
                frame->binaryMaskClips.clear();
                frame->layer_indices.clear();
                frame->directMaskIndices.assign(count, -1);
                frame->suppressedCommands.assign(count, 0);
                frame->omittedOperations.assign(layer_count, 0);
                frame->imageCursor = frame->layoutCursor = frame->elementCursor = 0;
                frame->captureCommand = frame->captureLayer = 0;
                frame->captureStarted = false;
                frame->clocks.clear();
                frame->clockLookup.clear();
                frame->imagePrograms.assign(count, {});
                frame->capturedPrograms.assign(layer_count, {});
                frame->capturedParameters.assign(layer_count, UINT32_MAX);
                frame->imageParameters.clear();
                frame->imagePending.clear();
                frame->imageSlots.assign(count, -1);
                frame->effectIndices.assign(layer_count, -1);
                frame->effectSlots.assign(layer_count, -1);
                uint32_t source_index = 0;
                for (const auto& paint : frame->paints)
                {
                    if (paint.reserved == 1)
                    {
                        PrepareSlot slot{};
                        slot.image = true;
                        slot.source_index = source_index++;
                        slot.request.element = paint.element;
                        slot.request.program = paint.shader;
                        slot.request.geometry = commands[paint.command_index].geometry;
                        slot.request.slot = uint32_t(paint.command_index);
                        slot.request.kind = URUI_PLAN_PREPARE_IMAGE;
                        frame->imageSlots[paint.command_index] = int32_t(frame->preparation.size());
                        frame->preparation.push_back(slot);
                    }
                }
                Invalidate(value);
                frame->input = true;
                frame->compiled = false;
                frame->epoch = value;
                frame->token = Identity();
                frame->width = width;
                frame->height = height;
                out = {frame->token,
                       value,
                       frame->commands.data(),
                       reinterpret_cast<const float*>(frame->matrices.data()),
                       frame->paints.data(),
                       frame->layers.data(),
                       count,
                       paint_count,
                       layer_count,
                       URUI_RENDER_PLAN_PROTOCOL,
                       frame->uniforms.data(),
                       frame->command_layers.data()};
                build.published = true;
                return 1;
            }
            catch (const std::bad_alloc&)
            {
                return -4;
            }
            catch (...)
            {
                return -8;
            }
        }
        ColorFilterInput* ColorScratch(size_t count)
        {
            colorScratch.resize(count);
            return colorScratch.data();
        }
        int SetColorFilters(uint64_t input, const ColorFilterInput* values, uint32_t count)
        {
            if (!Thread())
            {
                return -3;
            }
            auto* frame = Find(input);
            if (!frame || !frame->input || frame->epoch != epoch)
            {
                return -2;
            }
            if (frame->resolving || (!values && count) || count > 65536u * 16)
            {
                return -1;
            }
            try
            {
                uint32_t paint = 0;
                uint32_t consecutive = 0;
                for (uint32_t n = 0; n < count; n++)
                {
                    const auto& value = values[n];
                    if (value.command >= frame->commands.size() || value.kind > 4 || !std::isfinite(value.value) ||
                        (n && value.command < values[n - 1].command))
                    {
                        return -1;
                    }
                    consecutive = n && value.command == values[n - 1].command ? consecutive + 1 : 1;
                    if (consecutive > 16)
                    {
                        return -1;
                    }
                    while (paint < frame->paints.size() && uint32_t(frame->paints[paint].command_index) < value.command)
                    {
                        paint++;
                    }
                    if (paint == frame->paints.size() ||
                        uint32_t(frame->paints[paint].command_index) != value.command ||
                        frame->paints[paint].reserved != 2)
                    {
                        return -1;
                    }
                }
                frame->colorFilters.resize(count);
                if (count)
                {
                    std::copy_n(values, count, frame->colorFilters.data());
                }
                std::fill(frame->colorOffsets.begin(), frame->colorOffsets.end(), -1);
                std::fill(frame->colorCounts.begin(), frame->colorCounts.end(), 0);
                for (uint32_t n = 0; n < count; n++)
                {
                    const auto command = values[n].command;
                    if (frame->colorOffsets[command] < 0)
                    {
                        frame->colorOffsets[command] = int32_t(n);
                    }
                    frame->colorCounts[command]++;
                }
                return 1;
            }
            catch (const std::bad_alloc&)
            {
                return -4;
            }
            catch (...)
            {
                return -8;
            }
        }
        int SetLayerEffects(uint64_t input, const LayerEffect* effects, uint32_t count)
        {
            if (!Thread())
            {
                return -3;
            }
            auto* frame = Find(input);
            if (!frame || !frame->input || frame->epoch != epoch)
            {
                return -2;
            }
            if (frame->resolving || (!effects && count) || count > 65536)
            {
                return -1;
            }
            try
            {
                for (uint32_t n = 0; n < count; n++)
                {
                    const auto& effect = effects[n];
                    if (effect.layer_index >= frame->layers.size() || frame->layers[effect.layer_index].kind != 6 ||
                        effect.kind > 3)
                    {
                        return -1;
                    }
                    if (n && effect.layer_index <= effects[n - 1].layer_index)
                    {
                        return -1;
                    }
                }
                frame->effects.resize(count);
                if (count)
                {
                    std::copy_n(effects, count, frame->effects.data());
                }
                // Reconfiguration before resolve replaces custom slots rather than duplicating them.
                while (!frame->preparation.empty() && !frame->preparation.back().image)
                {
                    frame->preparation.pop_back();
                }
                std::fill(frame->effectIndices.begin(), frame->effectIndices.end(), -1);
                std::fill(frame->effectSlots.begin(), frame->effectSlots.end(), -1);
                for (uint32_t index = 0; index < frame->effects.size(); index++)
                {
                    const auto& effect = frame->effects[index];
                    frame->effectIndices[effect.layer_index] = int32_t(index);
                    if (effect.kind != 3)
                    {
                        continue;
                    }
                    PrepareSlot slot{};
                    slot.layer_index = effect.layer_index;
                    slot.request.document = effect.document;
                    slot.request.element = effect.element;
                    slot.request.program = effect.program;
                    slot.request.slot = effect.layer_index;
                    slot.request.layer = frame->layers[effect.layer_index].source;
                    slot.request.kind = URUI_PLAN_ELEMENT_LAYOUT;
                    frame->effectSlots[effect.layer_index] = int32_t(frame->preparation.size());
                    frame->preparation.push_back(slot);
                }
                uint64_t maximum = 0;
                for (const auto& operation : frame->layers)
                {
                    maximum = std::max(maximum, std::max(operation.source, operation.target));
                }
                if (maximum > 65536)
                {
                    return -1;
                }
                frame->layerComposites.assign(size_t(maximum) + 1, -1);
                frame->haloLayers.assign(size_t(maximum) + 1, 0);
                for (uint32_t n = 0; n < frame->layers.size(); n++)
                {
                    if (frame->layers[n].kind == 4)
                    {
                        frame->layerComposites[frame->layers[n].source] = int32_t(n);
                    }
                }
                for (const auto& effect : frame->effects)
                {
                    if (effect.kind == 1 || effect.kind == 2)
                    {
                        frame->haloLayers[frame->layers[effect.layer_index].source] = 1;
                    }
                }
                for (uint32_t n = 0; n < frame->commands.size(); n++)
                {
                    auto& command = frame->commands[n];
                    command.flags = frame->rawFlags[n];
                    const auto layer = frame->command_layers[n];
                    if (layer >= frame->haloLayers.size() || !frame->haloLayers[layer] ||
                        frame->layerComposites[layer] < 0)
                    {
                        continue;
                    }
                    const auto& output = frame->layers[frame->layerComposites[layer]];
                    if ((command.flags & 1) == output.flags && command.clip_x == output.clip_x &&
                        command.clip_y == output.clip_y && command.clip_w == output.clip_w &&
                        command.clip_h == output.clip_h)
                    {
                        command.flags &= ~1;
                    }
                }
                return 1;
            }
            catch (const std::bad_alloc&)
            {
                return -4;
            }
            catch (...)
            {
                return -8;
            }
        }
        static size_t ExternalSlot(uint64_t geometry, uint64_t version, size_t mask)
        {
            uint64_t hash = geometry ^ (version + 0x9e3779b97f4a7c15ull + (geometry << 6) + (geometry >> 2));
            hash ^= hash >> 30;
            hash *= 0xbf58476d1ce4e5b9ull;
            hash ^= hash >> 27;
            hash *= 0x94d049bb133111ebull;
            hash ^= hash >> 31;
            return size_t(hash) & mask;
        }
        uint32_t TryFindExternal(const Frame& frame, uint64_t id, uint64_t version) const
        {
            if (frame.externalLookup.empty())
            {
                return UINT32_MAX;
            }
            const auto mask = frame.externalLookup.size() - 1;
            size_t slot = ExternalSlot(id, version, mask);
            for (size_t probe = 0; probe < frame.externalLookup.size(); probe++, slot = (slot + 1) & mask)
            {
                const auto index = frame.externalLookup[slot];
                if (index == UINT32_MAX)
                {
                    break;
                }
                const auto& value = frame.external_geometry[index];
                if (value.geometry == id && value.version == version)
                {
                    return index;
                }
            }
            return UINT32_MAX;
        }
        uint32_t FindExternal(const Frame& frame, uint64_t id, uint64_t version) const
        {
            const auto found = TryFindExternal(frame, id, version);
            if (found == UINT32_MAX)
            {
                throw std::out_of_range("external geometry version");
            }
            return found;
        }
        static void RebuildExternalLookup(Frame& frame)
        {
            size_t size = 1;
            while (size < frame.external_geometry.size() * 2)
            {
                size *= 2;
            }
            frame.externalLookup.assign(size, UINT32_MAX);
            const auto mask = size - 1;
            for (uint32_t n = 0; n < frame.external_geometry.size(); n++)
            {
                const auto& geometry = frame.external_geometry[n];
                auto slot = ExternalSlot(geometry.geometry, geometry.version, mask);
                while (frame.externalLookup[slot] != UINT32_MAX)
                {
                    slot = (slot + 1) & mask;
                }
                frame.externalLookup[slot] = n;
            }
        }
        void SetExternalInputs(Frame& frame, const URUIPlanBindings& bindings)
        {
            if (bindings.external_geometry_count > 65536 || bindings.external_draw_count > 65536 ||
                (!bindings.external_geometry && bindings.external_geometry_count) ||
                (!bindings.external_draws && bindings.external_draw_count) ||
                (!bindings.externals && bindings.external_count) || bindings.external_count > frame.paints.size())
            {
                throw std::invalid_argument("external geometry bindings");
            }
            std::fill(frame.knownProviders.begin(), frame.knownProviders.end(), 0);
            std::fill(frame.externalOffsets.begin(), frame.externalOffsets.end(), -1);
            std::fill(frame.externalCounts.begin(), frame.externalCounts.end(), 0);
            frame.external_geometry.resize(bindings.external_geometry_count);
            if (bindings.external_geometry_count)
            {
                std::copy_n(bindings.external_geometry, bindings.external_geometry_count,
                            frame.external_geometry.data());
            }
            frame.externalBounds.resize(bindings.external_geometry_count);
            size_t lookup = 1;
            while (lookup < size_t(bindings.external_geometry_count) * 2)
            {
                lookup *= 2;
            }
            frame.externalLookup.assign(lookup, UINT32_MAX);
            for (uint32_t n = 0; n < bindings.external_geometry_count; n++)
            {
                const auto& geometry = frame.external_geometry[n];
                if (!ValidateExternalGeometry(geometry))
                {
                    throw std::invalid_argument("external geometry layout/range");
                }
                const auto mask = frame.externalLookup.size() - 1;
                size_t slot = ExternalSlot(geometry.geometry, geometry.version, mask);
                while (frame.externalLookup[slot] != UINT32_MAX)
                {
                    const auto& previous = frame.external_geometry[frame.externalLookup[slot]];
                    if (previous.geometry == geometry.geometry && previous.version == geometry.version)
                    {
                        throw std::invalid_argument("duplicate external geometry version");
                    }
                    slot = (slot + 1) & mask;
                }
                frame.externalLookup[slot] = n;
                std::array<float, 6> bounds{};
                for (uint32_t vertex = 0; vertex < geometry.vertex_count; vertex++)
                {
                    const auto value = ReadExternalVertex(geometry, vertex);
                    const float position[3]{value.x, value.y, value.z};
                    for (uint32_t axis = 0; axis < 3; axis++)
                    {
                        if (!vertex)
                        {
                            bounds[axis] = bounds[axis + 3] = position[axis];
                        }
                        else
                        {
                            bounds[axis] = std::min(bounds[axis], position[axis]);
                            bounds[axis + 3] = std::max(bounds[axis + 3], position[axis]);
                        }
                    }
                }
                frame.externalBounds[n] = bounds;
            }
            for (uint32_t n = 0; n < bindings.external_count; n++)
            {
                if (bindings.externals[n].flags & 8)
                {
                    const auto& provider = bindings.externals[n];
                    if (provider.command >= frame.commands.size() || provider.flags != 8 ||
                        frame.knownProviders[provider.command])
                    {
                        throw std::invalid_argument("external provider identity");
                    }
                    auto paint =
                        std::lower_bound(frame.paints.begin(), frame.paints.end(), provider.command,
                                         [](const auto& a, uint32_t b) { return uint32_t(a.command_index) < b; });
                    if (paint == frame.paints.end() || uint32_t(paint->command_index) != provider.command ||
                        paint->reserved != 0)
                    {
                        throw std::invalid_argument("external provider paint");
                    }
                    frame.knownProviders[provider.command] = 1;
                }
            }
            frame.external_draws.resize(bindings.external_draw_count);
            if (bindings.external_draw_count)
            {
                std::copy_n(bindings.external_draws, bindings.external_draw_count, frame.external_draws.data());
            }
            for (uint32_t n = 0; n < bindings.external_draw_count; n++)
            {
                auto& draw = frame.external_draws[n];
                if (draw.command >= frame.commands.size() || !frame.knownProviders[draw.command] ||
                    draw.geometry_index >= frame.external_geometry.size() || !draw.provider || !draw.program ||
                    (draw.flags & ~7u) || draw.reserved || (n && draw.command < frame.external_draws[n - 1].command))
                {
                    throw std::invalid_argument("external draw binding");
                }
                std::array<float, 32> values;
                std::memcpy(values.data(), &draw.parameter, sizeof(draw.parameter));
                for (uint32_t field = 0; field < values.size(); field++)
                {
                    if (!std::isfinite(values[field]))
                    {
                        throw std::invalid_argument("external draw parameter");
                    }
                }
                if (draw.flags & 4)
                {
                    QuadMapping mapping;
                    if (draw.flags != 4 || draw.parameter.uv[0] <= 0 || draw.parameter.uv[1] <= 0 ||
                        !AnalyticUnitQuad(frame.external_geometry[draw.geometry_index], mapping))
                    {
                        throw std::invalid_argument("analytic rectangle mask contract");
                    }
                    for (float feather : draw.parameter.tint)
                    {
                        if (feather < 0)
                        {
                            throw std::invalid_argument("negative analytic feather");
                        }
                    }
                }
                const URUIPlanUniform identity{{1, 1, 1, 1}, {1, 1, 0, 0}};
                const auto base = Parameter(frame.commands[draw.command], frame.matrices[draw.command], identity);
                float local[16];
                std::copy_n(draw.parameter.matrix, 16, local);
                for (int column = 0; column < 4; column++)
                {
                    for (int row = 0; row < 4; row++)
                    {
                        float value = 0;
                        for (int axis = 0; axis < 4; axis++)
                        {
                            value += base.matrix[axis * 4 + row] * local[column * 4 + axis];
                        }
                        if (!std::isfinite(value))
                        {
                            throw std::invalid_argument("external world matrix");
                        }
                        draw.parameter.matrix[column * 4 + row] = value;
                    }
                }
                std::copy_n(base.clip, 4, draw.parameter.clip);
                draw.parameter.metadata[0] = float(frame.commands[draw.command].flags & 1);
                if (frame.externalOffsets[draw.command] < 0)
                {
                    frame.externalOffsets[draw.command] = int32_t(n);
                }
                frame.externalCounts[draw.command]++;
            }
        }
        int SetImageGeometry(uint64_t input, uint64_t request, const URUIPlanExternalGeometry& geometry,
                             const float* transform)
        {
            if (!Thread())
            {
                return -3;
            }
            auto* frame = Find(input);
            if (!frame || !frame->input || frame->epoch != epoch || frame->pending == UINT32_MAX)
            {
                return -2;
            }
            auto& slot = frame->preparation[frame->pending];
            if ((!slot.image && slot.request.kind != URUI_PLAN_PREPARE_ELEMENT) || slot.request.request != request)
            {
                return -2;
            }
            if (slot.geometry_index != UINT32_MAX)
            {
                return -5;
            }
            if (!transform || !ValidateExternalLayout(geometry))
            {
                return -1;
            }
            for (uint32_t n = 0; n < 16; n++)
            {
                if (!std::isfinite(transform[n]))
                {
                    return -1;
                }
            }
            const auto count = frame->external_geometry.size();
            try
            {
                auto index = TryFindExternal(*frame, geometry.geometry, geometry.version);
                if (index == UINT32_MAX)
                {
                    if (count == 65536 || !ValidateExternalGeometry(geometry))
                    {
                        return -1;
                    }
                    index = uint32_t(count);
                    frame->external_geometry.push_back(geometry);
                    frame->externalBounds.push_back({});
                    RebuildExternalLookup(*frame);
                }
                slot.geometry_index = index;
                std::copy_n(transform, 16, slot.geometryTransform.data());
                return 1;
            }
            catch (const std::bad_alloc&)
            {
                frame->external_geometry.resize(count);
                frame->externalBounds.resize(count);
                return -4;
            }
            catch (...)
            {
                frame->external_geometry.resize(count);
                frame->externalBounds.resize(count);
                return -8;
            }
        }
        template <class Fill>
        int SetImageProgram(uint64_t input, uint64_t request, uint64_t program, uint32_t rows, Fill fill,
                            uint64_t clock = 0)
        {
            if (!Thread())
            {
                return -3;
            }
            auto* frame = Find(input);
            if (!frame || !frame->input || frame->epoch != epoch || frame->pending == UINT32_MAX)
            {
                return -2;
            }
            auto& slot = frame->preparation[frame->pending];
            if ((!slot.image && slot.request.kind != URUI_PLAN_PREPARE_ELEMENT) || slot.request.request != request)
            {
                return -2;
            }
            if (!program || rows < 2 || rows > 544 || clock > 65536)
            {
                return -1;
            }
            auto& binding =
                slot.image ? frame->imagePrograms[slot.request.slot] : frame->capturedPrograms[slot.layer_index];
            if (binding.program)
            {
                return -5;
            }
            const auto offset = frame->imageParameters.size();
            try
            {
                frame->imageParameters.resize(offset + rows);
                auto* data = frame->imageParameters[offset].data();
                const auto status = fill(slot.request, data, rows);
                if (status != 1)
                {
                    frame->imageParameters.resize(offset);
                    return status;
                }
                for (uint32_t n = 0; n < rows * 4; n++)
                {
                    if (!std::isfinite(data[n]))
                    {
                        frame->imageParameters.resize(offset);
                        return -1;
                    }
                }
                uint32_t clockIndex = UINT32_MAX;
                if (clock)
                {
                    if (frame->clockLookup.size() < clock)
                    {
                        frame->clockLookup.resize(size_t(clock), UINT32_MAX);
                    }
                    clockIndex = frame->clockLookup[size_t(clock) - 1];
                    if (clockIndex == UINT32_MAX)
                    {
                        clockIndex = uint32_t(frame->clocks.size());
                        frame->clocks.push_back({data[6], 0, 0, 0});
                        frame->clockLookup[size_t(clock) - 1] = clockIndex;
                    }
                    else if (frame->clocks[clockIndex][0] != data[6])
                    {
                        frame->imageParameters.resize(offset);
                        return -1;
                    }
                }
                // remapped only after the compiled atlas layout is final
                data[6] = -1;
                binding = {program, uint32_t(offset), rows, clockIndex};
                return 1;
            }
            catch (const std::bad_alloc&)
            {
                frame->imageParameters.resize(offset);
                return -4;
            }
            catch (...)
            {
                frame->imageParameters.resize(offset);
                return -8;
            }
        }
        template <class GeometryResolve>
        int Resolve(uint64_t input, const URUIPlanFeedback* feedback, GeometryResolve resolve,
                    URUIPlanPreparePacket& out, const URUIPlanBindings* resources = nullptr)
        {
            out = {};
            if (!Thread())
            {
                return -3;
            }
            auto* frame = Find(input);
            if (!frame || !frame->input || frame->epoch != epoch)
            {
                return -2;
            }
            try
            {
                if (!frame->resolving && resources)
                {
                    if (resources->texture_count > 65536 || (!resources->textures && resources->texture_count))
                    {
                        return -1;
                    }
                    frame->resources.resize(resources->texture_count);
                    if (resources->texture_count)
                    {
                        std::copy_n(resources->textures, resources->texture_count, frame->resources.data());
                    }
                    frame->resourceFlags = resources->frame_flags;
                    SetExternalInputs(*frame, *resources);
                }
                frame->resolving = true;
                if (feedback)
                {
                    if (frame->pending == UINT32_MAX)
                    {
                        return -1;
                    }
                    auto& slot = frame->preparation[frame->pending];
                    auto& request = slot.request;
                    if (feedback->request != request.request || feedback->kind != request.kind)
                    {
                        return -2;
                    }
                    if (request.kind == URUI_PLAN_PREPARE_IMAGE || request.kind == URUI_PLAN_PREPARE_ELEMENT)
                    {
                        if (!RectValid(feedback->ink))
                        {
                            return -1;
                        }
                        slot.ink.Rect(feedback->ink);
                        if (request.kind == URUI_PLAN_PREPARE_ELEMENT)
                        {
                            const int effect = frame->effectIndices[slot.layer_index];
                            if (effect < 0)
                            {
                                return -2;
                            }
                            InkRegion projected;
                            TransformInk(projected, slot.ink, frame->effects[effect].projection.data());
                            slot.ink = projected;
                        }
                        slot.prepared = true;
                    }
                    else if (request.kind == URUI_PLAN_ELEMENT_LAYOUT)
                    {
                        for (float value : feedback->expansion)
                        {
                            if (!std::isfinite(value) || value < 0)
                            {
                                return -1;
                            }
                        }
                        if (feedback->transform_count > 64 || (!feedback->transforms && feedback->transform_count))
                        {
                            return -1;
                        }
                        for (uint32_t n = 0; n < feedback->transform_count * 16; n++)
                        {
                            if (!std::isfinite(feedback->transforms[n]))
                            {
                                return -1;
                            }
                        }
                        slot.transformOffset = uint32_t(frame->coverage.size());
                        slot.transform_count = feedback->transform_count;
                        frame->coverage.resize(frame->coverage.size() + feedback->transform_count);
                        for (uint32_t n = 0; n < feedback->transform_count; n++)
                        {
                            std::copy_n(feedback->transforms + n * 16, 16,
                                        frame->coverage[slot.transformOffset + n].data());
                        }
                        if (feedback->capabilities > 1 || feedback->reserved)
                        {
                            return -1;
                        }
                        slot.unityBlend = (feedback->capabilities & 1) != 0;
                        std::copy_n(feedback->expansion, 4, slot.expansion.data());
                        slot.layout = true;
                    }
                    else if (request.kind == URUI_PLAN_ACQUIRE_CAPTURE)
                    {
                        if (!feedback->resource || !feedback->source_version || !feedback->pixel_width ||
                            !feedback->pixel_height || feedback->pixel_width > 16384 ||
                            feedback->pixel_height > 16384 || !RectValid(feedback->ink) || feedback->ink[2] <= 0 ||
                            feedback->ink[3] <= 0)
                        {
                            return -1;
                        }
                        const auto* actual = feedback->ink;
                        if (actual[0] > request.capture[0] || actual[1] > request.capture[1] ||
                            double(actual[0]) + actual[2] < double(request.capture[0]) + request.capture[2] ||
                            double(actual[1]) + actual[3] < double(request.capture[1]) + request.capture[3])
                        {
                            return -1;
                        }
                        request.resource = feedback->resource;
                        request.source_version = feedback->source_version;
                        request.pixel_width = feedback->pixel_width;
                        request.pixel_height = feedback->pixel_height;
                        std::copy_n(actual, 4, request.capture);
                        const auto* ink = request.source_ink;
                        request.uv[0] = (ink[0] - actual[0]) / actual[2];
                        request.uv[1] = (actual[1] + actual[3] - ink[1] - ink[3]) / actual[3];
                        request.uv[2] = ink[2] / actual[2];
                        request.uv[3] = ink[3] / actual[3];
                        request.kind = URUI_PLAN_PREPARE_ELEMENT;
                        request.request = Identity();
                        out = {input, &request, 1, 0};
                        return 1;
                    }
                    frame->pending = UINT32_MAX;
                }
                if (frame->pending != UINT32_MAX)
                {
                    out = {input, &frame->preparation[frame->pending].request, 1, 0};
                    return 1;
                }
                uint32_t next = UINT32_MAX;
                auto advance = [&](uint32_t& cursor, auto pending)
                {
                    while (cursor < frame->preparation.size() && !pending(frame->preparation[cursor]))
                    {
                        cursor++;
                    }
                    if (cursor < frame->preparation.size())
                    {
                        next = cursor;
                    }
                };
                advance(frame->imageCursor, [](const PrepareSlot& slot) { return slot.image && !slot.prepared; });
                if (next == UINT32_MAX)
                {
                    advance(frame->layoutCursor, [](const PrepareSlot& slot) { return !slot.image && !slot.layout; });
                }
                if (next == UINT32_MAX)
                {
                    advance(frame->elementCursor, [](const PrepareSlot& slot) { return !slot.prepared; });
                }
                if (next == UINT32_MAX)
                {
                    if (!frame->ready)
                    {
                        CompleteRegions(*frame, resolve);
                    }
                    out = {input,
                           nullptr,
                           0,
                           1,
                           frame->layerRegions.data(),
                           uint32_t(frame->layerRegions.size()),
                           NeedsRootTexture(*frame) ? 1u : 0u,
                           frame->layer_indices.data(),
                           uint32_t(frame->layer_indices.size()),
                           0};
                    return 1;
                }
                auto& slot = frame->preparation[next];
                auto& request = slot.request;
                if (slot.image)
                {
                    PrepareSource(*frame, slot, resolve);
                }
                if (!slot.image && slot.layout)
                {
                    auto ink = Capture(*frame, slot.layer_index, resolve);
                    request.source_ink[0] = float(ink.x0);
                    request.source_ink[1] = float(ink.y0);
                    request.source_ink[2] = float(ink.x1 - ink.x0);
                    request.source_ink[3] = float(ink.y1 - ink.y0);
                    // Input capture is frozen here; later output ink cannot resize it.
                    if (ink.valid)
                    {
                        ink.x0 -= slot.expansion[0];
                        ink.y0 -= slot.expansion[1];
                        ink.x1 += slot.expansion[2];
                        ink.y1 += slot.expansion[3];
                        const auto original = ink;
                        for (uint32_t n = 0; n < slot.transform_count; n++)
                        {
                            TransformInk(ink, original, frame->coverage[slot.transformOffset + n].data());
                        }
                    }
                    ink.Capture(request.capture);
                    request.kind = URUI_PLAN_ACQUIRE_CAPTURE;
                }
                request.request = Identity();
                frame->pending = next;
                out = {input, &request, 1, 0};
                return 1;
            }
            catch (const std::bad_alloc&)
            {
                return -4;
            }
            catch (const std::invalid_argument&)
            {
                return -1;
            }
            catch (const std::out_of_range&)
            {
                return -2;
            }
            catch (...)
            {
                return -8;
            }
        }
        template <class Resolve>
        int Compile(uint64_t input, const URUIPlanBindings& bindings, Resolve resolve, URUIRenderPlanPacket& out)
        {
            out = {};
            if (!Thread())
            {
                return -3;
            }
            auto* source = Find(input);
            if (!source || !source->input || source->epoch != epoch)
            {
                return -2;
            }
            if (!source->preparation.empty() && !source->ready)
            {
                return -5;
            }
            if (bindings.texture_count > 65536 || (!bindings.textures && bindings.texture_count))
            {
                return -1;
            }
            if (bindings.external_count > source->paints.size() || (!bindings.externals && bindings.external_count))
            {
                return -1;
            }
            size_t externalPaint = 0;
            for (uint32_t n = 0; n < bindings.external_count; n++)
            {
                const auto& binding = bindings.externals[n];
                if (binding.flags > 15 || (binding.flags & 8 && binding.flags != 8) || (binding.flags & 3) == 3 ||
                    (n && binding.command <= bindings.externals[n - 1].command))
                {
                    return -1;
                }
                while (externalPaint < source->paints.size() &&
                       uint32_t(source->paints[externalPaint].command_index) < binding.command)
                {
                    externalPaint++;
                }
                if (externalPaint == source->paints.size() ||
                    uint32_t(source->paints[externalPaint].command_index) != binding.command ||
                    source->paints[externalPaint].reserved == 2)
                {
                    return -1;
                }
                if ((source->resolving || source->ready) && (binding.flags & 8) &&
                    !source->knownProviders[binding.command])
                {
                    return -1;
                }
                if ((bindings.frame_flags & 32) && (binding.flags & 2) && source->paints[externalPaint].reserved == 0)
                {
                    return -1;
                }
            }
            if ((source->resolving || source->ready) &&
                ((bindings.external_geometry_count &&
                  bindings.external_geometry_count != source->external_geometry.size()) ||
                 (bindings.external_draw_count && bindings.external_draw_count != source->external_draws.size())))
            {
                return -1;
            }
            auto* target = Free();
            if (!target)
            {
                return -5;
            }
            geometryHintCursor = 0;
            compiling = target;
            struct BuildScope
            {
                Frame*& current;
                Frame* target;
                Budget* budget;
                bool published = false;
                ~BuildScope()
                {
                    current = nullptr;
                    if (!published)
                    {
                        *target = Frame(budget);
                    }
                }
            } scope{compiling, target, &frameBudget};
            try
            {
                if (!source->ready)
                {
                    source->resources.resize(bindings.texture_count);
                    if (bindings.texture_count)
                    {
                        std::copy_n(bindings.textures, bindings.texture_count, source->resources.data());
                    }
                    source->resourceFlags = bindings.frame_flags;
                    SetExternalInputs(*source, bindings);
                    CompleteRegions(*source, resolve);
                }
                if (bindings.layer_binding_count)
                {
                    if (bindings.frame_flags != source->resourceFlags ||
                        bindings.texture_count != source->resources.size())
                    {
                        return -1;
                    }
                    for (uint32_t n = 0; n < bindings.texture_count; n++)
                    {
                        const auto& a = bindings.textures[n];
                        const auto& b = source->resources[n];
                        if (a.texture != b.texture || a.version != b.version || a.domain != b.domain ||
                            a.flags != b.flags)
                        {
                            return -1;
                        }
                    }
                }
                if (bindings.layer_binding_count > source->layerRegions.size() ||
                    (!bindings.layer_bindings && bindings.layer_binding_count))
                {
                    return -1;
                }
                source->cachedLayers.assign(std::max(size_t(1), source->createdLayers.size()), 0);
                for (uint32_t n = 0; n < bindings.layer_binding_count; n++)
                {
                    const auto& cached = bindings.layer_bindings[n];
                    auto region = std::find_if(source->layerRegions.begin(), source->layerRegions.end(),
                                               [&](const auto& value) { return value.layer == cached.layer; });
                    if (cached.flags != 1 || region == source->layerRegions.end() || !(region->flags & 16) ||
                        cached.version != region->version)
                    {
                        return -1;
                    }
                    source->cachedLayers[cached.layer] = 1;
                }
                // Reusing a finished parent also suppresses writes in its nested scopes.
                source->layerStack.clear();
                source->layerStack.push_back(0);
                for (const auto& layer : source->layers)
                {
                    if (layer.kind == URUI_LAYER_PUSH)
                    {
                        source->cachedLayers[layer.target] |= source->cachedLayers[source->layerStack.back()];
                        source->layerStack.push_back(layer.target);
                    }
                    else if (layer.kind == URUI_LAYER_POP)
                    {
                        source->layerStack.pop_back();
                    }
                }
                for (const auto& mask : source->directMasks)
                {
                    auto normalized = [&](uint64_t texture)
                    {
                        if (!texture)
                        {
                            return true;
                        }
                        for (uint32_t n = 0; n < bindings.texture_count; n++)
                        {
                            if (bindings.textures[n].texture == texture)
                            {
                                return bindings.textures[n].domain == 0 && (bindings.textures[n].flags & 1);
                            }
                        }
                        return false;
                    };
                    if (!normalized(source->commands[mask.command].texture) || !normalized(mask.texture) ||
                        (!mask.composite.target && !(bindings.frame_flags & 1)))
                    {
                        throw std::invalid_argument("mask resource proof changed");
                    }
                }
                // Resolve 的裁剪证明不能在 Compile 时沿用已撤销的根像素网格条件。
                for (const auto& clip : source->binaryMaskClips)
                {
                    if (!source->layers[clip.layer_index].target && !(bindings.frame_flags & 1))
                    {
                        throw std::invalid_argument("binary mask pixel-grid proof changed");
                    }
                }
                const bool ordinary = source->paints.empty() && source->layers.empty() && source->preparation.empty() &&
                                      source->directMasks.empty() && source->clocks.empty() &&
                                      source->imageParameters.empty() && source->external_geometry.empty() &&
                                      source->external_draws.empty() && bindings.external_count == 0 &&
                                      bindings.external_geometry_count == 0 && bindings.external_draw_count == 0 &&
                                      bindings.layer_binding_count == 0;
                bool reuse = ordinary && ordinaryValid && ordinaryEpoch == source->epoch &&
                             ordinaryWidth == source->width && ordinaryHeight == source->height &&
                             ordinarySourceFlags == source->resourceFlags &&
                             ordinaryBindingFlags == bindings.frame_flags && Same(ordinaryCommands, source->commands) &&
                             Same(ordinaryMatrices, source->matrices) && Same(ordinaryUniforms, source->uniforms) &&
                             Same(ordinarySourceResources, source->resources) &&
                             ordinaryResources.size() == bindings.texture_count;
                if (reuse && bindings.texture_count)
                {
                    reuse = std::memcmp(ordinaryResources.data(), bindings.textures,
                                        bindings.texture_count * sizeof(URUIPlanTextureBinding)) == 0;
                }
                if (reuse)
                {
                    // The immutable geometry payload can expire independently of an
                    // input lease. Validate it even when every input value is equal.
                    for (const auto& command : source->commands)
                    {
                        if (command.geometry)
                        {
                            const auto geometry = resolve(command.geometry);
                            if (!geometry.vertices || !geometry.indices)
                            {
                                throw std::out_of_range("geometry expired");
                            }
                        }
                    }
                    for (const auto& operation : previousOperations)
                    {
                        if (operation.geometry)
                        {
                            auto found =
                                std::find_if(batches.begin(), batches.end(),
                                             [&](const auto& batch) {
                                                 return batch.id == operation.geometry && batch.epoch == source->epoch;
                                             });
                            if (found == batches.end())
                            {
                                reuse = false;
                                break;
                            }
                            found->lastUse = Identity();
                        }
                    }
                }
                ordinaryValid = false;
                if (reuse)
                {
                    // Each output lease owns its arrays; reuse never aliases a
                    // client's window or overwrites a still-published snapshot.
                    target->operations = previousOperations;
                    target->parameters = previousParameters;
                    target->extra_parameters = previousExtraParameters;
                    target->parameter_pages = previousParameterPages;
                    BuildTargetRequests(*source, *target);
                    target->input = false;
                    target->compiled = true;
                    target->epoch = epoch;
                    target->token = Identity();
                    target->version = operationVersion;
                    target->parameter_version = parameter_version;
                    out = {target->token,
                           epoch,
                           target->version,
                           target->parameter_version,
                           target->operations.data(),
                           target->parameters.data(),
                           uint32_t(target->operations.size()),
                           uint32_t(target->parameters.size()),
                           URUI_RENDER_PLAN_PROTOCOL,
                           uint32_t(target->extra_parameters.size()),
                           geometryBudget.retained,
                           frameBudget.retained,
                           counters.allocations,
                           counters.frees,
                           counters.bytes,
                           target->parameter_pages.data(),
                           uint32_t(target->parameter_pages.size()),
                           0,
                           reinterpret_cast<const float*>(target->extra_parameters.data()),
                           target->targets.data(),
                           uint32_t(target->targets.size()),
                           0};
                    ordinaryValid = true;
                    ordinaryReuses++;
                    scope.published = true;
                    return 1;
                }
                size_t parameter_count = source->commands.size() + source->external_draws.size();
                for (uint32_t layer = 0; layer < source->capturedPrograms.size(); layer++)
                {
                    if (source->capturedPrograms[layer].program)
                    {
                        source->capturedParameters[layer] = uint32_t(parameter_count++);
                    }
                }
                target->operations.clear();
                target->parameters.resize(parameter_count);
                keys.clear();
                pending.clear();
                target->extra_parameters.clear();
                target->colorOffsets.assign(source->commands.size(), -1);
                for (uint32_t command = 0; command < source->commands.size(); command++)
                {
                    if (source->colorCounts[command])
                    {
                        target->colorOffsets[command] = int32_t(target->extra_parameters.size());
                        target->extra_parameters.push_back({float(source->colorCounts[command]), 0, 0, 0});
                        for (uint32_t n = 0; n < source->colorCounts[command]; n++)
                        {
                            const auto& value = source->colorFilters[source->colorOffsets[command] + n];
                            target->extra_parameters.push_back({float(value.kind), value.value, 0, 0});
                        }
                    }
                }
                const size_t imageOffset = target->extra_parameters.size();
                target->extra_parameters.insert(target->extra_parameters.end(), source->imageParameters.begin(),
                                                source->imageParameters.end());
                const size_t maskOffset = target->extra_parameters.size();
                for (const auto& mask : source->directMasks)
                {
                    target->extra_parameters.insert(target->extra_parameters.end(), mask.values.begin(),
                                                    mask.values.end());
                }
                const size_t clockOffset = target->extra_parameters.size();
                target->extra_parameters.insert(target->extra_parameters.end(), source->clocks.begin(),
                                                source->clocks.end());
                for (const auto& image : source->imagePrograms)
                {
                    if (image.program && image.clock != UINT32_MAX)
                    {
                        target->extra_parameters[imageOffset + image.offset + 1][2] =
                            float(target->parameters.size() * 8 + clockOffset + image.clock);
                    }
                }
                for (const auto& image : source->capturedPrograms)
                {
                    if (image.program && image.clock != UINT32_MAX)
                    {
                        target->extra_parameters[imageOffset + image.offset + 1][2] =
                            float(target->parameters.size() * 8 + clockOffset + image.clock);
                    }
                }
                auto geometrySource = [&](uint64_t id, uint64_t version)
                {
                    if (!version)
                    {
                        return resolve(id);
                    }
                    const auto& data = source->external_geometry[FindExternal(*source, id, version)];
                    return GeometrySource{nullptr, data.vertex_count, nullptr, data.index_count, nullptr, &data};
                };
                auto flush = [&]()
                {
                    if (pending.empty())
                    {
                        return;
                    }
                    Order(*source, *target, bindings, resolve);
                    URUIPlanOperation operation{};
                    uint32_t domain = 0;
                    size_t vertices = 0;
                    size_t indices = 0;
                    InkRegion bounds;
                    auto batch = [&]()
                    {
                        if (keys.empty())
                        {
                            return;
                        }
                        if (bounds.valid && !(operation.reason & 0x80000000u))
                        {
                            const double values[4] = {bounds.x0, bounds.y0, bounds.x1, bounds.y1};
                            for (int n = 0; n < 4; n++)
                            {
                                if (std::abs(values[n]) > std::numeric_limits<float>::max())
                                {
                                    operation.reason |= 0x80000000u;
                                }
                                else
                                {
                                    operation.bounds[n] = float(values[n]);
                                }
                            }
                        }
                        const auto& geometryBatch = Geometry(source->epoch, geometrySource, source, target);
                        operation.geometry = geometryBatch.id;
                        operation.count = uint32_t(keys.size());
                        bool complete = operation.mask_texture == 0;
                        for (const auto& key : keys)
                        {
                            const auto& p = target->parameters[key.parameter];
                            if (uint32_t(p.metadata[0]) || p.metadata[1] != 0 || p.metadata[2] != 0 ||
                                p.metadata[3] != 0)
                            {
                                complete = false;
                            }
                            for (int item = 0; item < 16; item++)
                            {
                                if (p.matrix[item] != (item % 5 == 0 ? 1.f : 0.f))
                                {
                                    complete = false;
                                }
                            }
                            if (p.tint[0] != 1 || p.tint[1] != 1 || p.tint[2] != 1 || p.tint[3] != 1 || p.uv[0] != 1 ||
                                p.uv[1] != 1 || p.uv[2] != 0 || p.uv[3] != 0)
                            {
                                complete = false;
                            }
                        }
                        if (complete)
                        {
                            operation.reason |= 0x40000000u;
                        }
                        else
                        {
                            for (const auto& key : geometryBatch.keys)
                            {
                                const auto index = key.parameter;
                                auto& p = target->parameters[index];
                                const auto& c = source->commands[index];
                                const auto& m = source->matrices[index];
                                // 恢复原始矩阵的计算顺序，避免大坐标抵消放大精度误差。
                                for (int row = 0; row < 4; row++)
                                {
                                    p.matrix[12 + row] = m[12 + row] + m[row] * c.tx + m[4 + row] * c.ty;
                                }
                                if (key.originX != 0 || key.originY != 0)
                                {
                                    // 普通源顶点 z 恒为零，第三列 XY 可承载无损解码原点。
                                    p.matrix[8] = key.originX;
                                    p.matrix[9] = key.originY;
                                    p.metadata[0] = float(uint32_t(p.metadata[0]) | 512u);
                                }
                            }
                        }
                        target->operations.push_back(operation);
                        keys.clear();
                        operation = {};
                        vertices = indices = 0;
                        bounds = {};
                    };
                    for (auto ordered : order)
                    {
                        const auto n = pending[ordered];
                        const auto& c = source->commands[n];
                        const auto geometry = resolve(c.geometry);
                        if (!geometry.vertices || !geometry.indices)
                        {
                            throw std::out_of_range("geometry expired");
                        }
                        const auto currentDomain = Domain(c.texture, bindings);
                        const int maskIndex = source->directMaskIndices[n];
                        const uint64_t mask_texture = maskIndex >= 0 ? source->directMasks[maskIndex].texture : 0;
                        int slot = -1;
                        for (uint32_t t = 0; t < operation.texture_count; t++)
                        {
                            if (operation.textures[t] == c.texture)
                            {
                                slot = int(t);
                            }
                        }
                        const uint32_t reason =
                            operation.mask_texture && mask_texture && operation.mask_texture != mask_texture ? 4u
                            : currentDomain != domain                                                        ? 1u
                            : (slot < 0 && operation.texture_count == 8)                                     ? 2u
                            : (vertices + geometry.vertex_count > 100000 || indices + geometry.index_count > 300000)
                                ? 3u
                                : 0u;
                        if (!keys.empty() && reason)
                        {
                            batch();
                            operation.reason = reason;
                            slot = -1;
                        }
                        if (keys.empty())
                        {
                            operation.kind = URUI_PLAN_GEOMETRY;
                            operation.command_index = n;
                            domain = currentDomain;
                        }
                        if (mask_texture)
                        {
                            operation.mask_texture = mask_texture;
                        }
                        if (slot < 0)
                        {
                            slot = int(operation.texture_count);
                            operation.textures[operation.texture_count++] = c.texture;
                        }
                        const auto local = LocalBounds(geometry);
                        try
                        {
                            TransformInk(bounds, local, target->parameters[n].matrix);
                        }
                        catch (const std::invalid_argument&)
                        {
                            operation.reason |= 0x80000000u;
                        }
                        keys.push_back({c.geometry, n, uint32_t(slot) | 0x80000000u, 0, c.tx, c.ty});
                        vertices += geometry.vertex_count;
                        indices += geometry.index_count;
                    }
                    batch();
                    pending.clear();
                };
                source->externalPending.clear();
                auto flushExternal = [&]()
                {
                    if (source->externalPending.empty())
                    {
                        return;
                    }
                    keys.clear();
                    URUIPlanOperation operation{};
                    auto batch = [&]()
                    {
                        if (keys.empty())
                        {
                            return;
                        }
                        operation.geometry = Geometry(epoch, geometrySource).id;
                        target->operations.push_back(operation);
                        keys.clear();
                        operation = {};
                    };
                    for (auto index : source->externalPending)
                    {
                        const auto& draw = source->external_draws[index];
                        const auto& geometry = source->external_geometry[draw.geometry_index];
                        if (!geometry.vertex_count || !geometry.index_count)
                        {
                            continue;
                        }
                        if (draw.flags & 1)
                        {
                            batch();
                            URUIPlanOperation raw{};
                            raw.kind = URUI_PLAN_EXTERNAL_RAW_GEOMETRY;
                            raw.program = draw.program;
                            raw.command_index = draw.command;
                            raw.layer_index = index;
                            raw.count = 1;
                            target->operations.push_back(raw);
                            continue;
                        }
                        if (operation.program && operation.program != draw.program)
                        {
                            batch();
                        }
                        if (!operation.program)
                        {
                            operation.kind = URUI_PLAN_EXTERNAL_GEOMETRY;
                            operation.program = draw.program;
                            operation.command_index = draw.command;
                            operation.layer_index = index;
                        }
                        operation.count = index + 1 - operation.layer_index;
                        keys.push_back(
                            {geometry.geometry, uint32_t(source->commands.size() + index), 0, geometry.version});
                    }
                    batch();
                    source->externalPending.clear();
                };
                for (uint32_t n = 0; n < source->external_draws.size(); n++)
                {
                    target->parameters[source->commands.size() + n] = source->external_draws[n].parameter;
                }
                auto flushImages = [&]()
                {
                    if (source->imagePending.empty())
                    {
                        return;
                    }
                    keys.clear();
                    URUIPlanOperation operation{};
                    size_t vertices = 0;
                    size_t indices = 0;
                    auto batch = [&]()
                    {
                        if (keys.empty())
                        {
                            return;
                        }
                        operation.geometry = Geometry(source->epoch, geometrySource).id;
                        operation.count = uint32_t(keys.size());
                        target->operations.push_back(operation);
                        keys.clear();
                        operation = {};
                        vertices = indices = 0;
                    };
                    for (auto command : source->imagePending)
                    {
                        const auto& draw = source->commands[command];
                        const auto& program = source->imagePrograms[command];
                        auto geometryId = draw.geometry;
                        uint64_t geometryVersion = 0;
                        auto geometry = resolve(draw.geometry);
                        const auto& prepared = source->preparation[source->imageSlots[command]];
                        if (prepared.geometry_index != UINT32_MAX)
                        {
                            const auto& external = source->external_geometry[prepared.geometry_index];
                            geometryId = external.geometry;
                            geometryVersion = external.version;
                            geometry = {nullptr,  external.vertex_count, nullptr, external.index_count, nullptr,
                                        &external};
                        }
                        else if (!geometry.vertices || !geometry.indices)
                        {
                            throw std::out_of_range("image geometry expired");
                        }
                        int slot = -1;
                        for (uint32_t n = 0; n < operation.texture_count; n++)
                        {
                            if (operation.textures[n] == draw.texture)
                            {
                                slot = int(n);
                            }
                        }
                        if (!keys.empty() &&
                            (operation.program != program.program || (slot < 0 && operation.texture_count == 8) ||
                             vertices + geometry.vertex_count > 100000 || indices + geometry.index_count > 300000))
                        {
                            batch();
                            slot = -1;
                        }
                        if (keys.empty())
                        {
                            operation.kind = URUI_PLAN_PARAMETER_IMAGE;
                            operation.program = program.program;
                            operation.command_index = command;
                        }
                        if (slot < 0)
                        {
                            slot = int(operation.texture_count);
                            operation.textures[operation.texture_count++] = draw.texture;
                        }
                        keys.push_back({geometryId, command, uint32_t(slot), geometryVersion});
                        vertices += geometry.vertex_count;
                        indices += geometry.index_count;
                    }
                    batch();
                    source->imagePending.clear();
                };
                size_t layer = 0;
                size_t paint = 0;
                size_t external = 0;
                for (uint32_t n = 0; n <= source->commands.size(); n++)
                {
                    while (layer < source->layers.size() && source->layers[layer].command_index == int(n))
                    {
                        const auto index = layer++;
                        if (source->omittedOperations[index])
                        {
                            continue;
                        }
                        flushImages();
                        flushExternal();
                        flush();
                        URUIPlanOperation op{};
                        op.kind = URUI_PLAN_LAYER;
                        op.command_index = n;
                        op.layer_index = uint32_t(index);
                        const auto& layerOp = source->layers[index];
                        const auto* clipped = BinaryClip(*source, uint32_t(index));
                        if (clipped)
                        {
                            op.reason |= URUI_PLAN_LAYER_CLIP;
                            std::copy(clipped->bounds.begin(), clipped->bounds.end(), op.bounds);
                        }
                        const auto writeLayer = layerOp.kind == 6 ? layerOp.source : layerOp.target;
                        if ((layerOp.kind == 1 || layerOp.kind == 3 || layerOp.kind == 4 || layerOp.kind == 6) &&
                            source->cachedLayers[writeLayer])
                        {
                            op.reason |= 1;
                        }
                        const auto& captured = source->capturedPrograms[index];
                        if (captured.program)
                        {
                            const auto slotIndex = source->effectSlots[index];
                            if (slotIndex < 0)
                            {
                                throw std::invalid_argument("captured program slot");
                            }
                            const auto& prepared = source->preparation[slotIndex];
                            if (prepared.geometry_index == UINT32_MAX)
                            {
                                throw std::invalid_argument("captured program needs immutable geometry");
                            }
                            const auto& geometry = source->external_geometry[prepared.geometry_index];
                            const auto parameterIndex = source->capturedParameters[index];
                            URUICommand command{};
                            const URUIPlanUniform uniform{{1, 1, 1, 1}, {1, 1, 0, 0}};
                            auto parameter = Parameter(command, prepared.geometryTransform, uniform);
                            parameter.metadata[1] = 2;
                            parameter.metadata[2] =
                                float(target->parameters.size() * 8 + imageOffset + captured.offset);
                            target->parameters[parameterIndex] = parameter;
                            keys.clear();
                            keys.push_back({geometry.geometry, parameterIndex, 0, geometry.version});
                            op.geometry = Geometry(source->epoch, geometrySource).id;
                            op.program = captured.program;
                            op.count = 1;
                            keys.clear();
                        }
                        target->operations.push_back(op);
                    }
                    if (n == source->commands.size())
                    {
                        break;
                    }
                    const auto& c = source->commands[n];
                    target->parameters[n] = Parameter(c, source->matrices[n], source->uniforms[n]);
                    if (source->suppressedCommands[n] || source->cachedLayers[source->command_layers[n]])
                    {
                        if (paint < source->paints.size() && source->paints[paint].command_index == int(n))
                        {
                            paint++;
                        }
                        if (external < bindings.external_count && bindings.externals[external].command == n)
                        {
                            external++;
                        }
                        continue;
                    }
                    const int directMask = source->directMaskIndices[n];
                    if (directMask >= 0)
                    {
                        const auto& mask = source->directMasks[directMask];
                        auto& parameter = target->parameters[n];
                        parameter.metadata[0] = float(int(parameter.metadata[0]) | 256);
                        parameter.metadata[3] =
                            float(target->parameters.size() * 8 + maskOffset + size_t(directMask) * 8);
                        if (mask.composite.flags & 1)
                        {
                            const float clip[4] = {float(mask.composite.clip_x), float(mask.composite.clip_y),
                                                   float(double(mask.composite.clip_x) + mask.composite.clip_w),
                                                   float(double(mask.composite.clip_y) + mask.composite.clip_h)};
                            if (c.flags & 1)
                            {
                                for (int axis = 0; axis < 2; axis++)
                                {
                                    parameter.clip[axis] = std::max(parameter.clip[axis], clip[axis]);
                                    parameter.clip[axis + 2] = std::min(parameter.clip[axis + 2], clip[axis + 2]);
                                }
                            }
                            else
                            {
                                std::copy_n(clip, 4, parameter.clip);
                            }
                            parameter.metadata[0] = float(int(parameter.metadata[0]) | 1);
                        }
                    }
                    if (paint < source->paints.size() && source->paints[paint].command_index == int(n))
                    {
                        const auto& p = source->paints[paint++];
                        const auto& image = source->imagePrograms[n];
                        if (image.program)
                        {
                            flushExternal();
                            flush();
                            auto& parameter = target->parameters[n];
                            const auto& prepared = source->preparation[source->imageSlots[n]];
                            if (prepared.geometry_index != UINT32_MAX)
                            {
                                const auto original = parameter;
                                const auto& local = prepared.geometryTransform;
                                for (int column = 0; column < 4; column++)
                                {
                                    for (int row = 0; row < 4; row++)
                                    {
                                        float value = 0;
                                        for (int axis = 0; axis < 4; axis++)
                                        {
                                            value += original.matrix[axis * 4 + row] * local[column * 4 + axis];
                                        }
                                        if (!std::isfinite(value))
                                        {
                                            throw std::invalid_argument("image geometry matrix");
                                        }
                                        parameter.matrix[column * 4 + row] = value;
                                    }
                                }
                            }
                            parameter.metadata[1] = 2;
                            parameter.metadata[2] = float(target->parameters.size() * 8 + imageOffset + image.offset);
                            if (c.geometry)
                            {
                                source->imagePending.push_back(n);
                            }
                            continue;
                        }
                        flushImages();
                        if (source->knownProviders[n])
                        {
                            if (external >= bindings.external_count || bindings.externals[external].command != n ||
                                bindings.externals[external].flags != 8)
                            {
                                throw std::invalid_argument("external provider proof changed");
                            }
                            external++;
                            flush();
                            for (uint32_t item = 0; item < source->externalCounts[n]; item++)
                            {
                                source->externalPending.push_back(uint32_t(source->externalOffsets[n]) + item);
                            }
                            continue;
                        }
                        flushExternal();
                        if (p.reserved == 2)
                        {
                            if (source->colorOffsets[n] < 0 || !source->colorCounts[n])
                            {
                                throw std::out_of_range("point filter program fields missing");
                            }
                            auto& parameter = target->parameters[n];
                            parameter.metadata[1] = 1;
                            parameter.metadata[2] = float(target->parameters.size() * 8 + target->colorOffsets[n]);
                            if (c.geometry)
                            {
                                pending.push_back(n);
                            }
                            continue;
                        }
                        const bool bound =
                            external < bindings.external_count && bindings.externals[external].command == n;
                        const auto flags = bound ? bindings.externals[external++].flags : 1u;
                        if (p.reserved != 2 && bound && !(flags & 5))
                        {
                            if ((flags & 2) && c.geometry)
                            {
                                pending.push_back(n);
                            }
                            continue;
                        }
                        flush();
                        URUIPlanOperation op{};
                        op.kind = p.reserved == 0 ? URUI_PLAN_EXTERNAL : URUI_PLAN_IMAGE;
                        op.command_index = n;
                        op.count = 1;
                        target->operations.push_back(op);
                        if (bound && (flags & 2) && c.geometry)
                        {
                            pending.push_back(n);
                        }
                        continue;
                    }
                    flushImages();
                    flushExternal();
                    if (c.geometry)
                    {
                        pending.push_back(n);
                    }
                }
                flushImages();
                flushExternal();
                flush();
                WrapRootOperations(*source, *target);
                PlanRasterPasses(*source, *target, bindings);
                BuildTargetRequests(*source, *target);
                if (previousEpoch != epoch || !Same(previousOperations, target->operations))
                {
                    previousOperations = target->operations;
                    operationVersion = Identity();
                }
                const size_t baseBytes = target->parameters.size() * sizeof(URUIPlanParameter),
                             extraBytes = target->extra_parameters.size() * sizeof(std::array<float, 4>);
                const size_t bytes = baseBytes + extraBytes,
                             previousBytes = previousParameters.size() * sizeof(URUIPlanParameter) +
                                             previousExtraParameters.size() * sizeof(std::array<float, 4>);
                target->parameter_pages.resize((bytes + 4095) / 4096);
                for (size_t page = 0; page < target->parameter_pages.size(); page++)
                {
                    const size_t first = page * 4096;
                    const size_t count = std::min(size_t(4096), bytes - first);
                    const size_t oldCount = previousBytes > first ? std::min(size_t(4096), previousBytes - first) : 0;
                    bool same = target->parameters.size() == previousParameters.size() &&
                                page < previousParameterPages.size() && count == oldCount;
                    const size_t baseCount = first < baseBytes ? std::min(count, baseBytes - first) : 0;
                    if (same && baseCount)
                    {
                        same = std::memcmp(reinterpret_cast<const char*>(target->parameters.data()) + first,
                                           reinterpret_cast<const char*>(previousParameters.data()) + first,
                                           baseCount) == 0;
                    }
                    const size_t extraCount = count - baseCount;
                    const size_t extraStart = first + baseCount - baseBytes;
                    if (same && extraCount)
                    {
                        same = std::memcmp(reinterpret_cast<const char*>(target->extra_parameters.data()) + extraStart,
                                           reinterpret_cast<const char*>(previousExtraParameters.data()) + extraStart,
                                           extraCount) == 0;
                    }
                    target->parameter_pages[page] = same ? previousParameterPages[page] : Identity();
                }
                if (previousParameters.size() != target->parameters.size() ||
                    !Same(previousParameterPages, target->parameter_pages))
                {
                    previousParameters = target->parameters;
                    previousExtraParameters = target->extra_parameters;
                    previousParameterPages = target->parameter_pages;
                    parameter_version = Identity();
                }
                previousEpoch = epoch;
                if (ordinary)
                {
                    ordinaryCommands = source->commands;
                    ordinaryMatrices = source->matrices;
                    ordinaryUniforms = source->uniforms;
                    ordinarySourceResources = source->resources;
                    ordinaryResources.resize(bindings.texture_count);
                    if (bindings.texture_count)
                    {
                        std::copy_n(bindings.textures, bindings.texture_count, ordinaryResources.data());
                    }
                    ordinaryEpoch = source->epoch;
                    ordinaryWidth = source->width;
                    ordinaryHeight = source->height;
                    ordinarySourceFlags = source->resourceFlags;
                    ordinaryBindingFlags = bindings.frame_flags;
                    ordinaryValid = true;
                }
                target->input = false;
                target->compiled = true;
                target->epoch = epoch;
                target->token = Identity();
                target->version = operationVersion;
                target->parameter_version = parameter_version;
                out = {target->token,
                       epoch,
                       target->version,
                       target->parameter_version,
                       target->operations.data(),
                       target->parameters.data(),
                       uint32_t(target->operations.size()),
                       uint32_t(target->parameters.size()),
                       URUI_RENDER_PLAN_PROTOCOL,
                       uint32_t(target->extra_parameters.size()),
                       geometryBudget.retained,
                       frameBudget.retained,
                       counters.allocations,
                       counters.frees,
                       counters.bytes,
                       target->parameter_pages.data(),
                       uint32_t(target->parameter_pages.size()),
                       0,
                       reinterpret_cast<const float*>(target->extra_parameters.data()),
                       target->targets.data(),
                       uint32_t(target->targets.size()),
                       0};
                scope.published = true;
                return 1;
            }
            catch (const std::bad_alloc&)
            {
                return -4;
            }
            catch (const std::invalid_argument&)
            {
                return -1;
            }
            catch (const std::out_of_range&)
            {
                return -2;
            }
            catch (...)
            {
                return -8;
            }
        }
        int Geometry(uint64_t lease, uint64_t geometry, URUIGeometryView& out)
        try
        {
            out = {};
            if (!Thread())
            {
                return -3;
            }
            if (!geometry)
            {
                return -2;
            }
            auto* frame = Find(lease);
            if (!frame)
            {
                return -2;
            }
            if (frame->input)
            {
                if (frame->epoch != epoch)
                {
                    return -2;
                }
                for (const auto& slot : frame->preparation)
                {
                    if (slot.image && slot.sourceReady && slot.request.geometry == geometry)
                    {
                        auto& source = frame->sources[slot.source_index];
                        ComposeSource(source);
                        out = {source.vertices.data(), source.indices.data(), uint32_t(source.vertices.size()),
                               uint32_t(source.indices.size()), source.version};
                        return 1;
                    }
                }
                return -2;
            }
            if (!frame->compiled)
            {
                return -2;
            }
            bool used = false;
            for (const auto& op : frame->operations)
            {
                if (op.geometry == geometry)
                {
                    used = true;
                }
            }
            if (!used)
            {
                return -2;
            }
            for (const auto& batch : batches)
            {
                if (batch.id == geometry)
                {
                    out = {batch.vertices.data(), batch.indices.data(), uint32_t(batch.vertices.size()),
                           uint32_t(batch.indices.size()), batch.id};
                    return 1;
                }
            }
            return -2;
        }
        catch (const std::bad_alloc&)
        {
            out = {};
            return -4;
        }
        catch (...)
        {
            out = {};
            return -8;
        }
        int Release(uint64_t token)
        {
            if (!Thread())
            {
                return -3;
            }
            auto* frame = Find(token);
            if (!frame)
            {
                return -2;
            }
            frame->token = 0;
            CollectRetiredEpochs();
            return 1;
        }
    };
}
