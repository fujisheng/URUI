# Input & Events

Raw input enters once per frame at **`document.Surface.Input`** (`URUI.Hosting.SurfaceInput`), routing across documents by presentation layer, modal blocking, and focus. Event classes live under `URUI.Events`; see [Public API Reference](REFERENCE.md) for all event signatures.

## Subscribing & Unsubscribing

`element.RegisterCallback<TEvent>(Action<TEvent>, TrickleDown = NoTrickleDown)` returns an `IDisposable` subscription; `UnregisterCallback<TEvent>` removes matching delegate + phase subscriptions. Never register duplicate handlers inside `Update`.

```csharp
using System;
using URUI.Elements;
using URUI.Events;
using UnityEngine;

public static class PointerExamples
{
    public static IDisposable Observe(Element panel)
    {
        return panel.RegisterCallback<PointerDownEvent>(evt =>
        {
            Debug.Log(evt.Target.Name);
            evt.CurrentTarget.CapturePointer(evt.PointerId);
        }, TrickleDown.TrickleDown);
    }
}
```

Dispose leases on page unbind/disable. Duplicate calls with the same delegate, event type, and phase return the same subscription handle. `Button.Clicked` and field `Submitted` use standard C# `+=` / `-=`; see [Controls](CONTROLS.md) for `RegisterValueChangedCallback`.

## Built-in Event Types

| Event | Key Data / Purpose |
| --- | --- |
| `ClickEvent` | Element activation |
| `PointerDown/Move/Up/Cancel/Enter/LeaveEvent` | `PointerId`, `Position`, `LocalPosition`, `Button`, `PressedButtons`, `IsTouch`, `Modifiers` |
| `PointerCaptureOutEvent` | Pointer capture released or canceled |
| `WheelEvent` | Mouse wheel delta |
| `KeyDownEvent` / `KeyUpEvent` | Unity `KeyCode`, `InputModifiers` |
| `TextInputEvent` | Committed Unicode text |
| `FocusEvent` / `BlurEvent` | Focus transitions (underlying focus commit may still be finalizing during notification) |
| `ScrollEvent` | Scroll offset updates |
| `ChangeEvent<T>` | `PreviousValue`, `NewValue` |
| `GeometryChangedEvent` | Layout box changed |
| `AttachToDocumentEvent` / `DetachFromDocumentEvent` | Hierarchy attachment changes (`Detach` != `Destroy`) |
| `AnimationEndEvent` / `TransitionEndEvent` | Completed CSS animation/transition metadata (see [Reference](REFERENCE.md)) |

`EventBase.Target` is the originating leaf node (which may have no `id`), `CurrentTarget` is the current handler target, and `PropagationPhase` identifies TrickleDown / AtTarget / BubbleUp. Custom C# event subclasses can be dispatched synchronously via `element.SendEvent`. Geometry and attach events target only the affected node; standard UI and change events bubble along true DOM ancestors including composite control wrappers.

## Propagation & Default Actions

Native dispatch enters managed code synchronously. Before the first managed handler fires, URUI snapshots the root-to-target path and subscriptions, executes TrickleDown -> AtTarget -> BubbleUp, and then executes permitted native default actions.

| Method | Effect |
| --- | --- |
| `StopPropagation()` | Stops subsequent targets after handlers on `CurrentTarget` finish |
| `StopImmediatePropagation()` | Stops remaining handlers on `CurrentTarget` immediately as well |
| `PreventDefault()` | Cancels interruptible native default actions without halting managed propagation |

`PreventDefault()` cannot undo committed `Value` writes or focus shifts. Disposing an existing subscription inside a callback skips it immediately in the current dispatch, whereas newly registered listeners start taking effect on the next dispatch. Destroying, detaching, or closing the target document stops further dispatch; moving within the same document continues along the snapshotted path. Managed exceptions log to the Unity Console and never unwind across the C ABI boundary.

## `SurfaceInput` API

`UseUnityInput` defaults to `true` and polls Unity's legacy `Input Manager (Old / Both)`. When integrating Unity's new Input System package, mobile soft keyboards, or automated drivers, set `UseUnityInput = false` and feed events explicitly—never double-feed the same input from both sources.

```text
SendPointer(PointerPhase phase, Vector2 position, int pointerId = 0,
    bool isTouch = false, int button = 0, int pressedButtons = 0,
    Vector2 delta = default, InputModifiers modifiers = None)
SendKey(KeyCode key, bool pressed, InputModifiers modifiers = None)
SendText(string text)
SendComposition(string text)
CommitComposition(string text)
CancelComposition()
Reset()
CancelPointers(Element owner)
```

| Parameter / Method | Constraints |
| --- | --- |
| `phase` | `Down`, `Move`, `Up`, `Cancel`, `Wheel` |
| `position` | Surface document coordinates anchored at **top-left** (do not pass Unity bottom-left screen coordinates unflipped) |
| `pointerId` | Non-negative stable pointer ID across touch contact lifetime |
| `isTouch` | Touch vs mouse source flag |
| `button` / `pressedButtons` | Button index `0..2` / currently depressed bitmask |
| `delta` | Move or scroll delta |
| `SendKey` | Key press/release transitions; send printable text separately via `SendText` |
| `Reset` | Clears held pointer/key/composition state (e.g. input source switch or focus loss) |
| `CancelPointers(owner)` | Cancels active pointer gestures owned by a node or subtree |

All `SurfaceInput` calls require a loaded document on the Unity main thread and must not re-enter synchronously from inside active input, document lifecycle, or effect callbacks.

```csharp
using URUI.Events;
using URUI.Hosting;
using UnityEngine;

public static class InputExamples
{
    public static void Tap(Surface surface, Vector2 position, int pointerId)
    {
        var input = surface.Input;
        input.UseUnityInput = false;
        input.SendPointer(PointerPhase.Down, position, pointerId, true, pressedButtons: 1);
        input.SendPointer(PointerPhase.Up, position, pointerId, true);
    }
}
```

`CapturePointer` / `ReleasePointer` / `HasPointerCapture` scope per `pointerId`; once captured, out-of-bounds moves and releases route to the capturing node until released, disabled, blurred, or canceled. Dragging a `ScrollView` past threshold cancels accidental click activation, and recycled `ListView` rows never transfer old pointer presses to newly rebound items.

## Focus, Keyboard & IME Composition

`Tab` / `Shift+Tab` cycle focus across `Focusable`, visible, enabled elements sorted by positive `TabIndex` followed by DOM order. Built-in shortcuts map macOS `Command` editing bindings using `GUIUtility.systemCopyBuffer`.

`SendText` inserts committed text. `SendComposition` previews inline IME strings, `CommitComposition` commits once, and `CancelComposition` restores previous content and selection. Programmatic value writes or focus switches end active previews cleanly. `Selection` / `SelectRange` count Unicode characters (not UTF-8 bytes or UTF-16 code units). See [Limitations & Support Matrix](LIMITATIONS.md) for OS IME and mobile soft-keyboard validation coverage.

## Document Mutations Inside Callbacks

`Load` and `Unload` triggered inside input or lifecycle callbacks are deferred until the enclosing native call exits. When synchronous element creation, template cloning, or navigation is needed directly inside an event callback, use `document.RunAfterEvent(action)` or `surface.RunAfterEvent(action)`; it runs immediately after callback dispatch completes (before the outer input method returns, or synchronously when idle). Always re-verify `element.IsValid` inside deferred delegates.

`PreventDefault()` and propagation stops still execute synchronously inside the event callback itself. Paint/effect `Prepare` and `Record` callbacks must never mutate DOM or schedule page loads. Re-query and re-subscribe after page reloads, and unsubscribe owned delegates in `OnDisable`.
