# URUI Engine

Maintained C++ core for the URUI Unity package. Build the native bridge through
`../CMakeLists.txt` or the scripts in `Native~`; contract tests live in `../tests`.

The engine retains the existing layout, controls, fonts and rendering subset,
with strict HTML/CSS authoring and explicit document ownership. It does not ship
standalone window backends, upstream samples, Lua, SVG, Lottie or the upstream
debugger. URUI native diagnostics remain in `../Debug`.

See `UPSTREAM.md` and `LICENSE.txt` for origin and licensing. Public authoring
capabilities are recorded in the package's `Documentation~/HtmlCssSupport.json`.
