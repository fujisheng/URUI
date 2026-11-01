#if URUI_DEBUG
using System;
using System.Collections.Generic;

namespace URUI.Internal.Diagnostics
{
    // Read-only diagnostic shadow. Never returns a Mesh/resource to the renderer.
    internal sealed partial class GeometryContentProbe
    {
        internal static int Mode;
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
    }
}
#endif
