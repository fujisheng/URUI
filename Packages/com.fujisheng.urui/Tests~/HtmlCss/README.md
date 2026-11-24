# HTML/CSS Parity Validation

Test fixtures, shared base CSS, and bundled font bytes are loaded verbatim by both desktop Chromium/browser probes (CDP) and Unity URUI without injector patches (`forms.html` uses `/urui/preview.js` and is marked as a control-preview fallback).

Generate the native authoring closure from repository root and run the static test server + browser probe:

```sh
python3 Tools/HtmlCss/prepare_comparison.py --root "$PWD" --native-library <candidate-plugin> --output <inputs-dir>
python3 Tools/HtmlCss/serve.py --root "$PWD" --port 8765
node Tools/HtmlCss/capture_browser.mjs <inputs-dir>/inputs.json http://127.0.0.1:8765 <browser-out>
```

Requires Node 24 WebSocket support, an installed Chromium-family browser (`URUI_TEST_BROWSER` overrides executable path), and `Tools/HtmlCss/requirements-dev.txt` for image comparison.

For Unity Editor comparison on macOS GPU (do **not** pass `-nographics`), import `HtmlCssComparisonValidation.cs` plus native validation sidecar `URUIValidation` into a disposable test project, export `URUI_HTML_CSS_INPUTS` / `URUI_HTML_CSS_OUTPUT`, run `URUI.HtmlCssValidation.HtmlCssComparisonValidation.CaptureAll`, then compare:

```sh
python3 Tools/HtmlCss/compare.py --browser <case>/browser.json --unity <case>/unity.json --output <case>
```

Thresholds enforce **0.5 CSS px** geometry accuracy, **1 px** text line-box baseline accuracy (FreeType vs Canvas `fontBoundingBoxAscent`), and **2/255** sRGB flat color region parity (antialiasing edges excluded from flat color gates).

For macOS ARM64 IL2CPP Player parity validation without bundling native sidecar test libraries, stage `HtmlCssPlayerValidation.cs` and `Editor/HtmlCssPlayerBuild.cs` into an isolated project:

```sh
python3 Tools/HtmlCss/prepare_player.py --inputs <inputs-dir>/inputs.json --browser-root <browser-out> --native-library <prod-plugin> --project <isolated-project>
URUI_HTML_CSS_PLAYER_OUTPUT=<abs>/HtmlCssPlayerValidation.app <Unity> -batchmode -buildTarget StandaloneOSX -projectPath <isolated-project> -executeMethod URUI.Tests.HtmlCssPlayerBuild.BuildMacArm64 -logFile <build-log>
<app>/Contents/MacOS/HtmlCssPlayerValidation --urui-evidence <abs-evidence-dir> -logFile <run-log>
```

Player runs verify node metrics, font sizing, opacity, per-channel sRGB captures, duplicate-ID isolation, single-dispatch pointer routing, modal blocking, candidate rollback, single-page unload, and Surface recovery (`html-css-player.json` + process exit code `0`).
