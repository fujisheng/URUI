#if URUI_DEBUG
using System;
using UnityEngine;

namespace URUI.Internal.Diagnostics
{
    // 诊断帮助类的正确性自检，仅随 URUI_DEBUG 编译。
    internal sealed partial class GeometryContentProbe
    {
        static void Need(bool b, string s)
        {
            if (!b)
            {
                throw new Exception("GEOMETRY_CONTENT_FAIL " + s);
            }
        }

        public static void RunTests()
        {
            Vector3[] p = {
                new Vector3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(1, 1, 0),
                new Vector3(0, 1, 0)
            };
            Vector2[] u = {
                Vector2.zero,
                Vector2.right,
                Vector2.one,
                Vector2.up
            };
            Color32[] c = {
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255),
                new Color32(255, 255, 255, 255)
            };
            int[] ix = {
                0,
                1,
                2,
                0,
                2,
                3
            };
            var probe = new GeometryContentProbe(2, 1, 4096, 16, 32, 32, true);
            var a = probe.Observe(1, 1, p, u, c, ix);
            probe.Commit(a, 4, 6, 0, 10, true);
            var b = probe.Observe(1, 2, p, u, c, ix);
            Need(b.content == a.content && b.active && b.newHandle, "different live id full-equal");
            probe.Commit(b, 4, 6, 0, 10, true);
            probe.Released(1, 1);
            var self = probe.Observe(1, 2, p, u, c, ix);
            Need(!self.active && self.history, "self-owned snapshot not cross-id active opportunity");
            probe.Commit(a, 4, 6, 0, 0, true);
            var changed = (Vector3[])p.Clone();
            changed[0].x = BitConverter.Int32BitsToSingle(int.MinValue);
            var z = probe.Observe(1, 3, changed, u, c, ix);
            Need(z.content != a.content, "signedzero differs under forced collision");
            var cu = (Vector2[])u.Clone();
            cu[0].x = .125f;
            Need(probe.Observe(1, 4, p, cu, c, ix).content != a.content, "UV differs");
            var cc = (Color32[])c.Clone();
            cc[0].a--;
            Need(probe.Observe(1, 5, p, u, cc, ix).content != a.content, "color differs");
            Need(probe.Observe(1, 6, p, u, c, new[] { 0, 2, 1, 0, 3, 2 }).content != a.content, "index order differs");
            p[0].x = 99;
            var mutated = probe.Observe(1, 1, p, u, c, ix);
            Need(mutated.content != a.content && probe.total.sameIdentityChanged == 1, "caller mutation cannot mutate owned history");
            p[0].x = 0;
            probe.Released(1, 1);
            probe.Released(1, 2);
            var h = probe.Observe(1, 7, p, u, c, ix);
            Need(h.history && !h.active, "history not live reuse");
            var epoch = probe.Observe(2, 1, p, u, c, ix);
            Need(epoch.content != a.content && !epoch.active && !epoch.history, "epoch separated");
            Need(probe.total.payloadBytes <= 4096, "budget");
            probe.Clear();
            Need(probe.total.payloadBytes == 0 && probe.active.Count == 0 && probe.bySerial.Count == 0, "clear drops CPU refs");
            var tiny = new GeometryContentProbe(2, 2, 1, 1, 1, 1);
            Need(tiny.Observe(1, 1, p, u, c, ix).content == 0 && tiny.total.skippedLarge == 1, "oversize skipped");
            tiny.Observe(1, 1, p, u, c, ix);
            Need(tiny.total.newHandles == 1 && tiny.total.sameHandleSnapshots == 1, "skipped identities remembered");
            tiny.Clear();
            var bounded = new GeometryContentProbe(2, 3, 240, 2, 2, 2, true);
            for (int j = 0; j < 8; j++)
            {
                var q = (Vector3[])p.Clone();
                q[0].x = j;
                var x = bounded.Observe(1, (ulong)j + 1, q, u, c, ix);
                bounded.Commit(x, 4, 6, 0, 0, true);
            }

            Need(bounded.total.payloadBytes <= 240
                && bounded.bySerial.Count <= 2
                && bounded.seen.Count <= 2
                && bounded.active.Count <= 2
                && bounded.events.Count == 2
                && bounded.total.eventDrops == 6, "all shadow budgets bounded");
            bounded.Clear();
            var cap = new GeometryContentProbe(2, 4, 16384, 32, 64, 64, true);
            for (int j = 0; j < 17; j++)
            {
                var q = (Vector3[])p.Clone();
                q[0].x = j;
                cap.Observe(1, (ulong)j + 1, q, u, c, ix);
            }

            Need(cap.total.skippedBucket == 1, "collision work bound");
            cap.Clear();
            UnityEngine.Debug.Log("GEOMETRY_CONTENT_PASS completeFields collision signedzero UV color topology mutation epoch activeHistory budget clear");
        }
    }
}
#endif
