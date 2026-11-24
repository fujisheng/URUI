# HTML/CSS 同源验证

夹具、基础 CSS 和原始字体都由浏览器与 URUI 直接加载。浏览器探针通过 CDP 在加载后读取文档，作者文件不需要脚本或补丁 CSS。`forms.html` 使用现有 `Web~/preview.js`，报告明确标为控件预览回退。

从仓库根生成原生作者解析闭包，再启动只读服务器：

```sh
python3 URUI/Tools/HtmlCss/prepare_comparison.py --root "$PWD" --native-library <候选插件> --output <输入目录>
python3 URUI/Tools/HtmlCss/serve.py --root "$PWD" --port 8765
node URUI/Tools/HtmlCss/capture_browser.mjs <输入目录>/inputs.json http://127.0.0.1:8765 <结果目录>
```

工具依赖现有 Node 24 的 WebSocket 和已安装浏览器；`URUI_TEST_BROWSER` 可指定浏览器可执行文件。Python 图片比较依赖 `Tools/HtmlCss/requirements-dev.txt`。实际浏览器版本写入每份快照。

Unity 入口是本目录的 `HtmlCssComparisonValidation.cs`。只将它和测试程序集、原生测试构建产生的 `URUIValidation` 旁路模块导入隔离测试项目；旁路模块不进入 Runtime 插件目录或生产包。设置 `URUI_HTML_CSS_INPUTS` 和 `URUI_HTML_CSS_OUTPUT` 后执行 PlayMode 测试 `URUI.HtmlCssValidation.HtmlCssComparisonValidation.CaptureAll`。当前入口限定 macOS Editor，核对实际插件 SHA，并在真实 GPU 上采集，不能使用 `-nographics`。

```sh
python3 URUI/Tools/HtmlCss/compare.py --browser <单个结果目录>/browser.json --unity <单个结果目录>/unity.json --output <单个结果目录>
```

每个视口固定 DPR 1、手动时间 0。快照包含输入及字体 SHA、字体就绪、关键节点树、样式、几何、实际文字行、基线、截图 SHA 和指定颜色区域。Unity 使用 sRGB 输出纹理保存 PNG，避免把线性通道直接当成屏幕颜色。

文字几何采用 CSS `line-height` 行框，不能把 FreeType 光栅字体包围盒和浏览器 DOM Range 包围盒直接比较。Unity 基线来自实际布局；浏览器通过 DOM Range 与 Canvas `fontBoundingBoxAscent` 测量，方法保留在报告中。文字抗锯齿未纳入平坦颜色门禁。截图存在不代表全部像素已比较：仅 `cases.json` 指定区域有数值门禁，其余区域需要专项采集或人工检查。

门禁保持几何 0.5 CSS px、基线 1 px、颜色通道 2/255；差异为 `failed`，缺输入、字体、关键节点、文本行或截图等证据为 `unverified`。这组标准夹具通过也不能代替完整游戏路线、扩展效果、Sample.Import、Player 或其他平台验收。

macOS ARM64 IL2CPP 验证使用 `HtmlCssPlayerValidation.cs`，将它复制到已经停止的隔离项目 `Assets` 下，另将 `Editor/HtmlCssPlayerBuild.cs` 复制到 `Assets/Editor`。原样作者资源与已通过的浏览器结果由工具打包，不需要把原生验证旁路模块放入 Player：

```sh
python3 URUI/Tools/HtmlCss/prepare_player.py --inputs <输入目录>/inputs.json --browser-root <浏览器结果目录> --native-library <生产配置候选插件> --project <隔离项目>
URUI_HTML_CSS_PLAYER_OUTPUT=<绝对路径>/HtmlCssPlayerValidation.app <Unity> -batchmode -buildTarget StandaloneOSX -projectPath <隔离项目> -executeMethod URUI.Tests.HtmlCssPlayerBuild.BuildMacArm64 -logFile <构建日志>
<应用>/Contents/MacOS/HtmlCssPlayerValidation --urui-evidence <绝对结果目录> -logFile <运行日志>
```

隔离项目必须安装同一候选插件。打包前验证作者字节、浏览器输入/字体摘要、视口与截图摘要；构建时再次核验插件摘要。Player 在真实 GPU 的 sRGB RenderTexture 上逐组检查关键节点、字体大小、opacity 和指定区域的每个颜色通道，保存 PNG；另查同 ID 文档、原生单次指针分发、模态焦点、错误候选回滚、单页关闭、Surface 恢复和重开。报告 `html-css-player.json` 与进程退出码都必须通过。此入口不采集文字行基线，不代替实际 URP 屏幕输出或其他设备验证。
