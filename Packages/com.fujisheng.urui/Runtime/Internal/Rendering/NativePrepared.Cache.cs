using System;
using System.Collections.Generic;

namespace URUI.Internal
{
    internal sealed partial class NativePrepared
    {
        internal sealed class Payload
        {
            internal Vertex[] vertices;
            internal uint[] indices;
            internal ulong batch;
            internal long used;

            internal long Bytes => (long)vertices.Length * 32 + (long)indices.Length * 4;
        }

        // 这里只缓存有界 CPU 基础数据；批次身份用于验证增量基准，不能代表 GPU 仍在使用的 Mesh。
        internal sealed partial class PayloadCache
        {
            readonly Dictionary<uint, Payload> entries = new Dictionary<uint, Payload>();
            readonly long limit;
            readonly int maxEntries;
            long serial;

            internal long Bytes
            {
                get;
                private set;
            }

            internal PayloadCache(long limit = 4L * 1024 * 1024, int maxEntries = 64)
            {
                if (limit < 1 || maxEntries < 1)
                {
                    throw new ArgumentOutOfRangeException();
                }

                this.limit = limit;
                this.maxEntries = maxEntries;
            }

            internal void Clear()
            {
                entries.Clear();
                Bytes = 0;
                serial = 0;
            }

            internal Payload Get(uint slot, int vertices, int indices)
            {
                long bytes = (long)vertices * 32 + (long)indices * 4;
                if (vertices <= 0 || indices <= 0 || bytes > limit)
                {
                    return null;
                }

                if (serial == long.MaxValue)
                {
                    Clear();
                }

                serial++;
                if (entries.TryGetValue(slot, out var old))
                {
                    if (old.vertices.Length == vertices && old.indices.Length == indices)
                    {
                        old.used = serial;
                        return old;
                    }

                    entries.Remove(slot);
                    Bytes -= old.Bytes;
#if URUI_DEBUG
                    Evictions++;
#endif
                }

                while (entries.Count >= maxEntries || Bytes + bytes > limit)
                {
                    uint key = 0;
                    long oldest = long.MaxValue;
                    foreach (var pair in entries)
                    {
                        if (pair.Value.used < oldest)
                        {
                            key = pair.Key;
                            oldest = pair.Value.used;
                        }
                    }

                    Bytes -= entries[key].Bytes;
                    entries.Remove(key);
#if URUI_DEBUG
                    Evictions++;
#endif
                }

                var value = new Payload
                {
                    vertices = new Vertex[vertices],
                    indices = new uint[indices],
                    used = serial
                };
                entries.Add(slot, value);
                Bytes += bytes;
#if URUI_DEBUG
                Peak = Math.Max(Peak, Bytes);
#endif
                return value;
            }
        }

        readonly PayloadCache cpuPayloads;

        UnityEngine.Mesh UploadIncremental(Packet p, Draw d)
        {
            var data = cpuPayloads.Get(d.slot, (int)d.vertices, (int)d.indices);
            if (data == null)
            {
                if (vertices.Length < d.vertices)
                {
                    vertices = new Vertex[d.vertices];
                }

                if (indices.Length < d.indices)
                {
                    indices = new uint[d.indices];
                }

                if (CopyPinned(p, d, 0, vertices, indices, out var full) != 1)
                {
                    throw new InvalidOperationException("Native full-copy overflow fallback");
                }
#if URUI_DEBUG
                LastCopy = full;
#endif
                return UploadMesh(vertices, (int)d.vertices, indices, (int)d.indices);
            }

            DeltaInfo copied;
            int result;
            try
            {
                result = CopyPinned(p, d, data.batch, data.vertices, data.indices, out copied);
            }
            catch
            {
                data.batch = 0;
                throw;
            }
            // Negative native statuses are validated before any writes; retain the known
            // complete base. An uncertain interop exception invalidates it above.
            if (result != 1)
            {
                throw new InvalidOperationException("Native delta copy failed " + result);
            }

            data.batch = d.batch;
#if URUI_DEBUG
            LastCopy = copied;
#endif
            // CPU version commits after a complete synchronous copy. Unity upload can fail;
            // the retry still has complete CPU data. Old meshes never borrow these arrays.
            return UploadMesh(data.vertices, (int)d.vertices, data.indices, (int)d.indices);
        }
    }
}
