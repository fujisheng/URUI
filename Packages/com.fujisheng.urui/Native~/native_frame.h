#pragma once
#include "prepared_batch.h"
#include <cmath>
#include <algorithm>
namespace urui_frame
{
    static_assert(sizeof(void*) == 4 || sizeof(void*) == 8, "supported pointer width");
    static_assert(sizeof(UruiNativeFramePacket) == (sizeof(void*) == 8 ? 128 : 120) &&
                      offsetof(UruiNativeFramePacket, draws) == 72 &&
                      offsetof(UruiNativeFramePacket, count) == 72 + 3 * sizeof(void*),
                  "final frame ABI");
    static_assert(sizeof(UruiNativeMeshView) == 16 + 2 * sizeof(void*) &&
                      offsetof(UruiNativeMeshView, vertex_count) == 2 * sizeof(void*),
                  "mesh view ABI");
    struct Value
    {
        float data[8]{};
    };
    // Match Unity's PUBLIC managed Matrix4x4 operator: four rounded products,
    // left-associated rounded additions. The separate native NEON helper is not
    // this call path. Preserve signed zeros/subnormals; disable FP contraction.
    inline int Prepare(const float* m, float tx, float ty, float* out, Value& value, uint32_t arithmetic = 1)
    {
#if defined(__clang__)
#pragma clang fp contract(off)
#endif
        value = {};
        std::fill(out, out + 16, 0.f);
        if (!m || !std::isfinite(tx) || !std::isfinite(ty))
            return -1;
        for (int k = 0; k < 16; k++)
            if (!std::isfinite(m[k]))
                return -1;
        const float right[16] = {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, tx, ty, 0, 1};
        float next[16];
        for (int col = 0; col < 4; col++)
            for (int row = 0; row < 4; row++)
            {
                float result;
                if (arithmetic == 2)
                {
                    double v = double(m[row]) * right[col * 4];
                    v = v + double(m[4 + row]) * right[col * 4 + 1];
                    v = v + double(m[8 + row]) * right[col * 4 + 2];
                    v = v + double(m[12 + row]) * right[col * 4 + 3];
                    result = float(v);
                }
                else
                {
                    float v = m[row] * right[col * 4];
                    v = v + m[4 + row] * right[col * 4 + 1];
                    v = v + m[8 + row] * right[col * 4 + 2];
                    v = v + m[12 + row] * right[col * 4 + 3];
                    result = v;
                }
                next[col * 4 + row] = result;
            }
        for (float v : next)
            if (!std::isfinite(v))
                return -1;
        std::memcpy(out, next, 64);
        bool affine = next[8] == 0 && next[9] == 0 && next[2] == 0 && next[6] == 0 && next[10] == 1 && next[14] == 0 &&
                      next[3] == 0 && next[7] == 0 && next[11] == 0 && next[15] == 1;
        if (affine)
        {
            value.data[0] = next[0];
            value.data[1] = next[4];
            value.data[2] = next[12];
            value.data[4] = next[1];
            value.data[5] = next[5];
            value.data[6] = next[13];
        }
        return affine ? 1 : 2;
    }
    struct Frame
    {
        uint64_t id = 0;
        UruiFrameStamp stamp{};
        uint32_t affine = 0, fallback = 0, draw_count = 0, capacity = 0;
        std::unique_ptr<Value[]> values;
        std::unique_ptr<std::array<float, 16>[]> worlds;
        std::unique_ptr<UruiPreparedDraw[]> draws;
        std::shared_ptr<const urui_prepared::Plan> geometry;
        size_t Bytes() const
        {
            return size_t(capacity) * 152;
        }
        void Ensure(uint32_t count)
        {
            if (count <= capacity)
                return;
            uint32_t n = std::min(65536u, std::max(count, std::max(16u, capacity * 2)));
            auto v = std::make_unique<Value[]>(n);
            auto w = std::make_unique<std::array<float, 16>[]>(n);
            auto d = std::make_unique<UruiPreparedDraw[]>(n);
            values = std::move(v);
            worlds = std::move(w);
            draws = std::move(d);
            capacity = n;
        }
    };
    class Session
    {
        struct Lease
        {
            uint64_t token = 0;
            std::shared_ptr<const Frame> frame;
        };
        std::thread::id owner = std::this_thread::get_id();
        bool closing = false;
        std::array<Lease, 4> leases{};
        std::vector<std::shared_ptr<Frame>> storage;
        std::shared_ptr<const Frame> current;
        std::vector<uint8_t> mask;
        urui_prepared::Session geometry;
        size_t maxFrameBytes;
        const uint32_t arithmetic;
        static bool Same(const UruiFrameStamp& a, const UruiFrameStamp& b)
        {
            return a.epoch == b.epoch && a.commands == b.commands && a.resources == b.resources && a.count == b.count;
        }
        void Publish(Lease& lease, const std::shared_ptr<const Frame>& f, UruiNativeFramePacket& out)
        {
            const uint64_t token = urui_prepared::Token();
            lease.token = token;
            lease.frame = f;
            current = f;
            out = {id,
                   token,
                   f->stamp.epoch,
                   f->stamp.commands,
                   f->stamp.resources,
                   f->geometry->id,
                   f->id,
                   0,
                   0,
                   f->draws.get(),
                   reinterpret_cast<const float*>(f->values.get()),
                   reinterpret_cast<const float*>(f->worlds.get()),
                   f->stamp.count,
                   f->draw_count,
                   0,
                   0,
                   f->affine,
                   f->fallback,
                   1,
                   1u | (arithmetic << 8)};
            // Memory reporting is optional; actual frame/geometry budgets are always enforced.
            URUI_DEBUG_CODE(out.retained_bytes = uint64_t(Retained());)
        }

    public:
        const uint64_t id = urui_prepared::Token();
        explicit Session(size_t frameBudget = 48u * 1024 * 1024, uint32_t mode = 1)
            : maxFrameBytes(frameBudget), arithmetic(mode)
        {
        }
        bool Thread() const
        {
            return owner == std::this_thread::get_id();
        }
        size_t StorageCount() const
        {
            return storage.size();
        }
        size_t Retained() const
        {
            size_t n = geometry.Retained() + mask.capacity();
            for (const auto& f : storage)
                n += f->Bytes();
            return n;
        }
        bool Pending() const
        {
            for (const auto& l : leases)
                if (l.token)
                    return true;
            return false;
        }
        bool Closing() const
        {
            return closing;
        }
        int Close()
        {
            if (!Thread())
                return -3;
            closing = true;
            current.reset();
            return Pending() ? -5 : 1;
        }
        std::shared_ptr<const Frame> Get(uint64_t token) const
        {
            if (!Thread() || !token)
                return {};
            for (const auto& l : leases)
                if (l.token == token)
                    return l.frame;
            return {};
        }
        int Release(uint64_t token)
        {
            if (!Thread())
                return -3;
            for (auto& l : leases)
                if (token && l.token == token)
                {
                    l.frame.reset();
                    l.token = 0;
                    return 1;
                }
            return -2;
        }
        template <class Resolve, class ValidTexture>
        int Acquire(const UruiFrameStamp& stamp, const std::vector<UruiCommand>& commands,
                    const std::vector<std::array<float, 16>>& matrices, Resolve resolve, ValidTexture texture,
                    UruiNativeFramePacket& out)
        {
            out = {};
            if (!Thread())
                return -3;
            if (closing)
                return -5;
            if (stamp.flags != 1 || stamp.epoch == 0 || stamp.count > 65536 || commands.size() != stamp.count ||
                matrices.size() != stamp.count)
                return -1;
            Lease* free = nullptr;
            for (auto& l : leases)
                if (!l.token)
                {
                    free = &l;
                    break;
                }
            if (!free)
                return -5;
            if (current && Same(current->stamp, stamp))
            {
                Publish(*free, current, out);
                URUI_DEBUG_CODE(out.reused_batches = current->draw_count;)
                return 1;
            }
            // Reclaim ALL unpublished storage plans, including plans shared by multiple
            // free slots. Otherwise a first-free retry can retain unrelated old geometry
            // indefinitely under the independent geometry budget. Arrays stay bounded
            // and reusable; current/leased/shared-reader Frames are never touched.
            for (auto& f : storage)
                if (f.use_count() == 1)
                    f->geometry.reset();
            std::shared_ptr<Frame> next;
            for (auto& f : storage)
                if (f.use_count() == 1)
                {
                    next = f;
                    break;
                }
            if (!next)
            {
                if (storage.size() >= 5)
                    return -5;
                next = std::make_shared<Frame>();
                storage.push_back(next);
            }
            const uint32_t newCapacity =
                stamp.count <= next->capacity
                    ? next->capacity
                    : std::min(65536u, std::max(stamp.count, std::max(16u, next->capacity * 2)));
            size_t frameBytes = size_t(newCapacity) * 152;
            for (const auto& f : storage)
                if (f != next)
                    frameBytes += f->Bytes();
            if (frameBytes > maxFrameBytes)
                return -4;
            next->Ensure(stamp.count);
            mask.resize(stamp.count);
            next->affine = next->fallback = 0;
            next->geometry.reset();
            for (size_t n = 0; n < commands.size(); n++)
            {
                const auto& c = commands[n];
                if (c.geometry && !resolve(c.geometry).vertices)
                    return -2;
                if (c.texture && !texture(c.texture))
                    return -2;
                int kind = Prepare(matrices[n].data(), c.tx, c.ty, next->worlds[n].data(), next->values[n], arithmetic);
                if (kind < 0)
                    return -1;
                mask[n] = kind == 1;
                if (kind == 1)
                    next->affine++;
                else
                    next->fallback++;
            }
            UruiPreparedPacket p{};
            int status = geometry.Acquire(stamp, commands, mask.data(), int(stamp.count), resolve, p);
            if (status != 1)
                return status;
            auto plan = geometry.Get(p.lease);
            geometry.Release(p.lease);
            if (!plan || plan->batches.size() > stamp.count)
                return -8;
            for (size_t n = 0; n < plan->batches.size(); n++)
                next->draws[n] = plan->batches[n]->draw;
            next->geometry = plan;
            next->draw_count = uint32_t(plan->batches.size());
            next->stamp = stamp;
            next->id = urui_prepared::Token();
            Publish(*free, next, out);
            URUI_DEBUG_CODE(out.built_batches = p.built_batches; out.reused_batches = p.reused_batches;
                            out.built_bytes = p.built_bytes;)
            return 1;
        }
    };
} // namespace urui_frame
