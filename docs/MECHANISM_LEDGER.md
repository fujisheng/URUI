# Conditional mechanisms and combinations

A regression outside a mechanism's useful domain does not invalidate its successful component. Separate gains cannot simply be added. This release selects the verified managed default plus explicit native alternatives.

| Mechanism | Useful condition | Limitation / interaction | Release state |
|---|---|---|---|
| Typed transform + retained node reference | Repeated updates to a long-lived identity; avoids text parsing and repeated ID lookup | Owner thread and node/document validity; reacquire on replacement; independent of renderer | Available; enabled in RML sample |
| Ordered batching + immutable resources | Adjacent draws share texture/clip and supported vertex representation | No translucent draw reordering; topology churn rebuilds batches | C# and NativeBatch retained |
| NativeDelta | Sparse changes within a valid batch lineage | Base-generation/range validation; fragmentation/layout changes use full copy; lower CPU copies did not yield additional measured CPU gain | Explicit NativeBatch option; no implied GPU traffic saving |
| NativeCore / NativeFrame | Reduce repeated managed preparation/boundary work | Numeric fallback and separate CPU/GPU lifetimes; NativeFrame static total-frame tail regressed | Explicit options, global default unchanged |
| Whole-world paint snapshot reuse | Settled static subtrees; static1024 CPU improved about 34% in its own matched study | Parent transform +44%, text +26%, all transforms +17%; inherited world-state invalidation causes recapture and repeated allocation | Preserved local candidate, excluded here; useful static component retained as design input |

The next hypothesis separates immutable local geometry/topology from mutable transform, clip, color and material, combining reusable static work with cheaper dynamic updates. RmlUi 6.3 currently maintains per-element world state, so inherited updates can fan out without local geometry changes. Any replacement must preserve public matrix-query timing, nested transforms, painter order, unknown decorator/filter/mask fallbacks and lifetime. It is not implemented here.

Selection may use generic change type, validity, capacity and measured cost; it must not detect benchmark IDs or board structures. Combinations require hypothesis-driven pairwise tests and ablation, then correctness, worst-path/tail/memory checks and affected atomic plus Match3 regressions. Do not enumerate every toggle combination or assume separate speedups compose.
