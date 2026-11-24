# URUI Engine upstream provenance

This engine is derived from RmlUi 6.3, https://github.com/mikke89/RmlUi.
The original license and copyright remain in LICENSE.txt and existing source headers.
URUI owns this maintained fork and its naming, strict HTML/CSS subset and Unity bridge.

The previous vendored tree was Native~/ThirdParty/RmlUi. Original license and
upstream provenance headers were preserved across the maintained fork.
The fork preserves the original license; renaming is not a claim of original authorship.

External Gumbo and FreeType retain their names and their respective licenses.

## Distribution changes

The Unity fork removes the unlinked standalone backends/samples, Lua bindings,
SVG/Lottie plugins and upstream debugger, including their build options and
public plugin headers. They were not production bridge dependencies; their
removal is a source distribution reduction, not a measured runtime gain.
Original core/container licenses and source copyright headers remain intact.
