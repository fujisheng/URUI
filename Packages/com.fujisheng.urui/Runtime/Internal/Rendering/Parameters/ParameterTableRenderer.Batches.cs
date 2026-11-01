using System;
using System.Collections.Generic;
using UnityEngine;

namespace URUI.Internal.Rendering.Parameters
{
    internal sealed partial class ParameterTableRenderer
    {
        internal struct BatchSignature : IEquatable<BatchSignature>
        {
            internal ulong epoch;
            internal int slot;
            internal int count;
            internal ParameterKey state;

            public bool Equals(BatchSignature b) => epoch == b.epoch && slot == b.slot && count == b.count && state.Equals(b.state);
            public override bool Equals(object other) => other is BatchSignature b && Equals(b);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = (int)(epoch ^ (epoch >> 32));
                    h = h * 397 ^ slot;
                    h = h * 397 ^ count;
                    h = h * 397 ^ (int)(state.texture ^ (state.texture >> 32));
                    h = h * 397 ^ state.enabled;
                    h = h * 397 ^ state.x;
                    h = h * 397 ^ state.y;
                    h = h * 397 ^ state.w;
                    return h * 397 ^ state.h;
                }
            }
        }

        internal sealed class SharedBatch
        {
            internal BatchSignature signature;
            internal ulong[] ids;
            internal Mesh mesh;
            internal List<SharedMesh> sources;
            internal int references;
            internal long payload;
            internal long identityPayload;
        }

        readonly Dictionary<BatchSignature, List<SharedBatch>> sharedBatches = new Dictionary<BatchSignature, List<SharedBatch>>();
        readonly bool reuseBatches;
        long sourceCpuPayload;
        long batchIdentityPayload;

        internal int SharedBatchCount
        {
            get
            {
                int n = 0;
                foreach (var bucket in sharedBatches.Values)
                {
                    n += bucket.Count;
                }

                return n;
            }
        }

        static ParameterKey StateOf(ParameterKey value)
        {
            value.geometry = 0;
            return value;
        }

        static bool SameIds(ulong[] ids, ParameterKey[] keys, int start, int count)
        {
            if (ids.Length != count)
            {
                return false;
            }

            for (int j = 0; j < count; j++)
            {
                if (ids[j] != keys[start + j].geometry)
                {
                    return false;
                }
            }

            return true;
        }

        // 哈希只缩小候选范围；复用前仍校验成员顺序和状态，避免碰撞导致错误几何复用。
        bool BorrowBatch(Plan plan, ParameterFrame frame, int start, int end, ref Stats stats)
        {
            var signature = new BatchSignature
            {
                epoch = frame.stamp.epoch,
                slot = start,
                count = end - start,
                state = StateOf(frame.keys[start])
            };
            if (!sharedBatches.TryGetValue(signature, out var bucket))
            {
                return false;
            }

            foreach (var entry in bucket)
            {
                if (!SameIds(entry.ids, frame.keys, start, end - start))
                {
                    continue;
                }

                if (!entry.mesh)
                {
                    throw new InvalidOperationException("Immutable combined mesh destroyed");
                }

                foreach (var source in entry.sources)
                {
                    Source(plan, source.key.id, ref stats, !lazySourceMeshes);
                }

                if (plan.geometryBytes + entry.payload > MaxPlanGeometryBytes)
                {
                    throw new InvalidOperationException("Cached batch logical geometry budget");
                }

                plan.geometryBytes += entry.payload;
                plan.sharedBatches.Add(entry);
                entry.references++;
                plan.batches.Add(new Batch { mesh = entry.mesh, parameter = true, slot = start, count = end - start, key = frame.keys[start], shared = entry });
#if URUI_DEBUG
                stats.batchHits++;
#endif
                return true;
            }

            return false;
        }

        SharedBatch PublishBatch(Plan plan, ParameterFrame frame, int start, int end, Mesh mesh, long payload)
        {
            var entry = new SharedBatch
            {
                signature = new BatchSignature
                {
                    epoch = frame.stamp.epoch,
                    slot = start,
                    count = end - start,
                    state = StateOf(frame.keys[start])
                },
                payload = payload,
                mesh = mesh,
                ids = new ulong[end - start],
                sources = new List<SharedMesh>()
            };
            var seen = new HashSet<ulong>();
            for (int i = start; i < end; i++)
            {
                ulong id = frame.keys[i].geometry;
                entry.ids[i - start] = id;
                if (seen.Add(id))
                {
                    entry.sources.Add(sharedMeshes[(plan.epoch, id)]);
                }
            }

            entry.identityPayload = (long)entry.ids.Length * 8 + (long)entry.sources.Count * IntPtr.Size;
            AdmitPayload(entry.identityPayload);
            bool registered = false;
            bool owned = false;
            bool sourceOwned = false;
            try
            {
                if (!sharedBatches.TryGetValue(entry.signature, out var bucket))
                {
                    bucket = new List<SharedBatch>();
                    sharedBatches.Add(entry.signature, bucket);
                }

                bucket.Add(entry);
                registered = true;
                batchIdentityPayload += entry.identityPayload;
                foreach (var source in entry.sources)
                {
                    source.references++;
                }

                sourceOwned = true;
                plan.sharedBatches.Add(entry);
                entry.references++;
                owned = true;
                return entry;
            }
            catch
            {
                if (!owned)
                {
                    if (sharedBatches.TryGetValue(entry.signature, out var bucket))
                    {
                        if (registered)
                        {
                            bucket.Remove(entry);
                            batchIdentityPayload -= entry.identityPayload;
                        }

                        if (bucket.Count == 0)
                        {
                            sharedBatches.Remove(entry.signature);
                        }
                    }

                    if (sourceOwned)
                    {
                        foreach (var source in entry.sources)
                        {
                            Release(source);
                        }
                    }
                }

                throw;
            }
        }

        // 每个计划持有独立引用；最后一个计划退役后才能销毁共享 Mesh。
        void Release(SharedBatch entry)
        {
            if (entry.references <= 0)
            {
                throw new InvalidOperationException("Unbalanced immutable batch ownership");
            }

            if (--entry.references != 0)
            {
                return;
            }

            var bucket = sharedBatches[entry.signature];
            if (!bucket.Remove(entry))
            {
                throw new InvalidOperationException("Immutable batch identity mismatch");
            }

            if (bucket.Count == 0)
            {
                sharedBatches.Remove(entry.signature);
            }

            combinedPayload -= entry.payload;
            batchIdentityPayload -= entry.identityPayload;
            foreach (var source in entry.sources)
            {
                Release(source);
            }

            if (entry.mesh)
            {
                UnityEngine.Object.Destroy(entry.mesh);
            }
        }
    }
}
