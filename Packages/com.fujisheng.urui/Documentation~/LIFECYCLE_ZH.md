# 生命周期与资源

## 文档加载

`Document` 启用时加载 Inspector 中的 `Source`，或恢复最近一次 `LoadMarkup` 请求。`Load` / `LoadMarkup` 同步完成文档解析，成功后触发 `Loaded`，此时可以查询、更新与注册点击。布局、绘制和输入命中在后续 Unity 更新中完成。

重载依次使旧元素与订阅失效、释放原生 context 和渲染资源、触发 `Unloaded`，然后创建新文档并触发 `Loaded`。禁用组件会释放内容，重新启用会恢复最近的文档请求。显式 `Unload()` 会取消自动恢复，直到再次加载或设置 `Source`。

销毁组件会释放文档资源并清空生命周期委托。`Loaded` 和 `Unloaded` 中不能重入加载或卸载。若 `Unloaded` 回调禁用了组件，正在进行的加载会取消。

当前只支持一个活动原生 context，即一个活动 `Document`。所有操作均需在 Unity 主线程执行；异步读取文件后，应切回主线程再调用 URUI。

## 元素身份

`Q` 获取的是原节点的引用包装。节点移除、内部结构替换、文档卸载或重载后，旧引用失效；即使新结构有同名 ID，旧包装也不会复活。使用 `IsValid` 检查，或在 `Loaded` 后重新查询。

`Element.Dispose()` 释放包装和订阅，不删除节点。`Remove()` 才会删除节点。大量临时查询使用结束后可主动 `Dispose()`，文档释放也会统一清理。每个 context 的引用令牌上限为 65536；没有无限引用保证。

ID 必须唯一，并在节点存活期间保持不变。`SetAttribute` 禁止修改 ID；`SetInnerMarkup` 的调用者负责避免重复 ID。普通更新仍依赖 ID 定位，重复 ID 会破坏正确寻址。

## 资产所有权

| 资源 | 持有者 | URUI 的处理 |
| --- | --- | --- |
| Inspector 或 `Load` 的 `TextAsset` | 应用 | 读取内容，不销毁原资产 |
| `LoadMarkup` 的内部 `TextAsset` | `Document` | 卸载、重载或销毁时释放 |
| `Images` 中的 `Texture2D` | 应用 | 复制像素，不销毁原图；要求开启 Read/Write |
| 原生纹理副本、Unity mesh/material/command buffer | URUI | 文档释放时清理 |
| 元素引用与点击订阅 | `Document` / `Element` | 释放、失效或文档卸载时清理 |
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
