using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using URUI.Animation;
namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        sealed class ClockIdentity : IEqualityComparer<IUIClock>
        {
            internal static readonly ClockIdentity Instance = new();
            public bool Equals(IUIClock a, IUIClock b) => ReferenceEquals(a, b);
            public int GetHashCode(IUIClock value) => RuntimeHelpers.GetHashCode(value);
        }
        readonly Dictionary<IUIClock, (ulong Identity, double Time)> frozenEffectClocks = new(ClockIdentity.Instance);
        Action<IUIClock> freezeEffectClock;
        void FreezeEffectClocks()
        {
            frozenEffectClocks.Clear();
            freezeEffectClock ??= FreezeEffectClock;
            foreach (var owner in OwnerSurface.DocumentsSnapshot())
            {
                if (owner && owner.IsLoaded)
                {
                    owner.CollectEffectClocks(freezeEffectClock);
                }
            }
        }
        void FreezeEffectClock(IUIClock clock)
        {
            if (frozenEffectClocks.ContainsKey(clock))
            {
                return;
            }
            var time = clock.Now;
            if (!double.IsFinite(time) || !float.IsFinite((float)time))
            {
                throw new InvalidOperationException("效果时钟必须是可表示的有限时间。");
            }
            if (frozenEffectClocks.Count == 65536)
            {
                throw new InvalidOperationException("效果输入窗口的时钟数量超出容量。");
            }
            frozenEffectClocks.Add(clock, ((ulong)frozenEffectClocks.Count + 1, time));
        }
        internal double FrozenEffectTime(IUIClock clock, out ulong identity)
        {
            if (!frozenEffectClocks.TryGetValue(clock, out var value))
            {
                throw new InvalidOperationException("效果时钟未在输入窗口中冻结。");
            }
            identity = value.Identity;
            return value.Time;
        }
    }
}
