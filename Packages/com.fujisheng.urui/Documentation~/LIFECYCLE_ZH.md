# 生命周期与资源

## Surface 与独立文档

一个活动 `Surface` 拥有共享原生上下文、视口、UI 时钟、原始输入和绘制。多个 `Document` 显式关联它，各自拥有 DOM、样式、事件与资源命名空间。所有操作限 Unity 主线程；异步读取完成后先切回主线程。

完整作者文件使用真实 HTML/CSS。正式页面使用 Load(CompiledDocumentAsset)，Editor 已完成静态 HTML/CSS、模板和节点索引编译；显式源码加载接口仍处理运行时动态输入。`Load(TextAsset asset, string sourceUrl)` 和 `LoadHtml(string html, string sourceUrl)` 验证并构建候选，成功后发布；失败保留原文档、元素身份与资源。SourceUrl 是资源基址，不按 URL 后缀切换解析器。完整文档包含 doctype、html/head/body，非空元素必须显式闭合；片段按目标元素上下文解析。

候选更新、监听器和退役存储在发布前准备。发布后的通知异常单独处理，不把已提交的新页面报告为加载失败；暂时无法完成的呈现焦点会在后续更新中重试。焦点及祖先链先统一提交，回调内重新聚焦不会由外层旧事件覆盖。

启用 Document 时加载配置的 CompiledSource 或动态 Source/SourceUrl，或恢复最近请求。禁用后保留恢复请求；显式 `Unload()` 取消自动恢复。关闭 A 只使 A 的元素、事件和动画失效，不卸载 B；最后页面关闭后 Surface 仍可供新页面使用。禁用 Surface 暂停所有页面，重新启用按各自请求恢复。Surface 的 UIClock 只能在没有已加载页面时更换。

Loaded/Unloaded、点击、图片与动画回调中的生命周期变更进入共享队列，在当前原生调用返回后执行。需要同步创建节点的业务操作可用 `RunAfterEvent`。绘制效果的 Prepare/Record 只能消费不可变帧，不能在其中修改 DOM 或安排页面变更。

## 元素身份

`DocumentElement` 表示 html，`Body` 表示 body。节点携带所属文档和代际；同 ID 可存在于不同文档，查询不跨页。跨文档 Add/Insert、循环层级或移除文档根会被拒绝。每个上下文仍限制 65536 个被跟踪的活节点。

`Create<T>()` / `CreateElement(tag)` 创建所属文档的脱离节点。Remove/Clear 保留子树、身份和订阅，可再次挂载；不用时调用 Destroy。成功的 Text/SetInnerHtml 结构替换、Destroy、卸载或重载使对应旧身份失效，同名新节点不会复活。`Text` 只处理纯文本；`SetInnerHtml(html, sourceUrl)` 严格解析并事务替换，失败保留原子树。HTML template 惰性保存，Clone 直接复制结构。

Element 不实现 IDisposable；返回的订阅 IDisposable 负责解绑。文档卸载统一释放附着及脱离节点。节点名称可重复，Q 返回所属根下首项，Query 返回有序快照。

## 资产所有权

| 资源 | 所有者与释放规则 |
| --- | --- |
| Source、TextResources 中的 TextAsset | 应用持有；URUI 不销毁原资产，原生按文档资源代际保留所需内容 |
| Images 中的 Texture2D | 直接借用原 GPU 对象；不要求 Read/Write，不销毁原图 |
| ImageResolver 返回的 ImageLease | URUI 持有；原生与 GPU 引用均结束后归还租约 |
| 字体字节、字形与自有纹理 | 按真实来源共享，规则匹配仍属于文档；Surface 和 GPU 引用决定缓存/退役边界 |
| 附着/脱离节点与订阅 | Document/Element 拥有，结构销毁和卸载时清理 |
| mesh/material/command buffer、合成中间目标 | 宿主管理；已经提交的资源须等 fence 退役 |

## 字体、CSS 与图片

在所属文档卸载后配置资源；A 活动时可配置 B。相对地址按作者文件的规范 URL 解析。外部 CSS、字体和 HTML 原样注册：

```csharp
document.Unload();
document.TextResources = new[] {
    new TextResource("/ui/panel.css", cssAsset),
    new TextResource("/ui/fonts/panel.ttf", fontBytesAsset)
};
document.Images = new[] { new ImageAsset("icons/coin.png", texture) };
document.Load(panelHtmlAsset, "/ui/panel.html");
```

HTML 使用 `<link rel="stylesheet" href="panel.css">`；CSS 使用标准 `@font-face` 和 `url("fonts/panel.ttf")`。默认通用字体来自随包资源；精确对照需让浏览器与 URUI 使用同一份授权字体字节。字体族、字重、样式和回退逐文档匹配；字号保留小数并以 26.6 像素精度缓存。中文等文字须有相应字形。

TextResource 的资产类型是 TextAsset，不能直接传 Unity Font 对象。可将授权字体的原始文件以 `.bytes` TextAsset 提供，资源 URL 仍登记成 CSS 引用的 `/ui/fonts/panel.ttf`；不要将二进制字体转换成字符串。

图片路径须与 HTML/CSS 一致。同一 Texture2D 的多个路径共享资源记录；原生只保留借用图片的元数据。应用可更新像素并 Apply；借用和在途 GPU 引用期间不得 Destroy、UnloadAsset 或重建尺寸/格式。URUI 不自动远程下载或解码图片；应用负责得到 Texture2D 后注册。

颜色 Texture2D 使用 bilinear/clamp/mip0，不修改原图的 sampler 配置；要求完整常驻 mip0，不接受启用纹理流送或 active mipmap limit 的源图。Linear 下标准 UI 按浏览器 sRGB source-over 合成；游戏 RenderTexture 扩展保持 Unity 线性语义。非颜色数据、法线/深度与 HDR 保真不在这项标准 UI 对照范围。

## 初始化几何与完整绘制

`Document.SynchronizeGeometry()` 同步样式、布局和空间变换，普通元素不生成绘制命令。FUI 初始化使用此入口；尺寸、局部/世界坐标在返回时可读。带自定义 painter 或外部几何提供器的目标文档仍完成其绘制准备。后续正常帧、Built-in 相机或受控 SRP 提交会在消费前准备最新记录；需要立即取得完整记录时继续使用 `SynchronizeLayout()`。两者均须在文档回调外调用，输入与 `LayoutUpdated` 的时序不变。轻量同步会在刷新回调后再次完成空间变换，并复核回调新增的绘制器。同步几何及需要准备记录的命令缓冲借用还须避开元素 `OnUpdate`；回调内不会递归准备绘制。

文件资源注册借用 `TextAsset.GetData<byte>()` 原生只读区，避免按页面复制托管大数组；C ABI 同步消费数据，原生已有不可变资源共享和独立文档代际快照。指针不逃逸，未增加常驻缓存或资源失效开关。

FUI 初始化请求 `RequestGeometry(scope.Root)`，更新批次合并请求，真实几何读取或最外层批次结束时同步：文档几何仍全部更新，立即准备绘制器的范围是当前视图子树。同一文档其他子树的粒子或绘制器由正常帧、相机或受控提交统一准备。无参入口仍保证整份文档内的绘制器就绪。指定根必须有效、属于当前文档且已挂载；刷新回调销毁或重载根后，不再准备旧子树。

## 同步按需图片与提交

```csharp
document.ImageResolver = source => {
    var texture = ResolveApplicationTexture(source);
    return texture ? new ImageLease(texture) : null;
};
```

静态 Images 优先。回调主线程同步返回独立租约；null 是本代际失败缓存，不是异步 pending。归还资源可传 `new ImageLease(texture, release)`，交给 URUI 后不要自行 Dispose；共享 Resources 资产不能随意 UnloadAsset。可释放图片要求 GPU fence，无 fence 的设备应提供由应用长期持有的借用图片。

关闭页面先退役所属命名空间；其他页面和已提交 GPU 帧仍需使用的图片、字体或效果继续保留。关闭页面不等于所有共享缓存立即销毁，也不能按固定帧数猜测资源已安全释放。

SRP 通过 `Surface.SubmitRendering(context, target, viewport)` 每个 Surface 提交一次；Document 的入口提交同一共享 UI。方法排列命令与 fence，由管线在帧末 Submit。viewport 须有限且宽高为正。最终输出使用基础相机的完整像素视口（含原点），不使用 URP 中间渲染缩放尺寸。无效默认 context 在记录/固定资源前拒绝；真正提交结果不确定时保留租约并停止后续提交。正常在途记录上限为 8。

RenderCommandBuffer 仅在宿主没有需安全退役的资源时允许借用；可释放图片、图层以及 Linear 标准合成必须使用受控 SubmitRendering。更新原生动态库前停止相应 Editor/Player，重新启动后才能使用新的配套 ABI10 / render-plan3 插件。

屏幕背景需要参与标准颜色合成时，原生计划声明一个可采样的完整输出工作目标，并安排初始场景拷贝与最终输出。Unity 默认 Blit 负责解析屏幕目标；自定义 Shader 仅采样真实纹理。此路径按原生输入语义自动选择，不是用户优化开关。工作目标沿已有池和 GPU fence 退役；空的扩展回调不会租用目标或产生拷贝。额外颜色负载为输出宽×高×每像素 4 字节（LDR）或 8 字节（HDR），实际常驻与在途成本需按目标尺寸测量。

## 资源接口速查

| 接口 | 参数和使用顺序 |
| --- | --- |
| `TextResource(string source, TextAsset asset)` | source 是规范资源 URL，asset.bytes 保留 CSS/字体文件内容；先登记，再加载引用它的 HTML |
| `ImageAsset(string source, Texture2D texture)` | 借用原图片，同文档路径不能重复 |
| `ImageAsset(string source, Texture2D texture, SpriteRegion[] sprites)` | 同时登记命名区域；复制区域数组，不复制像素 |
| `SpriteRegion(string name, Rect region, float displayScale = 1)` | region 是左上角源像素区域；Image.SpriteName 按名称选择 |
| `ImageLease(Texture texture, Action release = null)` | 独立资源引用；交给 URUI 后由其归还；不能手动销毁仍在使用的纹理 |
| `ImageLeaseSource(Texture texture, Action release)` | 为同一纹理发布多个独立租约的所有权源 |
| `ImageLeaseSource.Acquire()` | 取得独立 ImageLease，交给一个消费者 |
| `ImageLeaseSource.Dispose()` | 归还源自身所有权；仍被获取的租约有效，最后引用结束后调用 release |
| `Document.TryLoadImage(string source, out Vector2Int dimensions)` | 已加载文档中同步预加载图片并读取尺寸；无图片返回 false，非法调用可抛异常 |
| `Document.ReleaseImage(string source)` | 退役该页路径的图片缓存；仍引用该路径的节点会再次解析，不影响其他文档 |
| `Document.AddStyleSheet(TextAsset asset, string sourceUrl, bool beforeAuthor = false)` | 文档已加载后追加已有 CSS；beforeAuthor=true 放在作者规则之前 |

ReleaseImage 不是停止媒体生产者的替代品。先移除/换掉相关节点，再释放缓存及自己的媒体引用；可释放图片仍等待 GPU 完成。ImageResolver 返回 null 后本代际会缓存失败，需要有明确的重新加载/释放缓存流程，不能把 null 当作异步 pending。

## 命名图片区域示例

```csharp
using URUI.Documents;
using URUI.Resources;
using UnityEngine;

public static class AtlasExamples
{
    public static void Configure(Document document, Texture2D atlas)
    {
        document.Unload();
        document.Images = new[] {
            new ImageAsset("atlas.png", atlas, new[] {
                new SpriteRegion("coin", new Rect(0, 0, 32, 32)),
                new SpriteRegion("gem", new Rect(32, 0, 32, 32))
            })
        };
    }
}
```

随后加载作者文档，图片使用 atlas.png，并通过 Image.SpriteName 选择 coin 或 gem。区域必须位于原图内；图集纹理仍由调用方保持有效。不要与效果 Wave 的完整纹理支持范围混为一谈。

全部资源类型、公开声明及重载见[接口参考](REFERENCE_ZH.md)，视频接口见[渲染扩展与媒体](RENDERING_ZH.md)。

元素状态刷新要求元素协议 v3。稳定布局复用元素已有的挂载和相对布局观察值；上下文变化或 token 失效立即撤销证明。控件 OnUpdate 每次仍执行，未知回调后重新核对变化代际，不能把它用作跳过业务更新的缓存。文档仅增加16字节证明，不新增节点快照或GPU资源。
