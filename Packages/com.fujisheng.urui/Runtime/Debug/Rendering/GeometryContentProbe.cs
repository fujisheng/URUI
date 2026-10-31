#if URUI_DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace URUI.Internal
{
    // Read-only diagnostic shadow. Never returns a Mesh/resource to the renderer.
    internal sealed class GeometryContentProbe
    {
        internal static int Mode;

        // Diagnostic startup only; does not alter renderer behavior.
        [Serializable]
        internal sealed class Totals
        {
            public int mode;
            public int owner;
            public int events;
            public int newHandles;
            public int sameHandleSnapshots;
            public int unknownIdentity;
            public int distinctPayloads;
            public int differentHandleSamePayload;
            public int newHandleContentMatches;
            public int newHandleActiveMatches;
            public int newHandleHistoryMatches;
            public int activeMatches;
            public int historyOnlyMatches;
            public int sameIdentityChanged;
            public int hashCollisions;
            public int skippedFrameBudget;
            public int skippedLarge;
            public int skippedBucket;
            public int historyResets;
            public int historyOverflow;
            public int eventDrops;
            public long payloadBytes;
            public long highWaterBytes;
            public long hashedBytes;
            public long comparedBytes;
            public long hashTicks;
            public long compareTicks;
            public long cloneTicks;
            public long probeTicks;
            public long snapshotTicks;
            public long meshTicks;
            public long activePotentialMeshTicks;
            public long historyPotentialMeshTicks;
            public int payloadEntries;
            public int activeSources;
            public int identityEntries;
            public long frequency = Stopwatch.Frequency;
            public string schema = "P3f/UV2f/RGBA8/I32/v1; renderer-owned material/pass scope";
        }

        internal struct Token
        {
            internal ulong epoch;
            internal ulong id;
            internal ulong content;
            internal bool newHandle;
            internal bool active;
            internal bool history;
            internal long observerTicks;
            internal int frame;
        }

        struct Event
        {
            internal Token t;
            internal int vertices;
            internal int indices;
            internal long snapshot;
            internal long mesh;
        }

        sealed class Payload
        {
            internal ulong serial;
            internal ulong hash;
            internal ulong origin;
            internal Vector3[] p;
            internal Vector2[] u;
            internal Color32[] c;
            internal int[] i;
            internal long bytes;
        }

        readonly int mode;
        readonly int maxEntries;
        readonly int maxIds;
        readonly int maxEvents;
        readonly int thread;
        readonly long budget;
        readonly bool collisions;
        readonly Dictionary<ulong, List<Payload>> buckets = new Dictionary<ulong, List<Payload>>();
        readonly Dictionary<ulong, Payload> bySerial = new Dictionary<ulong, Payload>();
        readonly Queue<ulong> fifo = new Queue<ulong>();
        readonly Dictionary<(ulong, ulong), ulong> seen = new Dictionary<(ulong, ulong), ulong>();
        readonly Dictionary<(ulong, ulong), ulong> active = new Dictionary<(ulong, ulong), ulong>();
        readonly Dictionary<ulong, int> activeCounts = new Dictionary<ulong, int>();
        readonly List<Event> events;
        int hashFrame = -1;
        long frameBytes;
        ulong epoch;
        ulong serial;
        internal readonly Totals total;

        internal GeometryContentProbe(
            int mode,
            int owner,
            long budget = 4 * 1024 * 1024,
            int maxEntries = 4096,
            int maxIds = 32768,
            int maxEvents = 8192,
            bool forceCollisions = false)
        {
            if (mode < 1 || mode > 2 || budget < 1 || maxEntries < 1 || maxIds < 1 || maxEvents < 1)
            {
                throw new ArgumentOutOfRangeException();
            }

            this.mode = mode;
            this.budget = budget;
            this.maxEntries = maxEntries;
            this.maxIds = maxIds;
            this.maxEvents = maxEvents;
            collisions = forceCollisions;
            thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            events = new List<Event>(Math.Min(maxEvents, 8192));
            total = new Totals
            {
                mode = mode,
                owner = owner
            };
        }

        void Thread()
        {
            if (thread != System.Threading.Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException("Geometry observer thread mismatch");
            }
        }

        static uint B(float x) => unchecked((uint)BitConverter.SingleToInt32Bits(x));

        static ulong H(ulong h, uint x)
        {
            unchecked
            {
                return (h ^ x) * 1099511628211UL;
            }
        }

        ulong Hash(Vector3[] p, Vector2[] u, Color32[] c, int[] ix)
        {
            if (collisions)
            {
                return 1;
            }

            ulong h = H(H(H(1469598103934665603UL, 1), (uint)p.Length), (uint)ix.Length);
            for (int j = 0; j < p.Length; j++)
            {
                h = H(H(H(H(H(h, B(p[j].x)), B(p[j].y)), B(p[j].z)), B(u[j].x)), B(u[j].y));
                h = H(h, ((uint)c[j].r | ((uint)c[j].g << 8) | ((uint)c[j].b << 16) | ((uint)c[j].a << 24)));
            }

            foreach (int k in ix)
            {
                h = H(h, unchecked((uint)k));
            }

            return h;
        }

        bool Equal(Payload x, Vector3[] p, Vector2[] u, Color32[] c, int[] ix)
        {
            if (x.p.Length != p.Length || x.i.Length != ix.Length)
            {
                return false;
            }

            for (int j = 0; j < p.Length; j++)
            {
                total.comparedBytes += 24;
                if (B(x.p[j].x) != B(p[j].x)
                    || B(x.p[j].y) != B(p[j].y)
                    || B(x.p[j].z) != B(p[j].z)
                    || B(x.u[j].x) != B(u[j].x)
                    || B(x.u[j].y) != B(u[j].y)
                    || x.c[j].r != c[j].r
                    || x.c[j].g != c[j].g
                    || x.c[j].b != c[j].b
                    || x.c[j].a != c[j].a)
                {
                    return false;
                }
            }

            for (int j = 0; j < ix.Length; j++)
            {
                total.comparedBytes += 4;
                if (x.i[j] != ix[j])
                {
                    return false;
                }
            }

            return true;
        }

        void ResetHistory()
        {
            buckets.Clear();
            bySerial.Clear();
            fifo.Clear();
            seen.Clear();
            active.Clear();
            activeCounts.Clear();
            total.payloadBytes = 0;
            total.historyResets++;
        }

        void Evict()
        {
            ulong id = fifo.Dequeue();
            if (!bySerial.TryGetValue(id, out var x))
            {
                return;
            }

            bySerial.Remove(id);
            var b = buckets[x.hash];
            b.Remove(x);
            if (b.Count == 0)
            {
                buckets.Remove(x.hash);
            }

            total.payloadBytes -= x.bytes;
        }

        internal Token Observe(ulong e, ulong id, Vector3[] p, Vector2[] u, Color32[] c, int[] ix)
        {
            Thread();
            long start = Stopwatch.GetTimestamp();
            var token = new Token
            {
                epoch = e,
                id = id,
                frame = Time.frameCount
            };
            total.events++;
            if (mode == 1)
            {
                return Finish(token, start);
            }

            if (epoch != 0 && epoch != e)
            {
                ResetHistory();
            }

            epoch = e;
            var key = (e, id);
            bool known = seen.TryGetValue(key, out ulong previous);
            bool remember = known || seen.Count < maxIds;
            token.newHandle = !known && remember;
            if (!known && remember)
            {
                seen.Add(key, 0);
            }

            if (!known && !remember)
            {
                total.historyOverflow++;
                total.unknownIdentity++;
            }
            else if (known)
            {
                total.sameHandleSnapshots++;
            }
            else
            {
                total.newHandles++;
            }

            if (p == null || u == null || c == null || ix == null || p.Length != u.Length || p.Length != c.Length)
            {
                throw new ArgumentException("Snapshot channel lengths");
            }

            long bytes = (long)p.Length * 24 + (long)ix.Length * 4;
            if (bytes > budget || bytes > 1024 * 1024)
            {
                total.skippedLarge++;
                return Finish(token, start);
            }

            if (hashFrame != Time.frameCount)
            {
                hashFrame = Time.frameCount;
                frameBytes = 0;
            }

            if (frameBytes + bytes > 4 * 1024 * 1024)
            {
                total.skippedFrameBudget++;
                return Finish(token, start);
            }

            frameBytes += bytes;
            long t = Stopwatch.GetTimestamp();
            ulong hash = Hash(p, u, c, ix);
            total.hashTicks += Stopwatch.GetTimestamp() - t;
            total.hashedBytes += bytes;
            Payload match = null;
            if (buckets.TryGetValue(hash, out var bucket))
            {
                foreach (var x in bucket)
                {
                    t = Stopwatch.GetTimestamp();
                    bool equal = Equal(x, p, u, c, ix);
                    total.compareTicks += Stopwatch.GetTimestamp() - t;
                    if (equal)
                    {
                        match = x;
                        break;
                    }

                    total.hashCollisions++;
                }
            }

            if (known && previous != 0 && bySerial.TryGetValue(previous, out var old) && old != match)
            {
                t = Stopwatch.GetTimestamp();
                bool equal = Equal(old, p, u, c, ix);
                total.compareTicks += Stopwatch.GetTimestamp() - t;
                if (!equal)
                {
                    total.sameIdentityChanged++;
                }
            }

            if (match == null)
            {
                if (bucket != null && bucket.Count >= 16)
                {
                    total.skippedBucket++;
                    return Finish(token, start);
                }

                while (total.payloadBytes + bytes > budget || bySerial.Count >= maxEntries)
                {
                    Evict();
                }

                if (serial == ulong.MaxValue)
                {
                    throw new InvalidOperationException("Observer serial exhausted");
                }

                t = Stopwatch.GetTimestamp();
                match = new Payload
                {
                    serial = ++serial,
                    hash = hash,
                    origin = id,
                    p = (Vector3[])p.Clone(),
                    u = (Vector2[])u.Clone(),
                    c = (Color32[])c.Clone(),
                    i = (int[])ix.Clone(),
                    bytes = bytes
                };
                total.cloneTicks += Stopwatch.GetTimestamp() - t;
                if (!buckets.TryGetValue(hash, out bucket))
                {
                    bucket = new List<Payload>();
                    buckets.Add(hash, bucket);
                }

                bucket.Add(match);
                bySerial.Add(match.serial, match);
                fifo.Enqueue(match.serial);
                total.payloadBytes += bytes;
                total.highWaterBytes = Math.Max(total.highWaterBytes, total.payloadBytes);
                total.distinctPayloads++;
            }
            else if (match.origin != id)
            {
                total.differentHandleSamePayload++;
                activeCounts.TryGetValue(match.serial, out int n);
                if (active.TryGetValue(key, out ulong self) && self == match.serial)
                {
                    n--;
                }

                token.active = n > 0;
                token.history = !token.active;
                if (token.active)
                {
                    total.activeMatches++;
                }
                else
                {
                    total.historyOnlyMatches++;
                }

                if (token.newHandle)
                {
                    total.newHandleContentMatches++;
                    if (token.active)
                    {
                        total.newHandleActiveMatches++;
                    }
                    else
                    {
                        total.newHandleHistoryMatches++;
                    }
                }
            }

            token.content = match.serial;
            if (remember)
            {
                seen[key] = match.serial;
            }

            return Finish(token, start);
        }

        Token Finish(Token t, long start)
        {
            t.observerTicks = Stopwatch.GetTimestamp() - start;
            total.probeTicks += t.observerTicks;
            return t;
        }

        internal void Commit(Token token, int n, int k, long snapshotTicks, long meshTicks, bool register)
        {
            Thread();
            total.snapshotTicks += snapshotTicks;
            total.meshTicks += meshTicks;
            if (token.active)
            {
                total.activePotentialMeshTicks += meshTicks;
            }

            if (token.history)
            {
                total.historyPotentialMeshTicks += meshTicks;
            }

            if (register && token.content != 0)
            {
                var key = (token.epoch, token.id);
                if (active.TryGetValue(key, out var old))
                {
                    if (old != token.content)
                    {
                        RemoveActive(old);
                        active[key] = token.content;
                        AddActive(token.content);
                    }
                }
                else if (active.Count < maxIds)
                {
                    active.Add(key, token.content);
                    AddActive(token.content);
                }
            }

            if (events.Count < maxEvents)
            {
                events.Add(new Event { t = token, vertices = n, indices = k, snapshot = snapshotTicks, mesh = meshTicks });
            }
            else
            {
                total.eventDrops++;
            }
        }

        void AddActive(ulong id)
        {
            activeCounts.TryGetValue(id, out int n);
            activeCounts[id] = n + 1;
        }

        void RemoveActive(ulong id)
        {
            if (!activeCounts.TryGetValue(id, out int n))
            {
                return;
            }

            if (n <= 1)
            {
                activeCounts.Remove(id);
            }
            else
            {
                activeCounts[id] = n - 1;
            }
        }

        internal void Released(ulong e, ulong id)
        {
            Thread();
            var key = (e, id);
            if (active.TryGetValue(key, out var value))
            {
                active.Remove(key);
                RemoveActive(value);
            }
        }

        internal string Save(string directory)
        {
            Thread();
            Directory.CreateDirectory(directory);
            total.payloadEntries = bySerial.Count;
            total.identityEntries = seen.Count;
            total.activeSources = active.Count;
            File.WriteAllText(Path.Combine(directory, "summary.json"), JsonUtility.ToJson(total, true));
            var b = new StringBuilder(events.Count * 100);
            b.AppendLine("frame,epoch,id,content,vertices,indices,new_handle,active_match,history_match,observer_ticks,snapshot_ticks,mesh_ticks");
            foreach (var x in events)
            {
                b.Append(x.t.frame).Append(',').Append(x.t.epoch).Append(',').Append(x.t.id).Append(',').Append(x.t.content).Append(',').Append(x.vertices).Append(',').Append(x.indices).Append(',').Append(x.t.newHandle ? 1 : 0).Append(',').Append(x.t.active ? 1 : 0).Append(',').Append(x.t.history ? 1 : 0).Append(',').Append(x.t.observerTicks).Append(',').Append(x.snapshot).Append(',').Append(x.mesh).AppendLine();
            }

            File.WriteAllText(Path.Combine(directory, "events.csv"), b.ToString());
            return directory;
        }

        internal void Clear()
        {
            Thread();
            buckets.Clear();
            bySerial.Clear();
            fifo.Clear();
            seen.Clear();
            active.Clear();
            activeCounts.Clear();
            events.Clear();
            total.payloadBytes = 0;
        }

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
