using System;

namespace URUI.Samples.Tideglass
{
    internal enum FlightOrder { Surge, Rewind }

    internal static class FlightPlan
    {
        internal const int SurgeCharge = 40;
        internal static readonly string[] Orders = { "潮能注入", "冷却回拨" };
        internal static readonly string[] OrderNotes = { "立即补入 40 潮能，上限 100。", "立即恢复引潮与裂光的冷却。" };
        internal static readonly string[] DrillNames = { "蓄能与终结", "双层破壳", "交替连携" };
        internal static readonly string[] DrillNotes = { "积攒 60 潮能，释放一次潮鸣。", "用裂光击碎两层镜壳。", "连续交替施放三次引潮与裂光。" };
        internal static readonly int[] DrillGoals = { 1, 2, 3 };
    }

    [Serializable]
    internal sealed class FlightReport
    {
        public int milliseconds, route, seal, order, escort;
        public bool orderUsed;
        public int[] casts, damage;

        internal long TotalDamage => (long)damage[0] + damage[1] + damage[2];

        internal bool IsValid => milliseconds > 0 && (uint)route < TideglassRoute.All.Length &&
            (uint)seal < TideglassCatalog.Seals.Length && (uint)order < FlightPlan.Orders.Length &&
            (uint)escort <= (int)Wing.Observer && casts?.Length == 3 && damage?.Length == 3 &&
            casts[0] >= 0 && casts[1] >= 0 && casts[2] >= 0 &&
            damage[0] >= 0 && damage[1] >= 0 && damage[2] >= 0 && TotalDamage > 0;
    }
}
