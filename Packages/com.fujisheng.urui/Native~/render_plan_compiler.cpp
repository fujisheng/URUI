#include "render_plan_compiler.h"
#include <algorithm>
#include <cmath>
#include <cstring>

namespace URUI::RenderPlan
{
    namespace
    {
        constexpr uint32_t EmptySlot = UINT32_MAX;
        constexpr uint32_t TombstoneSlot = UINT32_MAX - 1;

        inline uint64_t MixHash(uint64_t value) noexcept
        {
            value ^= value >> 30;
            value *= 0xbf58476d1ce4e5b9ULL;
            value ^= value >> 27;
            value *= 0x94d049bb133111ebULL;
            return value ^ (value >> 31);
        }

        inline uint64_t HashKeys(uint64_t epoch, const Buffer<GeometryKey>& keys) noexcept
        {
            uint64_t h = MixHash(epoch ^ (uint64_t(keys.size()) * 0x9e3779b97f4a7c15ULL));
            for (const auto& k : keys)
            {
                h ^= MixHash(k.geometry);
                h = MixHash(h ^ (uint64_t(k.parameter) | (uint64_t(k.texture) << 32)));
                if (k.version)
                {
                    h = MixHash(h ^ k.version);
                }
            }
            return h;
        }

        inline size_t ExternalSlot(uint64_t geometry, uint64_t version, size_t mask) noexcept
        {
            const uint64_t hash =
                MixHash(geometry ^ (version + 0x9e3779b97f4a7c15ull + (geometry << 6) + (geometry >> 2)));
            return size_t(hash) & mask;
        }

        inline bool CommandEqual(const URUICommand& a, const URUICommand& b) noexcept
        {
            return a.geometry == b.geometry && a.texture == b.texture && a.tx == b.tx && a.ty == b.ty &&
                   a.flags == b.flags && a.clip_x == b.clip_x && a.clip_y == b.clip_y && a.clip_w == b.clip_w &&
                   a.clip_h == b.clip_h && a.reserved == b.reserved;
        }

        inline bool SameCommands(const Buffer<URUICommand>& a, const Buffer<URUICommand>& b) noexcept
        {
            if (a.size() != b.size())
            {
                return false;
            }
            for (size_t i = 0; i < a.size(); ++i)
            {
                if (!CommandEqual(a[i], b[i]))
                {
                    return false;
                }
            }
            return true;
        }

        template <class T> inline bool SameBytes(const Buffer<T>& a, const Buffer<T>& b) noexcept
        {
            return a.size() == b.size() && (a.empty() || std::memcmp(a.data(), b.data(), a.size() * sizeof(T)) == 0);
        }

        inline bool SamePaintCommands(const Buffer<URUIPaintCommand>& a, const Buffer<URUIPaintCommand>& b) noexcept
        {
            if (a.size() != b.size())
            {
                return false;
            }
            for (size_t i = 0; i < a.size(); ++i)
            {
                if (a[i].shader != b[i].shader || a[i].element != b[i].element ||
                    a[i].command_index != b[i].command_index || a[i].reserved != b[i].reserved)
                {
                    return false;
                }
            }
            return true;
        }

        inline bool SameLayerCommands(const Buffer<URUILayerCommand>& a, const Buffer<URUILayerCommand>& b) noexcept
        {
            if (a.size() != b.size())
            {
                return false;
            }
            for (size_t i = 0; i < a.size(); ++i)
            {
                if (a[i].command_index != b[i].command_index || a[i].kind != b[i].kind || a[i].source != b[i].source ||
                    a[i].target != b[i].target || a[i].mask != b[i].mask || a[i].blend != b[i].blend ||
                    a[i].flags != b[i].flags || a[i].clip_x != b[i].clip_x || a[i].clip_y != b[i].clip_y ||
                    a[i].clip_w != b[i].clip_w || a[i].clip_h != b[i].clip_h)
                {
                    return false;
                }
            }
            return true;
        }

        inline bool SameColorFilters(const Buffer<ColorFilterInput>& a, const Buffer<ColorFilterInput>& b) noexcept
        {
            if (a.size() != b.size())
            {
                return false;
            }
            for (size_t i = 0; i < a.size(); ++i)
            {
                if (a[i].command != b[i].command || a[i].kind != b[i].kind || a[i].value != b[i].value)
                {
                    return false;
                }
            }
            return true;
        }

        inline bool SameLayerEffects(const Buffer<LayerEffect>& a, const Buffer<LayerEffect>& b) noexcept
        {
            if (a.size() != b.size())
            {
                return false;
            }
            for (size_t i = 0; i < a.size(); ++i)
            {
                if (a[i].layer_index != b[i].layer_index || a[i].document != b[i].document ||
                    a[i].element != b[i].element || a[i].program != b[i].program || a[i].kind != b[i].kind ||
                    a[i].sigma != b[i].sigma || a[i].offset_x != b[i].offset_x || a[i].offset_y != b[i].offset_y ||
                    a[i].value != b[i].value || a[i].cacheable != b[i].cacheable || a[i].projection != b[i].projection)
                {
                    return false;
                }
            }
            return true;
        }

        inline bool SameImagePrograms(const Buffer<ImageProgram>& a, const Buffer<ImageProgram>& b) noexcept
        {
            if (a.size() != b.size())
            {
                return false;
            }
            for (size_t i = 0; i < a.size(); ++i)
            {
                if (a[i].program != b[i].program || a[i].offset != b[i].offset || a[i].count != b[i].count ||
                    a[i].clock != b[i].clock)
                {
                    return false;
                }
            }
            return true;
        }

        inline bool SameTextureBindings(const URUIPlanTextureBinding* a, size_t na, const URUIPlanTextureBinding* b,
                                        size_t nb) noexcept
        {
            if (na != nb)
            {
                return false;
            }
            for (size_t i = 0; i < na; ++i)
            {
                if (a[i].texture != b[i].texture || a[i].version != b[i].version || a[i].domain != b[i].domain ||
                    a[i].flags != b[i].flags)
                {
                    return false;
                }
            }
            return true;
        }

        inline bool SameExternalBindings(const URUIPlanExternalBinding* a, size_t na, const URUIPlanExternalBinding* b,
                                         size_t nb) noexcept
        {
            if (na != nb)
            {
                return false;
            }
            for (size_t i = 0; i < na; ++i)
            {
                if (a[i].command != b[i].command || a[i].flags != b[i].flags)
                {
                    return false;
                }
            }
            return true;
        }

        inline bool SameLayerBindings(const URUIPlanLayerBinding* a, size_t na, const URUIPlanLayerBinding* b,
                                      size_t nb) noexcept
        {
            if (na != nb)
            {
                return false;
            }
            for (size_t i = 0; i < na; ++i)
            {
                if (a[i].layer != b[i].layer || a[i].version != b[i].version || a[i].flags != b[i].flags ||
                    a[i].reserved != b[i].reserved)
                {
                    return false;
                }
            }
            return true;
        }

        inline bool RectValid(const float* b) noexcept
        {
            return std::isfinite(b[0]) && std::isfinite(b[1]) && std::isfinite(b[2]) && std::isfinite(b[3]) &&
                   b[2] >= 0 && b[3] >= 0;
        }

        inline InkRegion LocalBounds(const GeometrySource& mesh)
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

        inline bool TryTransformInk(InkRegion& out, const InkRegion& ink, const float* m, double tx = 0,
                                    double ty = 0) noexcept
        {
            if (!ink.valid)
            {
                return true;
            }
            if (m[3] == 0 && m[7] == 0 && m[15] == 1)
            {
                const double x0 = ink.x0 + tx;
                const double x1 = ink.x1 + tx;
                const double y0 = ink.y0 + ty;
                const double y1 = ink.y1 + ty;
                InkRegion next = out;
                if (!next.TryPoint(m[0] * (m[0] >= 0 ? x0 : x1) + m[4] * (m[4] >= 0 ? y0 : y1) + m[12],
                                   m[1] * (m[1] >= 0 ? x0 : x1) + m[5] * (m[5] >= 0 ? y0 : y1) + m[13]) ||
                    !next.TryPoint(m[0] * (m[0] >= 0 ? x1 : x0) + m[4] * (m[4] >= 0 ? y1 : y0) + m[12],
                                   m[1] * (m[1] >= 0 ? x1 : x0) + m[5] * (m[5] >= 0 ? y1 : y0) + m[13]))
                {
                    return false;
                }
                out = next;
                return true;
            }
            double low = std::numeric_limits<double>::infinity();
            double high = -low;
            InkRegion next = out;
            for (int corner = 0; corner < 4; corner++)
            {
                const double x = (corner & 1 ? ink.x1 : ink.x0) + tx;
                const double y = (corner & 2 ? ink.y1 : ink.y0) + ty;
                const double w = m[3] * x + m[7] * y + m[15];
                low = std::min(low, w);
                high = std::max(high, w);
                if (std::abs(w) < 1e-9 ||
                    !next.TryPoint((m[0] * x + m[4] * y + m[12]) / w, (m[1] * x + m[5] * y + m[13]) / w))
                {
                    return false;
                }
            }
            if (low < 0 && high > 0)
            {
                return false;
            }
            out = next;
            return true;
        }

        inline void TransformInkOrThrow(InkRegion& out, const InkRegion& ink, const float* m, double tx = 0,
                                        double ty = 0)
        {
            if (!TryTransformInk(out, ink, m, tx, ty))
            {
                throw std::invalid_argument("unbounded projected ink");
            }
        }

        inline void TransformExternalInk(InkRegion& out, const std::array<float, 6>& bounds, const float* matrix)
        {
            double low = std::numeric_limits<double>::infinity();
            double high = -low;
            const int corners = bounds[2] == bounds[5] ? 4 : 8;
            for (int corner = 0; corner < corners; corner++)
            {
                const double x = bounds[corner & 1 ? 3 : 0];
                const double y = bounds[corner & 2 ? 4 : 1];
                const double z = bounds[corner & 4 ? 5 : 2];
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

        inline bool CellBounds(const InkRegion& b, int& x0, int& y0, int& x1, int& y1) noexcept
        {
            if (!b.valid || std::abs(b.x0) > 1e7 || std::abs(b.y0) > 1e7 || std::abs(b.x1) > 1e7 ||
                std::abs(b.y1) > 1e7)
            {
                return false;
            }
            x0 = int(std::floor(b.x0 * (1.0 / 32.0)));
            y0 = int(std::floor(b.y0 * (1.0 / 32.0)));
            x1 = int(std::floor(b.x1 * (1.0 / 32.0)));
            y1 = int(std::floor(b.y1 * (1.0 / 32.0)));
            return int64_t(x1 - x0 + 1) * (y1 - y0 + 1) <= 64;
        }

        inline bool Disjoint(const InkRegion& a, const InkRegion& b) noexcept
        {
            return a.valid && b.valid && (a.x1 <= b.x0 || b.x1 <= a.x0 || a.y1 <= b.y0 || b.y1 <= a.y0);
        }

        inline bool AnalyticUnitQuad(const URUIPlanExternalGeometry& geometry, QuadMapping& mapping)
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

        inline URUIPlanParameter MakeParameter(const URUICommand& command, const std::array<float, 16>& matrix,
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

        inline const Frame::BinaryMaskClip* BinaryClip(const Frame& frame, uint32_t index) noexcept
        {
            const auto found = std::lower_bound(frame.binaryMaskClips.begin(), frame.binaryMaskClips.end(), index,
                                                [](const Frame::BinaryMaskClip& clip, uint32_t layer)
                                                { return clip.layer_index < layer; });
            return found != frame.binaryMaskClips.end() && found->layer_index == index ? &*found : nullptr;
        }

        // Compare logical byte range [offset, offset + length) across two [base | extra] parameter streams.
        inline bool SameParameterPageSlice(const char* baseA, size_t baseBytesA, const char* extraA, const char* baseB,
                                           size_t baseBytesB, const char* extraB, size_t offset, size_t length) noexcept
        {
            size_t pos = offset;
            size_t remaining = length;
            while (remaining > 0)
            {
                const bool inBaseA = pos < baseBytesA;
                const bool inBaseB = pos < baseBytesB;
                const size_t availA = inBaseA ? (baseBytesA - pos) : remaining;
                const size_t availB = inBaseB ? (baseBytesB - pos) : remaining;
                const size_t chunk = std::min(remaining, std::min(availA, availB));
                const char* ptrA = inBaseA ? (baseA + pos) : (extraA + (pos - baseBytesA));
                const char* ptrB = inBaseB ? (baseB + pos) : (extraB + (pos - baseBytesB));
                if (std::memcmp(ptrA, ptrB, chunk) != 0)
                {
                    return false;
                }
                pos += chunk;
                remaining -= chunk;
            }
            return true;
        }
    }

    bool InkRegion::TryPoint(double x, double y) noexcept
    {
        if (!std::isfinite(x) || !std::isfinite(y))
        {
            return false;
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
        return true;
    }

    void InkRegion::Point(double x, double y)
    {
        if (!TryPoint(x, y))
        {
            throw std::invalid_argument("nonfinite ink");
        }
    }

    void InkRegion::Join(const InkRegion& b)
    {
        if (b.valid)
        {
            Point(b.x0, b.y0);
            Point(b.x1, b.y1);
        }
    }

    void InkRegion::Rect(const float* b)
    {
        Point(b[0], b[1]);
        Point(double(b[0]) + b[2], double(b[1]) + b[3]);
    }

    void InkRegion::Capture(float* out) const
    {
        const double x = valid ? std::floor(x0) : 0;
        const double y = valid ? std::floor(y0) : 0;
        const double w = valid ? std::max(1., std::ceil(x1) - x) : 1;
        const double h = valid ? std::max(1., std::ceil(y1) - y) : 1;
        if (w > 8192 || h > 8192 || std::abs(x) > 1000000 || std::abs(y) > 1000000)
        {
            throw std::invalid_argument("capture bounds");
        }
        out[0] = float(x);
        out[1] = float(y);
        out[2] = float(w);
        out[3] = float(h);
    }

    Frame::Frame(Budget* budget)
        : commands(Allocator<URUICommand>(budget)), matrices(Allocator<std::array<float, 16>>(budget)),
          uniforms(Allocator<URUIPlanUniform>(budget)), command_layers(Allocator<uint64_t>(budget)),
          rawFlags(Allocator<int32_t>(budget)), layerComposites(Allocator<int32_t>(budget)),
          haloLayers(Allocator<uint8_t>(budget)), paints(Allocator<URUIPaintCommand>(budget)),
          layers(Allocator<URUILayerCommand>(budget)), operations(Allocator<URUIPlanOperation>(budget)),
          targets(Allocator<URUIPlanTargetRequest>(budget)), targetLookup(Allocator<uint32_t>(budget)),
          targetCapabilities(Allocator<uint8_t>(budget)), parameters(Allocator<URUIPlanParameter>(budget)),
          extra_parameters(Allocator<std::array<float, 4>>(budget)), clocks(Allocator<std::array<float, 4>>(budget)),
          clockLookup(Allocator<uint32_t>(budget)), imagePrograms(Allocator<ImageProgram>(budget)),
          capturedPrograms(Allocator<ImageProgram>(budget)), capturedParameters(Allocator<uint32_t>(budget)),
          imageParameters(Allocator<std::array<float, 4>>(budget)), imagePending(Allocator<uint32_t>(budget)),
          colorFilters(Allocator<ColorFilterInput>(budget)), colorOffsets(Allocator<int32_t>(budget)),
          colorCounts(Allocator<uint32_t>(budget)), parameter_pages(Allocator<uint64_t>(budget)),
          effects(Allocator<LayerEffect>(budget)), preparation(Allocator<PrepareSlot>(budget)),
          pendingRequests(Allocator<URUIPlanPrepareRequest>(budget)), pendingSlots(Allocator<uint32_t>(budget)),
          sources(Allocator<SourceSnapshot>(budget)), imageSlots(Allocator<int32_t>(budget)),
          effectIndices(Allocator<int32_t>(budget)), effectSlots(Allocator<int32_t>(budget)),
          coverage(Allocator<std::array<float, 16>>(budget)), layerInk(Allocator<InkRegion>(budget)),
          maskCropUsage(Allocator<MaskCropUsage>(budget)), createdLayers(Allocator<uint8_t>(budget)),
          layerRegions(Allocator<URUIPlanLayerRegion>(budget)), layerRegionLookup(Allocator<uint32_t>(budget)),
          layer_indices(Allocator<uint32_t>(budget)), omittedLayers(Allocator<uint8_t>(budget)),
          omittedOperations(Allocator<uint8_t>(budget)), suppressedCommands(Allocator<uint8_t>(budget)),
          directMaskIndices(Allocator<int32_t>(budget)), directMasks(Allocator<DirectMask>(budget)),
          binaryMaskClips(Allocator<BinaryMaskClip>(budget)), dependencies(Allocator<LayerDependencyState>(budget)),
          cachedLayers(Allocator<uint8_t>(budget)), knownProviders(Allocator<uint8_t>(budget)),
          external_geometry(Allocator<URUIPlanExternalGeometry>(budget)),
          external_draws(Allocator<URUIPlanExternalDraw>(budget)), externalOffsets(Allocator<int32_t>(budget)),
          externalCounts(Allocator<uint32_t>(budget)), externalLookup(Allocator<uint32_t>(budget)),
          externalBounds(Allocator<std::array<float, 6>>(budget)), externalPending(Allocator<uint32_t>(budget)),
          resources(Allocator<URUIPlanTextureBinding>(budget)), resourceLookup(Allocator<uint32_t>(budget)),
          layerStack(Allocator<uint64_t>(budget)), lastLayerWrite(Allocator<uint64_t>(budget)),
          maskSaveSequence(Allocator<uint64_t>(budget)), maskCopySources(Allocator<uint32_t>(budget)),
          maskCopyCounts(Allocator<uint32_t>(budget)), maskCopyState(Allocator<uint8_t>(budget))
    {
    }

    void Frame::Reset() noexcept
    {
        token = 0;
        epoch = 0;
        version = 0;
        parameter_version = 0;
        input = false;
        compiled = false;
        commands.clear();
        matrices.clear();
        uniforms.clear();
        command_layers.clear();
        rawFlags.clear();
        layerComposites.clear();
        haloLayers.clear();
        paints.clear();
        layers.clear();
        operations.clear();
        targets.clear();
        targetLookup.clear();
        targetCapabilities.clear();
        parameters.clear();
        extra_parameters.clear();
        clocks.clear();
        clockLookup.clear();
        imagePrograms.clear();
        capturedPrograms.clear();
        capturedParameters.clear();
        imageParameters.clear();
        imagePending.clear();
        colorFilters.clear();
        colorOffsets.clear();
        colorCounts.clear();
        parameter_pages.clear();
        effects.clear();
        preparation.clear();
        pendingRequests.clear();
        pendingSlots.clear();
        imageSlots.clear();
        effectIndices.clear();
        effectSlots.clear();
        coverage.clear();
        layerInk.clear();
        maskCropUsage.clear();
        createdLayers.clear();
        layerRegions.clear();
        layerRegionLookup.clear();
        layer_indices.clear();
        omittedLayers.clear();
        omittedOperations.clear();
        suppressedCommands.clear();
        directMaskIndices.clear();
        directMasks.clear();
        binaryMaskClips.clear();
        cachedLayers.clear();
        knownProviders.clear();
        external_geometry.clear();
        external_draws.clear();
        externalOffsets.clear();
        externalCounts.clear();
        externalLookup.clear();
        externalBounds.clear();
        externalPending.clear();
        resources.clear();
        resourceLookup.clear();
        resourceFlags = 0;
        discardedEmptyMask = false;
        layerStack.clear();
        lastLayerWrite.clear();
        maskSaveSequence.clear();
        maskCopySources.clear();
        maskCopyCounts.clear();
        maskCopyState.clear();
        imageCursor = layoutCursor = elementCursor = 0;
        captureCommand = captureLayer = 0;
        captureStarted = false;
        geometryQueryCursor = 0;
        resolving = false;
        ready = false;
    }

    void Frame::ShrinkAndReset() noexcept
    {
        Reset();
        commands.shrink_to_fit();
        matrices.shrink_to_fit();
        uniforms.shrink_to_fit();
        command_layers.shrink_to_fit();
        rawFlags.shrink_to_fit();
        layerComposites.shrink_to_fit();
        haloLayers.shrink_to_fit();
        paints.shrink_to_fit();
        layers.shrink_to_fit();
        operations.shrink_to_fit();
        targets.shrink_to_fit();
        targetLookup.shrink_to_fit();
        targetCapabilities.shrink_to_fit();
        parameters.shrink_to_fit();
        extra_parameters.shrink_to_fit();
        clocks.shrink_to_fit();
        clockLookup.shrink_to_fit();
        imagePrograms.shrink_to_fit();
        capturedPrograms.shrink_to_fit();
        capturedParameters.shrink_to_fit();
        imageParameters.shrink_to_fit();
        imagePending.shrink_to_fit();
        colorFilters.shrink_to_fit();
        colorOffsets.shrink_to_fit();
        colorCounts.shrink_to_fit();
        parameter_pages.shrink_to_fit();
        effects.shrink_to_fit();
        preparation.shrink_to_fit();
        pendingRequests.shrink_to_fit();
        pendingSlots.shrink_to_fit();
        imageSlots.shrink_to_fit();
        effectIndices.shrink_to_fit();
        effectSlots.shrink_to_fit();
        coverage.shrink_to_fit();
        layerInk.shrink_to_fit();
        maskCropUsage.shrink_to_fit();
        createdLayers.shrink_to_fit();
        layerRegions.shrink_to_fit();
        layerRegionLookup.shrink_to_fit();
        layer_indices.shrink_to_fit();
        omittedLayers.shrink_to_fit();
        omittedOperations.shrink_to_fit();
        suppressedCommands.shrink_to_fit();
        directMaskIndices.shrink_to_fit();
        directMasks.shrink_to_fit();
        binaryMaskClips.shrink_to_fit();
        cachedLayers.shrink_to_fit();
        knownProviders.shrink_to_fit();
        external_geometry.shrink_to_fit();
        external_draws.shrink_to_fit();
        externalOffsets.shrink_to_fit();
        externalCounts.shrink_to_fit();
        externalLookup.shrink_to_fit();
        externalBounds.shrink_to_fit();
        externalPending.shrink_to_fit();
        resources.shrink_to_fit();
        resourceLookup.shrink_to_fit();
        layerStack.shrink_to_fit();
        lastLayerWrite.shrink_to_fit();
        maskSaveSequence.shrink_to_fit();
        maskCopySources.shrink_to_fit();
        maskCopyCounts.shrink_to_fit();
        maskCopyState.shrink_to_fit();
    }

    Session::Session(size_t geometryLimit, size_t frameLimit)
        : geometryBudget{&counters, 0, geometryLimit}, frameBudget{&counters, 0, frameLimit},
          dependencyCache(&frameBudget),
          frames{Frame(&frameBudget), Frame(&frameBudget), Frame(&frameBudget), Frame(&frameBudget)},
          batches(Allocator<Batch>(&geometryBudget)), batchKeyIndex(Allocator<uint32_t>(&frameBudget)),
          batchIdIndex(Allocator<uint32_t>(&frameBudget)), freeBatchSlots(Allocator<uint32_t>(&frameBudget)),
          keys(Allocator<GeometryKey>(&frameBudget)), batchSources(Allocator<GeometrySource>(&frameBudget)),
          geometryHints(Allocator<size_t>(&frameBudget)),
          paintScratch(Allocator<URUIPaintCommand>(&frameBudget)), effectScratch(Allocator<LayerEffect>(&frameBudget)),
          colorScratch(Allocator<ColorFilterInput>(&frameBudget)),
          previousOperations(Allocator<URUIPlanOperation>(&frameBudget)),
          previousParameters(Allocator<URUIPlanParameter>(&frameBudget)),
          previousExtraParameters(Allocator<std::array<float, 4>>(&frameBudget)),
          previousParameterPages(Allocator<uint64_t>(&frameBudget)),
          previousTargets(Allocator<URUIPlanTargetRequest>(&frameBudget)),
          reuseCommands(Allocator<URUICommand>(&frameBudget)),
          reuseMatrices(Allocator<std::array<float, 16>>(&frameBudget)),
          reuseUniforms(Allocator<URUIPlanUniform>(&frameBudget)),
          reusePaints(Allocator<URUIPaintCommand>(&frameBudget)),
          reuseLayers(Allocator<URUILayerCommand>(&frameBudget)),
          reuseColorFilters(Allocator<ColorFilterInput>(&frameBudget)),
          reuseEffects(Allocator<LayerEffect>(&frameBudget)),
          reuseImagePrograms(Allocator<ImageProgram>(&frameBudget)),
          reuseCapturedPrograms(Allocator<ImageProgram>(&frameBudget)),
          reuseImageParameters(Allocator<std::array<float, 4>>(&frameBudget)),
          reuseClocks(Allocator<std::array<float, 4>>(&frameBudget)),
          reuseLayerRegions(Allocator<URUIPlanLayerRegion>(&frameBudget)),
          reuseExternalGeo(Allocator<ExternalGeometryIdentity>(&frameBudget)),
          reuseExternalDraws(Allocator<URUIPlanExternalDraw>(&frameBudget)),
          reuseResources(Allocator<URUIPlanTextureBinding>(&frameBudget)),
          reuseSourceResources(Allocator<URUIPlanTextureBinding>(&frameBudget)),
          reuseExternals(Allocator<URUIPlanExternalBinding>(&frameBudget)),
          reuseLayerBindings(Allocator<URUIPlanLayerBinding>(&frameBudget)),
          bindingTextureLookup(Allocator<uint32_t>(&frameBudget)), sortMemo(Allocator<SortMemoKey>(&frameBudget)),
          sortMemoOrder(Allocator<uint32_t>(&frameBudget)), sortNodes(Allocator<SortNode>(&frameBudget)),
          sortStates(Allocator<SortState>(&frameBudget)), sortCells(Allocator<Cell>(&frameBudget)),
          sortLinks(Allocator<Link>(&frameBudget)), pending(Allocator<uint32_t>(&frameBudget)),
          order(Allocator<uint32_t>(&frameBudget)), large(Allocator<uint32_t>(&frameBudget))
    {
    }

    URUIPaintCommand* Session::PaintScratch(size_t count)
    {
        paintScratch.resize(count);
        return paintScratch.data();
    }

    LayerEffect* Session::EffectScratch(size_t count)
    {
        effectScratch.resize(count);
        return effectScratch.data();
    }

    ColorFilterInput* Session::ColorScratch(size_t count)
    {
        colorScratch.resize(count);
        return colorScratch.data();
    }

    Frame* Session::Find(uint64_t token) noexcept
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

    Frame* Session::Free() noexcept
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

    void Session::PinFrame(Frame& frame) noexcept
    {
        if (frame.input || !frame.compiled)
        {
            return;
        }
        for (const auto& op : frame.operations)
        {
            if (op.geometry)
            {
                const uint32_t idx = FindBatchById(op.geometry);
                if (idx != EmptySlot)
                {
                    batches[idx].refCount++;
                }
            }
        }
    }

    void Session::UnpinFrame(Frame& frame) noexcept
    {
        if (frame.input || !frame.compiled)
        {
            return;
        }
        for (const auto& op : frame.operations)
        {
            if (op.geometry)
            {
                const uint32_t idx = FindBatchById(op.geometry);
                if (idx != EmptySlot && batches[idx].refCount > 0)
                {
                    batches[idx].refCount--;
                }
            }
        }
    }

    void Session::ClearCompilingPins() noexcept
    {
        for (auto& batch : batches)
        {
            batch.compilingPin = false;
        }
    }

    bool Session::BatchInUse(uint32_t index) const noexcept
    {
        const auto& b = batches[index];
        return b.id != 0 && (b.refCount > 0 || b.compilingPin);
    }

    void Session::RebuildBatchIndices()
    {
        size_t active = 0;
        freeBatchSlots.clear();
        for (uint32_t i = 0; i < batches.size(); ++i)
        {
            if (batches[i].id)
            {
                active++;
            }
            else
            {
                freeBatchSlots.push_back(i);
            }
        }
        size_t tableSize = 16;
        while (tableSize < active * 2 + 4)
        {
            tableSize *= 2;
        }
        batchKeyIndex.assign(tableSize, EmptySlot);
        batchIdIndex.assign(tableSize, EmptySlot);
        batchActiveCount = uint32_t(active);
        batchTombstoneCount = 0;
        const size_t mask = tableSize - 1;
        for (uint32_t i = 0; i < batches.size(); ++i)
        {
            const auto& b = batches[i];
            if (!b.id)
            {
                continue;
            }
            size_t kslot = size_t(MixHash(b.epoch ^ MixHash(b.keysHash))) & mask;
            while (batchKeyIndex[kslot] != EmptySlot)
            {
                kslot = (kslot + 1) & mask;
            }
            batchKeyIndex[kslot] = i;

            size_t islot = size_t(MixHash(b.id)) & mask;
            while (batchIdIndex[islot] != EmptySlot)
            {
                islot = (islot + 1) & mask;
            }
            batchIdIndex[islot] = i;
        }
    }

    void Session::InsertBatchIndex(uint32_t index)
    {
        const auto& b = batches[index];
        if (!b.id)
        {
            return;
        }
        if (batchKeyIndex.empty() || size_t(batchActiveCount + batchTombstoneCount + 1) * 2 >= batchKeyIndex.size())
        {
            RebuildBatchIndices();
            return;
        }
        const size_t mask = batchKeyIndex.size() - 1;
        size_t kslot = size_t(MixHash(b.epoch ^ MixHash(b.keysHash))) & mask;
        while (batchKeyIndex[kslot] != EmptySlot && batchKeyIndex[kslot] != TombstoneSlot)
        {
            kslot = (kslot + 1) & mask;
        }
        const bool reusedKeyTombstone = batchKeyIndex[kslot] == TombstoneSlot;
        batchKeyIndex[kslot] = index;

        size_t islot = size_t(MixHash(b.id)) & mask;
        while (batchIdIndex[islot] != EmptySlot && batchIdIndex[islot] != TombstoneSlot)
        {
            islot = (islot + 1) & mask;
        }
        const bool reusedIdTombstone = batchIdIndex[islot] == TombstoneSlot;
        batchIdIndex[islot] = index;

        batchActiveCount++;
        if (reusedKeyTombstone && reusedIdTombstone && batchTombstoneCount > 0)
        {
            batchTombstoneCount--;
        }
    }

    void Session::RemoveBatchIndex(uint32_t index, uint64_t oldId, uint64_t oldEpoch, uint64_t oldKeysHash) noexcept
    {
        if (!oldId || batchKeyIndex.empty())
        {
            return;
        }
        const size_t mask = batchKeyIndex.size() - 1;
        size_t kslot = size_t(MixHash(oldEpoch ^ MixHash(oldKeysHash))) & mask;
        for (size_t probe = 0; probe < batchKeyIndex.size(); ++probe, kslot = (kslot + 1) & mask)
        {
            const uint32_t slot = batchKeyIndex[kslot];
            if (slot == EmptySlot)
            {
                break;
            }
            if (slot == index)
            {
                batchKeyIndex[kslot] = TombstoneSlot;
                break;
            }
        }
        size_t islot = size_t(MixHash(oldId)) & mask;
        for (size_t probe = 0; probe < batchIdIndex.size(); ++probe, islot = (islot + 1) & mask)
        {
            const uint32_t slot = batchIdIndex[islot];
            if (slot == EmptySlot)
            {
                break;
            }
            if (slot == index)
            {
                batchIdIndex[islot] = TombstoneSlot;
                break;
            }
        }
        if (batchActiveCount > 0)
        {
            batchActiveCount--;
        }
        batchTombstoneCount++;
    }

    uint32_t Session::FindBatchById(uint64_t id) const noexcept
    {
        if (!id || batchIdIndex.empty())
        {
            return EmptySlot;
        }
        const size_t mask = batchIdIndex.size() - 1;
        size_t slot = size_t(MixHash(id)) & mask;
        for (size_t probe = 0; probe < batchIdIndex.size(); ++probe, slot = (slot + 1) & mask)
        {
            const uint32_t idx = batchIdIndex[slot];
            if (idx == EmptySlot)
            {
                return EmptySlot;
            }
            if (idx != TombstoneSlot && batches[idx].id == id)
            {
                return idx;
            }
        }
        return EmptySlot;
    }

    uint32_t Session::FindBatchByKeys(uint64_t batchEpoch, uint64_t keysHash) const noexcept
    {
        if (batchKeyIndex.empty())
        {
            return EmptySlot;
        }
        const size_t mask = batchKeyIndex.size() - 1;
        size_t slot = size_t(MixHash(batchEpoch ^ MixHash(keysHash))) & mask;
        for (size_t probe = 0; probe < batchKeyIndex.size(); ++probe, slot = (slot + 1) & mask)
        {
            const uint32_t idx = batchKeyIndex[slot];
            if (idx == EmptySlot)
            {
                return EmptySlot;
            }
            if (idx != TombstoneSlot)
            {
                const auto& b = batches[idx];
                if (b.id && b.epoch == batchEpoch && b.keysHash == keysHash && b.keys == keys)
                {
                    return idx;
                }
            }
        }
        return EmptySlot;
    }

    void Session::CollectRetiredEpochs()
    {
        if (!retiredEpochBatches)
        {
            return;
        }
        retiredEpochBatches = false;
        bool changed = false;
        for (uint32_t i = 0; i < batches.size(); ++i)
        {
            auto& batch = batches[i];
            if (batch.id && batch.epoch != epoch)
            {
                if (BatchInUse(i))
                {
                    retiredEpochBatches = true;
                    continue;
                }
                batch.id = 0;
                batch.lastUse = 0;
                batch.keysHash = 0;
                changed = true;
            }
        }
        if (changed)
        {
            RebuildBatchIndices();
        }
    }

    size_t Session::Reclaimable(size_t except) const noexcept
    {
        size_t best = SIZE_MAX;
        for (size_t n = 0; n < batches.size(); n++)
        {
            if (n != except && !BatchInUse(uint32_t(n)) &&
                (best == SIZE_MAX || batches[n].lastUse < batches[best].lastUse))
            {
                best = n;
            }
        }
        return best;
    }

    void Session::DropBatch(uint32_t index)
    {
        auto& batch = batches[index];
        const uint64_t oldId = batch.id;
        const uint64_t oldEpoch = batch.epoch;
        const uint64_t oldKeysHash = batch.keysHash;
        batch.id = 0;
        batch.lastUse = 0;
        batch.keysHash = 0;
        batch.refCount = 0;
        batch.compilingPin = false;
        Buffer<GeometryKey>(Allocator<GeometryKey>(&geometryBudget)).swap(batch.keys);
        Buffer<uint8_t>(Allocator<uint8_t>(&geometryBudget)).swap(batch.uniformColors);
        Buffer<URUIPlanVertex>(Allocator<URUIPlanVertex>(&geometryBudget)).swap(batch.vertices);
        Buffer<uint32_t>(Allocator<uint32_t>(&geometryBudget)).swap(batch.indices);
        if (oldId)
        {
            RemoveBatchIndex(index, oldId, oldEpoch, oldKeysHash);
            freeBatchSlots.push_back(index);
        }
    }

    void Session::BuildResourceLookup(Frame& frame)
    {
        size_t size = 16;
        while (size < frame.resources.size() * 2 + 2)
        {
            size *= 2;
        }
        frame.resourceLookup.assign(size, EmptySlot);
        const size_t mask = size - 1;
        for (uint32_t i = 0; i < frame.resources.size(); ++i)
        {
            const uint64_t tex = frame.resources[i].texture;
            size_t slot = size_t(MixHash(tex)) & mask;
            while (frame.resourceLookup[slot] != EmptySlot)
            {
                if (frame.resources[frame.resourceLookup[slot]].texture == tex)
                {
                    break;
                }
                slot = (slot + 1) & mask;
            }
            if (frame.resourceLookup[slot] == EmptySlot)
            {
                frame.resourceLookup[slot] = i;
            }
        }
    }

    void Session::BuildBindingTextureLookup(const URUIPlanBindings& bindings)
    {
        bindingTexturePtr = bindings.textures;
        bindingTextureCount = bindings.texture_count;
        hasNonZeroBindingDomain = false;
        if (!bindings.texture_count)
        {
            bindingTextureLookup.clear();
            return;
        }
        size_t size = 16;
        while (size < size_t(bindings.texture_count) * 2 + 2)
        {
            size *= 2;
        }
        bindingTextureLookup.assign(size, EmptySlot);
        const size_t mask = size - 1;
        for (uint32_t i = 0; i < bindings.texture_count; ++i)
        {
            if (bindings.textures[i].domain != 0)
            {
                hasNonZeroBindingDomain = true;
            }
            const uint64_t tex = bindings.textures[i].texture;
            size_t slot = size_t(MixHash(tex)) & mask;
            while (bindingTextureLookup[slot] != EmptySlot)
            {
                if (bindings.textures[bindingTextureLookup[slot]].texture == tex)
                {
                    break;
                }
                slot = (slot + 1) & mask;
            }
            if (bindingTextureLookup[slot] == EmptySlot)
            {
                bindingTextureLookup[slot] = i;
            }
        }
    }

    uint32_t Session::Domain(uint64_t texture) const noexcept
    {
        if (!hasNonZeroBindingDomain || !bindingTextureCount || bindingTextureLookup.empty())
        {
            return 0;
        }
        const size_t mask = bindingTextureLookup.size() - 1;
        size_t slot = size_t(MixHash(texture)) & mask;
        for (size_t probe = 0; probe < bindingTextureLookup.size(); ++probe, slot = (slot + 1) & mask)
        {
            const uint32_t idx = bindingTextureLookup[slot];
            if (idx == EmptySlot)
            {
                return 0;
            }
            if (bindingTexturePtr[idx].texture == texture)
            {
                return bindingTexturePtr[idx].domain;
            }
        }
        return 0;
    }

    bool Session::NormalizedBindingTexture(uint64_t id) const noexcept
    {
        if (!id)
        {
            return true;
        }
        if (!bindingTextureCount || bindingTextureLookup.empty())
        {
            return false;
        }
        const size_t mask = bindingTextureLookup.size() - 1;
        size_t slot = size_t(MixHash(id)) & mask;
        for (size_t probe = 0; probe < bindingTextureLookup.size(); ++probe, slot = (slot + 1) & mask)
        {
            const uint32_t idx = bindingTextureLookup[slot];
            if (idx == EmptySlot)
            {
                return false;
            }
            if (bindingTexturePtr[idx].texture == id)
            {
                return bindingTexturePtr[idx].domain == 0 && (bindingTexturePtr[idx].flags & 1) != 0;
            }
        }
        return false;
    }

    const URUIPlanTextureBinding* Session::FindFrameResource(const Frame& frame, uint64_t id) const noexcept
    {
        if (frame.resources.empty() || frame.resourceLookup.empty())
        {
            return nullptr;
        }
        const size_t mask = frame.resourceLookup.size() - 1;
        size_t slot = size_t(MixHash(id)) & mask;
        for (size_t probe = 0; probe < frame.resourceLookup.size(); ++probe, slot = (slot + 1) & mask)
        {
            const uint32_t idx = frame.resourceLookup[slot];
            if (idx == EmptySlot)
            {
                return nullptr;
            }
            if (frame.resources[idx].texture == id)
            {
                return &frame.resources[idx];
            }
        }
        return nullptr;
    }

    bool Session::NormalizedFrameTexture(const Frame& frame, uint64_t id) const noexcept
    {
        if (!id)
        {
            return true;
        }
        const auto* resource = FindFrameResource(frame, id);
        return resource && resource->domain == 0 && (resource->flags & 1) != 0;
    }

    void Session::Order(const Frame& source, const Frame& target, GeometryResolver resolve)
    {
        sortBoundsValid = false;
        order.resize(pending.size());
        // A compatible palette needs no spatial graph or per-node graph data.
        std::array<SortState, 8> palette{};
        uint32_t paletteCount = 0;
        uint32_t firstDomain = 0;
        uint64_t firstMask = 0;
        bool graph = false;
        for (uint32_t n = 0; n < pending.size(); n++)
        {
            order[n] = n;
            const auto command = pending[n];
            const auto& c = source.commands[command];
            const auto d = Domain(c.texture);
            const auto index = source.directMaskIndices[command];
            const auto m = index >= 0 ? source.directMasks[index].texture : 0;
            if (!n)
            {
                firstDomain = d;
            }
            if (d != firstDomain || (firstMask && m && firstMask != m))
            {
                graph = true;
                break;
            }
            if (m)
            {
                firstMask = m;
            }
            uint32_t slot = 0;
            while (slot < paletteCount && (palette[slot].texture != c.texture || palette[slot].domain != d))
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
                palette[paletteCount++] = {c.texture, 0, d, 0, 0};
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
            same = CommandEqual(key.command, c) &&
                   std::memcmp(key.matrix.data(), target.parameters[command].matrix, 16 * sizeof(float)) == 0 &&
                   key.mask == m && key.domain == Domain(c.texture);
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
        for (uint32_t n = 0; n < pending.size(); n++)
        {
            const auto command = pending[n];
            order[n] = n;
            auto& node = sortNodes[n];
            node = {};
            node.command = command;
            const auto& c = source.commands[command];
            const auto domain = Domain(c.texture);
            const auto maskIndex = source.directMaskIndices[command];
            const uint64_t mask = maskIndex >= 0 ? source.directMasks[maskIndex].texture : 0;
            auto slot = size_t(MixHash(c.texture ^ MixHash(domain) ^ MixHash(mask))) & (sortStates.size() - 1);
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
        sortBuilds++;
        if (sortCells.size() < 4096)
        {
            sortCells.resize(4096);
        }
        const size_t cellMask = sortCells.size() - 1;
        uint32_t activeCells = 0;
        sortLinks.clear();
        large.clear();
        uint32_t comparisons = 0;
        for (uint32_t n = 0; n < pending.size(); n++)
        {
            auto& node = sortNodes[n];
            const auto& c = source.commands[node.command];
            const auto mesh = resolve(c.geometry);
            const auto local = LocalBounds(mesh);
            if (!TryTransformInk(node.bounds, local, target.parameters[node.command].matrix))
            {
                // Unbounded projection is an ordering barrier, not a culling claim.
                node.bounds = {};
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
            int x0 = 0, y0 = 0, x1 = 0, y1 = 0;
            const bool small = CellBounds(node.bounds, x0, y0, x1, y1);
            if (!small)
            {
                for (uint32_t previous = 0; previous < n; previous++)
                {
                    if (!dependency(previous))
                    {
                        return;
                    }
                }
                large.push_back(n);
                continue;
            }
            for (auto previous : large)
            {
                if (!dependency(previous))
                {
                    return;
                }
            }
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    const uint64_t key = (uint64_t(uint32_t(x)) << 32) | uint32_t(y);
                    auto slot = size_t(MixHash(key)) & cellMask;
                    while (sortCells[slot].generation == sortGeneration && sortCells[slot].key != key)
                    {
                        slot = (slot + 1) & cellMask;
                    }
                    const auto& cell = sortCells[slot];
                    if (cell.generation != sortGeneration || cell.maxDepth + 1u <= node.depth)
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
            if (sortLinks.size() + size_t(x1 - x0 + 1) * (y1 - y0 + 1) > 16384 || activeCells > 3072)
            {
                return;
            }
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    const uint64_t key = (uint64_t(uint32_t(x)) << 32) | uint32_t(y);
                    auto slot = size_t(MixHash(key)) & cellMask;
                    while (sortCells[slot].generation == sortGeneration && sortCells[slot].key != key)
                    {
                        slot = (slot + 1) & cellMask;
                    }
                    auto& cell = sortCells[slot];
                    if (cell.generation != sortGeneration)
                    {
                        cell = {key, sortGeneration, node.depth, -1};
                        activeCells++;
                    }
                    else if (node.depth > cell.maxDepth)
                    {
                        cell.maxDepth = node.depth;
                    }
                    sortLinks.push_back({n, cell.head});
                    cell.head = int(sortLinks.size() - 1);
                }
            }
        }
        sortBoundsValid = true;
        std::sort(order.begin(), order.end(),
                  [&](uint32_t a, uint32_t b)
                  {
                      const auto& x = sortNodes[a];
                      const auto& y = sortNodes[b];
                      return x.depth != y.depth ? x.depth < y.depth : x.state != y.state ? x.state < y.state : a < b;
                  });
        sortMemoEpoch = 0;
        sortMemo.resize(pending.size());
        sortMemoOrder.assign(order.begin(), order.end());
        for (uint32_t n = 0; n < pending.size(); n++)
        {
            const auto command = pending[n];
            auto& key = sortMemo[n];
            key.command = source.commands[command];
            std::copy_n(target.parameters[command].matrix, 16, key.matrix.data());
            key.domain = Domain(key.command.texture);
            const auto index = source.directMaskIndices[command];
            key.mask = index >= 0 ? source.directMasks[index].texture : 0;
        }
        sortMemoEpoch = source.epoch;
    }

    InkRegion Session::Capture(Frame& frame, uint32_t stop, GeometryResolver resolve)
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
                if (c.kind == URUI_LAYER_PUSH)
                {
                    if (!c.target)
                    {
                        throw std::invalid_argument("root layer push");
                    }
                    frame.layerInk[c.target] = {};
                    frame.createdLayers[c.target] = 1;
                    frame.layerStack.push_back(c.target);
                }
                else if (c.kind == URUI_LAYER_POP)
                {
                    if (frame.layerStack.size() < 2 || frame.layerStack.back() != c.source ||
                        frame.layerStack[frame.layerStack.size() - 2] != c.target)
                    {
                        throw std::invalid_argument("unbalanced layer");
                    }
                    frame.layerStack.pop_back();
                }
                else if (c.kind == URUI_LAYER_SAVE_MASK)
                {
                    if (!c.target)
                    {
                        throw std::invalid_argument("root mask");
                    }
                    frame.layerInk[c.target] = frame.layerInk[c.source];
                    frame.createdLayers[c.target] = 1;
                }
                else if (c.kind == URUI_LAYER_COMPOSITE)
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
                else if (c.kind == URUI_LAYER_APPLY_MASK && !c.blend)
                {
                    const int effect_index = frame.effectIndices[layer];
                    if (effect_index >= 0)
                    {
                        const auto& effect = frame.effects[effect_index];
                        auto& ink = frame.layerInk[c.source];
                        if (effect.kind == 1 || effect.kind == 2)
                        {
                            auto shifted = ink;
                            const double halo = std::max(0.f, effect.sigma) * 3;
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
                double px = 0;
                double py = 0;
                for (const auto& effect : frame.effects)
                {
                    if (effect.kind != 1 && effect.kind != 2)
                    {
                        continue;
                    }
                    const auto source = frame.layers[effect.layer_index].source;
                    if (std::find(frame.layerStack.begin(), frame.layerStack.end(), source) == frame.layerStack.end())
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
            const auto geometry = resolve(command.geometry);
            auto local = LocalBounds(geometry);
            const int image = frame.imageSlots[n];
            if (image >= 0 && frame.preparation[image].prepared)
            {
                auto& slot = frame.preparation[image];
                local.Join(slot.ink);
                if (slot.localImage)
                {
                    TransformInkOrThrow(slot.worldInk, local, frame.matrices[n].data(), command.tx, command.ty);
                }
            }
            TransformInkOrThrow(frame.layerInk[frame.layerStack.back()], local, frame.matrices[n].data(), command.tx,
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

    void Session::LowerDirectMasks(Frame& frame, GeometryResolver resolve)
    {
        frame.directMasks.clear();
        frame.discardedEmptyMask = false;
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
            if (a.kind != URUI_LAYER_PUSH || b.kind != URUI_LAYER_PUSH || c.kind != URUI_LAYER_SAVE_MASK ||
                d.kind != URUI_LAYER_POP || e.kind != URUI_LAYER_APPLY_MASK || f.kind != URUI_LAYER_COMPOSITE ||
                g.kind != URUI_LAYER_POP || h.kind != URUI_LAYER_RELEASE_MASK)
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
                c.command_index != end || d.command_index != end || e.command_index != end || f.command_index != end ||
                g.command_index != end || h.command_index != end)
            {
                continue;
            }
            if (frame.command_layers[command] != source || frame.command_layers[maskCommand] != mask)
            {
                continue;
            }
            auto paint = std::lower_bound(frame.paints.begin(), frame.paints.end(), command,
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
                if (draw.flags != URUI_PLAN_DRAW_ANALYTIC_UNIT_RECT)
                {
                    continue;
                }
                analytic = &draw;
            }
            const auto& content = frame.commands[command];
            const auto& alpha = frame.commands[maskCommand];
            if (!NormalizedFrameTexture(frame, content.texture) ||
                (!analytic && !NormalizedFrameTexture(frame, alpha.texture)))
            {
                continue;
            }
            const auto contentMesh = resolve(content.geometry);
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
            const auto parameter =
                analytic ? analytic->parameter : MakeParameter(alpha, frame.matrices[maskCommand], frame.uniforms[maskCommand]);
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

    void Session::LowerBinaryMaskClips(Frame& frame)
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
            if (b.kind != URUI_LAYER_PUSH || c.kind != URUI_LAYER_SAVE_MASK || d.kind != URUI_LAYER_POP ||
                e.kind != URUI_LAYER_APPLY_MASK || f.kind != URUI_LAYER_COMPOSITE || g.kind != URUI_LAYER_POP ||
                h.kind != URUI_LAYER_RELEASE_MASK)
            {
                continue;
            }
            const auto source = e.source;
            const auto mask = b.target;
            const auto saved = c.target;
            if (!source || !mask || !saved || source == mask || source == saved || mask == saved ||
                c.source != mask || d.source != mask || d.target != source || e.target != source || e.mask != saved ||
                e.blend != 1 || f.source != source || f.mask || f.blend || g.source != source || g.target != f.target ||
                h.source != saved)
            {
                continue;
            }
            const auto command = b.command_index;
            const auto end = c.command_index;
            if (command < 0 || end != command + 1 || size_t(end) > frame.commands.size() || d.command_index != end ||
                e.command_index != end || f.command_index != end || g.command_index != end || h.command_index != end)
            {
                continue;
            }
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
            if (draw.flags != URUI_PLAN_DRAW_ANALYTIC_UNIT_RECT)
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

    void Session::PlanMaskCopies(Frame& frame)
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

    void Session::ConstrainTerminalMasks(Frame& frame)
    {
        auto& usage = frame.maskCropUsage;
        usage.assign(frame.createdLayers.size(), MaskCropUsage{});
        for (uint32_t n = 0; n < frame.layers.size(); n++)
        {
            if (!frame.omittedOperations[n])
            {
                const auto& op = frame.layers[n];
                if (op.kind == URUI_LAYER_PUSH || op.kind == URUI_LAYER_SAVE_MASK)
                {
                    usage[op.target].creates++;
                }
                if (op.kind == URUI_LAYER_SAVE_MASK || op.kind == URUI_LAYER_COMPOSITE ||
                    op.kind == URUI_LAYER_APPLY_MASK)
                {
                    usage[op.source].reads++;
                }
                if ((op.kind == URUI_LAYER_COMPOSITE || (op.kind == URUI_LAYER_APPLY_MASK && op.blend == 1)) && op.mask)
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
        const auto& idxOrder = frame.layer_indices;
        for (size_t n = 0; n + 7 < idxOrder.size(); n++)
        {
            const auto& a = frame.layers[idxOrder[n]];
            const auto& b = frame.layers[idxOrder[n + 1]];
            const auto& c = frame.layers[idxOrder[n + 2]];
            const auto& d = frame.layers[idxOrder[n + 3]];
            const auto& e = frame.layers[idxOrder[n + 4]];
            const auto& f = frame.layers[idxOrder[n + 5]];
            const auto& g = frame.layers[idxOrder[n + 6]];
            const auto& h = frame.layers[idxOrder[n + 7]];
            if (a.kind != URUI_LAYER_PUSH || b.kind != URUI_LAYER_PUSH || c.kind != URUI_LAYER_SAVE_MASK ||
                d.kind != URUI_LAYER_POP || e.kind != URUI_LAYER_APPLY_MASK || f.kind != URUI_LAYER_COMPOSITE ||
                g.kind != URUI_LAYER_POP || h.kind != URUI_LAYER_RELEASE_MASK)
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
            if ((frame.resourceFlags & URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH) && a.command_index == b.command_index &&
                usage[mask].reads == 1 && usage[saved].reads == 1 && !usage[mask].opaque && !usage[saved].opaque &&
                !frame.layerInk[source].valid)
            {
                const auto first = frame.commands.begin() + b.command_index;
                const auto last = frame.commands.begin() + c.command_index;
                const auto painter =
                    std::lower_bound(frame.paints.begin(), frame.paints.end(), b.command_index,
                                     [](const auto& paint, int command) { return paint.command_index < command; });
                if ((painter == frame.paints.end() || painter->command_index >= c.command_index) &&
                    std::all_of(first, last,
                                [&](const auto& draw) { return NormalizedFrameTexture(frame, draw.texture); }))
                {
                    frame.discardedEmptyMask = true;
                    frame.omittedLayers[source] = frame.omittedLayers[mask] = frame.omittedLayers[saved] = 1;
                    for (size_t offset = 0; offset < 8; offset++)
                    {
                        frame.omittedOperations[idxOrder[n + offset]] = OmittedEmptyMask;
                    }
                    std::fill(frame.suppressedCommands.begin() + b.command_index,
                              frame.suppressedCommands.begin() + c.command_index, OmittedEmptyMask);
                    n += 7;
                    continue;
                }
            }
            constexpr uint32_t scalarRequirements =
                URUI_PLAN_RESOURCE_HDR_CAMERA | URUI_PLAN_RESOURCE_SCALAR_MASK_SUPPORTED;
            if ((frame.resourceFlags & scalarRequirements) == scalarRequirements && b.command_index >= 0 &&
                c.command_index == b.command_index + 1 && usage[mask].reads == 1 && usage[saved].reads == 1 &&
                !usage[mask].opaque && !usage[saved].opaque)
            {
                const auto command = b.command_index;
                if (frame.knownProviders[command] && frame.externalCounts[command] == 1 &&
                    frame.external_draws[frame.externalOffsets[command]].flags == URUI_PLAN_DRAW_ANALYTIC_UNIT_RECT)
                {
                    usage[mask].scalar = true;
                    usage[saved].scalar = true;
                }
            }
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

    void Session::BuildLayerDependencies(Frame& frame)
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
        auto layerOrigin = [&](uint64_t id, float& ox, float& oy)
        {
            if (id && id < frame.layerInk.size() && frame.layerInk[id].valid)
            {
                ox = float(std::floor(frame.layerInk[id].x0));
                oy = float(std::floor(frame.layerInk[id].y0));
            }
            else
            {
                ox = 0.f;
                oy = 0.f;
            }
        };
        for (size_t layer = 0; layer < count; layer++)
        {
            auto& state = frame.dependencies[layer];
            state.Reset();
            state.Add(frame.epoch);
            state.Add(frame.resourceFlags);
            if (layer)
            {
                float bounds[4];
                frame.layerInk[layer].Capture(bounds);
                // Hash layer size (not world origin) so integer-scrolled layers keep 100% cache hit rate;
                // commands and child composites inside this layer are normalized relative to (bounds[0], bounds[1]).
                state.Bytes(bounds + 2, sizeof(float) * 2);
                state.Add(uint64_t(frame.maskCropUsage[layer].scalar) << 32);
            }
            else
            {
                state.Add(frame.width);
                state.Add(frame.height);
            }
        }
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
                if (op.kind == URUI_LAYER_SAVE_MASK || (op.kind == URUI_LAYER_COMPOSITE && op.source != op.target))
                {
                    forbid(op.source);
                }
                if (op.kind == URUI_LAYER_APPLY_MASK && op.blend == 1)
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
        auto descriptor = [](LayerDependencyState& state, const URUILayerCommand& op, float originX, float originY)
        {
            state.Add(uint64_t(op.kind));
            state.Add(uint64_t(op.blend));
            state.Add(uint64_t(op.flags));
            if (op.flags & 1)
            {
                const float relClip[4] = {float(op.clip_x) - originX, float(op.clip_y) - originY, float(op.clip_w),
                                          float(op.clip_h)};
                state.Bytes(relClip, sizeof(relClip));
            }
        };
        for (uint32_t command = 0; command <= frame.commands.size(); command++)
        {
            while (operation < frame.layers.size() && frame.layers[operation].command_index == int(command))
            {
                const auto index = operation++;
                const auto omission = frame.omittedOperations[index];
                if (omission == OmittedBinaryMask || omission == OmittedEmptyMask)
                {
                    continue;
                }
                const auto& op = frame.layers[index];
                auto& source = frame.dependencies[op.source];
                auto& target = frame.dependencies[op.target];
                if (op.kind == URUI_LAYER_SAVE_MASK)
                {
                    target.words.clear();
                    target.Add(0x53415645);
                    target.Add(version(op.source));
                    target.dynamic |= source.dynamic || source.damage.valid;
                    source.damageBlocked = target.damageBlocked = true;
                }
                else if (op.kind == URUI_LAYER_COMPOSITE && op.target)
                {
                    target.damageBlocked = true;
                    float tx = 0.f, ty = 0.f;
                    layerOrigin(op.target, tx, ty);
                    if (!op.source)
                    {
                        target.dynamic = true;
                    }
                    else
                    {
                        float sx = 0.f, sy = 0.f;
                        layerOrigin(op.source, sx, sy);
                        const auto child = version(op.source);
                        target.Add(child);
                        const float relOffset[2] = {sx - tx, sy - ty};
                        target.Bytes(relOffset, sizeof(relOffset));
                        target.dynamic |= source.dynamic || source.damage.valid;
                    }
                    if (const auto* clip = BinaryClip(frame, uint32_t(index)))
                    {
                        auto effective = op;
                        effective.flags |= 1;
                        effective.clip_x = int(clip->bounds[0]);
                        effective.clip_y = int(clip->bounds[1]);
                        effective.clip_w = int(clip->bounds[2] - clip->bounds[0]);
                        effective.clip_h = int(clip->bounds[3] - clip->bounds[1]);
                        descriptor(target, effective, tx, ty);
                    }
                    else
                    {
                        descriptor(target, op, tx, ty);
                    }
                    if (op.mask)
                    {
                        float mx = 0.f, my = 0.f;
                        layerOrigin(op.mask, mx, my);
                        const auto mask = version(op.mask);
                        target.Add(mask);
                        const float maskOffset[2] = {mx - tx, my - ty};
                        target.Bytes(maskOffset, sizeof(maskOffset));
                        target.dynamic |=
                            frame.dependencies[op.mask].dynamic || frame.dependencies[op.mask].damage.valid;
                    }
                }
                else if (op.kind == URUI_LAYER_APPLY_MASK)
                {
                    source.damageBlocked = true;
                    float sx = 0.f, sy = 0.f;
                    layerOrigin(op.source, sx, sy);
                    descriptor(source, op, sx, sy);
                    if (op.blend == 1)
                    {
                        float mx = 0.f, my = 0.f;
                        layerOrigin(op.mask, mx, my);
                        source.Add(version(op.mask));
                        const float maskOffset[2] = {mx - sx, my - sy};
                        source.Bytes(maskOffset, sizeof(maskOffset));
                        source.dynamic |=
                            frame.dependencies[op.mask].dynamic || frame.dependencies[op.mask].damage.valid;
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
            if (!layer || frame.suppressedCommands[command] == OmittedEmptyMask)
            {
                continue;
            }
            auto& state = frame.dependencies[layer];
            float ox = 0.f, oy = 0.f;
            layerOrigin(layer, ox, oy);
            const auto& draw = frame.commands[command];
            const bool hasDirectMask = frame.directMaskIndices[command] >= 0;
            state.damageBlocked |= hasDirectMask;
            if (frame.knownProviders[command])
            {
                state.damageBlocked = true;
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
                    auto param = external.parameter;
                    if (param.matrix[3] == 0.f && param.matrix[7] == 0.f && param.matrix[11] == 0.f &&
                        param.matrix[15] == 1.f)
                    {
                        param.matrix[12] -= ox;
                        param.matrix[13] -= oy;
                        if (uint32_t(param.metadata[0]) & 1u)
                        {
                            param.clip[0] -= ox;
                            param.clip[1] -= oy;
                            param.clip[2] -= ox;
                            param.clip[3] -= oy;
                        }
                    }
                    else
                    {
                        const float origin[2] = {ox, oy};
                        state.Bytes(origin, sizeof(origin));
                    }
                    state.Bytes(&param, sizeof(param));
                }
                continue;
            }
            state.Add(0x44524157);
            state.Add(draw.geometry);
            state.Add(draw.texture);
            auto parameter = MakeParameter(draw, frame.matrices[command], frame.uniforms[command]);
            if (!hasDirectMask && parameter.matrix[3] == 0.f && parameter.matrix[7] == 0.f &&
                parameter.matrix[11] == 0.f && parameter.matrix[15] == 1.f)
            {
                parameter.matrix[12] -= ox;
                parameter.matrix[13] -= oy;
                if (uint32_t(parameter.metadata[0]) & 1u)
                {
                    parameter.clip[0] -= ox;
                    parameter.clip[1] -= oy;
                    parameter.clip[2] -= ox;
                    parameter.clip[3] -= oy;
                }
            }
            else
            {
                const float origin[2] = {ox, oy};
                state.Bytes(origin, sizeof(origin));
            }
            state.Bytes(&parameter, sizeof(parameter));
            if (draw.texture)
            {
                const auto* resource = FindFrameResource(frame, draw.texture);
                if (!resource)
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
                    const auto image = frame.imageSlots[command];
                    if (program.reserved == 1 && image >= 0 && frame.preparation[image].worldInk.valid)
                    {
                        state.damage.Join(frame.preparation[image].worldInk);
                    }
                    else
                    {
                        state.dynamic = true;
                    }
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

    void Session::CompleteRegions(Frame& frame, GeometryResolver resolve)
    {
        frame.layerRegions.clear();
        frame.layerRegionLookup.clear();
        if (frame.layers.empty())
        {
            frame.ready = true;
            return;
        }
        Capture(frame, UINT32_MAX, resolve);
        LowerDirectMasks(frame, resolve);
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
            if (operation.kind == URUI_LAYER_SAVE_MASK)
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
        frame.layerRegionLookup.assign(frame.createdLayers.size(), EmptySlot);
        for (size_t layer = 1; layer < frame.createdLayers.size(); layer++)
        {
            if (frame.createdLayers[layer] && !frame.omittedLayers[layer])
            {
                URUIPlanLayerRegion result{};
                result.layer = layer;
                result.flags = frame.createdLayers[layer] >> 1;
                frame.layerInk[layer].Capture(result.bounds);
                if (frame.maskCropUsage[layer].scalar)
                {
                    result.flags |= URUI_PLAN_LAYER_SCALAR_MASK;
                }
                const auto source = frame.maskCopySources[layer];
                const auto saved = frame.maskSaveSequence[layer];
                if (frame.maskCopyCounts[layer] == 1 && source != layer && frame.lastLayerWrite[source] < saved &&
                    frame.lastLayerWrite[layer] == saved)
                {
                    result.flags |= URUI_PLAN_LAYER_REGION_ALIAS;
                    result.alias_layer = source;
                }
                auto& state = frame.dependencies[layer];
                if (state.damage.valid)
                {
                    auto& rectangle = state.damageRect;
                    const auto* bounds = result.bounds;
                    const double left = std::max(double(bounds[0]), std::floor(state.damage.x0) - 2);
                    const double top = std::max(double(bounds[1]), std::floor(state.damage.y0) - 2);
                    const double right = std::min(double(bounds[0]) + bounds[2], std::ceil(state.damage.x1) + 2);
                    const double bottom = std::min(double(bounds[1]) + bounds[3], std::ceil(state.damage.y1) + 2);
                    const double area = double(bounds[2]) * bounds[3];
                    if (!state.dynamic && !state.damageBlocked && !(result.flags & 15) && right > left &&
                        bottom > top && area >= 65536 && (right - left) * (bottom - top) < area * .25)
                    {
                        rectangle = {float(left), float(top), float(right - left), float(bottom - top)};
                        state.Bytes(rectangle.data(), sizeof(rectangle));
                    }
                    else
                    {
                        state.dynamic = true;
                    }
                }
                result.version = dependencyCache.Version(state);
                if (!state.dynamic && !state.damage.valid)
                {
                    result.flags |= URUI_PLAN_LAYER_REGION_CACHEABLE;
                }
                frame.layerRegionLookup[layer] = uint32_t(frame.layerRegions.size());
                frame.layerRegions.push_back(result);
            }
        }
        frame.ready = true;
    }

    void Session::BuildTargetRequests(const Frame& source, Frame& target)
    {
        auto& requests = target.targets;
        requests.clear();
        const size_t layerSlots = std::max(size_t(1), source.createdLayers.size());
        target.targetLookup.assign(layerSlots * 4, EmptySlot);
        auto add = [&](uint64_t layer, int32_t slot, uint32_t kind, uint64_t related, uint32_t ordinal)
        {
            uint32_t* lookup = nullptr;
            if (slot < 3)
            {
                const size_t key = size_t(layer) * 4 + size_t(slot + 1);
                if (key >= target.targetLookup.size())
                {
                    throw std::out_of_range("target layer");
                }
                lookup = &target.targetLookup[key];
            }
            if (lookup && *lookup != EmptySlot)
            {
                auto& value = requests[*lookup];
                value.first_operation = std::min(value.first_operation, ordinal);
                value.last_operation = std::max(value.last_operation, ordinal);
                return;
            }
            if (lookup)
            {
                *lookup = uint32_t(requests.size());
            }
            requests.push_back({layer, related, slot, kind, ordinal, ordinal});
        };
        for (const auto& region : source.layerRegions)
        {
            if (!(region.flags & URUI_PLAN_LAYER_REGION_ALIAS))
            {
                add(region.layer, 0, URUI_PLAN_TARGET_LAYER, 0, EmptySlot);
            }
        }
        auto touch = [&](uint64_t layer, uint32_t ordinal)
        {
            if (!layer)
            {
                return;
            }
            size_t key = size_t(layer) * 4 + 1;
            for (size_t steps = 0; key < target.targetLookup.size() && target.targetLookup[key] == EmptySlot &&
                                   steps < source.maskCopySources.size();
                 steps++)
            {
                if (layer >= source.maskCopySources.size() || !source.maskCopySources[layer])
                {
                    return;
                }
                layer = source.maskCopySources[layer];
                key = size_t(layer) * 4 + 1;
            }
            if (key >= target.targetLookup.size() || target.targetLookup[key] == EmptySlot)
            {
                return;
            }
            auto& request = requests[target.targetLookup[key]];
            request.first_operation = std::min(request.first_operation, ordinal);
            request.last_operation =
                request.last_operation == EmptySlot ? ordinal : std::max(request.last_operation, ordinal);
        };
        uint32_t rootCapture = EmptySlot;
        auto captureRoot = [&](uint32_t ordinal)
        {
            if (rootCapture == EmptySlot)
            {
                rootCapture = uint32_t(requests.size());
                requests.push_back({0, 0, -3, URUI_PLAN_TARGET_ROOT_CAPTURE, ordinal, ordinal});
            }
            else
            {
                requests[rootCapture].last_operation = ordinal;
            }
        };
        const bool screenOutput = (source.resourceFlags & URUI_PLAN_RESOURCE_SCREEN_OUTPUT) != 0;
        uint64_t activeLayer = 0;
        for (uint32_t ordinal = 0; ordinal < target.operations.size(); ordinal++)
        {
            const auto& operation = target.operations[ordinal];
            if (operation.kind != URUI_PLAN_LAYER)
            {
                touch(activeLayer, ordinal);
                continue;
            }
            const auto& layer = source.layers[operation.layer_index];
            touch(layer.source, ordinal);
            touch(layer.target, ordinal);
            touch(layer.mask, ordinal);
            if (layer.kind == URUI_LAYER_PUSH || layer.kind == URUI_LAYER_POP ||
                (layer.kind == URUI_LAYER_COMPOSITE && layer.source && layer.source != layer.target))
            {
                activeLayer = layer.target;
            }
            if (operation.reason & URUI_PLAN_LAYER_SKIP_CACHE_WRITE)
            {
                continue;
            }
            if (screenOutput && layer.kind == URUI_LAYER_COMPOSITE && !layer.source)
            {
                captureRoot(ordinal);
            }
            if (layer.kind == URUI_LAYER_APPLY_MASK)
            {
                const bool element = operation.layer_index < source.effectIndices.size() &&
                                     source.effectIndices[operation.layer_index] >= 0 &&
                                     source.effects[source.effectIndices[operation.layer_index]].kind == 3;
                const bool maskFetch =
                    layer.blend == 1 && (source.resourceFlags & URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH);
                if (!element && !maskFetch)
                {
                    add(layer.source, 1, URUI_PLAN_TARGET_MASK_SCRATCH, 0, ordinal);
                }
                if (layer.blend != 1)
                {
                    add(layer.source, 2, URUI_PLAN_TARGET_MASK_SCRATCH, 0, ordinal);
                }
            }
        }
        for (auto& request : requests)
        {
            if (request.first_operation == EmptySlot)
            {
                request.first_operation = request.last_operation = 0;
            }
        }
    }

    template <class T> void Session::ResizeSource(Buffer<T>& data, size_t size)
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
                    if (BatchInUse(uint32_t(n)) ||
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
                DropBatch(uint32_t(victim));
            }
            data.reserve(size);
        }
        data.resize(size);
    }

    void Session::ComposeSource(SourceSnapshot& snapshot)
    {
        if (snapshot.composedVersion == snapshot.version)
        {
            return;
        }
        ResizeSource(snapshot.vertices, snapshot.baseVertices.size());
        const auto& uniform = snapshot.uniform;
        const bool identityTint = uniform.tint[0] == 1.f && uniform.tint[1] == 1.f && uniform.tint[2] == 1.f &&
                                  uniform.tint[3] == 1.f;
        const bool identityUv =
            uniform.uv[0] == 1.f && uniform.uv[1] == 1.f && uniform.uv[2] == 0.f && uniform.uv[3] == 0.f;
        if (identityTint && identityUv)
        {
            for (size_t n = 0; n < snapshot.baseVertices.size(); n++)
            {
                const auto& v = snapshot.baseVertices[n];
                snapshot.vertices[n] = {v.x, v.y, 0, v.r, v.g, v.b, v.a, v.u, v.v, 0, 0};
            }
        }
        else if (identityTint)
        {
            for (size_t n = 0; n < snapshot.baseVertices.size(); n++)
            {
                const auto& v = snapshot.baseVertices[n];
                snapshot.vertices[n] = {v.x, v.y, 0, v.r, v.g, v.b, v.a, v.u * uniform.uv[0] + uniform.uv[2],
                                        v.v * uniform.uv[1] + uniform.uv[3], 0, 0};
            }
        }
        else
        {
            auto color = [](uint8_t value, float tint)
            { return uint8_t(std::clamp(std::lround(value * tint), 0l, 255l)); };
            for (size_t n = 0; n < snapshot.baseVertices.size(); n++)
            {
                const auto& v = snapshot.baseVertices[n];
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
        }
        snapshot.composedVersion = snapshot.version;
    }

    void Session::PrepareSource(Frame& frame, PrepareSlot& slot, GeometryResolver resolve)
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

    Batch& Session::BuildOrReuseBatch(uint64_t e, uint32_t /*baseParameter*/, GeometryResolver resolve,
                                      const Frame* sourceFrame)
    {
        const uint64_t keysHash = HashKeys(e, keys);
        const auto hint = geometryHintCursor++;
        if (hint == geometryHints.size())
        {
            geometryHints.push_back(SIZE_MAX);
        }
        auto reuse = [&](uint32_t index) -> Batch&
        {
            auto& batch = batches[index];
            geometryHints[hint] = index;
            batch.lastUse = ++nextLastUse;
            batch.compilingPin = true;
            return batch;
        };
        const auto previous = geometryHints[hint];
        if (previous < batches.size())
        {
            const auto& batch = batches[previous];
            geometrySearches++;
            if (batch.id && batch.epoch == e && batch.keysHash == keysHash && batch.keys == keys)
            {
                return reuse(uint32_t(previous));
            }
        }
        geometrySearches++;
        const uint32_t found = FindBatchByKeys(e, keysHash);
        if (found != EmptySlot)
        {
            return reuse(found);
        }
        auto lookupGeometry = [&](uint64_t id, uint64_t version) -> GeometrySource
        {
            if (!version)
            {
                return resolve(id);
            }
            const auto& data = sourceFrame->external_geometry[FindExternal(*sourceFrame, id, version)];
            return GeometrySource{nullptr, data.vertex_count, nullptr, data.index_count, nullptr, &data};
        };
        size_t nv = 0;
        size_t ni = 0;
        bool hasVersionedKey = false;
        batchSources.resize(keys.size());
        for (size_t keyIdx = 0; keyIdx < keys.size(); ++keyIdx)
        {
            const auto& key = keys[keyIdx];
            hasVersionedKey |= (key.version != 0);
            const auto source = lookupGeometry(key.geometry, key.version);
            if ((!source.external && (!source.vertices || !source.indices)) ||
                source.vertex_count > geometryBudget.limit / sizeof(URUIPlanVertex) ||
                source.index_count > geometryBudget.limit / sizeof(uint32_t))
            {
                throw std::invalid_argument("geometry expired");
            }
            batchSources[keyIdx] = source;
            nv += source.vertex_count;
            ni += source.index_count;
        }
        size_t selected = SIZE_MAX;
        while (!freeBatchSlots.empty())
        {
            const uint32_t candidate = freeBatchSlots.back();
            freeBatchSlots.pop_back();
            if (candidate < batches.size() && !batches[candidate].id)
            {
                selected = candidate;
                break;
            }
        }
        if (selected == SIZE_MAX && hasVersionedKey)
        {
            for (size_t n = 0; n < batches.size(); n++)
            {
                const auto& candidate = batches[n];
                if (candidate.epoch != e || candidate.keys.size() != keys.size() || BatchInUse(uint32_t(n)))
                {
                    continue;
                }
                bool same = true;
                bool changed = false;
                for (size_t key = 0; key < keys.size(); key++)
                {
                    const auto& old = candidate.keys[key];
                    const auto& next = keys[key];
                    if (old.geometry != next.geometry || old.parameter != next.parameter || old.texture != next.texture)
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
        const size_t payload = keys.size() * (sizeof(GeometryKey) + sizeof(uint8_t)) + nv * sizeof(URUIPlanVertex) +
                               ni * sizeof(uint32_t);
        if (selected == SIZE_MAX)
        {
            const size_t capacity =
                batches.size() == batches.capacity() ? std::max(size_t(1), batches.capacity() * 2) : batches.capacity();
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
        if (batch.id)
        {
            RemoveBatchIndex(uint32_t(selected), batch.id, batch.epoch, batch.keysHash);
        }
        batch.id = 0;
        batch.keysHash = 0;
        if (batch.keys.capacity() < keys.size())
        {
            Buffer<GeometryKey>(Allocator<GeometryKey>(&geometryBudget)).swap(batch.keys);
        }
        if (batch.uniformColors.capacity() < keys.size())
        {
            Buffer<uint8_t>(Allocator<uint8_t>(&geometryBudget)).swap(batch.uniformColors);
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
                   (batch.uniformColors.capacity() < keys.size() ? keys.size() * sizeof(uint8_t) : 0) +
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
            if (!batches[victim].keys.capacity() && !batches[victim].uniformColors.capacity() &&
                !batches[victim].vertices.capacity() && !batches[victim].indices.capacity())
            {
                size_t next = SIZE_MAX;
                for (size_t n = 0; n < batches.size(); n++)
                {
                    if (n != selected && !BatchInUse(uint32_t(n)) &&
                        (batches[n].keys.capacity() || batches[n].uniformColors.capacity() ||
                         batches[n].vertices.capacity() || batches[n].indices.capacity()))
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
            DropBatch(uint32_t(victim));
        }
        try
        {
            batch.keys = keys;
            batch.uniformColors.resize(keys.size());
            batch.vertices.resize(nv);
            batch.indices.resize(ni);
            size_t v = 0;
            size_t k = 0;
            for (size_t keyIdx = 0; keyIdx < batch.keys.size(); ++keyIdx)
            {
                auto& key = batch.keys[keyIdx];
                const auto& source = batchSources[keyIdx];
                const float textureSlot = float(key.texture & 7);
                const float paramFloat = float(key.parameter);
                uint8_t uniformColor = 1;
                if (source.external)
                {
                    uint8_t r0 = 0, g0 = 0, b0 = 0, a0 = 0;
                    for (size_t n = 0; n < source.vertex_count; n++)
                    {
                        auto value = ReadExternalVertex(*source.external, uint32_t(n));
                        value.texture = textureSlot;
                        value.parameter = paramFloat;
                        if (n == 0)
                        {
                            r0 = value.r;
                            g0 = value.g;
                            b0 = value.b;
                            a0 = value.a;
                        }
                        else if (value.r != r0 || value.g != g0 || value.b != b0 || value.a != a0)
                        {
                            uniformColor = 0;
                        }
                        batch.vertices[v + n] = value;
                    }
                    for (size_t n = 0; n < source.index_count; n++)
                    {
                        const auto index = ExternalIndex(*source.external, uint32_t(n));
                        if (index >= source.vertex_count || v > UINT32_MAX - index)
                        {
                            throw std::invalid_argument("geometry index");
                        }
                        batch.indices[k + n] = uint32_t(v) + index;
                    }
                }
                else
                {
                    const bool layout = (key.texture & 0x80000000u) != 0;
                    if (layout)
                    {
                        float ox = key.originX;
                        float oy = key.originY;
                        for (size_t n = 0; n < source.vertex_count; n++)
                        {
                            const auto& p = source.vertices[n];
                            const float x = p.x + ox;
                            const float y = p.y + oy;
                            if (!std::isfinite(x) || x - ox != p.x)
                            {
                                ox = 0.f;
                            }
                            if (!std::isfinite(y) || y - oy != p.y)
                            {
                                oy = 0.f;
                            }
                        }
                        key.originX = ox;
                        key.originY = oy;
                    }
                    const float ox = layout ? key.originX : 0.f;
                    const float oy = layout ? key.originY : 0.f;
                    uint8_t r0 = 0, g0 = 0, b0 = 0, a0 = 0;
                    if (source.vertex_count > 0)
                    {
                        r0 = source.vertices[0].r;
                        g0 = source.vertices[0].g;
                        b0 = source.vertices[0].b;
                        a0 = source.vertices[0].a;
                    }
                    for (size_t n = 0; n < source.vertex_count; n++)
                    {
                        const auto& p = source.vertices[n];
                        const float x = p.x + ox;
                        const float y = p.y + oy;
                        if (!std::isfinite(x) || !std::isfinite(y))
                        {
                            throw std::invalid_argument("layout vertex");
                        }
                        if (p.r != r0 || p.g != g0 || p.b != b0 || p.a != a0)
                        {
                            uniformColor = 0;
                        }
                        batch.vertices[v + n] = {x, y, 0, p.r, p.g, p.b, p.a, p.u, p.v, textureSlot, paramFloat};
                    }
                    const uint32_t vbase = uint32_t(v);
                    for (size_t n = 0; n < source.index_count; n++)
                    {
                        const auto index = uint32_t(source.indices[n]);
                        if (index >= source.vertex_count || v > UINT32_MAX - index)
                        {
                            throw std::invalid_argument("geometry index");
                        }
                        batch.indices[k + n] = vbase + index;
                    }
                }
                batch.uniformColors[keyIdx] = uniformColor;
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
            batch.lastUse = ++nextLastUse;
            batch.keysHash = keysHash;
            batch.compilingPin = true;
            InsertBatchIndex(uint32_t(selected));
            geometryHints[hint] = selected;
            return batch;
        }
        catch (...)
        {
            batch.id = 0;
            batch.keysHash = 0;
            batch.compilingPin = false;
            freeBatchSlots.push_back(uint32_t(selected));
            throw;
        }
    }

    uint32_t Session::TryFindExternal(const Frame& frame, uint64_t id, uint64_t version) const noexcept
    {
        if (frame.externalLookup.empty())
        {
            return EmptySlot;
        }
        const auto mask = frame.externalLookup.size() - 1;
        size_t slot = ExternalSlot(id, version, mask);
        for (size_t probe = 0; probe < frame.externalLookup.size(); probe++, slot = (slot + 1) & mask)
        {
            const auto index = frame.externalLookup[slot];
            if (index == EmptySlot)
            {
                break;
            }
            const auto& value = frame.external_geometry[index];
            if (value.geometry == id && value.version == version)
            {
                return index;
            }
        }
        return EmptySlot;
    }

    uint32_t Session::FindExternal(const Frame& frame, uint64_t id, uint64_t version) const
    {
        const auto found = TryFindExternal(frame, id, version);
        if (found == EmptySlot)
        {
            throw std::out_of_range("external geometry version");
        }
        return found;
    }

    void Session::InsertExternalLookup(Frame& frame, uint32_t index)
    {
        if (frame.externalLookup.empty() || frame.external_geometry.size() * 2 > frame.externalLookup.size())
        {
            size_t size = 16;
            while (size < frame.external_geometry.size() * 2 + 2)
            {
                size *= 2;
            }
            frame.externalLookup.assign(size, EmptySlot);
            const auto mask = size - 1;
            for (uint32_t n = 0; n < frame.external_geometry.size(); n++)
            {
                const auto& geometry = frame.external_geometry[n];
                auto slot = ExternalSlot(geometry.geometry, geometry.version, mask);
                while (frame.externalLookup[slot] != EmptySlot)
                {
                    slot = (slot + 1) & mask;
                }
                frame.externalLookup[slot] = n;
            }
            return;
        }
        const auto mask = frame.externalLookup.size() - 1;
        const auto& geometry = frame.external_geometry[index];
        auto slot = ExternalSlot(geometry.geometry, geometry.version, mask);
        while (frame.externalLookup[slot] != EmptySlot)
        {
            slot = (slot + 1) & mask;
        }
        frame.externalLookup[slot] = index;
    }

    void Session::SetExternalInputs(Frame& frame, const URUIPlanBindings& bindings)
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
            std::copy_n(bindings.external_geometry, bindings.external_geometry_count, frame.external_geometry.data());
        }
        frame.externalBounds.resize(bindings.external_geometry_count);
        size_t lookup = 16;
        while (lookup < size_t(bindings.external_geometry_count) * 2 + 2)
        {
            lookup *= 2;
        }
        frame.externalLookup.assign(lookup, EmptySlot);
        for (uint32_t n = 0; n < bindings.external_geometry_count; n++)
        {
            const auto& geometry = frame.external_geometry[n];
            if (!ValidateExternalGeometry(geometry))
            {
                throw std::invalid_argument("external geometry layout/range");
            }
            const auto mask = frame.externalLookup.size() - 1;
            size_t slot = ExternalSlot(geometry.geometry, geometry.version, mask);
            while (frame.externalLookup[slot] != EmptySlot)
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
            if (bindings.externals[n].flags & URUI_PLAN_EXTERNAL_KNOWN_PROVIDER)
            {
                const auto& provider = bindings.externals[n];
                if (provider.command >= frame.commands.size() || provider.flags != URUI_PLAN_EXTERNAL_KNOWN_PROVIDER ||
                    frame.knownProviders[provider.command])
                {
                    throw std::invalid_argument("external provider identity");
                }
                auto paint = std::lower_bound(frame.paints.begin(), frame.paints.end(), provider.command,
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
            for (float field : values)
            {
                if (!std::isfinite(field))
                {
                    throw std::invalid_argument("external draw parameter");
                }
            }
            if (draw.flags & URUI_PLAN_DRAW_ANALYTIC_UNIT_RECT)
            {
                QuadMapping mapping;
                if (draw.flags != URUI_PLAN_DRAW_ANALYTIC_UNIT_RECT || draw.parameter.uv[0] <= 0 ||
                    draw.parameter.uv[1] <= 0 ||
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
            const auto base = MakeParameter(frame.commands[draw.command], frame.matrices[draw.command], identity);
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

    uint32_t Session::FindPendingSlot(const Frame& frame, uint64_t request) const noexcept
    {
        for (size_t i = 0; i < frame.pendingSlots.size(); ++i)
        {
            const uint32_t slotIndex = frame.pendingSlots[i];
            if (frame.preparation[slotIndex].request.request == request)
            {
                return slotIndex;
            }
        }
        return EmptySlot;
    }

    void Session::Invalidate(uint64_t value)
    {
        if (epoch == value)
        {
            return;
        }
        epoch = value;
        reuseValid = false;
        retiredEpochBatches = true;
        CollectRetiredEpochs();
    }

    int Session::Begin(uint64_t value, const URUICommand* commands, const std::array<float, 16>* matrices,
                       uint32_t count, const URUIPaintCommand* paints, uint32_t paint_count,
                       const URUILayerCommand* layers, uint32_t layer_count, URUIPlanInputPacket& out,
                       const URUIPlanUniform* uniforms)
    {
        out = {};
        if (!Thread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        if (!value || count > 65536 || (!commands && count) || (!matrices && count) || (!paints && paint_count) ||
            (!layers && layer_count))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* frame = Free();
        if (!frame)
        {
            return URUI_ERR_REENTRANCY;
        }
        struct UnpublishedInput
        {
            Frame* frame;
            bool published = false;
            ~UnpublishedInput()
            {
                if (!published)
                {
                    frame->ShrinkAndReset();
                }
            }
        } build{frame};
        try
        {
            frame->Reset();
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
                std::fill(frame->uniforms.begin(), frame->uniforms.end(), URUIPlanUniform{{1, 1, 1, 1}, {1, 1, 0, 0}});
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
                    return URUI_ERR_INVALID_ARG;
                }
                previousPaint = p.command_index;
            }
            int previous = 0;
            for (const auto& l : frame->layers)
            {
                if (l.command_index < previous || uint32_t(l.command_index) > count)
                {
                    return URUI_ERR_INVALID_ARG;
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
                    if (operation.kind == URUI_LAYER_PUSH || operation.kind == URUI_LAYER_POP)
                    {
                        currentLayer = operation.target;
                    }
                }
                frame->command_layers[command] = currentLayer;
            }
            frame->colorOffsets.assign(count, -1);
            frame->colorCounts.assign(count, 0);
            frame->knownProviders.assign(count, 0);
            frame->externalOffsets.assign(count, -1);
            frame->externalCounts.assign(count, 0);
            frame->directMaskIndices.assign(count, -1);
            frame->suppressedCommands.assign(count, 0);
            frame->omittedOperations.assign(layer_count, 0);
            frame->imagePrograms.assign(count, {});
            frame->capturedPrograms.assign(layer_count, {});
            frame->capturedParameters.assign(layer_count, EmptySlot);
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
            return URUI_OK;
        }
        catch (const std::bad_alloc&)
        {
            return URUI_ERR_BUDGET;
        }
        catch (...)
        {
            return URUI_ERR_INTERNAL;
        }
    }

    int Session::SetImageDocument(uint64_t input, uint32_t command, uint64_t document)
    {
        if (!Thread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        auto* frame = Find(input);
        if (!frame || !frame->input || frame->epoch != epoch)
        {
            return URUI_ERR_NOT_READY;
        }
        if (frame->resolving || !document || command >= frame->imageSlots.size())
        {
            return URUI_ERR_INVALID_ARG;
        }
        const int32_t slotIndex = frame->imageSlots[command];
        if (slotIndex < 0 || size_t(slotIndex) >= frame->preparation.size())
        {
            return URUI_ERR_NOT_READY;
        }
        auto& slot = frame->preparation[size_t(slotIndex)];
        if (!slot.image || slot.request.slot != command)
        {
            return URUI_ERR_NOT_READY;
        }
        slot.request.document = document;
        return URUI_OK;
    }

    int Session::SetColorFilters(uint64_t input, const ColorFilterInput* values, uint32_t count)
    {
        if (!Thread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        auto* frame = Find(input);
        if (!frame || !frame->input || frame->epoch != epoch)
        {
            return URUI_ERR_NOT_READY;
        }
        if (frame->resolving || (!values && count) || count > 65536u * 16)
        {
            return URUI_ERR_INVALID_ARG;
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
                    return URUI_ERR_INVALID_ARG;
                }
                consecutive = n && value.command == values[n - 1].command ? consecutive + 1 : 1;
                if (consecutive > 16)
                {
                    return URUI_ERR_INVALID_ARG;
                }
                while (paint < frame->paints.size() && uint32_t(frame->paints[paint].command_index) < value.command)
                {
                    paint++;
                }
                if (paint == frame->paints.size() || uint32_t(frame->paints[paint].command_index) != value.command ||
                    frame->paints[paint].reserved != 2)
                {
                    return URUI_ERR_INVALID_ARG;
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
            return URUI_OK;
        }
        catch (const std::bad_alloc&)
        {
            return URUI_ERR_BUDGET;
        }
        catch (...)
        {
            return URUI_ERR_INTERNAL;
        }
    }

    int Session::SetLayerEffects(uint64_t input, const LayerEffect* effects, uint32_t count)
    {
        if (!Thread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        auto* frame = Find(input);
        if (!frame || !frame->input || frame->epoch != epoch)
        {
            return URUI_ERR_NOT_READY;
        }
        if (frame->resolving || (!effects && count) || count > 65536)
        {
            return URUI_ERR_INVALID_ARG;
        }
        try
        {
            for (uint32_t n = 0; n < count; n++)
            {
                const auto& effect = effects[n];
                if (effect.layer_index >= frame->layers.size() ||
                    frame->layers[effect.layer_index].kind != URUI_LAYER_APPLY_MASK || effect.kind > 3)
                {
                    return URUI_ERR_INVALID_ARG;
                }
                if (n && effect.layer_index <= effects[n - 1].layer_index)
                {
                    return URUI_ERR_INVALID_ARG;
                }
            }
            frame->effects.resize(count);
            if (count)
            {
                std::copy_n(effects, count, frame->effects.data());
            }
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
                return URUI_ERR_INVALID_ARG;
            }
            frame->layerComposites.assign(size_t(maximum) + 1, -1);
            frame->haloLayers.assign(size_t(maximum) + 1, 0);
            for (uint32_t n = 0; n < frame->layers.size(); n++)
            {
                if (frame->layers[n].kind == URUI_LAYER_COMPOSITE)
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
                if (layer >= frame->haloLayers.size() || !frame->haloLayers[layer] || frame->layerComposites[layer] < 0)
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
            return URUI_OK;
        }
        catch (const std::bad_alloc&)
        {
            return URUI_ERR_BUDGET;
        }
        catch (...)
        {
            return URUI_ERR_INTERNAL;
        }
    }

    int Session::SetImageGeometry(uint64_t input, uint64_t request, const URUIPlanExternalGeometry& geometry,
                                  const float* transform)
    {
        if (!Thread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        auto* frame = Find(input);
        if (!frame || !frame->input || frame->epoch != epoch)
        {
            return URUI_ERR_NOT_READY;
        }
        const uint32_t slotIndex = FindPendingSlot(*frame, request);
        if (slotIndex == EmptySlot)
        {
            return URUI_ERR_NOT_READY;
        }
        auto& slot = frame->preparation[slotIndex];
        if (!slot.image && slot.request.kind != URUI_PLAN_PREPARE_ELEMENT)
        {
            return URUI_ERR_NOT_READY;
        }
        if (slot.geometry_index != EmptySlot)
        {
            return URUI_ERR_REENTRANCY;
        }
        if (!transform || !ValidateExternalLayout(geometry))
        {
            return URUI_ERR_INVALID_ARG;
        }
        for (uint32_t n = 0; n < 16; n++)
        {
            if (!std::isfinite(transform[n]))
            {
                return URUI_ERR_INVALID_ARG;
            }
        }
        const auto count = frame->external_geometry.size();
        try
        {
            auto index = TryFindExternal(*frame, geometry.geometry, geometry.version);
            if (index == EmptySlot)
            {
                if (count == 65536 || !ValidateExternalGeometry(geometry))
                {
                    return URUI_ERR_INVALID_ARG;
                }
                index = uint32_t(count);
                frame->external_geometry.push_back(geometry);
                frame->externalBounds.push_back({});
                InsertExternalLookup(*frame, index);
            }
            slot.geometry_index = index;
            std::copy_n(transform, 16, slot.geometryTransform.data());
            return URUI_OK;
        }
        catch (const std::bad_alloc&)
        {
            frame->external_geometry.resize(count);
            frame->externalBounds.resize(count);
            return URUI_ERR_BUDGET;
        }
        catch (...)
        {
            frame->external_geometry.resize(count);
            frame->externalBounds.resize(count);
            return URUI_ERR_INTERNAL;
        }
    }

    int Session::SetImageProgramImpl(uint64_t input, uint64_t request, uint64_t program, uint32_t rows,
                                     ImageProgramFiller fill, uint64_t clock)
    {
        if (!Thread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        auto* frame = Find(input);
        if (!frame || !frame->input || frame->epoch != epoch)
        {
            return URUI_ERR_NOT_READY;
        }
        const uint32_t slotIndex = FindPendingSlot(*frame, request);
        if (slotIndex == EmptySlot)
        {
            return URUI_ERR_NOT_READY;
        }
        auto& slot = frame->preparation[slotIndex];
        if (!slot.image && slot.request.kind != URUI_PLAN_PREPARE_ELEMENT)
        {
            return URUI_ERR_NOT_READY;
        }
        if (!program || rows < 2 || rows > 544 || clock > 65536)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto& binding =
            slot.image ? frame->imagePrograms[slot.request.slot] : frame->capturedPrograms[slot.layer_index];
        if (binding.program)
        {
            return URUI_ERR_REENTRANCY;
        }
        const auto offset = frame->imageParameters.size();
        try
        {
            frame->imageParameters.resize(offset + rows);
            auto* data = frame->imageParameters[offset].data();
            const auto status = fill(slot.request, data, rows);
            if (status != URUI_OK)
            {
                frame->imageParameters.resize(offset);
                return status;
            }
            for (uint32_t n = 0; n < rows * 4; n++)
            {
                if (!std::isfinite(data[n]))
                {
                    frame->imageParameters.resize(offset);
                    return URUI_ERR_INVALID_ARG;
                }
            }
            uint32_t clockIndex = EmptySlot;
            if (clock)
            {
                if (frame->clockLookup.size() < clock)
                {
                    frame->clockLookup.resize(size_t(clock), EmptySlot);
                }
                clockIndex = frame->clockLookup[size_t(clock) - 1];
                if (clockIndex == EmptySlot)
                {
                    clockIndex = uint32_t(frame->clocks.size());
                    frame->clocks.push_back({data[6], 0, 0, 0});
                    frame->clockLookup[size_t(clock) - 1] = clockIndex;
                }
                else if (frame->clocks[clockIndex][0] != data[6])
                {
                    frame->imageParameters.resize(offset);
                    return URUI_ERR_INVALID_ARG;
                }
            }
            data[6] = -1;
            binding = {program, uint32_t(offset), rows, clockIndex};
            return URUI_OK;
        }
        catch (const std::bad_alloc&)
        {
            frame->imageParameters.resize(offset);
            return URUI_ERR_BUDGET;
        }
        catch (...)
        {
            frame->imageParameters.resize(offset);
            return URUI_ERR_INTERNAL;
        }
    }

    int Session::ResolveImpl(uint64_t input, const URUIPlanFeedback* feedbacks, uint32_t feedback_count,
                             GeometryResolver resolve, URUIPlanPreparePacket& out, const URUIPlanBindings* resources)
    {
        out = {};
        if (!Thread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        if (feedback_count && !feedbacks)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* frame = Find(input);
        if (!frame || !frame->input || frame->epoch != epoch)
        {
            return URUI_ERR_NOT_READY;
        }
        try
        {
            if (!frame->resolving && resources)
            {
                if (resources->texture_count > 65536 || (!resources->textures && resources->texture_count))
                {
                    return URUI_ERR_INVALID_ARG;
                }
                frame->resources.resize(resources->texture_count);
                if (resources->texture_count)
                {
                    std::copy_n(resources->textures, resources->texture_count, frame->resources.data());
                }
                BuildResourceLookup(*frame);
                frame->resourceFlags = resources->frame_flags;
                SetExternalInputs(*frame, *resources);
            }
            frame->resolving = true;
            if (feedback_count > 0)
            {
                if (frame->pendingSlots.empty() || feedback_count > frame->pendingSlots.size())
                {
                    return URUI_ERR_INVALID_ARG;
                }
                for (uint32_t f = 0; f < feedback_count; ++f)
                {
                    const auto& fb = feedbacks[f];
                    const uint32_t slotIndex = FindPendingSlot(*frame, fb.request);
                    if (slotIndex == EmptySlot)
                    {
                        return URUI_ERR_NOT_READY;
                    }
                    auto& slot = frame->preparation[slotIndex];
                    auto& request = slot.request;
                    if (fb.kind != request.kind)
                    {
                        return URUI_ERR_NOT_READY;
                    }
                    if (request.kind == URUI_PLAN_PREPARE_IMAGE || request.kind == URUI_PLAN_PREPARE_ELEMENT)
                    {
                        if (!RectValid(fb.ink))
                        {
                            return URUI_ERR_INVALID_ARG;
                        }
                        slot.ink.Rect(fb.ink);
                        slot.localImage = slot.image && (fb.capabilities & URUI_PLAN_IMAGE_LOCAL) &&
                                          frame->imagePrograms[slot.request.slot].program &&
                                          slot.geometry_index == EmptySlot;
                        if (request.kind == URUI_PLAN_PREPARE_ELEMENT)
                        {
                            const int effect = frame->effectIndices[slot.layer_index];
                            if (effect < 0)
                            {
                                return URUI_ERR_NOT_READY;
                            }
                            InkRegion projected;
                            TransformInkOrThrow(projected, slot.ink, frame->effects[effect].projection.data());
                            slot.ink = projected;
                        }
                        slot.prepared = true;
                    }
                    else if (request.kind == URUI_PLAN_ELEMENT_LAYOUT)
                    {
                        for (float value : fb.expansion)
                        {
                            if (!std::isfinite(value) || value < 0)
                            {
                                return URUI_ERR_INVALID_ARG;
                            }
                        }
                        if (fb.transform_count > 64 || (!fb.transforms && fb.transform_count))
                        {
                            return URUI_ERR_INVALID_ARG;
                        }
                        for (uint32_t n = 0; n < fb.transform_count * 16; n++)
                        {
                            if (!std::isfinite(fb.transforms[n]))
                            {
                                return URUI_ERR_INVALID_ARG;
                            }
                        }
                        if (fb.capabilities > 1 || fb.reserved)
                        {
                            return URUI_ERR_INVALID_ARG;
                        }
                        slot.transformOffset = uint32_t(frame->coverage.size());
                        slot.transform_count = fb.transform_count;
                        frame->coverage.resize(frame->coverage.size() + fb.transform_count);
                        for (uint32_t n = 0; n < fb.transform_count; n++)
                        {
                            std::copy_n(fb.transforms + n * 16, 16, frame->coverage[slot.transformOffset + n].data());
                        }
                        slot.unityBlend = (fb.capabilities & URUI_PLAN_ELEMENT_UNITY_BLEND) != 0;
                        std::copy_n(fb.expansion, 4, slot.expansion.data());
                        slot.layout = true;
                    }
                    else if (request.kind == URUI_PLAN_ACQUIRE_CAPTURE)
                    {
                        if (feedback_count != 1 || !fb.resource || !fb.source_version || !fb.pixel_width ||
                            !fb.pixel_height || fb.pixel_width > 16384 || fb.pixel_height > 16384 ||
                            !RectValid(fb.ink) || fb.ink[2] <= 0 || fb.ink[3] <= 0)
                        {
                            return URUI_ERR_INVALID_ARG;
                        }
                        const auto* actual = fb.ink;
                        if (actual[0] > request.capture[0] || actual[1] > request.capture[1] ||
                            double(actual[0]) + actual[2] < double(request.capture[0]) + request.capture[2] ||
                            double(actual[1]) + actual[3] < double(request.capture[1]) + request.capture[3])
                        {
                            return URUI_ERR_INVALID_ARG;
                        }
                        request.resource = fb.resource;
                        request.source_version = fb.source_version;
                        request.pixel_width = fb.pixel_width;
                        request.pixel_height = fb.pixel_height;
                        std::copy_n(actual, 4, request.capture);
                        const auto* ink = request.source_ink;
                        request.uv[0] = (ink[0] - actual[0]) / actual[2];
                        request.uv[1] = (actual[1] + actual[3] - ink[1] - ink[3]) / actual[3];
                        request.uv[2] = ink[2] / actual[2];
                        request.uv[3] = ink[3] / actual[3];
                        request.kind = URUI_PLAN_PREPARE_ELEMENT;
                        request.request = Identity();
                        frame->pendingRequests.assign(1, request);
                        frame->pendingSlots.assign(1, slotIndex);
                        out = {input, frame->pendingRequests.data(), 1, 0, nullptr, 0, 0, nullptr, 0, 0};
                        return URUI_OK;
                    }
                    // Remove satisfied request from pending lists in O(1).
                    for (size_t i = 0; i < frame->pendingSlots.size(); ++i)
                    {
                        if (frame->pendingSlots[i] == slotIndex)
                        {
                            const size_t last = frame->pendingSlots.size() - 1;
                            if (i != last)
                            {
                                frame->pendingSlots[i] = frame->pendingSlots[last];
                                frame->pendingRequests[i] = frame->pendingRequests[last];
                            }
                            frame->pendingSlots.pop_back();
                            frame->pendingRequests.pop_back();
                            break;
                        }
                    }
                }
            }
            if (!frame->pendingRequests.empty())
            {
                out = {input,
                       frame->pendingRequests.data(),
                       uint32_t(frame->pendingRequests.size()),
                       0,
                       nullptr,
                       0,
                       0,
                       nullptr,
                       0,
                       0};
                return URUI_OK;
            }
            // Phase 1: Batch ALL independent image-effect preparation requests in one call.
            while (frame->imageCursor < frame->preparation.size())
            {
                const uint32_t idx = frame->imageCursor++;
                auto& slot = frame->preparation[idx];
                if (!slot.image || slot.prepared)
                {
                    continue;
                }
                PrepareSource(*frame, slot, resolve);
                slot.request.request = Identity();
                frame->pendingSlots.push_back(idx);
                frame->pendingRequests.push_back(slot.request);
            }
            if (!frame->pendingRequests.empty())
            {
                out = {input,
                       frame->pendingRequests.data(),
                       uint32_t(frame->pendingRequests.size()),
                       0,
                       nullptr,
                       0,
                       0,
                       nullptr,
                       0,
                       0};
                return URUI_OK;
            }
            // Phase 2: Batch ALL independent element-effect layout queries in one call.
            while (frame->layoutCursor < frame->preparation.size())
            {
                const uint32_t idx = frame->layoutCursor++;
                auto& slot = frame->preparation[idx];
                if (slot.image || slot.layout)
                {
                    continue;
                }
                slot.request.request = Identity();
                frame->pendingSlots.push_back(idx);
                frame->pendingRequests.push_back(slot.request);
            }
            if (!frame->pendingRequests.empty())
            {
                out = {input,
                       frame->pendingRequests.data(),
                       uint32_t(frame->pendingRequests.size()),
                       0,
                       nullptr,
                       0,
                       0,
                       nullptr,
                       0,
                       0};
                return URUI_OK;
            }
            // Phase 3: Sequential element capture (child output ink feeds parent input capture).
            while (frame->elementCursor < frame->preparation.size())
            {
                const uint32_t idx = frame->elementCursor++;
                auto& slot = frame->preparation[idx];
                if (slot.prepared)
                {
                    continue;
                }
                auto& request = slot.request;
                auto ink = Capture(*frame, slot.layer_index, resolve);
                request.source_ink[0] = float(ink.x0);
                request.source_ink[1] = float(ink.y0);
                request.source_ink[2] = float(ink.x1 - ink.x0);
                request.source_ink[3] = float(ink.y1 - ink.y0);
                if (ink.valid)
                {
                    ink.x0 -= slot.expansion[0];
                    ink.y0 -= slot.expansion[1];
                    ink.x1 += slot.expansion[2];
                    ink.y1 += slot.expansion[3];
                    const auto original = ink;
                    for (uint32_t n = 0; n < slot.transform_count; n++)
                    {
                        TransformInkOrThrow(ink, original, frame->coverage[slot.transformOffset + n].data());
                    }
                }
                ink.Capture(request.capture);
                request.kind = URUI_PLAN_ACQUIRE_CAPTURE;
                request.request = Identity();
                frame->pendingSlots.push_back(idx);
                frame->pendingRequests.push_back(request);
                out = {input, frame->pendingRequests.data(), 1, 0, nullptr, 0, 0, nullptr, 0, 0};
                return URUI_OK;
            }
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
                   0,
                   frame->layer_indices.data(),
                   uint32_t(frame->layer_indices.size()),
                   0};
            return URUI_OK;
        }
        catch (const std::bad_alloc&)
        {
            return URUI_ERR_BUDGET;
        }
        catch (const std::invalid_argument&)
        {
            return URUI_ERR_INVALID_ARG;
        }
        catch (const std::out_of_range&)
        {
            return URUI_ERR_NOT_READY;
        }
        catch (...)
        {
            return URUI_ERR_INTERNAL;
        }
    }

    bool Session::MatchesReuseSnapshot(const Frame& source, const URUIPlanBindings& bindings) const noexcept
    {
        if (!reuseValid || reuseEpoch != source.epoch || reuseWidth != source.width || reuseHeight != source.height ||
            reuseSourceFlags != source.resourceFlags || reuseBindingFlags != bindings.frame_flags)
        {
            return false;
        }
        // Check small, frequently changing inputs first for fast-fail on animated frames.
        if (!SameBytes(reuseClocks, source.clocks) ||
            !SameLayerBindings(reuseLayerBindings.data(), reuseLayerBindings.size(), bindings.layer_bindings,
                               bindings.layer_binding_count) ||
            !SameBytes(reuseImageParameters, source.imageParameters) ||
            !SameBytes(reuseExternalDraws, source.external_draws) ||
            !SameTextureBindings(reuseResources.data(), reuseResources.size(), bindings.textures,
                                 bindings.texture_count) ||
            !SameTextureBindings(reuseSourceResources.data(), reuseSourceResources.size(), source.resources.data(),
                                 source.resources.size()) ||
            !SameExternalBindings(reuseExternals.data(), reuseExternals.size(), bindings.externals,
                                  bindings.external_count))
        {
            return false;
        }
        if (reuseExternalGeo.size() != source.external_geometry.size())
        {
            return false;
        }
        for (size_t i = 0; i < reuseExternalGeo.size(); ++i)
        {
            const auto& a = reuseExternalGeo[i];
            const auto& b = source.external_geometry[i];
            if (a.geometry != b.geometry || a.version != b.version || a.vertex_count != b.vertex_count ||
                a.index_count != b.index_count)
            {
                return false;
            }
        }
        if (!SameBytes(reuseLayerRegions, source.layerRegions) ||
            !SameLayerEffects(reuseEffects, source.effects) ||
            !SameColorFilters(reuseColorFilters, source.colorFilters) ||
            !SameImagePrograms(reuseImagePrograms, source.imagePrograms) ||
            !SameImagePrograms(reuseCapturedPrograms, source.capturedPrograms) ||
            !SameLayerCommands(reuseLayers, source.layers) || !SamePaintCommands(reusePaints, source.paints) ||
            !SameBytes(reuseUniforms, source.uniforms) || !SameBytes(reuseMatrices, source.matrices) ||
            !SameCommands(reuseCommands, source.commands))
        {
            return false;
        }
        return true;
    }

    void Session::SaveReuseSnapshot(const Frame& source, const URUIPlanBindings& bindings)
    {
        reuseCommands = source.commands;
        reuseMatrices = source.matrices;
        reuseUniforms = source.uniforms;
        reusePaints = source.paints;
        reuseLayers = source.layers;
        reuseColorFilters = source.colorFilters;
        reuseEffects = source.effects;
        reuseImagePrograms = source.imagePrograms;
        reuseCapturedPrograms = source.capturedPrograms;
        reuseImageParameters = source.imageParameters;
        reuseClocks = source.clocks;
        reuseLayerRegions = source.layerRegions;
        reuseExternalGeo.resize(source.external_geometry.size());
        for (size_t i = 0; i < source.external_geometry.size(); ++i)
        {
            const auto& g = source.external_geometry[i];
            reuseExternalGeo[i] = {g.geometry, g.version, g.vertex_count, g.index_count};
        }
        reuseExternalDraws = source.external_draws;
        reuseSourceResources = source.resources;
        reuseResources.resize(bindings.texture_count);
        if (bindings.texture_count)
        {
            std::copy_n(bindings.textures, bindings.texture_count, reuseResources.data());
        }
        reuseExternals.resize(bindings.external_count);
        if (bindings.external_count)
        {
            std::copy_n(bindings.externals, bindings.external_count, reuseExternals.data());
        }
        reuseLayerBindings.resize(bindings.layer_binding_count);
        if (bindings.layer_binding_count)
        {
            std::copy_n(bindings.layer_bindings, bindings.layer_binding_count, reuseLayerBindings.data());
        }
        reuseEpoch = source.epoch;
        reuseWidth = source.width;
        reuseHeight = source.height;
        reuseSourceFlags = source.resourceFlags;
        reuseBindingFlags = bindings.frame_flags;
        reuseValid = true;
    }

    int Session::CompileImpl(uint64_t input, const URUIPlanBindings& bindings, GeometryResolver resolve,
                             URUIRenderPlanPacket& out)
    {
        out = {};
        if (!Thread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        auto* source = Find(input);
        if (!source || !source->input || source->epoch != epoch)
        {
            return URUI_ERR_NOT_READY;
        }
        if (!source->preparation.empty() && !source->ready)
        {
            return URUI_ERR_REENTRANCY;
        }
        if (bindings.texture_count > 65536 || (!bindings.textures && bindings.texture_count))
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (bindings.external_count > source->paints.size() || (!bindings.externals && bindings.external_count))
        {
            return URUI_ERR_INVALID_ARG;
        }
        size_t externalPaint = 0;
        for (uint32_t n = 0; n < bindings.external_count; n++)
        {
            const auto& binding = bindings.externals[n];
            if (binding.flags > 15 ||
                ((binding.flags & URUI_PLAN_EXTERNAL_KNOWN_PROVIDER) &&
                 binding.flags != URUI_PLAN_EXTERNAL_KNOWN_PROVIDER) ||
                (binding.flags & 3) == 3 || (n && binding.command <= bindings.externals[n - 1].command))
            {
                return URUI_ERR_INVALID_ARG;
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
                return URUI_ERR_INVALID_ARG;
            }
            if ((source->resolving || source->ready) && (binding.flags & URUI_PLAN_EXTERNAL_KNOWN_PROVIDER) &&
                !source->knownProviders[binding.command])
            {
                return URUI_ERR_INVALID_ARG;
            }
            if ((bindings.frame_flags & URUI_PLAN_RESOURCE_SHADER_IMAGE_PAINT) &&
                (binding.flags & URUI_PLAN_EXTERNAL_ORIGINAL_GEOMETRY) && source->paints[externalPaint].reserved == 0)
            {
                return URUI_ERR_INVALID_ARG;
            }
        }
        if ((source->resolving || source->ready) &&
            ((bindings.external_geometry_count && bindings.external_geometry_count != source->external_geometry.size()) ||
             (bindings.external_draw_count && bindings.external_draw_count != source->external_draws.size())))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* target = Free();
        if (!target)
        {
            return URUI_ERR_REENTRANCY;
        }
        geometryHintCursor = 0;
        compiling = target;
        struct BuildScope
        {
            Session& session;
            Frame* target;
            bool published = false;
            ~BuildScope()
            {
                session.compiling = nullptr;
                session.ClearCompilingPins();
                if (!published)
                {
                    target->Reset();
                }
            }
        } scope{*this, target};
        try
        {
            target->Reset();
            BuildBindingTextureLookup(bindings);
            if (!source->ready)
            {
                source->resources.resize(bindings.texture_count);
                if (bindings.texture_count)
                {
                    std::copy_n(bindings.textures, bindings.texture_count, source->resources.data());
                }
                BuildResourceLookup(*source);
                source->resourceFlags = bindings.frame_flags;
                SetExternalInputs(*source, bindings);
                CompleteRegions(*source, resolve);
            }
            else if (source->resolving)
            {
                // Validate consistency of frame_flags and texture bindings between Resolve and Compile.
                if (bindings.frame_flags != source->resourceFlags ||
                    !SameTextureBindings(bindings.textures, bindings.texture_count, source->resources.data(),
                                         source->resources.size()))
                {
                    return URUI_ERR_INVALID_ARG;
                }
            }
            if (bindings.layer_binding_count > source->layerRegions.size() ||
                (!bindings.layer_bindings && bindings.layer_binding_count))
            {
                return URUI_ERR_INVALID_ARG;
            }
            source->cachedLayers.assign(std::max(size_t(1), source->createdLayers.size()), 0);
            for (uint32_t n = 0; n < bindings.layer_binding_count; n++)
            {
                const auto& cached = bindings.layer_bindings[n];
                const uint32_t regionIdx = cached.layer < source->layerRegionLookup.size()
                                               ? source->layerRegionLookup[size_t(cached.layer)]
                                               : EmptySlot;
                if (cached.flags != 1 || regionIdx == EmptySlot)
                {
                    return URUI_ERR_INVALID_ARG;
                }
                const auto& region = source->layerRegions[regionIdx];
                if (!(region.flags & URUI_PLAN_LAYER_REGION_CACHEABLE) || cached.version != region.version)
                {
                    return URUI_ERR_INVALID_ARG;
                }
                source->cachedLayers[size_t(cached.layer)] = 1;
            }
            source->layerStack.clear();
            source->layerStack.push_back(0);
            for (const auto& layer : source->layers)
            {
                if (layer.kind == URUI_LAYER_PUSH)
                {
                    source->cachedLayers[size_t(layer.target)] |= source->cachedLayers[size_t(source->layerStack.back())];
                    source->layerStack.push_back(layer.target);
                }
                else if (layer.kind == URUI_LAYER_POP)
                {
                    source->layerStack.pop_back();
                }
            }
            for (const auto& mask : source->directMasks)
            {
                if (!NormalizedBindingTexture(source->commands[mask.command].texture) ||
                    !NormalizedBindingTexture(mask.texture) ||
                    (!mask.composite.target && !(bindings.frame_flags & URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT)))
                {
                    throw std::invalid_argument("mask resource proof changed");
                }
            }
            if (source->discardedEmptyMask && !(bindings.frame_flags & URUI_PLAN_RESOURCE_FRAMEBUFFER_FETCH))
            {
                throw std::invalid_argument("empty masked group resource proof changed");
            }
            for (const auto& clip : source->binaryMaskClips)
            {
                if (!source->layers[clip.layer_index].target &&
                    !(bindings.frame_flags & URUI_PLAN_RESOURCE_PIXEL_ALIGNED_ROOT))
                {
                    throw std::invalid_argument("binary mask pixel-grid proof changed");
                }
            }
            bool reuse = MatchesReuseSnapshot(*source, bindings);
            if (reuse)
            {
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
                        const uint32_t idx = FindBatchById(operation.geometry);
                        if (idx == EmptySlot || batches[idx].epoch != source->epoch)
                        {
                            reuse = false;
                            break;
                        }
                        batches[idx].lastUse = ++nextLastUse;
                    }
                }
            }
            reuseValid = false;
            if (reuse)
            {
                target->operations = previousOperations;
                target->parameters = previousParameters;
                target->extra_parameters = previousExtraParameters;
                target->parameter_pages = previousParameterPages;
                target->targets = previousTargets;
                target->input = false;
                target->compiled = true;
                target->epoch = epoch;
                target->token = Identity();
                target->version = operationVersion;
                target->parameter_version = parameter_version;
                PinFrame(*target);
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
                reuseValid = true;
                planReuses++;
                scope.published = true;
                return URUI_OK;
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
                target->extra_parameters.insert(target->extra_parameters.end(), mask.values.begin(), mask.values.end());
            }
            const size_t clockOffset = target->extra_parameters.size();
            target->extra_parameters.insert(target->extra_parameters.end(), source->clocks.begin(),
                                            source->clocks.end());
            for (const auto& image : source->imagePrograms)
            {
                if (image.program && image.clock != EmptySlot)
                {
                    target->extra_parameters[imageOffset + image.offset + 1][2] =
                        float(target->parameters.size() * 8 + clockOffset + image.clock);
                }
            }
            for (const auto& image : source->capturedPrograms)
            {
                if (image.program && image.clock != EmptySlot)
                {
                    target->extra_parameters[imageOffset + image.offset + 1][2] =
                        float(target->parameters.size() * 8 + clockOffset + image.clock);
                }
            }
            auto normalizeKeyParameters = [&]() -> uint32_t
            {
                uint32_t base = UINT32_MAX;
                for (const auto& key : keys)
                {
                    base = std::min(base, key.parameter);
                }
                for (auto& key : keys)
                {
                    key.parameter -= base;
                }
                return base;
            };
            auto flush = [&]()
            {
                if (pending.empty())
                {
                    return;
                }
                Order(*source, *target, resolve);
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
                    if (bounds.valid && !(operation.reason & URUI_PLAN_REASON_UNBOUNDED_BOUNDS))
                    {
                        const double values[4] = {bounds.x0, bounds.y0, bounds.x1, bounds.y1};
                        for (int n = 0; n < 4; n++)
                        {
                            if (std::abs(values[n]) > std::numeric_limits<float>::max())
                            {
                                operation.reason |= URUI_PLAN_REASON_UNBOUNDED_BOUNDS;
                            }
                            else
                            {
                                operation.bounds[n] = float(values[n]);
                            }
                        }
                    }
                    const uint32_t baseParameter = normalizeKeyParameters();
                    const auto& geometryBatch = BuildOrReuseBatch(source->epoch, baseParameter, resolve, source);
                    operation.geometry = geometryBatch.id;
                    operation.count = uint32_t(keys.size());
                    operation.base_parameter = baseParameter;
                    bool complete = operation.mask_texture == 0;
                    bool noMaskOrColorFilter = operation.mask_texture == 0;
                    bool hasSharedMatrix = false;
                    bool sharedIsIdentity = true;
                    std::array<float, 16> sharedMatrix{};
                    for (size_t keyIdx = 0; keyIdx < geometryBatch.keys.size(); ++keyIdx)
                    {
                        const auto& key = geometryBatch.keys[keyIdx];
                        const uint32_t index = baseParameter + key.parameter;
                        const auto& p = target->parameters[index];
                        const uint32_t flags = uint32_t(p.metadata[0]);
                        if ((flags & 256u) != 0 || p.metadata[1] != 0.f)
                        {
                            noMaskOrColorFilter = false;
                        }
                        if (!complete)
                        {
                            continue;
                        }
                        if (flags != 0 || p.metadata[1] != 0 || p.metadata[2] != 0 || p.metadata[3] != 0 ||
                            p.tint[0] != 1 || p.tint[1] != 1 || p.tint[2] != 1 || p.tint[3] != 1 || p.uv[0] != 1 ||
                            p.uv[1] != 1 || p.uv[2] != 0 || p.uv[3] != 0)
                        {
                            complete = false;
                            continue;
                        }
                        const auto& m = source->matrices[index];
                        if (m[3] != 0.f || m[7] != 0.f || m[11] != 0.f || m[15] != 1.f)
                        {
                            complete = false;
                            continue;
                        }
                        const auto& c = source->commands[index];
                        const double dx = double(c.tx) - key.originX;
                        const double dy = double(c.ty) - key.originY;
                        if (dx != 0.0 || dy != 0.0)
                        {
                            complete = false;
                            continue;
                        }
                        if (!hasSharedMatrix)
                        {
                            sharedMatrix = m;
                            hasSharedMatrix = true;
                            for (int item = 0; item < 16; item++)
                            {
                                if (m[item] != (item % 5 == 0 ? 1.f : 0.f))
                                {
                                    sharedIsIdentity = false;
                                    break;
                                }
                            }
                        }
                        else if (std::memcmp(sharedMatrix.data(), m.data(), 16 * sizeof(float)) != 0)
                        {
                            complete = false;
                            continue;
                        }
                    }
                    if (complete && hasSharedMatrix)
                    {
                        operation.reason |= URUI_PLAN_REASON_COMPLETE_GEOMETRY;
                        if (sharedIsIdentity)
                        {
                            for (size_t keyIdx = 0; keyIdx < geometryBatch.keys.size(); ++keyIdx)
                            {
                                const auto& key = geometryBatch.keys[keyIdx];
                                const uint32_t index = baseParameter + key.parameter;
                                auto& p = target->parameters[index];
                                p.matrix[12] = 0.f;
                                p.matrix[13] = 0.f;
                                p.matrix[14] = 0.f;
                                if (keyIdx < geometryBatch.uniformColors.size() && geometryBatch.uniformColors[keyIdx] != 0)
                                {
                                    p.metadata[0] = float(uint32_t(p.metadata[0]) | 1024u);
                                }
                            }
                        }
                        else
                        {
                            operation.reason |= URUI_PLAN_REASON_SHARED_MATRIX;
                            for (size_t keyIdx = 0; keyIdx < geometryBatch.keys.size(); ++keyIdx)
                            {
                                const auto& key = geometryBatch.keys[keyIdx];
                                const uint32_t index = baseParameter + key.parameter;
                                auto& p = target->parameters[index];
                                uint32_t flags = uint32_t(p.metadata[0]);
                                if (key.originX != 0 || key.originY != 0)
                                {
                                    p.matrix[8] = key.originX;
                                    p.matrix[9] = key.originY;
                                    flags |= 512u;
                                }
                                if (keyIdx < geometryBatch.uniformColors.size() && geometryBatch.uniformColors[keyIdx] != 0)
                                {
                                    flags |= 1024u;
                                }
                                p.metadata[0] = float(flags);
                            }
                        }
                    }
                    else
                    {
                        if (noMaskOrColorFilter)
                        {
                            operation.reason |= URUI_PLAN_REASON_NO_MASK_OR_COLOR_FILTER;
                        }
                        for (size_t keyIdx = 0; keyIdx < geometryBatch.keys.size(); ++keyIdx)
                        {
                            const auto& key = geometryBatch.keys[keyIdx];
                            const uint32_t index = baseParameter + key.parameter;
                            auto& p = target->parameters[index];
                            uint32_t flags = uint32_t(p.metadata[0]);
                            if (key.originX != 0 || key.originY != 0)
                            {
                                p.matrix[8] = key.originX;
                                p.matrix[9] = key.originY;
                                flags |= 512u;
                            }
                            if (keyIdx < geometryBatch.uniformColors.size() && geometryBatch.uniformColors[keyIdx] != 0)
                            {
                                flags |= 1024u;
                            }
                            p.metadata[0] = float(flags);
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
                    const auto currentDomain = Domain(c.texture);
                    const int maskIndex = source->directMaskIndices[n];
                    const uint64_t mask_texture = maskIndex >= 0 ? source->directMasks[maskIndex].texture : 0;
                    int slot = -1;
                    for (uint32_t t = 0; t < operation.texture_count; t++)
                    {
                        if (operation.textures[t] == c.texture)
                        {
                            slot = int(t);
                            break;
                        }
                    }
                    const uint32_t reason =
                        operation.mask_texture && mask_texture && operation.mask_texture != mask_texture ? 4u
                        : currentDomain != domain                                                        ? 1u
                        : (slot < 0 && operation.texture_count == 8)                                     ? 2u
                        : (vertices + geometry.vertex_count > 100000 || indices + geometry.index_count > 300000) ? 3u
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
                    InkRegion cmdBounds;
                    bool cmdBoundsValid = false;
                    if (sortBoundsValid)
                    {
                        cmdBounds = sortNodes[ordered].bounds;
                        cmdBoundsValid = cmdBounds.valid;
                        if (cmdBoundsValid)
                        {
                            bounds.Join(cmdBounds);
                        }
                        else if (geometry.vertex_count > 0)
                        {
                            operation.reason |= URUI_PLAN_REASON_UNBOUNDED_BOUNDS;
                        }
                    }
                    else
                    {
                        const auto local = LocalBounds(geometry);
                        cmdBoundsValid = TryTransformInk(cmdBounds, local, target->parameters[n].matrix);
                        if (cmdBoundsValid)
                        {
                            bounds.Join(cmdBounds);
                        }
                        else
                        {
                            operation.reason |= URUI_PLAN_REASON_UNBOUNDED_BOUNDS;
                        }
                    }
                    // Analytic clip stripping: clear clip bit when an affine command lies strictly inside its clip rect.
                    auto& param = target->parameters[n];
                    const uint32_t paramFlags = uint32_t(param.metadata[0]);
                    if ((paramFlags & 1u) != 0 && cmdBoundsValid && cmdBounds.valid)
                    {
                        const auto& m = source->matrices[n];
                        if (m[3] == 0.f && m[7] == 0.f && m[11] == 0.f && m[15] == 1.f &&
                            cmdBounds.x0 >= double(param.clip[0]) + 0.5 &&
                            cmdBounds.y0 >= double(param.clip[1]) + 0.5 &&
                            cmdBounds.x1 <= double(param.clip[2]) - 0.5 &&
                            cmdBounds.y1 <= double(param.clip[3]) - 0.5)
                        {
                            param.metadata[0] = float(paramFlags & ~1u);
                        }
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
                    const uint32_t baseParameter = normalizeKeyParameters();
                    operation.geometry = BuildOrReuseBatch(epoch, baseParameter, resolve, source).id;
                    operation.base_parameter = baseParameter;
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
                    if (draw.flags & URUI_PLAN_DRAW_RAW_CONTRACT)
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
                        {geometry.geometry, uint32_t(source->commands.size() + index), 0, geometry.version, 0, 0});
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
                    const uint32_t baseParameter = normalizeKeyParameters();
                    operation.geometry = BuildOrReuseBatch(source->epoch, baseParameter, resolve, source).id;
                    operation.count = uint32_t(keys.size());
                    operation.base_parameter = baseParameter;
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
                    if (prepared.geometry_index != EmptySlot)
                    {
                        const auto& external = source->external_geometry[prepared.geometry_index];
                        geometryId = external.geometry;
                        geometryVersion = external.version;
                        geometry = {nullptr, external.vertex_count, nullptr, external.index_count, nullptr, &external};
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
                            break;
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
                    keys.push_back({geometryId, command, uint32_t(slot), geometryVersion, 0, 0});
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
                    if (const auto* clipped = BinaryClip(*source, uint32_t(index)))
                    {
                        op.reason |= URUI_PLAN_LAYER_CLIP;
                        std::copy(clipped->bounds.begin(), clipped->bounds.end(), op.bounds);
                    }
                    const auto writeLayer = layerOp.kind == URUI_LAYER_APPLY_MASK ? layerOp.source : layerOp.target;
                    if ((layerOp.kind == URUI_LAYER_PUSH || layerOp.kind == URUI_LAYER_SAVE_MASK ||
                         layerOp.kind == URUI_LAYER_COMPOSITE || layerOp.kind == URUI_LAYER_APPLY_MASK) &&
                        source->cachedLayers[size_t(writeLayer)])
                    {
                        op.reason |= URUI_PLAN_LAYER_SKIP_CACHE_WRITE;
                    }
                    if (layerOp.kind == URUI_LAYER_PUSH)
                    {
                        const auto& damage = source->dependencies[size_t(writeLayer)].damageRect;
                        if (damage[2] > 0)
                        {
                            op.reason |= URUI_PLAN_LAYER_DAMAGE;
                            std::copy_n(damage.data(), 4, op.bounds);
                        }
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
                        if (prepared.geometry_index == EmptySlot)
                        {
                            throw std::invalid_argument("captured program needs immutable geometry");
                        }
                        const auto& geometry = source->external_geometry[prepared.geometry_index];
                        const auto parameterIndex = source->capturedParameters[index];
                        URUICommand command{};
                        const URUIPlanUniform uniform{{1, 1, 1, 1}, {1, 1, 0, 0}};
                        auto parameter = MakeParameter(command, prepared.geometryTransform, uniform);
                        parameter.metadata[1] = 2;
                        parameter.metadata[2] = float(target->parameters.size() * 8 + imageOffset + captured.offset);
                        target->parameters[parameterIndex] = parameter;
                        keys.clear();
                        keys.push_back({geometry.geometry, 0, 0, geometry.version, 0, 0});
                        op.geometry = BuildOrReuseBatch(source->epoch, parameterIndex, resolve, source).id;
                        op.program = captured.program;
                        op.count = 1;
                        op.base_parameter = parameterIndex;
                        keys.clear();
                    }
                    target->operations.push_back(op);
                }
                if (n == source->commands.size())
                {
                    break;
                }
                const auto& c = source->commands[n];
                target->parameters[n] = MakeParameter(c, source->matrices[n], source->uniforms[n]);
                if (source->suppressedCommands[n] || source->cachedLayers[size_t(source->command_layers[n])])
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
                        if (prepared.geometry_index != EmptySlot)
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
                            bindings.externals[external].flags != URUI_PLAN_EXTERNAL_KNOWN_PROVIDER)
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
                    const bool bound = external < bindings.external_count && bindings.externals[external].command == n;
                    const auto flags = bound ? bindings.externals[external++].flags : 1u;
                    if (p.reserved != 2 && bound && !(flags & 5))
                    {
                        if ((flags & URUI_PLAN_EXTERNAL_ORIGINAL_GEOMETRY) && c.geometry)
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
                    if (bound && (flags & URUI_PLAN_EXTERNAL_ORIGINAL_GEOMETRY) && c.geometry)
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
            BuildTargetRequests(*source, *target);
            if (previousEpoch != epoch || !SameBytes(previousOperations, target->operations))
            {
                previousOperations = target->operations;
                operationVersion = Identity();
            }
            previousTargets = target->targets;
            // Independent per-page 4KB diff: earlier unchanged pages stay valid even when trailing params change.
            const size_t baseBytes = target->parameters.size() * sizeof(URUIPlanParameter);
            const size_t extraBytes = target->extra_parameters.size() * sizeof(std::array<float, 4>);
            const size_t bytes = baseBytes + extraBytes;
            const size_t prevBaseBytes = previousParameters.size() * sizeof(URUIPlanParameter);
            const size_t prevExtraBytes = previousExtraParameters.size() * sizeof(std::array<float, 4>);
            const size_t prevBytes = prevBaseBytes + prevExtraBytes;
            const size_t pageCount = (bytes + 4095) / 4096;
            target->parameter_pages.resize(pageCount);
            const char* basePtr = reinterpret_cast<const char*>(target->parameters.data());
            const char* extraPtr = reinterpret_cast<const char*>(target->extra_parameters.data());
            const char* prevBasePtr = reinterpret_cast<const char*>(previousParameters.data());
            const char* prevExtraPtr = reinterpret_cast<const char*>(previousExtraParameters.data());
            const bool canPatchInPlace = previousParameters.size() == target->parameters.size() &&
                                         previousExtraParameters.size() == target->extra_parameters.size() &&
                                         previousParameterPages.size() == pageCount;
            bool anyPageChanged = !canPatchInPlace;
            for (size_t page = 0; page < pageCount; page++)
            {
                const size_t first = page * 4096;
                const size_t count = std::min(size_t(4096), bytes - first);
                const size_t oldCount = prevBytes > first ? std::min(size_t(4096), prevBytes - first) : 0;
                const bool same = page < previousParameterPages.size() && count == oldCount &&
                                  SameParameterPageSlice(basePtr, baseBytes, extraPtr, prevBasePtr, prevBaseBytes,
                                                         prevExtraPtr, first, count);
                if (same)
                {
                    target->parameter_pages[page] = previousParameterPages[page];
                }
                else
                {
                    target->parameter_pages[page] = Identity();
                    anyPageChanged = true;
                    if (canPatchInPlace)
                    {
                        size_t offset = first;
                        size_t remaining = count;
                        if (offset < baseBytes)
                        {
                            const size_t n = std::min(remaining, baseBytes - offset);
                            std::memcpy(reinterpret_cast<char*>(previousParameters.data()) + offset, basePtr + offset,
                                        n);
                            offset += n;
                            remaining -= n;
                        }
                        if (remaining > 0)
                        {
                            const size_t extraOffset = offset - baseBytes;
                            std::memcpy(reinterpret_cast<char*>(previousExtraParameters.data()) + extraOffset,
                                        extraPtr + extraOffset, remaining);
                        }
                        previousParameterPages[page] = target->parameter_pages[page];
                    }
                }
            }
            if (anyPageChanged)
            {
                if (!canPatchInPlace)
                {
                    previousParameters = target->parameters;
                    previousExtraParameters = target->extra_parameters;
                    previousParameterPages = target->parameter_pages;
                }
                parameter_version = Identity();
            }
            previousEpoch = epoch;
            SaveReuseSnapshot(*source, bindings);
            target->input = false;
            target->compiled = true;
            target->epoch = epoch;
            target->token = Identity();
            target->version = operationVersion;
            target->parameter_version = parameter_version;
            PinFrame(*target);
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
            return URUI_OK;
        }
        catch (const std::bad_alloc&)
        {
            return URUI_ERR_BUDGET;
        }
        catch (const std::invalid_argument&)
        {
            return URUI_ERR_INVALID_ARG;
        }
        catch (const std::out_of_range&)
        {
            return URUI_ERR_NOT_READY;
        }
        catch (...)
        {
            return URUI_ERR_INTERNAL;
        }
    }

    int Session::Geometry(uint64_t lease, uint64_t geometry, URUIGeometryView& out)
    try
    {
        out = {};
        if (!Thread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        if (!geometry)
        {
            return URUI_ERR_NOT_READY;
        }
        auto* frame = Find(lease);
        if (!frame)
        {
            return URUI_ERR_NOT_READY;
        }
        if (frame->input)
        {
            if (frame->epoch != epoch)
            {
                return URUI_ERR_NOT_READY;
            }
            for (const auto& slot : frame->preparation)
            {
                if (slot.image && slot.sourceReady && slot.request.geometry == geometry)
                {
                    auto& source = frame->sources[slot.source_index];
                    ComposeSource(source);
                    out = {source.vertices.data(), source.indices.data(), uint32_t(source.vertices.size()),
                           uint32_t(source.indices.size()), source.version};
                    return URUI_OK;
                }
            }
            return URUI_ERR_NOT_READY;
        }
        if (!frame->compiled)
        {
            return URUI_ERR_NOT_READY;
        }
        bool used = false;
        const size_t opCount = frame->operations.size();
        if (opCount > 0)
        {
            const size_t start = frame->geometryQueryCursor < opCount ? frame->geometryQueryCursor : 0;
            for (size_t step = 0; step < opCount; ++step)
            {
                const size_t idxOp = (start + step) < opCount ? (start + step) : (start + step - opCount);
                if (frame->operations[idxOp].geometry == geometry)
                {
                    used = true;
                    frame->geometryQueryCursor = idxOp + 1 < opCount ? idxOp + 1 : 0;
                    break;
                }
            }
        }
        if (!used)
        {
            return URUI_ERR_NOT_READY;
        }
        const uint32_t idx = FindBatchById(geometry);
        if (idx == EmptySlot)
        {
            return URUI_ERR_NOT_READY;
        }
        const auto& batch = batches[idx];
        out = {batch.vertices.data(), batch.indices.data(), uint32_t(batch.vertices.size()),
               uint32_t(batch.indices.size()), batch.id};
        return URUI_OK;
    }
    catch (const std::bad_alloc&)
    {
        out = {};
        return URUI_ERR_BUDGET;
    }
    catch (...)
    {
        out = {};
        return URUI_ERR_INTERNAL;
    }

    int Session::Release(uint64_t token)
    {
        if (!Thread())
        {
            return URUI_ERR_WRONG_THREAD;
        }
        auto* frame = Find(token);
        if (!frame)
        {
            return URUI_ERR_NOT_READY;
        }
        UnpinFrame(*frame);
        frame->token = 0;
        frame->input = false;
        frame->compiled = false;
        CollectRetiredEpochs();
        return URUI_OK;
    }
}
