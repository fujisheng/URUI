# Effect 快速开始

当前包 `0.4.1-preview.1`，要求 Core `0.7.1-preview.1`。先按 [Core 安装](../../com.fujisheng.urui/Documentation~/INSTALL_ZH.md)准备 Unity、MainCamera、输入和配套 native 插件。

## 安装与程序集

在 Package Manager → Add package from disk 选择本包 package.json，或把完整包目录作为嵌入包安装。Core 同时必须能够解析。依赖为 com.fujisheng.urui 与 com.unity.modules.particlesystem。有 asmdef 的业务代码引用 URUI.Runtime 和 URUI.Effect；没有 FUI 依赖，也没有单独效果 native 插件。

## 准备作者资源

创建 `EffectPanel.html`：

```html
<!doctype html>
<html><head>
<link rel="stylesheet" href="/urui/base.css">
<link rel="stylesheet" href="/urui/controls.css">
<link rel="stylesheet" href="/urui-effect/Effect.css">
<link rel="stylesheet" href="panel.css">
</head><body>
<img id="reward" src="icon.png" class="reward">
</body></html>
```

创建 `panel.css`：

```css
body { margin: 24px; }
.reward {
  width: 160px;
  height: 160px;
  --urui-effect: ui;
  --urui-fx-tone-filter: grayscale;
  --urui-fx-tone-intensity: .5;
  --urui-fx-transition-filter: fade;
  --urui-fx-transition-progress: 0;
}
```

HTML/CSS 是作者文件；Unity 中分别作为可引用的 TextAsset 交给宿主。Effect.css 使用包内 `Runtime/Resources/URUI.Effect/Effect.css` 资产，不要只写 link 而不注册资源。标准 HTML/CSS 布局可在浏览器检查，`--urui-*` 效果需要 URUI 运行时。

## 完整宿主组件

场景中已有启用的 Surface 和 MainCamera。给下面组件配置 Surface、HTML、panel.css、包内 Effect.css 和图标 Texture2D；图标须符合 Core 图片借用要求。

```csharp
using System;
using URUI.Documents;
using URUI.Effect;
using URUI.Hosting;
using URUI.Resources;
using UnityEngine;

public sealed class EffectPanelHost : MonoBehaviour
{
    [SerializeField] Surface surface;
    [SerializeField] TextAsset html;
    [SerializeField] TextAsset panelCss;
    [SerializeField] TextAsset effectCss;
    [SerializeField] Texture2D icon;
    Document document;
    EffectModule module;

    void OnEnable()
    {
        if (!surface || !html || !panelCss || !effectCss || !icon)
            throw new InvalidOperationException("请先配置页面资源和 Surface。");
        var page = new GameObject("Effect Panel");
        page.SetActive(false);
        page.transform.SetParent(transform, false);
        document = page.AddComponent<Document>();
        try
        {
            document.Surface = surface;
            document.TextResources = new[] {
                new TextResource("/demo/panel.css", panelCss),
                new TextResource("/urui-effect/Effect.css", effectCss)
            };
            document.Images = new[] { new ImageAsset("icon.png", icon) };
            module = new EffectModule(document);
            page.SetActive(true);
            document.Load(html, "/demo/panel.html");
        }
        catch
        {
            ReleasePage();
            throw;
        }
    }

    void OnDisable() => ReleasePage();

    void ReleasePage()
    {
        var previous = document;
        document = null;
        try { if (previous) previous.Unload(); }
        finally
        {
            module?.Dispose();
            module = null;
            if (previous) Destroy(previous.gameObject);
        }
    }
}
```

调用顺序为：配置 Document 资源 → 创建 EffectModule → 激活并加载 → 查询节点/取得控制器。不要给同一 Document 重复创建模块；模块释放不意味着原注册表变回未注册状态，需要重建宿主时使用新 Document。

## 从 CSS 转到 C# 播放

用 `module.GetController(document.Q("reward"))` 获取节点控制器。CSS 中已启用 fade 时，调用 `controller.Play(new EffectTweenDefinition { Channels = EffectTweenChannels.Transition, Duration = .35, WrapMode = EffectWrapMode.Once })` 即可推动转场进度。若 CSS animation、important 或 transition 拥有同一属性，按级联规则优先生效，详见 [C# 接口](API_ZH.md)。

只使用纯 CSS 效果时无需创建每个节点的 EffectController。复杂纹理/粒子资源通过 IEffectResourceResolver 提供，见[渲染与资源](RENDERING_ZH.md)。

## 常见问题

| 现象 | 核对内容 |
| --- | --- |
| unknown effect 或未知 urui-particles 标签 | 模块是否在 Load 前注册 |
| 效果参数未知/默认不符合预期 | Effect.css 是否映射且被 HTML 链接，版本是否配套 |
| Apply 后没有效果 | 节点是否声明 `--urui-effect:ui`，作者 CSS 是否覆盖预设 |
| 进度不触发完成 | 是否使用默认 Loop；需要完成 Task 时显式选 Once/PingPongOnce |
| 重载后控制器报失效 | 重新查询当前文档节点并 GetController |
| 纹理/粒子不显示 | 资源键与 resolver 是否一致，是否提供有效尺寸和源 |
