#if URUI_DEBUG
namespace URUI.Internal.Rendering.Meshes
{
    internal sealed partial class BatchScratch
    {
        internal long ClearTicks
        {
            get;
            private set;
        }
        internal long HighWaterBytes
        {
            get;
            private set;
        }
        internal long OverflowCount
        {
            get;
            private set;
        }
        internal long Leases
        {
            get;
            private set;
        }
        internal long ColdLeases
        {
            get;
            private set;
        }
        internal long CapacityGrowthEvents
        {
            get;
            private set;
        }

        long atBegin;
    }
}
#endif
