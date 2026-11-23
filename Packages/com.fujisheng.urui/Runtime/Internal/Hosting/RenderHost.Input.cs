using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Internal.Interop;

namespace URUI.Internal.Hosting
{
    // 采集 Unity 输入并推进文档更新，再排空原生点击队列；保留输入与布局的调用顺序。
    internal sealed partial class RenderHost
    {
        void Update()
        {
            RecoverAbortedBuiltinCamera();
            if (OwnerSurface && OwnerSurface.ManualDrive)
            {
                return;
            }
            if (!active)
            {
                return;
            }
            CollectPendingRetirements();
#if URUI_DEBUG
            using var hostScope = hostMarker.Auto();
            long inputTimer = phaseDiagnostics ? Now() : 0;
            using (inputMarker.Auto())
#endif
            {
                var width = UIWidth;
                var height = UIHeight;
                if (lastWidth != width || lastHeight != height)
                {
                    lastWidth = width;
                    lastHeight = height;
                    NativeMethods.urui_surface_resize(SurfaceHandle, width, height);
                }
                OwnerSurface.Input.PollUnity();
            }
#if URUI_DEBUG
            long timer = (measureBridge || phaseDiagnostics) ? Now() : 0;
            long alloc = phaseDiagnostics ? ThreadAllocationBytes() : -1;
#endif
            var phase = new PhaseFrame();
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.inputMs = Millis(timer - inputTimer);
            }

            long step = phaseDiagnostics ? Now() : 0;
#endif
            if (!active || !isActiveAndEnabled)
            {
                return;
            }
#if URUI_DEBUG
            using (updateMarker.Auto())
#endif
            {
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
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.contextUpdateMs = Millis(Now() - step);
                phase.nativeCalls++;
                step = Now();
            }

            using (eventMarker.Auto())
#endif
            {
                DrainEvents();
            }
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.eventsMs = Millis(Now() - step);
                step = Now();
            }
#endif
            if (!active || !isActiveAndEnabled)
            {
                return;
            }

            if (GraphicsSettings.currentRenderPipeline != null)
            {
                // SRP defers CommandBuffer recording until SubmitRendering so the frame is
                // recorded exactly once for the actual SRP render target and viewport.
                geometryRecordingPending = true;
                OwnerSurface.BeginDispatch();
                try
                {
                    CommitAnimationCompletionClasses();
                    if (active && isActiveAndEnabled)
                    {
                        NativeMethods.urui_surface_synchronize_geometry(SurfaceHandle);
                    }
                }
                finally
                {
                    OwnerSurface.EndDispatch();
                    OwnerSurface.FinishNativeCall();
                }
            }
            else if (!DrawFrame(ref phase))
            {
                return;
            }
            if (!active || !isActiveAndEnabled)
            {
                return;
            }
            // URUI 在录制绘制或几何同步时更新变换矩阵；此后才可同步屏幕空间值。
            OwnerSurface.NotifyLayoutUpdated();
            RecordSynchronizedFrameState();
#if URUI_DEBUG
            if (phaseDiagnostics)
            {
                phase.hostTotalMs = Millis(Now() - timer);
                phase.managedAllocBytes = alloc < 0 ? -1 : ThreadAllocationBytes() - alloc;
                phase.setTextMs = textMilliseconds;
                phase.setTextCalls = textCalls;
                phase.nativeCalls += textCalls;
                LastPhase = phase;
                textMilliseconds = 0;
                textCalls = 0;
            }

            if (measureBridge)
            {
                UpdateMilliseconds = Millis(Now() - timer);
            }
#endif
        }

        void DrainEvents()
        {
            foreach (var document in OwnerSurface.DocumentsSnapshot())
            {
                if (document && document.IsLoaded)
                {
                    while (active && NativeMethods.urui_element_pop_click(SurfaceHandle, document.NativeHandle, out _) > 0)
                    {
                    }
                }
            }
            OwnerSurface.RefreshDocuments(true);
            OwnerSurface.FinishNativeCall();
        }
    }
}
