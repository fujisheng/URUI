# 快速开始

对应 Core `0.8.0-preview.1`。先按[安装](INSTALL_ZH.md)准备 Unity 6000.3.22f1、Camera、配套原生插件与 Input Manager (Old/Both)。

## 运行潮汐信标

用 Unity 6000.3.22f1 打开 [SampleProject](../../../SampleProject/)，选择 **Tools → URUI → Open Tideglass Beacon Sample**，然后进入 Play Mode。Built-in 工程也可以在 Package Manager 导入 **URUI → Samples → Tideglass Beacon**。查看[操作和源码](../Samples~/Tideglass%20Beacon/README_ZH.md)。

## 创建最小作者文件

创建 `Panel.html`：

```html
<!doctype html>
<html><head>
<link rel="stylesheet" href="/urui/base.css">
<link rel="stylesheet" href="/urui/controls.css">
<link rel="stylesheet" href="panel.css">
</head><body>
<div id="panel">
  <label id="caption">Hello URUI</label>
  <button id="action" type="button">Click</button>
</div>
</body></html>
```

创建 `panel.css`：

```css
body { margin: 24px; color: white; font-family: sans-serif; font-size: 24px; }
#panel { display: block; width: 360px; padding: 20px; background-color: #204080; }
#caption { display: block; }
#action { width: 220px; height: 48px; margin-top: 20px; }
```

文件使用标准 HTML/CSS 的受支持子集，普通非空元素显式闭合。浏览器打开 Panel.html 可检查布局；精确字体对照须提供同一份字体字节。

## 完整宿主组件

场景已有启用的 Surface 和 MainCamera。先由项目 Editor 导入器调用 CompiledDocumentCompiler.CompileAndSave，将 HTML/CSS 与资源闭包生成 CompiledDocumentAsset；规范 URI 映射见生命周期文档。Tools → URUI 的最小场景菜单也会直接创建配套编译资产。创建普通 GameObject，挂下面组件，在 Inspector 配置 Surface 与编译资产。文件名与类名均为 PanelController。

```csharp
using System;
using URUI.Controls;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using UnityEngine;

public sealed class PanelController : MonoBehaviour
{
    [SerializeField] Surface surface;
    [SerializeField] CompiledDocumentAsset compiled;
    Document document;
    Button button;
    Action clicked;

    void OnEnable()
    {
        if (!surface || !compiled)
            throw new InvalidOperationException("请配置 Surface 与编译资产。");
        var page = new GameObject("Panel Document");
        page.SetActive(false);
        page.transform.SetParent(transform, false);
        document = page.AddComponent<Document>();
        try
        {
            document.Surface = surface;
            document.Loaded += Bind;
            document.Unloaded += Unbind;
            page.SetActive(true);
            document.Load(compiled);
        }
        catch
        {
            ReleasePage();
            throw;
        }
    }

    void Bind(Document loaded)
    {
        Unbind(loaded);
        var caption = loaded.Q<Label>("caption");
        button = loaded.Q<Button>("action");
        clicked = () => caption.Text = "Clicked";
        button.Clicked += clicked;
    }

    void Unbind(Document owner)
    {
        if (button != null && button.IsValid) button.Clicked -= clicked;
        button = null;
        clicked = null;
    }

    void OnDisable() => ReleasePage();

    void ReleasePage()
    {
        var previous = document;
        document = null;
        if (!previous) return;
        Unbind(previous);
        previous.Loaded -= Bind;
        previous.Unloaded -= Unbind;
        try { previous.Unload(); }
        finally { Destroy(previous.gameObject); }
    }
}
```

此例由组件创建并拥有 Document，禁用时卸载和销毁。若使用 Inspector 配置的 Document.CompiledSource 自动加载，应在订阅 Loaded 后检查 IsLoaded，并在已加载时立即绑定；不要依赖不同组件 OnEnable 的先后顺序。

## 动态节点和输入

通过 `document.Create<Label>()` 创建后，再 `document.Body.Add(label)` 挂载。RemoveFromHierarchy 保留身份和订阅，重新 Add 可恢复；Destroy 则永久销毁。`Q<Button>("action")` 使用 id，不带 `#`；查询不解析 CSS 选择器。

外部输入入口为 `document.Surface.Input`，先设置 UseUnityInput=false 再发送，见[输入与事件](EVENTS_ZH.md)。资源映射和字体加载见[生命周期](LIFECYCLE_ZH.md)，值控件与虚拟列表见[控件](CONTROLS_ZH.md)，完整节点接口见 [API](API_ZH.md)。
