using System;
using System.Collections.Generic;

namespace URUI.Internal.Rendering
{
    // A completed transparent source is immutable. Recording/submission references
    // prevent this private owner from returning to its pool before every alias ends.
    internal sealed class LayerContentLease : IDisposable
    {
        static readonly Stack<LayerContentLease> idle = new();
        internal EffectTargetPool.Lease Target
        {
            get;
            private set;
        }
        internal ulong Version
        {
            get;
            private set;
        }
        internal bool Cacheable
        {
            get;
            private set;
        }
        internal bool Available
        {
            get;
            private set;
        }
        int references;
        bool retired;
        internal static LayerContentLease Rent(EffectTargetPool.Lease target, ulong version, bool cacheable)
        {
            var value = idle.Count > 0 ? idle.Pop() : new LayerContentLease();
            value.Target = target;
            value.Version = version;
            value.Cacheable = cacheable;
            value.Available = false;
            value.retired = false;
            return value;
        }
        // Only private, unsubmitted content can change identity in place. A
        // completed cached source may still be referenced by read-only buffers.
        internal bool TryUpdateUnpublished(ulong version, bool cacheable)
        {
            if (retired || references != 0 || Available || !Cacheable || !cacheable || version == 0)
            {
                return false;
            }
            Version = version;
            return true;
        }
        internal void Retain()
        {
            if (retired)
            {
                throw new ObjectDisposedException(nameof(LayerContentLease));
            }
            references++;
        }
        internal void Release(bool completed)
        {
            if (references <= 0)
            {
                throw new InvalidOperationException("Unbalanced URUI layer reference.");
            }
            if (completed && !retired)
            {
                Available = true;
            }
            references--;
            Collect();
        }
        public void Dispose()
        {
            if (retired)
            {
                return;
            }
            retired = true;
            Available = false;
            Collect();
        }
        void Collect()
        {
            if (!retired || references != 0)
            {
                return;
            }
            Target.Dispose();
            Target = null;
            Version = 0;
            Cacheable = false;
            if (idle.Count < 2048)
            {
                idle.Push(this);
            }
        }
    }
}
