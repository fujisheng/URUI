# Borrowed texture checks

Run `python3 Tools/verify-borrowed-images.py` from the source repository in the URUI development workspace. It copies these ignored package fixtures into the existing Unity project, runs short Editor ownership/retirement checks and Gamma/Linear GPU sampling comparisons, then restores the original color-space field. No Player or performance matrix is run. `URUI.Tests.TextureBorrowing` is a friend test assembly, never a sample/runtime dependency.

Add `--batching` for the small three-layout RGBA-reference/borrowed comparison with batching off/on. It reports native texture identity, steady combined groups, recorded DrawMesh submissions and command-buffer bytes. Headless global GPU counters return unusable zero samples, so these are not advertised as driver GPU draw counts.
