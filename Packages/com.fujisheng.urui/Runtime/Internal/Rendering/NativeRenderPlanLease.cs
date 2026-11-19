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
        internal const uint ProtocolVersion = 8;
        ulong token;
        internal ulong Token => token;
        internal NativeRenderPlanLease(ulong value)
        {
            token = value;
        }
        internal static NativeRenderPlanLease Begin(out RenderPlanInputPacket packet)
        {
            Require(NativeMethods.urui_plan_begin(out packet));
            if (packet.Protocol != ProtocolVersion)
            {
                NativeMethods.urui_plan_release(packet.Lease);
                throw new NotSupportedException("需要 URUI 绘制计划协议8。");
            }
            return new NativeRenderPlanLease(packet.Lease);
        }
        internal NativeRenderPlanLease Compile(in RenderPlanBindings bindings, out RenderPlanPacket packet)
        {
            Require(NativeMethods.urui_plan_compile(token, bindings, out packet));
            return new NativeRenderPlanLease(packet.Lease);
        }
        internal RenderPlanGeometryView Geometry(ulong geometry)
        {
            Require(NativeMethods.urui_plan_geometry(token, geometry, out var view));
            return view;
        }
        internal static void Require(int result)
        {
            if (result != 1)
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
            var owned = token;
            token = 0;
            Require(NativeMethods.urui_plan_release(owned));
        }
    }
}
