#if URUI_DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace URUI.Internal.Diagnostics
{
    // 诊断模式下比较几何内容并维护有界历史，不参与默认运行时。
    internal sealed partial class GeometryContentProbe
    {
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
    }
}
#endif
