# 快速开始：候选托管 API

此页适用于 `urui/managed-api-docs` 候选分支。已发布 `v0.1.0-preview.1` 仍使用兼容 `RmlUnityHost`，不含下面的 `URUI.Document`。不会修改该 tag。

## 环境与安装

使用 Unity 6000.3.22f1、macOS ARM64、Built-in / Metal。仓库为私有，Git 安装需已有仓库权限；本机 Git 未认证时，从完整包目录选择 `package.json` 或复制到项目 Packages。不要与旧包同时安装。

候选分支安装地址（会随候选提交更新，评审前可改为固定提交 SHA）：

```text
https://github.com/fujisheng/URUI.git?path=/Packages/com.fujisheng.urui#urui/managed-api-docs
```

应用的 Input Handling 使用 Input Manager (Old) 或 Both；场景中需有标记 MainCamera 的相机。目前仅支持一个活动 context，不可同时运行多个 Document 或旧 Host。

## 最短可运行路径

1. Package Manager → URUI → Samples → 导入 Hello URUI。
2. 空 GameObject 添加 `HelloUrui`。
3. 进入 Play Mode，点击按钮查看计数与背景色变化。示例在没有相机时创建自己的相机。

也可运行 `Tools > URUI > Create Minimal UI Scene`，生成带 Source 的 Document 场景。工具会询问保存当前场景，创建唯一资产路径，避免覆盖已编辑的文档。

## 自己写一个文档

创建 `Panel.txt`（Unity 会将 .txt 导入为 TextAsset）：

```xml
<rml>
<head><style>
body { display:block; font-family:__RMLUNITY_FONT__; color:white; font-size:24px; }
div { display:block; }
#panel { width:320px; height:140px; background-color:#204080; }
</style></head>
<body><div id="panel"><div id="label">Hello URUI</div><div id="action">Click</div></div></body>
</rml>
```

给 GameObject 添加 `URUI.Document`，将 Panel.txt 拖入 Source。业务组件在 OnEnable 订阅 Loaded，并处理已经加载的情况（Unity 组件启用顺序不固定）：

```csharp
using System;
using UnityEngine;
using URUI;

public sealed class PanelController : MonoBehaviour
{
    [SerializeField] Document document;
    IDisposable click;

    void OnEnable()
    {
        document.Loaded += Bind;
        if (document.IsLoaded) Bind(document);
    }

    void Bind(Document loaded)
    {
        click?.Dispose();
        var label = loaded.Q("label");
        label.SetText("Ready");
        loaded.Q("panel").Style.SetWidth(360);
        click = loaded.Q("action").RegisterClickCallback(
            notification => label.SetText("Clicked"));
    }

    void OnDisable()
    {
        document.Loaded -= Bind;
        click?.Dispose();
        click = null;
    }
}
```

也可 `document.Load(asset)` 或 `document.LoadMarkup(rml)` 同步加载。Loaded 表示可以查询和修改节点；布局、绘制、输入命中在后续 Unity Update 完成。重载/禁用后旧节点失效，重新在 Loaded 绑定。

字体占位符沿用兼容层的 `__RMLUNITY_FONT__`；需要自己的字体时，在加载前设置 FontPath（绝对路径）并在 RCSS 使用实际 family。默认路径是开发环境回退，不代表字体会被打包到 Player。中文内容需实际包含中文字形的字体。

更多：[API](API_ZH.md)、[事件](EVENTS_ZH.md)、[资源与生命周期](LIFECYCLE_ZH.md)、[限制](LIMITATIONS_ZH.md)。

RmlUi 不自动提供浏览器的默认标签样式；需要块布局时显式写 `div { display:block; }`，否则宽高等属性在行内布局中可能不按预期生效。
