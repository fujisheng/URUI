# Borrowed texture checks

Run `python3 Tools/verify-borrowed-images.py` from the source repository in the URUI development workspace. It copies these ignored package fixtures into the existing Unity project, runs short Editor ownership/retirement checks and Gamma/Linear GPU sampling comparisons, then restores the original color-space field. No Player or performance matrix is run. `URUI.Tests.TextureBorrowing` is a friend test assembly, never a sample/runtime dependency.

Add `--batching` for the three current borrowed-image layouts with batching off/on. The obsolete global RGBA-reference host is removed; native owned-RGBA protocol coverage lives in the standalone contract tests. It reports native texture identity, steady combined groups, recorded DrawMesh submissions and command-buffer bytes. Headless global GPU counters return unusable zero samples, so these are not advertised as driver GPU draw counts.
