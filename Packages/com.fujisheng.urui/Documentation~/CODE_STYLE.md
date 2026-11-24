# Code Style

URUI keeps application-facing APIs deliberately compact: HTML/CSS owns UI structure and visual appearance while C# drives state explicitly through `Document`, `Element`, and typed controls.

## C# and C++

- Use 4-space indentation and Allman braces; always use braces on control-flow bodies. Put separate statements, fields, locals, enum members, and object-initializer entries on separate lines.
- Use `PascalCase` for types/methods/properties and `camelCase` for fields/locals. Keep acronyms `URUI` and `UI` uppercase in identifiers.
- Place explanatory comments above the relevant declaration/block focusing on ownership, constraints, and non-obvious rationale. Split XML doc tags cleanly across lines when verbose.
- Omit explicit `private` modifiers where already implicit by default, while preserving explicit `private set` / `private protected` / C++ access labels.
- Keep native subsystems cleanly inside namespaces such as `URUI::RenderPlan` with named constants for protocol IDs and flags; keep C ABI signatures strict across headers and binaries.

## C# Conventions

- Place attributes on dedicated lines; expand multi-statement getters/setters cleanly.
- Keep public API surfaces narrow with explicit XML docs describing lifetime, main-thread constraints, and invalidation rules.
- Expose typed style properties cleanly without fabricating fake computed getters on `Style` (inline writes go to `Element.Style`, resolved pixels/values read from `ResolvedStyle`, `DocumentStyles.GetComputed`, or `Element.Layout`).
- Store event/style subscriptions returned as `IDisposable` and dispose them deterministically in `OnDisable` / `Unbind`; always re-query nodes after reloads.
- Split large `partial` classes coherently by lifecycle, resource management, recording, and submission without reordering shared field initialization.
- Application gameplay code should depend exclusively on public `URUI.Runtime` APIs.

## HTML/CSS Conventions

- Assign concise, unique, stable `id` attributes to elements queried from C#.
- Express layout rules explicitly in CSS (`display`, `flex-direction`, positioning, spacing).
- Use `Element.Text` for plain text updates and `SetInnerHtml` only for deliberate structural replacements.
- Use semantically clear class names and tokens so diffs stay transparent for both humans and coding agents.
- Declare true `@font-face` families matching bundled font bytes and consistent `ImageAsset.Source` paths.
