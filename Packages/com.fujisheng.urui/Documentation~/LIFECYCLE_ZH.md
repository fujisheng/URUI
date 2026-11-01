# 生命周期与资源

## 文档加载

`Document` 启用时加载 Inspector 中的 `Source`，或恢复最近一次 `LoadMarkup` 请求。`Load` / `LoadMarkup` 同步完成文档解析，成功后触发 `Loaded`，此时可以查询、更新与注册点击。布局、绘制和输入命中在后续 Unity 更新中完成。

重载依次使旧元素与订阅失效、释放原生 context 和渲染资源、触发 `Unloaded`，然后创建新文档并触发 `Loaded`。禁用组件会释放内容，重新启用会恢复最近的文档请求。显式 `Unload()` 会取消自动恢复，直到再次加载或设置 `Source`。

销毁组件会释放文档资源并清空生命周期委托。`Loaded` 和 `Unloaded` 中不能重入加载或卸载。若 `Unloaded` 回调禁用了组件，正在进行的加载会取消。

当前只支持一个活动原生 context，即一个活动 `Document`。所有操作均需在 Unity 主线程执行；异步读取文件后，应切回主线程再调用 URUI。

## 元素身份

节点由 Document 拥有，Element 包装表示稳定的原生对象身份。Name 变化不会改换对象，查询、Parent 和索引返回同一包装。节点预算为每个 context 65536 个被跟踪的活节点；没有无限引用保证。

CreateElement 产生脱离层级的节点，Add/Insert 后才显示并接收输入。RemoveFromHierarchy、Remove(child)、Clear 会保留子树、身份与回调，之后可以重新挂载。长期不再使用时调用 Destroy，或由 Unload 统一释放；不能把脱离层级当作释放资源。

Destroy、Text/SetInnerMarkup 替换子结构、文档重载或卸载会让对应旧包装失效，即使同名节点再次出现也不会复活。Element 本身不实现 IDisposable；IDisposable 订阅只用于解绑。Name 可以重复，Q 取局部首项，Query 取全部；样式和外部 ID 使用仍由应用维护命名约定。

## 资产所有权

| 资源 | 持有者 | URUI 的处理 |
| --- | --- | --- |
| Inspector 或 `Load` 的 `TextAsset` | 应用 | 读取内容，不销毁原资产 |
| `LoadMarkup` 的内部 `TextAsset` | `Document` | 卸载、重载或销毁时释放 |
| `Images` 中的 `Texture2D` | 应用 | 复制像素，不销毁原图；要求开启 Read/Write |
| 原生纹理副本、Unity mesh/material/command buffer | URUI | 文档释放时清理 |
| 附着及脱离的节点、点击订阅 | `Document` / `Element` | Destroy、结构替换或文档卸载时清理 |
| 字体文件 | 应用或平台 | 应用保证文件路径和所需字形可用 |

## 字体与图片

加载前配置资源，修改已经加载的文档配置前先卸载：

```csharp
document.Unload();
document.FontPath = absoluteFontPath;
document.Images = new[] { new ImageAsset("icons/coin.png", readableTexture) };
document.Load(markupAsset);
```

图片 `Source` 必须唯一，并与 RML/RCSS 中引用的相对路径一致，例如 `<img src="icons/coin.png" />`。纹理导入设置必须开启 **Read/Write**。当前没有远程图片自动下载或异步资源提供接口；入门文档将 RCSS 内联在 RML 的 `<style>` 中。

`FontPath` 使用可读取的字体文件绝对路径。为空时使用平台默认路径；macOS 默认路径为 `/System/Library/Fonts/Supplemental/Arial.ttf`。示例中的 `__URUI_FONT__` 按默认字体约定替换，不是任意字体的自动 family 检测器；使用自定义字体时，在 RCSS 中填写该字体真实的 `font-family`。

应用需要自行分发有使用授权的字体，并在运行时获得实际路径。系统默认路径不代表字体会随 Player 打包。中文等内容需要字体包含对应字形。
