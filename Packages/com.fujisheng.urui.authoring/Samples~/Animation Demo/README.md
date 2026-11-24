# Animation Demo

Import into `Assets/` via Package Manager (**URUI Authoring → Samples → Animation Demo → Import**). Double-click `Reward.html`, switch to the **Animation** workspace, scrub the timeline playhead or press Play. Select a track inside `0..duration`, enable **Record**, tweak opacity or transform in Inspector, and click **Save**. Requires only `com.fujisheng.urui` + `com.fujisheng.urui.authoring`.

At runtime, configure your `Document` text/image resources, wait for layout after load, parse `Reward.urui-sequence.json` with `SequenceDefinition.Parse`, and drive it with `new SequencePlayer(document.Body, definition)` (sequences do not auto-start silently).
