using UnityInput = UnityEngine.Input;
using System;
using System.Text;
using UnityEngine;
using URUI.Internal.Input;
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
                return;
            if (!active)
            {
                return;
            }
#if URUI_DEBUG
            using var hostScope = hostMarker.Auto();
            long inputTimer = phaseDiagnostics ? Now() : 0;
            using (inputMarker.Auto())
#endif
            {
                if (lastWidth != Screen.width || lastHeight != Screen.height)
                {
                    lastWidth = Screen.width;
                    lastHeight = Screen.height;
                    NativeMethods.urui_resize(UiWidth, UiHeight);
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
                SynchronizeUiClock();
                foreach (var document in OwnerSurface.DocumentsSnapshot())
                    if (document)
                        document.AdvanceSequences();
                using (nativeUpdatePhase.Auto())
                {
                    NativeMethods.urui_update();
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

            if (!DrawFrame(ref phase))
                return;
            if (!active || !isActiveAndEnabled)
                return;
            // URUI 在录制绘制时更新变换矩阵；此后才可同步屏幕空间值。
            OwnerSurface.NotifyLayoutUpdated();
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
                if (document && document.IsLoaded)
                    while (active && NativeMethods.urui_element_pop_click(document.NativeHandle, out _) > 0)
                    {
                    }
            OwnerSurface.RefreshDocuments(true);
            OwnerSurface.FinishNativeCall();
        }

        public string HitTestId(float logicalX, float logicalY)
        {
            if (!active)
                return null;
            var token = NativeMethods.urui_surface_pick(logicalX, logicalY, out var handle);
            var owner = URUI.Hosting.Surface.FindDocument(handle);
            for (var element = owner?.WrapElement(token); element != null; element = element.Parent)
                if (!string.IsNullOrEmpty(element.Name))
                    return element.Name;
            return null;
        }

        void EmitMousePointer(PointerPhase pointerPhase, Vector3 mouse)
        {
            if (PointerInput == null)
            {
                return;
            }

            float x = mouse.x * UiWidth / Screen.width;
            float y = UiHeight - 1 - mouse.y * UiHeight / Screen.height;
            PointerInput(new PointerInputEvent { phase = pointerPhase, pointerId = 0, x = x, y = y, hitId = HitTestId(x, y) });
        }
    }
}
