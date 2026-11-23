using System;
using URUI.Internal.Interop;

namespace URUI.Internal.Rendering
{
    /// <summary>
    /// 同步借用原生连续视图的唯一所有者；Unity GPU 资源另沿提交 fence 保活。
    /// </summary>
    internal struct NativeRenderPlanLease : IDisposable
    {
        static readonly Unity.Profiling.ProfilerMarker ReleaseMarker = new("URUI.Native.Plan.Release");
        internal const uint ProtocolVersion = 9;
        ulong surface;
        ulong token;
        internal ulong Surface => surface;
        internal ulong Token => token;

        internal NativeRenderPlanLease(ulong surface, ulong value)
        {
            this.surface = surface;
            token = value;
        }

        internal static NativeRenderPlanLease Begin(ulong surface, out RenderPlanInputPacket packet)
        {
            Require(NativeMethods.urui_plan_begin(surface, out packet));
            if (packet.Protocol != ProtocolVersion)
            {
                NativeMethods.urui_plan_release(surface, packet.Lease);
                throw new NotSupportedException("需要 URUI 绘制计划协议9。");
            }
            return new NativeRenderPlanLease(surface, packet.Lease);
        }

        internal NativeRenderPlanLease Compile(in RenderPlanBindings bindings, out RenderPlanPacket packet)
        {
            Require(NativeMethods.urui_plan_compile(surface, token, bindings, out packet));
            return new NativeRenderPlanLease(surface, packet.Lease);
        }

        internal RenderPlanGeometryView Geometry(ulong geometry)
        {
            Require(NativeMethods.urui_plan_geometry(surface, token, geometry, out var view));
            return view;
        }

        internal static void Require(int result)
        {
            if (result != NativeStatus.Ok)
            {
                throw new InvalidOperationException("URUI 原生绘制计划失败，错误码：" + result);
            }
        }

        public void Dispose()
        {
            using var profile = ReleaseMarker.Auto();
            if (token == 0)
            {
                return;
            }
            var ownedSurface = surface;
            var owned = token;
            surface = 0;
            token = 0;
            Require(NativeMethods.urui_plan_release(ownedSurface, owned));
        }
    }
}
