#if URUI_DEBUG
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace URUI.Internal
{
    internal sealed partial class NativePrepared
    {
        [ThreadStatic]
        internal static int pinsForProbe;
        internal static Action afterVertexPinForProbe;

        internal static void DeltaContract()
        {
            void Need(bool value, string m)
            {
                if (!value)
                {
                    throw new Exception("NATIVE_DELTA_CONTRACT " + m);
                }
            }

            Need(Marshal.SizeOf<DeltaInfo>() == 48, "info ABI48");
            var bounded = new PayloadCache(512, 2);
            var one = bounded.Get(0, 3, 3);
            one.batch = 92;
            bounded.Get(1, 3, 3);
            bounded.Get(1, 3, 3);
            bounded.Get(2, 3, 3);
            Need(bounded.Evictions == 1 && bounded.Bytes == 216, "LRU bounded eviction");
            Need(!ReferenceEquals(bounded.Get(0, 3, 3), one), "evicted base cannot alias");
            Need(bounded.Get(9, int.MaxValue, 3) == null && bounded.Bytes <= 512, "oversize fallback bounded");
            bounded.Clear();
            Need(bounded.Bytes == 0, "clear releases arrays");
            Need(NativeMethods.urui_create(256, 256, null) == 1, "create");
            NativePrepared owner = null;
            NativePrepared oracle = null;
            Mesh a = null;
            Mesh b = null;
            Mesh c = null;
            Color32 oldColor = default;
            try
            {
                NativeMethods.urui_render_plan_enable(1);
                Need(NativeMethods.urui_load_document("<rml><head><style>body{margin:0;}div{position:absolute;width:80px;height:80px;background-color:#f02070a0;}</style></head><body><div id='delta-a'/><div id='delta-b' style='left:12px;'/></body></rml>") == 1, "load");
                NativeMethods.urui_update();
                NativeMethods.urui_render();
                var f = new ParameterFrame();
                Need(f.Capture(), "capture");
                owner = new NativePrepared(true);
                oracle = new NativePrepared(false, true);
                var p = owner.Acquire(f);
                var stale = p;
                Draw staleDraw = owner.draws[0];
                try
                {
                    bool pinFailed = false;
                    afterVertexPinForProbe = () => throw new InvalidOperationException("injected second-pin allocation boundary");
                    try
                    {
                        owner.Upload(p, owner.draws[0]);
                    }
                    catch (InvalidOperationException)
                    {
                        pinFailed = true;
                    }
                    finally
                    {
                        afterVertexPinForProbe = null;
                    }

                    Need(pinFailed && pinsForProbe == 0, "partial pin failure cleanup");
                    a = owner.Upload(p, owner.draws[0]);
                    Need(pinsForProbe == 0, "synchronous pins released");
                    oldColor = a.colors32[0];
                    Need(owner.LastCopy.mode == 0, "first full");
                }
                finally
                {
                    owner.Release(p);
                }

                Need(NativeMethods.urui_set_style("delta-a", "background-color", "#80f020c0") == 1, "mutate");
                NativeMethods.urui_update();
                NativeMethods.urui_render();
                Need(f.Capture(), "changed capture");
                p = owner.Acquire(f);
                try
                {
                    bool staleRejected = false;
                    bool memberRejected = false;
                    try
                    {
                        var wrong = owner.Upload(stale, staleDraw);
                        UnityEngine.Object.DestroyImmediate(wrong);
                    }
                    catch (InvalidOperationException)
                    {
                        staleRejected = true;
                    }

                    try
                    {
                        var wrong = owner.Upload(p, staleDraw);
                        UnityEngine.Object.DestroyImmediate(wrong);
                    }
                    catch (InvalidOperationException)
                    {
                        memberRejected = true;
                    }

                    Debug.Log("NATIVE_DELTA_REVIEW_CHECK stale=" + staleRejected + " member=" + memberRejected);
                    Need(staleRejected && memberRejected, "stale packet and absent member must reject before cache reuse");
                    bool releaseRejected = false;
                    try
                    {
                        owner.Release(stale);
                    }
                    catch (InvalidOperationException)
                    {
                        releaseRejected = true;
                    }

                    Need(releaseRejected, "stale release preserves active lease");
                    b = owner.Upload(p, owner.draws[0]);
                    Need(owner.LastCopy.mode == 1 && owner.LastCopy.copiedBytes < owner.LastCopy.fullBytes, "expected strict base delta copy");
                }
                finally
                {
                    owner.Release(p);
                }

                var fresh = oracle.Acquire(f);
                try
                {
                    c = oracle.Upload(fresh, oracle.draws[0]);
                }
                finally
                {
                    oracle.Release(fresh);
                }

                Need(b.vertexCount == c.vertexCount, "length");
                for (int i = 0; i < b.vertexCount; i++)
                {
                    Need(b.vertices[i] == c.vertices[i] && b.uv[i] == c.uv[i] && b.colors32[i].Equals(c.colors32[i]) && b.uv2[i] == c.uv2[i], "fresh oracle attributes");
                }

                Need(a.colors32[0].Equals(oldColor), "old Mesh unchanged");
                for (int i = 0; i < b.triangles.Length; i++)
                {
                    Need(b.triangles[i] == c.triangles[i], "fresh oracle indices");
                }

                Debug.Log("NATIVE_DELTA_CPU_CACHE_ORACLE_PASS copied=" + owner.LastCopy.copiedBytes + " full=" + owner.LastCopy.fullBytes);
            }
            finally
            {
                owner?.Dispose();
                oracle?.Dispose();
                NativeMethods.urui_destroy();
                if (a)
                {
                    UnityEngine.Object.DestroyImmediate(a);
                }

                if (b)
                {
                    UnityEngine.Object.DestroyImmediate(b);
                }

                if (c)
                {
                    UnityEngine.Object.DestroyImmediate(c);
                }
            }
        }

        internal static void Contract()
        {
            void Need(bool v, string m)
            {
                if (!v)
                {
                    throw new Exception("NATIVE_PREPARED_CONTRACT " + m);
                }
            }

            Need(Available, "API1");
            Need(NativeMethods.urui_create(256, 256, null) == 1, "create");
            NativePrepared owner = null;
            Mesh a = null;
            Mesh b = null;
            try
            {
                Need(NativeMethods.urui_render_plan_enable(1) == 1, "enable");
                Need(NativeMethods.urui_load_document("<rml><head><style>body{margin:0;}div{position:absolute;width:80px;height:80px;background-color:#ff008080;}</style></head><body><div/><div style='left:12px;'/></body></rml>") == 1, "load");
                NativeMethods.urui_update();
                NativeMethods.urui_render();
                var f = new ParameterFrame();
                Need(f.Capture() && f.count == 2, "frame");
                owner = new NativePrepared();
                var p = owner.Acquire(f);
                try
                {
                    Need(p.draws == 1 && p.built == 1, "native grouping hit");
                    var d = owner.draws[0];
                    a = owner.Upload(p, d);
                    Need(a.vertexCount == (int)d.vertices && a.GetVertexBufferStride(0) == 32, "actual upload");
                    var av = a.vertices;
                    var ac = a.colors32;
                    var at = a.triangles;
                    var au = a.uv;
                    int offset = 0;
                    int index = 0;
                    for (int n = 0; n < f.count; n++)
                    {
                        Need(NativeMethods.urui_get_geometry_info(f.keys[n].geometry, out int vc, out int ic) == 1, "source info");
                        var original = new VertexNative[vc];
                        var ids = new int[ic];
                        Need(NativeMethods.urui_copy_geometry(f.keys[n].geometry, original, vc, ids, ic) == 1, "oracle copy");
                        for (int j = 0; j < vc; j++)
                        {
                            var q = original[j];
                            Need(av[offset + j] == new Vector3(q.x, q.y, 0)
                                && au[offset + j] == new Vector2(q.u, q.v)
                                && ac[offset + j].r == q.r
                                && ac[offset + j].a == q.a, "packed source oracle");
                            Need(a.uv2[offset + j].x == n, "instance slot");
                        }

                        for (int j = 0; j < ic; j++)
                        {
                            Need(at[index + j] == offset + ids[j], "ordered index");
                        }

                        offset += vc;
                        index += ic;
                    }
                    // Public Mesh API owns a CPU copy before the same staging arrays are reused.
                    owner.vertices[0].x += 91;
                    owner.vertices[0].a = 1;
                    owner.indices[0] = owner.indices[1];
                    b = UploadMesh(owner.vertices, (int)d.vertices, owner.indices, (int)d.indices);
                    owner.vertices[0].x = -902;
                    owner.indices[0] = 0;
                    Need(a.vertices[0] == av[0] && a.colors32[0].a == ac[0].a && a.triangles[0] == at[0], "first upload independent");
                    Need(b.vertices[0].x == av[0].x + 91 && b.colors32[0].a == 1, "second upload independent");
                }
                finally
                {
                    owner.Release(p);
                }

                var warm = owner.Acquire(f);
                Need(warm.builtBytes == 0 && warm.reused == warm.draws, "native reuse");
                owner.Release(warm);
                Need(NativeMethods.urui_remove_element("absent") == 0, "missing safe");
                NativeMethods.urui_unload_document();
                NativeMethods.urui_destroy();
                owner.Dispose();
                owner = null;
                Debug.Log("NATIVE_PREPARED_CPU_COPY_ABI_ORACLE_PASS packed32 draw56 packet88");
            }
            finally
            {
                if (owner != null)
                {
                    owner.Dispose();
                }

                NativeMethods.urui_destroy();
                if (a)
                {
                    UnityEngine.Object.DestroyImmediate(a);
                }

                if (b)
                {
                    UnityEngine.Object.DestroyImmediate(b);
                }
            }
        }

        internal DeltaInfo LastCopy
        {
            get;
            private set;
        }

        internal sealed partial class PayloadCache
        {
            internal long Peak
            {
                get;
                private set;
            }
            internal int Evictions
            {
                get;
                private set;
            }
        }

        internal long RetainedBytes
        {
            get;
            private set;
        }

        internal long StagingBytes => (long)vertices.Length * 32 + (long)indices.Length * 4 + (long)draws.Length * 56 + mask.Length + (cpuPayloads?.Bytes ?? 0);
    }
}
#endif
