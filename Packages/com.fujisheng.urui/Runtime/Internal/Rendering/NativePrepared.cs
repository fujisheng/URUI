using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal
{
    // Copy-only additive ABI. No pointer is retained across the synchronous C call.
    // CPU lease ownership is separate from Unity Mesh/GraphicsFence ownership.
    internal sealed partial class NativePrepared : IDisposable
    {
        const string Dll = NativeLibrary.Name;

        [StructLayout(LayoutKind.Sequential)]
        internal struct Vertex
        {
            internal float x;
            internal float y;
            internal float z;
            internal byte r;
            internal byte g;
            internal byte b;
            internal byte a;
            internal float u;
            internal float v;
            internal float slot;
            internal float pad;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Draw
        {
            internal ulong batch;
            internal ulong texture;
            internal uint slot;
            internal uint count;
            internal uint vertices;
            internal uint indices;
            internal uint kind;
            internal int enabled;
            internal int x;
            internal int y;
            internal int w;
            internal int h;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Packet
        {
            internal ulong lease;
            internal ulong session;
            internal ulong epoch;
            internal ulong plan;
            internal ulong resources;
            internal ulong retained;
            internal ulong builtBytes;
            internal uint draws;
            internal uint commands;
            internal uint built;
            internal uint reused;
            internal uint verticesCopied;
            internal uint indicesCopied;
            internal uint stride;
            internal uint version;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DeltaInfo
        {
            internal ulong baseBatch;
            internal ulong fullBytes;
            internal ulong possibleBytes;
            internal ulong copiedBytes;
            internal uint mode;
            internal uint vertexRanges;
            internal uint indexRanges;
            internal uint reserved;
        }

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_prepared_delta_api_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_prepared_delta_enable(ulong session, int enabled);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_prepared_copy_incremental(
            ulong session,
            ulong lease,
            ulong batch,
            ulong baseBatch,
            [Out] Vertex[] vertices,
            int vc,
            [Out] uint[] indices,
            int ic,
            out DeltaInfo info);

        [DllImport(Dll, EntryPoint = "urui_prepared_copy_incremental", CallingConvention = CallingConvention.Cdecl)]
        static extern int CopyRaw(
            ulong session,
            ulong lease,
            ulong batch,
            ulong baseBatch,
            IntPtr vertices,
            int vc,
            IntPtr indices,
            int ic,
            out DeltaInfo info);

        int CopyPinned(Packet p, Draw d, ulong baseBatch, Vertex[] v, uint[] ix, out DeltaInfo info)
        {
            // 两次固定操作可能分别失败；finally 按实际分配状态释放，避免异常路径长期固定数组。
            GCHandle vp = default;
            GCHandle ip = default;
            try
            {
                vp = GCHandle.Alloc(v, GCHandleType.Pinned);
#if URUI_DEBUG
                pinsForProbe++;
                afterVertexPinForProbe?.Invoke();
#endif
                ip = GCHandle.Alloc(ix, GCHandleType.Pinned);
#if URUI_DEBUG
                pinsForProbe++;
#endif
                return CopyRaw(session, p.lease, d.batch, baseBatch, vp.AddrOfPinnedObject(), v.Length, ip.AddrOfPinnedObject(), ix.Length, out info);
            }
            finally
            {
                if (ip.IsAllocated)
                {
                    ip.Free();
#if URUI_DEBUG
                    pinsForProbe--;
#endif
                }

                if (vp.IsAllocated)
                {
                    vp.Free();
#if URUI_DEBUG
                    pinsForProbe--;
#endif
                }
            }
        }

        readonly bool incremental;

        // Private bounded CPU bases; never Unity/GPU buffers.
        readonly bool pinnedPayload;

        // Private bounded CPU bases; never Unity/GPU buffers.
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_prepared_api_version();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern ulong urui_prepared_create();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_prepared_destroy(ulong session);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_prepared_acquire(ulong session, ref UruiFrameStampNative expected, [In] byte[] mask, int count, out Packet packet);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_prepared_copy_draws(ulong session, ulong lease, [Out] Draw[] draws, int capacity);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_prepared_copy_batch(ulong session, ulong lease, ulong batch, [Out] Vertex[] vertices, int vc, [Out] uint[] indices, int ic);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_prepared_release(ulong session, ulong lease);

        internal static bool Available
        {
            get
            {
                try
                {
                    return urui_prepared_api_version() == 1;
                }
                catch (EntryPointNotFoundException)
                {
                    return false;
                }
                catch (DllNotFoundException)
                {
                    return false;
                }
            }
        }

        static readonly VertexAttributeDescriptor[] layout = {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2)
        };
        readonly int thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        ulong session;
        ulong activeLease;
        bool active;
        byte[] mask = Array.Empty<byte>();
        internal Draw[] draws = Array.Empty<Draw>();
        Vertex[] vertices = Array.Empty<Vertex>();
        uint[] indices = Array.Empty<uint>();

        internal NativePrepared(bool incremental = false, bool pinnedPayload = false)
        {
            if (Marshal.SizeOf<Vertex>() != 32
                || Marshal.SizeOf<Draw>() != 56
                || Marshal.SizeOf<Packet>() != 88
                || Marshal.OffsetOf<Vertex>(nameof(Vertex.r)).ToInt32() != 12
                || Marshal.OffsetOf<Vertex>(nameof(Vertex.u)).ToInt32() != 16
                || Marshal.OffsetOf<Vertex>(nameof(Vertex.slot)).ToInt32() != 24)
            {
                throw new InvalidOperationException("Native prepared ABI layout");
            }

            session = urui_prepared_create();
            if (session == 0)
            {
                throw new InvalidOperationException("Native prepared session unavailable");
            }

            try
            {
                this.pinnedPayload = (incremental || pinnedPayload) && urui_prepared_delta_api_version() == 1;
                this.incremental = incremental && this.pinnedPayload;
                if (this.incremental && urui_prepared_delta_enable(session, 1) != 1)
                {
                    throw new InvalidOperationException("Native incremental session");
                }

                if (this.incremental)
                {
                    cpuPayloads = new PayloadCache();
                }
            }
            catch (EntryPointNotFoundException)
            {
                this.incremental = false;
                this.pinnedPayload = false;
            }
            catch
            {
                urui_prepared_destroy(session);
                session = 0;
                throw;
            }
        }

        void Check()
        {
            if (session == 0 || thread != System.Threading.Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException("Native prepared owner thread/disposed");
            }
        }

        internal Packet Acquire(ParameterFrame frame)
        {
            Check();
            if (active)
            {
                throw new InvalidOperationException("Native prepared reentrant lease");
            }

            if (mask.Length < frame.count)
            {
                mask = new byte[frame.count];
            }

            for (int i = 0; i < frame.count; i++)
            {
                mask[i] = frame.keys[i].affine ? (byte)1 : (byte)0;
            }

            int result = urui_prepared_acquire(session, ref frame.stamp, mask, frame.count, out var p);
            if (result != 1)
            {
                throw new InvalidOperationException("Native prepared acquire " + result);
            }

            active = true;
            activeLease = p.lease;
#if URUI_DEBUG
            RetainedBytes = (long)p.retained;
#endif
            try
            {
                if (p.session != session
                    || p.epoch != frame.stamp.epoch
                    || p.commands != frame.count
                    || p.draws > 65536
                    || p.stride != 32
                    || p.version != 1)
                {
                    throw new InvalidOperationException("Native prepared packet contract");
                }

                if (draws.Length < p.draws)
                {
                    draws = new Draw[p.draws];
                }

                if (urui_prepared_copy_draws(session, p.lease, draws, draws.Length) != (int)p.draws)
                {
                    throw new InvalidOperationException("Native prepared descriptors");
                }

                return p;
            }
            catch
            {
                Release(p);
                throw;
            }
        }

        internal Mesh Upload(Packet p, Draw d)
        {
            Check();
            if (!active
                || p.session != session
                || p.lease != activeLease
                || d.vertices == 0
                || d.vertices > 1048576
                || d.indices == 0
                || d.indices > 3145728)
            {
                throw new InvalidOperationException("Native prepared upload contract");
            }

            if (incremental)
            {
                return UploadIncremental(p, d);
            }

            if (vertices.Length < d.vertices)
            {
                vertices = new Vertex[d.vertices];
            }

            if (indices.Length < d.indices)
            {
                indices = new uint[d.indices];
            }

            if (pinnedPayload)
            {
                if (CopyPinned(p, d, 0, vertices, indices, out var direct) != 1)
                {
                    throw new InvalidOperationException("Native pinned full copy");
                }
#if URUI_DEBUG
                LastCopy = direct;
#endif
            }
            else
            {
                if (urui_prepared_copy_batch(session, p.lease, d.batch, vertices, vertices.Length, indices, indices.Length) != 1)
                {
                    throw new InvalidOperationException("Native prepared payload copy");
                }
#if URUI_DEBUG
                LastCopy = new DeltaInfo
                {
                    fullBytes = (ulong)d.vertices * 32 + (ulong)d.indices * 4,
                    copiedBytes = (ulong)d.vertices * 32 + (ulong)d.indices * 4,
                    possibleBytes = (ulong)d.vertices * 32 + (ulong)d.indices * 4
                };
#endif
            }

            return UploadMesh(vertices, (int)d.vertices, indices, (int)d.indices);
        }

        internal static Mesh UploadMesh(Vertex[] v, int vc, uint[] ix, int ic)
        {
            var mesh = new Mesh
            {
                name = "URUI native prepared immutable batch",
                indexFormat = IndexFormat.UInt32
            };
            try
            {
                mesh.SetVertexBufferParams(vc, layout);
                if (mesh.GetVertexBufferStride(0) != 32
                    || mesh.GetVertexAttributeOffset(VertexAttribute.Color) != 12
                    || mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0) != 16
                    || mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord1) != 24)
                {
                    throw new InvalidOperationException("Unity packed vertex layout");
                }

                mesh.SetVertexBufferData(v, 0, 0, vc, 0, MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetIndexBufferParams(ic, IndexFormat.UInt32);
                mesh.SetIndexBufferData(ix, 0, 0, ic, MeshUpdateFlags.DontRecalculateBounds);
                mesh.subMeshCount = 1;
                mesh.SetSubMesh(0, new SubMeshDescriptor(0, ic, MeshTopology.Triangles) { firstVertex = 0, vertexCount = vc }, MeshUpdateFlags.DontRecalculateBounds);
                mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e9f, 1e9f, 1e6f));
                return mesh;
            }
            catch
            {
                UnityEngine.Object.Destroy(mesh);
                throw;
            }
        }

        internal void Release(Packet p)
        {
            Check();
            if (!active || p.session != session || p.lease != activeLease)
            {
                throw new InvalidOperationException("Native prepared unbalanced lease");
            }

            int result = urui_prepared_release(session, p.lease);
            active = false;
            activeLease = 0;
            if ((long)vertices.Length * 32 + (long)indices.Length * 4 > 4L * 1024 * 1024)
            {
                vertices = Array.Empty<Vertex>();
                indices = Array.Empty<uint>();
            }

            if (result != 1)
            {
                throw new InvalidOperationException("Native prepared release " + result);
            }
        }

        // 活跃租约仍可能被同步复制使用，必须先 Release，不能直接销毁其原生会话。
        public void Dispose()
        {
            Check();
            if (active)
            {
                throw new InvalidOperationException("Native prepared active lease dispose");
            }

            int result = urui_prepared_destroy(session);
            if (result != 1)
            {
                throw new InvalidOperationException("Native prepared close " + result);
            }

            session = 0;
#if URUI_DEBUG
            RetainedBytes = 0;
#endif
            cpuPayloads?.Clear();
            vertices = Array.Empty<Vertex>();
            indices = Array.Empty<uint>();
            draws = Array.Empty<Draw>();
            mask = Array.Empty<byte>();
        }
    }
}
