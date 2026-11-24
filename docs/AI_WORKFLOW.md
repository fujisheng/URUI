# AI-Assisted UI Workflow

**English** | [中文](AI_WORKFLOW_ZH.md)

URUI was built from the ground up so coding agents and human designers collaborate in **readable text artifacts** with tight feedback loops.

## Why Text-First UI Accelerates Agents

| Traditional Game UI | URUI Workflow |
| --- | --- |
| Opaque YAML scene/prefab diffs (`.prefab`, `.unity` GUID noise) | Clean HTML, CSS, C#, and `.urui-sequence.json` text diffs |
| Runtime inspection requires guessing nested `RectTransform` trees | Exact DOM hierarchy, CSS computed origin, and pixel `WorldBound` |
| Theme or copy swaps recreate Prefabs and break merge requests | CSS `@property` tokens + text markup edits merge cleanly |

## Closed-Loop Authoring Pipeline

```text
  [1] Author HTML / CSS / .urui-sequence.json
               |
               v
  [2] Validate in Browser OR Authoring Local HTTP API v1
      (Library/URUI/Authoring/endpoint.json -> preview.capture)
               |
               v
  [3] Inspect Screenshot & Bounds -> Refine CSS/DOM with Undo support
               |
               v
  [4] Precompile Static Pages -> CompiledDocumentAsset (Zero Runtime Parse)
```

### Key Workflows Available Out of the Box

1. **Direct Source Editing + Browser Pre-check**: Open raw HTML/CSS pages cleanly in standard desktop browsers to verify flexbox responsiveness before entering Unity Play Mode.
2. **Localhost Agent Protocol (`com.fujisheng.urui.authoring`)**: When opted in from **Tools → URUI**, agents read `Library/URUI/Authoring/endpoint.json` and send revision-checked commands over localhost HTTP:
   - `document.open`, `hierarchy.children`, `hierarchy.find`
   - `properties.read`, `properties.set`, `style.rule`
   - `structure.insert`, `structure.move`, `structure.remove`
   - `animation.open`, `animation.preset`, `animation.key`, `animation.sample`
   - `preview.render`, `preview.capture` (real rendered screenshot paths for multimodal visual verification)
   - `document.save` (explicit atomic commit)
   See [Agent HTTP API Specification](../Packages/com.fujisheng.urui.authoring/Documentation~/agent-api.md).
3. **Recommended Rules for AI Agents Editing URUI**:
   - Keep layout rules in CSS (`display: flex`, `gap`, classes), never ad-hoc inline C# layout loops.
   - Use semantic, stable `id` attributes for elements bound in C#.
   - Never cache DOM element handles or node keys across page reloads.
   - Always verify visual rendering in-engine (`preview.capture` or real GPU capture) before claiming layout completion.
