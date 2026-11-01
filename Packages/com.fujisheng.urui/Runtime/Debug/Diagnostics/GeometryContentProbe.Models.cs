#if URUI_DEBUG
using System;
using System.Diagnostics;
using UnityEngine;

namespace URUI.Internal.Diagnostics
{
    // 所属类型的内部状态记录；保留嵌套访问边界与原有资源引用关系。
    internal sealed partial class GeometryContentProbe
    {
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
    }
}
#endif
