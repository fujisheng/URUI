# 样式与动画

使用 `URUI.Elements`、`URUI.Styles` 和 `URUI.Animation`。HTML/CSS 拥有静态布局、状态规则、关键帧及曲线；C# 更新值、class 和播放身份。全部重载见[接口声明参考](REFERENCE_ZH.md)。

## 写入行内样式

| 接口 | 参数与用途 |
| --- | --- |
| `element.Style.Width/Height/... : Length` | px、百分比、em、rem、auto 或 Unset |
| `Length.Pixels/Percent/Em/Rem(float)` | Percent(50) 表示 50%，不传 0.5 |
| `Length.Auto` / `Length.Unset` | 自动值 / 移除行内覆盖 |
| `Style.Display/FlexDirection/AlignItems/...` | 当前强类型映射的枚举属性 |
| `Style.Opacity/FlexGrow/FlexShrink : float?` | null 移除行内覆盖 |
| `Style.BackgroundColor : Color?` | null 恢复级联结果 |
| `Set(string property,string value)` | 复杂 CSS 表达式的字符串入口 |
| `SetNumber/SetPixels/SetPercent/SetDensityPixels` | 按对应数值类型写入；属性须接受该类型 |
| `SetColor(string,Color)` | 写颜色 |
| `SetKeyword(string,int)` | 写已注册属性的关键字序号，经原生解析器校验 |
| `Remove(string)` | 删除行内声明 |
| `Get(string)` | 读取行内声明，不等于最终级联结果 |

```csharp
using URUI.Elements;
using UnityEngine;

public static class StyleExamples
{
    public static void Open(Element panel)
    {
        panel.Style.Display = DisplayStyle.Flex;
        panel.Style.FlexDirection = FlexDirection.Column;
        panel.Style.Width = Length.Percent(50);
        panel.Style.PaddingLeft = 12;
        panel.Style.Opacity = .8f;
        panel.Style.BackgroundColor = Color.blue;
        panel.Style.Width = Length.Unset;
    }
}
```

ResolvedStyle.Get 读取级联值，任意属性可能仍保留相对单位。`ResolvedStyle.TryGetLength(name, out Length)` 读取有限像素、百分比、em、rem 或 auto，不创建托管字符串；缺失、未解析或非长度值返回 false。它保留单位；无单位数值与 `Length.Parse` 一样视为像素，不代替 CSS 属性类型校验，也不把百分比等相对单位自动换算成像素。ResolvedStyle.Width/Height 为实际 border 像素尺寸；测量位置和内容区用 Layout/ContentRect/WorldBound。首次布局前检查 HasLayout。

## 自定义属性与计算快照

需要类型化读取或动画的作者属性在 CSS 中显式注册：

```css
@property --progress { syntax: "<number>"; inherits: false; initial-value: 0; }
@property --accent { syntax: "<color>"; inherits: false; initial-value: white; }
.panel { --progress: 0.5; --accent: #4080ff; }
```

`document.Styles.GetComputed(element)` 要求节点属于当前有效文档且已有布局。返回不可变 ComputedStyleSnapshot；未变化时可复用相同快照，旧快照不随下一帧改写。

| 接口 | 返回值/约束 |
| --- | --- |
| `GetNumber(string)` | 注册的 number |
| `GetPixels(string)` | 已解析的 px 长度 |
| `GetColor(string)` | Unity Color |
| `GetKeyword(string)` | 关键字字符串 |
| `GetImage(string)` | ComputedImageValue，包含 Kind、Url、CssText、SourceUrl |
| `GetOrigin(string)` | Initial/UserAgent/Author/Animation/Important/Transition |
| `Contains(StylePropertyDefinition)` | 检查名称及类型是否存在 |
| `Generation` / `Revision` | 文档资源代际 / 该快照值版本 |
| `WithOverrides(dictionary, maximumOrigin)` | 创建带局部覆盖的快照视图，不修改 DOM；超过给定优先级的声明保留 |
| `document.Styles.Subscribe(element, callback)` | 从后续稳定布局通知开始，值变化时通知，返回可释放订阅 |

```csharp
using System;
using URUI.Documents;
using URUI.Elements;
using URUI.Styles;

public static class ComputedStyleExamples
{
    public static IDisposable Observe(Document document, Element panel, Action<float> changed)
    {
        var definition = new StylePropertyDefinition("--progress", StyleValueType.Number);
        return document.Styles.Subscribe(panel, snapshot =>
        {
            if (snapshot.Contains(definition)) changed(snapshot.GetNumber("--progress"));
        });
    }
}
```

Getter 与属性类型不匹配会失败，应先保证作者注册及加载资源正确。ComputedImageValue 的相对 URL 保留实际 CSS 来源，不按调用者当前目录解析。重载使旧元素失效；订阅仍应由业务主动 Dispose。

## 命名 CSS 动画

CSS 定义状态和动画，C# 指定根、激活 class 和目标组：

```css
@keyframes panel-enter {
  from { opacity: 0; }
  to { opacity: 1; }
}
.panel.entering { animation: panel-enter 0.2s ease-out; }
```

```csharp
using System.Threading;
using System.Threading.Tasks;
using URUI.Animation;
using URUI.Documents;
using URUI.Elements;

public static class AnimationExamples
{
    public static async Task Enter(Document document, Element panel, CancellationToken token)
    {
        var targets = AnimationGroup.Named(new[] { new AnimationTarget(panel, "panel-enter") });
        using (var playback = document.Animations.Play(panel, "entering", targets, token))
        {
            await playback.Finished;
        }
    }
}
```

| 接口 | 语义 |
| --- | --- |
| `AnimationTarget(Element,string name)` | 真实作者节点与 CSS animation-name |
| `AnimationGroup.Named(IEnumerable<AnimationTarget>)` | 明确有限目标组 |
| `AnimationGroup.FromAuthorTargets(Func<IReadOnlyList<Element>>)` | 布局准备后从作者节点解析有限动画组 |
| `document.Animations.Play(root,activeClass,group,token)` | 返回独立 AnimationPlayback；根须属于当前文档并挂载 |
| `playback.Identity` | 播放身份，用于区分重入 |
| `playback.Finished : Task` | 全部目标的属性轨道自然结束后成功 |
| `playback.IsPlaying` | 尚未结束 |
| `Cancel()` / `Dispose()` | 取消播放，不报告自然成功 |

同一根和激活 class 的新播放会取消旧播放。隐藏、销毁、卸载及 token 取消均不能误完成新播放。等待成功需要有限动画；不要把无限循环的 CSS 轨道作为页面入场完成信号。不要用固定延时替代 Finished。CSS 支持 linear、cubic-bezier 和 linear() 分段缓动，范围以 HTML/CSS 支持清单为准。

## UI 时钟

`IUiClock` 提供 `double Now` 与 `float Delta`。Surface 默认 `UnityUiClock.Unscaled`，不受 Time.timeScale 影响；`UnityUiClock.Scaled` 随游戏时间缩放。需在所有文档卸载时配置 Surface.UiClock，活动页面期间替换会抛异常。

```csharp
using URUI.Animation;

public sealed class ManualUiClock : IUiClock
{
    public double Now { get; private set; }
    public float Delta { get; private set; }
    public void Advance(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0)
            throw new System.ArgumentOutOfRangeException(nameof(seconds));
        Delta = seconds;
        Now += seconds;
    }
}
```

加载前将此实例赋给 surface.UiClock，在主线程推进，保持 Now 单调递增。`document.Effects.BindClock(element, clock)` 为单个效果节点建立独立租约，Dispose 恢复文档时钟；它不改变 CSS 动画或媒体解码时基。

## 通用行为

完整作者样式参考包内 [behaviors.css](../Runtime/Resources/URUI/behaviors.css)，使用时在 HTML 中链接 `/urui/behaviors.css`。这些控制器不属于 Effect 包。

| 类型与入口 | 作者前提及生命周期 |
| --- | --- |
| `ScrollMotion(Element viewport,Element content)` | 同文档 viewport/content 具有 scroll-motion / scroll-motion-content class；ScrollTo(Vector2) 程序滚动；Dispose 解绑并恢复覆盖 |
| `StaggeredReveal(root,container,activeClass,prepareClass,revealedClass)` | 作者声明 `--urui-stagger-edge/span/gap`，可选 delay/boundary；准备状态测量，激活后逐项加 revealedClass；Dispose 释放 |
| `BezierMotion(path,rotation,source,target)` | 作者声明路径参数和实际 source/target 节点；Begin 启动、Stop 停止、Dispose 释放 |
| `VisibilityClass(target,activation,className)` | 按 activation 的实际可见性维护 target 的状态 class；Dispose 清理 |

ScrollMotion 的边界为 elastic/clamped/unrestricted；灵敏度、拖动阈值、弹性和减速来自 CSS，不在每个业务 Update 重建控制器。StaggeredReveal 的 span/gap/delay 以秒的 number 声明。各构造参数、全部枚举和声明见接口参考；现成的作者结构可从 Game Flow 或 Runtime Controls 样例复制。
