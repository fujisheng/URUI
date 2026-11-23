using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal.Rendering
{
    internal sealed unsafe partial class RenderPlanResources
    {
        // 内部所有者与 GPU Mesh 同时复用；提交引用归零之前不改写，也不重绑旧记录中的对象。
        internal sealed class Entry : IDisposable
        {
            static readonly Stack<Entry> idle = new();

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            static void ResetStatics()
            {
                idle.Clear();
            }

            internal Mesh Mesh;
            internal long Bytes;
            internal long Used;
            internal RenderPlanResources Owner;
            internal int VertexCapacity;
            internal int IndexCapacity;
            internal IndexFormat IndexFormat;
            internal VertexAttributeDescriptor[] Layout;
            internal int References
            {
                get;
                private set;
            }
            bool retired;

            internal static Entry Rent()
            {
                var entry = idle.Count > 0 ? idle.Pop() : new Entry();
                entry.retired = false;
                return entry;
            }

            internal void RetainReference()
            {
                if (retired)
                {
                    throw new ObjectDisposedException(nameof(Entry));
                }
                checked
                {
                    References++;
                }
            }

            internal void ReleaseReference()
            {
                if (References == 0)
                {
                    throw new InvalidOperationException("几何版本引用被重复释放。");
                }
                References--;
                Collect();
            }

            public void Dispose()
            {
                if (retired)
                {
                    return;
                }
                retired = true;
                Collect();
            }

            void Collect()
            {
                if (!retired || References != 0)
                {
                    return;
                }
                if (Mesh)
                {
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(Mesh);
                }
                Mesh = null;
                if (Owner != null)
                {
                    Owner.allocatedBytes -= Bytes;
                }
                Bytes = 0;
                Owner = null;
                Layout = null;
                VertexCapacity = IndexCapacity = 0;
                if (idle.Count < 2048)
                {
                    idle.Push(this);
                }
            }
        }

        List<Entry> recording = new();
        List<Entry> nextRecording = new();
        internal bool HasRecordingGeometry => recording.Count != 0;

        static bool SameLayout(VertexAttributeDescriptor[] a, VertexAttributeDescriptor[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || a.Length != b.Length)
            {
                return false;
            }
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i].attribute != b[i].attribute || a[i].format != b[i].format || a[i].dimension != b[i].dimension || a[i].stream != b[i].stream)
                {
                    return false;
                }
            }
            return true;
        }

        Entry Acquire(ulong identity, int vertices, int indices, IndexFormat format, VertexAttributeDescriptor[] attributes, int vertexStride, bool external, out bool configure)
        {
            var vertexCapacity = external ? Mathf.NextPowerOfTwo(vertices) : vertices;
            var indexCapacity = external ? Mathf.NextPowerOfTwo(indices) : indices;
            var bucketBytes = (long)vertexCapacity * vertexStride + (long)indexCapacity * (format == IndexFormat.UInt16 ? 2 : 4);

            ulong oldIdentity = 0;
            Entry candidate = null;
            int maxReuseVerts = external && vertexCapacity <= int.MaxValue / 4 ? Math.Max(64, vertexCapacity * 4) : int.MaxValue;
            int maxReuseIndices = external && indexCapacity <= int.MaxValue / 4 ? Math.Max(96, indexCapacity * 4) : int.MaxValue;
            foreach (var pair in meshes)
            {
                var entry = pair.Value;
                if (used.Contains(pair.Key) || entry.References != 0 || entry.IndexFormat != format ||
                    entry.VertexCapacity < vertices || entry.IndexCapacity < indices ||
                    entry.VertexCapacity > maxReuseVerts || entry.IndexCapacity > maxReuseIndices ||
                    !SameLayout(entry.Layout, attributes))
                {
                    continue;
                }
                if (candidate == null || entry.Used < candidate.Used)
                {
                    oldIdentity = pair.Key;
                    candidate = entry;
                }
            }
            if (candidate != null)
            {
                meshes.Remove(oldIdentity);
                candidate.Used = ++use;
                configure = false;
                return candidate;
            }

            Reserve(bucketBytes);
            var mesh = new Mesh
            {
                name = "URUI native geometry",
                hideFlags = HideFlags.HideAndDontSave
            };
            var value = Entry.Rent();
            value.Mesh = mesh;
            value.Bytes = bucketBytes;
            value.Owner = this;
            value.Used = ++use;
            value.Layout = attributes;
            value.VertexCapacity = vertexCapacity;
            value.IndexCapacity = indexCapacity;
            value.IndexFormat = format;
            allocatedBytes += bucketBytes;
            configure = true;
            return value;
        }

        void CommitRecording()
        {
            nextRecording.Clear();
            foreach (var identity in used)
            {
                var entry = meshes[identity];
                entry.RetainReference();
                nextRecording.Add(entry);
            }
            EndRecording();
            var previous = recording;
            recording = nextRecording;
            nextRecording = previous;
        }

        internal void EndRecording()
        {
            for (var i = 0; i < recording.Count; i++)
            {
                var entry = recording[i];
                recording[i] = null;
                entry.ReleaseReference();
            }
            recording.Clear();
        }

        internal void PinSubmission(List<Entry> destination, HashSet<Entry> dedup)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(RenderPlanResources));
            }
            for (var i = 0; i < recording.Count; i++)
            {
                var entry = recording[i];
                if (!dedup.Add(entry))
                {
                    continue;
                }
                try
                {
                    entry.RetainReference();
                    destination.Add(entry);
                }
                catch
                {
                    dedup.Remove(entry);
                    throw;
                }
            }
        }
    }
}
