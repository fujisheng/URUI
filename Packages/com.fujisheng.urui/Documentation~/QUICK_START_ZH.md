# 快速开始

按[安装指南](INSTALL_ZH.md)准备 Unity 6000.3.22f1 / macOS ARM64 / Built-in / Metal 工程、MainCamera 和 Input Manager (Old) 或 Both。

## 文档资产

创建 TextAsset，例如 `Panel.rml.txt`，将它指定给 Document.Source：

```xml
<rml><head><style>
body { margin:24px; color:white; font-family:__URUI_FONT__; font-size:24px; }
div { display:block; }
#panel { width:360px; padding:20px; background-color:#204080; }
#action { width:220px; height:48px; margin-top:20px; background-color:#4070b0; }
</style></head><body>
<div id="panel"><div id="label">Hello URUI</div><div id="action">Click</div></div>
</body></rml>
```

默认字体约定及图片资源见[生命周期与资源](LIFECYCLE_ZH.md)。

## 业务控制器

Source 由 Document 在启用时自动加载。业务组件订阅 Loaded，并处理已经加载的情况；不依赖不同组件的 OnEnable 顺序。

```csharp
using System;
using URUI;
using UnityEngine;

[RequireComponent(typeof(Document))]
public sealed class PanelController : MonoBehaviour
{
    Document document;
    IDisposable click;

    void OnEnable()
    {
        document = GetComponent<Document>();
        document.Loaded += Bind;
        if (document.IsLoaded) Bind(document);
    }

    void Bind(Document loaded)
    {
        click?.Dispose();
        var panel = loaded.Root.Q("panel");
        var label = panel.Q("label");
        label.Text = "Ready";
        click = panel.Q("action").RegisterCallback<ClickEvent>(evt =>
        {
            label.Text = "Clicked " + evt.CurrentTarget.Name;
            panel.EnableInClassList("active", true);
        });
    }

    void OnDisable()
    {
        click?.Dispose();
        click = null;
        if (document) document.Loaded -= Bind;
    }
}
```

## 动态节点

```csharp
var panel = document.Root.Q("panel");
var caption = document.CreateElement("span");
caption.Name = "caption";
caption.Text = "New node";
caption.Style.SetWidth(200);
panel.Add(caption);
caption.RemoveFromHierarchy(); // 有效，但暂不显示/接收输入
panel.Insert(0, caption);       // 同一个对象重新挂载
caption.Destroy();             // 明确销毁
```

Name/Class 查询是局部、精确匹配，Query 返回快照；不是 CSS 选择器或完整 UITK。完整按钮操作见 Element Playground。继续阅读 [API](API_ZH.md)、[事件](EVENTS_ZH.md)和[支持范围](LIMITATIONS_ZH.md)。
