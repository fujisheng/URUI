<div align="center">

<a href="docs/images/urui-icon.png"><img src="docs/images/urui-icon-128.png" width="88" height="88" alt="URUI 黑白连字图标"></a>

# URUI

**让你的 AI 用熟悉的 HTML 和 CSS，更快做出 Unity 游戏界面。**

用 **C#** 编写交互脚本，在 Git 中审阅布局、样式和动画时间线。URUI 的原生 C++ 引擎计算布局，Unity 通过自己的绘图接口直接绘制控件、文字和图片。

[为什么用 URUI](#为什么用-urui) · [运行样例](#潮汐信标) · [安装 Core](#安装-core) · [与 WebView 的区别](#urui-与-webview) · [开发文档](#开发文档) · [English](README.md)

</div>

## 为什么用 URUI

**打破手工搭 UI 的惯例，把时间留给打磨游戏手感。**

只想加宽一个登录按钮，在 uGUI 里也可能要先打开 Prefab，确认宽度是由按钮的 `RectTransform` 决定，还是由父级布局组控制。AI 即使读懂了 C# 点击回调，也得弄清这些组件的关系。一套菜单反复改几轮，光是手工查层级、调参数就很费时间。[uGUI 自动布局原理](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/UIAutoLayout.html)

URUI 把这项工作放进代码助手熟悉的 HTML 和 CSS。AI 可以按你的描述起草一个界面，也可以沿着《潮汐信标》登录按钮的样式类，找到 `.login .commit` 这条宽度规则。你在 Git 里看它改了哪几行，回到 Unity 点一下，就知道按钮是否好用。[登录页 HTML](Packages/com.fujisheng.urui/Samples~/Tideglass%20Beacon/Resources/URUI/Tideglass/Pages/Login.html) · [界面样式](Packages/com.fujisheng.urui/Samples~/Tideglass%20Beacon/Resources/URUI/Tideglass/Tideglass.css)

## 潮汐信标

![潮汐信标 Unity 实机演示，展示引潮站、随指针倾转的舰员立绘、配装、技能和战斗](docs/images/tideglass-beacon.gif)

从引潮站挑选随航舰员，买下模组，再带着新配装挑战信标。移动指针时，舰员卡片跟着倾转，舰桥按钮背后的玻璃会折射场景。《潮汐信标》完全由 AI 制作，涵盖界面设计、代码、原创美术、动效和音效。上方动图录自 Unity Play Mode。

**在 Unity 中体验。** 克隆或下载仓库，用 Unity **6000.3.22f1** 打开 [SampleProject](SampleProject)，选择 **Tools → URUI → Open Tideglass Beacon Sample**，然后按 **Play**。运行时按 **F1** 可以查看渲染统计。[样例玩法与操作说明](Packages/com.fujisheng.urui/Samples~/Tideglass%20Beacon/README_ZH.md)

**让你的 AI 改一次登录页。** 把下面这句话交给代码助手，让它编辑 `Assets/Samples` 下已导入的样例。重新进入 Play Mode，就能在第一屏试到改动。

> 把登录按钮从 255px 加宽到 300px，让鼠标悬停时的高光更亮一些。把样式改动限定在登录页，沿用现有的 C# 点击逻辑。

<details>
<summary>查看引潮站和英雄详情的高清画面</summary>

<p align="center">
  <a href="docs/images/tideglass-lobby.png"><img src="docs/images/tideglass-lobby.png" width="48%" alt="引潮站舰桥，两侧操作键排列在折射玻璃上"></a>
  <a href="docs/images/tideglass-hero.png"><img src="docs/images/tideglass-hero.png" width="48%" alt="英雄详情页中的角色立绘、技能选择与预演入口"></a>
</p>

</details>

## URUI 与 WebView

**谁绘制界面？** URUI 为控件、文字和图片生成几何数据，Unity 使用自己的材质，通过 `CommandBuffer.DrawMesh` 直接绘制这些网格。Built-in 把命令缓冲区挂到相机上。样例的 URP RenderGraph 通道则将相机当前的颜色目标传给 `Surface.SubmitRendering`，界面直接绘制到该目标。[Core 绘制实现](Packages/com.fujisheng.urui/Runtime/Internal/Hosting/RenderHost.NativePlan.cs) · [URP 渲染通道](SampleProject/Assets/Rendering/UruiUrpRendererFeature.cs)

WebView 方案先由浏览器引擎绘制网页，宿主再将原生网页视图叠放在游戏画面上，或把浏览器渲染好的网页纹理显示在场景中。[WebView 的两种显示方式](https://support.vuplex.com/articles/native-2d-mode/)

**交互脚本在哪里运行？** URUI 的控件直接在 Unity 中触发 C# 事件，处理函数可以读取背包、调用战斗逻辑或切换界面。WebView 的页面脚本运行在 JavaScript 环境中，调用宿主逻辑通常经过脚本桥接。[WebView 脚本接口](https://developer.android.com/develop/ui/views/layout/webapps/webview#BindingJavaScript)

在《潮汐信标》的结算页，C# 控制器从航行目标读取名称，再把 HTML 中的两个按钮绑定到星图和大厅。

```csharp
result.Q("result-sector").Text = destination.Name;
result.Q<Button>("result-chart").Clicked += () => Request(Page.Chart);
result.Q<Button>("finish").Clicked += () => Request(Page.Lobby);
```

[结算页 HTML](Packages/com.fujisheng.urui/Samples~/Tideglass%20Beacon/Resources/URUI/Tideglass/Pages/Result.html) · [C# 玩法控制器](Packages/com.fujisheng.urui/Samples~/Tideglass%20Beacon/Runtime/TideglassDemo.cs)

## 安装 Core

在 Unity Package Manager 中选择 **Install package from git URL**，粘贴下面的地址即可安装 **0.8.0-preview.1** 版 Core 运行时。

```text
https://github.com/fujisheng/URUI.git?path=/Packages/com.fujisheng.urui#v0.8.0-preview.1
```

Core 自带控件、渲染器和示例场景。需要视觉特效时可加装 Effect，需要可视化工作台时可加装 Authoring。发布页提供三个模块各自的 UPM 安装包。[版本说明与下载](https://github.com/fujisheng/URUI/releases/tag/v0.8.0-preview.1) · [安装指南](Packages/com.fujisheng.urui/Documentation~/INSTALL_ZH.md)

## 可以用它做什么

| 模块 | 游戏中的用途 |
| --- | --- |
| [**Core**](Packages/com.fujisheng.urui/README_ZH.md) | 用 HTML 和 CSS 排布战斗 HUD，用 C# 将控件接入玩法，或为长背包列表复用列表项。原生渲染器通过 Unity 中的 Surface 绘制界面文档。[Core API](Packages/com.fujisheng.urui/Documentation~/REFERENCE_ZH.md) |
| [**Effect**](Packages/com.fujisheng.urui.effect/README_ZH.md) | 让蓄能槽随波形起伏、面板溶解退场，或在技能命中时触发粒子。特效参数可以写进 CSS 并随时间线播放。[特效 CSS](Packages/com.fujisheng.urui.effect/Documentation~/CSS_ZH.md) |
| [**Authoring**](Packages/com.fujisheng.urui.authoring/README_ZH.md) | 检查层级和计算样式，拖动时间线预览动画，也可以让 AI 通过本机编辑器接口修改文档并截取 Unity 渲染结果。[Agent 接口](Packages/com.fujisheng.urui.authoring/Documentation~/agent-api_ZH.md) |

## 开发文档

| 要做的事 | 从这里开始 |
| --- | --- |
| 编写第一个界面 | [入门指南](docs/GETTING_STARTED_ZH.md) · [HTML 与 CSS 支持速查](docs/HTML_CSS_CHEATSHEET_ZH.md) · [AI 辅助编辑流程](docs/AI_WORKFLOW_ZH.md) |
| 接入渲染管线 | [引擎架构](docs/ARCHITECTURE_ZH.md) · [URP 与 Built-in 接入](docs/RENDER_PIPELINE_URP_ZH.md) · [合批与内存](docs/PERFORMANCE_AND_MEMORY_ZH.md) |
| 参与引擎开发 | [构建与测试](docs/DEVELOPMENT_ZH.md) · [源码与程序集](docs/CODE_ORGANIZATION_ZH.md) · [第三方许可](Packages/com.fujisheng.urui/Third%20Party%20Notices.md) |

Core 随包提供 macOS（Apple Silicon 与 Intel）、Windows x64、Android（ARM64 与 x86_64）、iOS ARM64 和 WebGL wasm32 的原生插件。样例使用 Unity **6000.3.22f1** 与 URP。[各平台验证范围](Packages/com.fujisheng.urui/Documentation~/LIMITATIONS_ZH.md) · [二进制校验清单](Packages/com.fujisheng.urui/Documentation~/PLUGINS.json)
