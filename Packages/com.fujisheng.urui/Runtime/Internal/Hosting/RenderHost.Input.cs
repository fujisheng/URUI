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
#if UNITY_ANDROID && !UNITY_EDITOR
                for (int i = 0; i < UnityInput.touchCount; i++)
                {
                    var touch = UnityInput.GetTouch(i);
                    int touchPhase = touch.phase == TouchPhase.Began ? 0 : touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary ? 1 : touch.phase == TouchPhase.Ended ? 2 : 3;
                    float x = touch.position.x * UiWidth / Screen.width;
                    float y = UiHeight - 1 - touch.position.y * UiHeight / Screen.height;
                    if (NativeMethods.urui_touch_event(touchPhase, (ulong)(uint)touch.fingerId, x, y) == 1)
                    {
                        PointerInput?.Invoke(new PointerInputEvent { phase = (PointerPhase)touchPhase, pointerId = touch.fingerId, x = x, y = y, hitId = HitTestId(x, y) });
                    }
                }
#else
                var mouse = UnityInput.mousePosition;
                NativeMethods.urui_mouse_move((int)(mouse.x * UiWidth / Screen.width), UiHeight - 1 - (int)(mouse.y * UiHeight / Screen.height));
                for (int b = 0; b < 3; b++)
                {
                    if (UnityInput.GetMouseButtonDown(b))
                    {
                        NativeMethods.urui_mouse_button(b, 1);
                        if (b == 0)
                        {
                            EmitMousePointer(PointerPhase.Down, mouse);
                        }
                    }

                    if (UnityInput.GetMouseButtonUp(b))
                    {
                        NativeMethods.urui_mouse_button(b, 0);
                        if (b == 0)
                        {
                            EmitMousePointer(PointerPhase.Up, mouse);
                        }
                    }
                }

                if (UnityInput.GetMouseButton(0) && !UnityInput.GetMouseButtonDown(0))
                {
                    EmitMousePointer(PointerPhase.Move, mouse);
                }

                if (Mathf.Abs(UnityInput.mouseScrollDelta.y) > 0.001f)
                {
                    NativeMethods.urui_mouse_wheel(-UnityInput.mouseScrollDelta.y);
                }
#endif
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
                NativeMethods.urui_update();
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

            DrawFrame(ref phase);
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
            while (active)
            {
                int result = NativeMethods.urui_element_pop_click(out var target);
                if (result == 0)
                {
                    break;
                }

                if (result < 0)
                {
                    throw new InvalidOperationException("URUI 点击身份队列无效。");
                }

                Clicked?.Invoke(target);
            }
        }

        public string HitTestId(float logicalX, float logicalY)
        {
            if (!active)
            {
                return null;
            }

            var bytes = new byte[512];
            int n = NativeMethods.urui_hit_test_id(logicalX, logicalY, bytes, bytes.Length);
            if (n < 0)
            {
                int required = -n;
                if (required > 1024 * 1024)
                {
                    throw new Exception("URUI hit ID exceeds 1 MiB");
                }

                bytes = new byte[required];
                n = NativeMethods.urui_hit_test_id(logicalX, logicalY, bytes, bytes.Length);
            }

            return n > 0 ? Encoding.UTF8.GetString(bytes, 0, n) : null;
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
