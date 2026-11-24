# URUI 新手入门指南

[English](GETTING_STARTED.md) | **中文**

目标 Unity 版本：**`6000.3.22f1`**。

## 1. 一分钟运行 SOLSTICE《日冕旅团》示范工程（`SampleProject/`）

仓库根目录自带独立、开箱即用的 URP 示范工程：

1. 使用 **Unity `6000.3.22f1`** 打开 `SampleProject/` 目录。
2. 打开场景 `Assets/Solstice/Scenes/Solstice.unity`。
3. 点击 **Play**，体验 6 个 RPG 游戏模块（圣殿大厅、首领战与环形冷却技能、英灵灵装材质、星盘祈愿序列动画、万级虚拟军械库、主题设置与实时渲染监控 HUD）与 3 套纯 CSS 主题。

## 2. 将 URUI 安装到你自己的 Unity 工程

打开 **Window → Package Manager → + → Add package from disk...** 按顺序引入：

1. 必选核心运行时包：`Packages/com.fujisheng.urui/package.json`（`0.8.0-preview.1`）
2. 可选特效与粒子包：`Packages/com.fujisheng.urui.effect/package.json`（`0.5.0-preview.1`）
3. 可选 Editor 可视化与 Agent 接口包：`Packages/com.fujisheng.urui.authoring/package.json`（`0.1.0-preview.1`）

引入后可直接运行包内主推示例：
- 点击顶部菜单 **Tools → URUI → Open Solstice Sample**，Unity 将自动导入 `Samples~/Solstice` 并打开 `Solstice.unity`；也可在 Package Manager 中按需导入 `Hello URUI`、`Element Playground` 或 `Runtime Controls`。

## 3. 30 秒构建最小界面

1. 确保场景中有一个启用的主相机（带有 `MainCamera` 标签）。
2. 如项目使用 **URP**，请挂载调用 `surface.SubmitRendering(cmd, target, viewport, info)` 的 `ScriptableRendererFeature`（详见 [URP 集成指南](RENDER_PIPELINE_URP_ZH.md)）；**Built-in RP** 下无需额外配置。
3. 挂载 `Surface` 与 `Document`，加载标准 HTML/CSS（内置样式需按 `TextResource` 注册，见 [Core 安装说明](../Packages/com.fujisheng.urui/Documentation~/INSTALL_ZH.md)）：

```csharp
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Controls;
using URUI.Events;

public sealed class QuickGreeting : MonoBehaviour
{
    void Start()
    {
        var surface = gameObject.AddComponent<Surface>();
        var doc = gameObject.AddComponent<Document>();
        doc.Surface = surface;
        doc.LoadHtml(@"<!doctype html>
<html>
<head>
  <style>
    body { margin: 32px; background: #f6f1e7; }
    #btn { width: 180px; height: 40px; background: #ef5a36; color: white; border-radius: 999px; }
  </style>
</head>
<body>
  <h2 id='title'>Hello URUI!</h2>
  <button id='btn' type='button'>点我交互</button>
</body>
</html>", "/ui/greeting.html");

        doc.Q<Button>("btn")?.RegisterCallback<ClickEvent>(_ =>
        {
            doc.Q("title").Text = "响应成功！";
        });
    }
}
```

## 4. 下一步阅读

- 深入理解核心架构与 `ABI13`：`Surface` 与原生渲染计划 -> [架构说明](ARCHITECTURE_ZH.md)。
- 在 URP RenderGraph 中正确叠加透明 UI -> [URP 渲染管线集成](RENDER_PIPELINE_URP_ZH.md)。
- 查阅 URUI 支持的 HTML 与 CSS 属性子集 -> [HTML/CSS 支持速查表](HTML_CSS_CHEATSHEET_ZH.md)。
