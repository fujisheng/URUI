using System;
using UnityEngine;
using URUI.Internal.Interop;
using URUI.Internal.Rendering;

namespace URUI.Internal.Hosting
{
    internal sealed partial class RenderHost
    {
        static readonly Unity.Profiling.ProfilerMarker geometrySynchronizationMarker = new("URUI.SynchronizeGeometry");
        bool geometryRecordingPending;
        int synchronizedFrame = -1;
        NativeMethods.ElementRefreshStamp synchronizedStamp;
        static readonly Unity.Profiling.ProfilerMarker geometryProvidersMarker = new("URUI.Geometry.PrepareProviders");

        internal void PrepareGeometryProviders()
        {
            using var sample = geometryProvidersMarker.Auto();
            if (!active || !isActiveAndEnabled)
            {
                return;
            }
            // 几何回调只准备只读输入；GPU 计划、材质效果和命令由真实录制阶段统一处理。
            geometryRecordingPending = true;
            OwnerSurface.BeginDispatch();
            try
            {
                foreach (var owner in OwnerSurface.DocumentsSnapshot())
                {
                    if (owner && owner.IsLoaded)
                    {
                        owner.UpdateEffectParameters();
                    }
                }
                externalGeometryInputs ??= new ExternalGeometryInputs();
                using var window = externalGeometryInputs.Begin();
                using var input = NativeRenderPlanLease.Begin(SurfaceHandle, out var packet);
                planInputLease = input;
                try
                {
                    ReadNativeFrame(packet);
                    foreach (var owner in OwnerSurface.DocumentsSnapshot())
                    {
                        if (owner && owner.IsLoaded)
                        {
                            owner.FreezeEffectParameters();
                        }
                    }
                    FreezeEffectClocks();
                    PrepareGeometryProviders(packet);
                }
                finally
                {
                    planInputLease = default;
                    nativeInputCommands = IntPtr.Zero;
                    nativeInputMatrices = IntPtr.Zero;
                    nativeInputUniforms = IntPtr.Zero;
                    frozenEffectClocks.Clear();
                }
            }
            finally
            {
                OwnerSurface.EndDispatch();
                OwnerSurface.FinishNativeCall();
            }
        }

        internal void SynchronizeGeometry()
        {
            using var profile = geometrySynchronizationMarker.Auto();
            // 这是录制失效状态，不是优化选项；任何真实提交前都必须准备最新的命令。
            geometryRecordingPending = true;
            UpdateLayoutState();
            if (!active || !isActiveAndEnabled)
            {
                return;
            }
            if (NativeMethods.urui_surface_synchronize_geometry(SurfaceHandle) != NativeStatus.Ok)
            {
                throw new InvalidOperationException("URUI 几何同步失败：" + NativeMethods.LastError(SurfaceHandle));
            }
            // 几何事件观察者必须读取到当前变换，不能在新版本下缓存旧世界边界。
            OwnerSurface.RefreshDocuments(true);
            if (!active || !isActiveAndEnabled)
            {
                return;
            }
            // 回调可设置滚动偏移等空间状态；返回前完成它们产生的变换失效。
            if (NativeMethods.urui_surface_synchronize_geometry(SurfaceHandle) != NativeStatus.Ok)
            {
                throw new InvalidOperationException("URUI 几何同步失败：" + NativeMethods.LastError(SurfaceHandle));
            }
        }

        void PrepareGeometryRecording()
        {
            if (!geometryRecordingPending || !active || !isActiveAndEnabled)
            {
                return;
            }
            if (OwnerSurface.IsBusy || OwnerSurface.IsRefreshingDocuments)
            {
                throw new InvalidOperationException("同步准备绘制必须在文档回调或元素更新之外执行。");
            }
            SynchronizeLayoutState();
            if (!active || !isActiveAndEnabled)
            {
                return;
            }
            var phase = new PhaseFrame();
            DrawFrame(ref phase);
        }

        // 与正常帧共用记录/资源所有权；初始化读取不能绕开GPU资源的安全退役。
        internal void SynchronizeLayout()
        {
            using var profile = synchronizeMarker.Auto();
            SynchronizeLayoutState();
            if (!active || !isActiveAndEnabled)
            {
                return;
            }
            var phase = new PhaseFrame();
            DrawFrame(ref phase);
        }

        void RecordSynchronizedFrameState()
        {
            if (active && isActiveAndEnabled && SurfaceHandle != 0 &&
                NativeMethods.urui_element_refresh_stamp(SurfaceHandle, out var stamp) == NativeStatus.Ok && stamp.Reusable)
            {
                synchronizedFrame = Time.frameCount;
                synchronizedStamp = stamp;
            }
            else
            {
                synchronizedFrame = -1;
                synchronizedStamp = default;
            }
        }

        void SynchronizeLayoutStateForSubmission()
        {
            var width = UIWidth;
            var height = UIHeight;
            var resized = lastWidth != width || lastHeight != height;
            if (resized)
            {
                lastWidth = width;
                lastHeight = height;
                NativeMethods.urui_surface_resize(SurfaceHandle, width, height);
            }
            var timeChanged = SynchronizeUIClock();
            foreach (var document in OwnerSurface.DocumentsSnapshot())
            {
                if (document)
                {
                    document.AdvanceSequences();
                }
            }
            if (!resized && !timeChanged && synchronizedFrame == Time.frameCount &&
                NativeMethods.urui_element_refresh_stamp(SurfaceHandle, out var currentStamp) == NativeStatus.Ok &&
                synchronizedStamp.Matches(currentStamp))
            {
                return;
            }
            using (nativeUpdatePhase.Auto())
            {
                NativeMethods.urui_surface_update(SurfaceHandle);
            }
            OwnerSurface.RefreshDocuments(true);
            RecordSynchronizedFrameState();
        }

        void SynchronizeLayoutState()
        {
            UpdateLayoutState();
            // 与正常帧的 DrainEvents 相同，绘制回调读取样式前先发布布局版本。
            OwnerSurface.RefreshDocuments(true);
            RecordSynchronizedFrameState();
        }

        void UpdateLayoutState()
        {
            var width = UIWidth;
            var height = UIHeight;
            if (lastWidth != width || lastHeight != height)
            {
                lastWidth = width;
                lastHeight = height;
                NativeMethods.urui_surface_resize(SurfaceHandle, width, height);
            }
            SynchronizeUIClock();
            foreach (var document in OwnerSurface.DocumentsSnapshot())
            {
                if (document)
                {
                    document.AdvanceSequences();
                }
            }
            using (nativeUpdatePhase.Auto())
            {
                NativeMethods.urui_surface_update(SurfaceHandle);
            }
        }
    }
}
