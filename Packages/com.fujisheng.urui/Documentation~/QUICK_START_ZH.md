# 快速开始

准备 Unity 6000.3.22f1、macOS ARM64、Built-in / Metal 项目，并按[安装指南](INSTALL_ZH.md)安装 URUI。启用 **Input Manager (Old)** 或 **Both**，场景中保留一个标记为 `MainCamera` 的相机。

## 运行样例

1. Package Manager → URUI → Samples → 导入 **Hello URUI**。
2. 将 `HelloUrui` 添加到空 GameObject。
3. 进入 Play Mode，点击按钮查看计数与背景色变化。

样例会在缺少 MainCamera 时创建相机。一次只运行一个活动文档，体验样例前先禁用其他 URUI 文档。

也可使用 **Tools > URUI > Create Minimal UI Scene**。工具提示保存当前场景后，创建包含 `Document`、RML 文本资产和相机的场景；重复使用会生成独立资产路径。

## 编写 RML/RCSS

创建 `Panel.txt`，Unity 会将其导入为 `TextAsset`：

```xml
<rml>
<head><style>
body { display:block; font-family:__URUI_FONT__; color:white; font-size:24px; }
div { display:block; }
#panel { width:320px; height:160px; padding:20px; background-color:#204080; }
#action { width:220px; height:48px; margin-top:20px; background-color:#4070b0; }
</style></head>
<body>
  <div id="panel">
    <div id="label">Hello URUI</div>
    <div id="action">Click</div>
  </div>
</body>
</rml>
```

RmlUi 不提供浏览器的完整默认标签样式。示例显式将 `div` 设为块级布局，宽高和定位规则以 RCSS 为准。`__URUI_FONT__` 是 URUI 为示例默认字体替换的占位符；自定义字体见[字体与图片](LIFECYCLE_ZH.md)。

## 连接 C# 业务

给 GameObject 添加 **URUI > Document**，把 `Panel.txt` 拖入 **Source**。再添加以下组件，将同一个 `Document` 拖入业务组件的 **Document** 字段。使用 asmdef 时引用 `URUI.Runtime`。

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

Unity 组件启用顺序不固定，因此业务组件同时订阅 `Loaded` 并处理已经加载的情况。加载后 `Q("label")` 按唯一 ID 查找，参数不带 `#`；找不到时返回 `null`。

也可在启用的 `Document` 上调用 `Load(asset)` 或 `LoadMarkup(rml)` 同步加载。`Loaded` 表示可以查询和修改元素；布局、绘制与输入命中在后续 Unity 更新中完成。文档重载后，旧元素与订阅失效，重新在 `Loaded` 中查询和绑定。

接下来阅读 [C# API](API_ZH.md)、[点击事件](EVENTS_ZH.md)、[生命周期与资源](LIFECYCLE_ZH.md)和[支持范围](LIMITATIONS_ZH.md)。
