using System;
using System.Collections.Generic;

namespace URUI.Internal.Rendering
{
    // A completed transparent source is immutable. Recording/submission references
    // prevent this private owner from returning to its pool before every alias ends.
    internal sealed class LayerContentLease : IDisposable
    {
        static readonly Stack<LayerContentLease> idle = new();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            idle.Clear();
        }
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
        // Writes from different layouts may be queued in different SRP contexts.
        // Only a whole-layer write queued after older layouts have finished can
        // establish which pixels outside the current dirty region are valid.
        ulong writeLayout;
        ulong writeGeneration;
        ulong completeLayout;
        int currentWrites;
        int olderWrites;
        ulong recordedSubmission;
        ulong recordedLayout;
        bool recordedPartial;
        internal static LayerContentLease Rent(EffectTargetPool.Lease target, ulong version, bool cacheable)
        {
            var value = idle.Count > 0 ? idle.Pop() : new LayerContentLease();
            value.Target = target;
            value.Version = version;
            value.Cacheable = cacheable;
            value.Available = false;
            value.retired = false;
            value.writeLayout = value.writeGeneration = value.completeLayout = 0;
            value.currentWrites = value.olderWrites = 0;
            value.recordedSubmission = value.recordedLayout = 0;
            value.recordedPartial = false;
            return value;
        }
        internal bool CanRedrawRegion(ulong layout)
            => !retired && layout != 0 && completeLayout == layout && Target.Texture.IsCreated();

        internal void RecordWrite(ulong submission, ulong layout, bool partial)
        {
            recordedSubmission = submission;
            recordedLayout = layout;
            recordedPartial = partial;
        }

        internal bool PinWrite(ulong submission, out ulong generation, out bool canPublish)
        {
            generation = 0;
            canPublish = false;
            if (recordedSubmission != submission || Cacheable)
            {
                return false;
            }
            if (recordedPartial && !CanRedrawRegion(recordedLayout))
            {
                throw new InvalidOperationException("局部重绘所需的图层内容已失效。");
            }
            if (writeLayout != recordedLayout)
            {
                olderWrites += currentWrites;
                currentWrites = 0;
                writeLayout = recordedLayout;
                completeLayout = 0;
                checked
                {
                    writeGeneration++;
                }
            }
            currentWrites++;
            generation = writeGeneration;
            canPublish = recordedLayout != 0 && !recordedPartial && olderWrites == 0;
            return true;
        }

        internal void FinishWrite(ulong generation, bool publish)
        {
            if (generation != writeGeneration)
            {
                olderWrites--;
                return;
            }
            currentWrites--;
            if (publish && !retired && Target.Texture.IsCreated())
            {
                completeLayout = writeLayout;
            }
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
