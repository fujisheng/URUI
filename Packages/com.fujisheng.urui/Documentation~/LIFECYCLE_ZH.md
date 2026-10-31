# 生命周期与资源

此页适用于候选托管 API。固定预览的兼容 Host 生命周期见 INSTALL_ZH.md。

## 加载与更新

Document 组件启用时加载 Inspector Source，或恢复最近 LoadMarkup 请求。Load/LoadMarkup 同步完成 native 文档解析，随后触发 Loaded；此时可 Q、设置文字样式、注册点击。RmlUi 布局与 Unity 绘制在后续 Update 完成，刚创建文档当帧不要假设命中框已计算。

重载先使旧 Element 失效、释放点击订阅和原生 context，再发送 Unloaded，创建新文档后发送 Loaded。禁用自动释放；恢复启用重新加载。显式 Unload 会取消自动恢复，直到再次调用 Load 或设置 Source。销毁释放资源并清空生命周期委托。

当前仅有一个原生 context，第二个 Document 或兼容 Host 已活动时加载会被拒绝。不要同时运行 Hello 和自己的 Document；不要在生命周期通知中再次 Load/Unload。生命周期委托的异常按普通 C# event 传播，加载会回收已经创建的资源；业务订阅应自行处理错误。

## 元素身份

Q 返回原对象的弱引用包装，不是永久字符串选择器。移除、内部结构替换、禁用、卸载和重载会使旧包装失效。IsValid 可检查；无效写入抛异常。Dispose 只释放引用与订阅，不删除节点；以后重新 Q 可取得新的包装。即使新树包含同 ID 的节点，旧包装也不会复活。

**必须使用唯一 ID，不能通过兼容 Host 修改已包装节点的 id，也不能在 SetInnerMarkup 中引入重复 ID。** 当前底层一般属性写入仍按 ID 寻址，弱引用只验证原对象存在，不能证明原 ID 仍指向它。因此新接口禁止 SetAttribute("id", ...)，但无法替你审计绕过接口的变更。需要重新组织树时卸载或替换，然后重新查询。

Document 缓存有效查询包装。对大量临时查询主动 Dispose，避免占用引用预算；文档释放会统一回收。兼容桥每 context 最多 65536 个 token，Q 为 null 也可能表示预算已用尽；没有无限引用保证。

## 资产所有权

| 资源 | 创建/持有者 | 释放时机 |
| --- | --- | --- |
| Inspector/Load 的 TextAsset | 应用 | URUI 不销毁，应用按自身资源系统释放 |
| LoadMarkup 内部 TextAsset | Document | 卸载/重载/销毁 |
| Images 中 Texture2D | 应用 | URUI 读像素并复制，不销毁原图；需 Read/Write |
| 原生纹理副本、Unity mesh/material/command buffer | URUI renderer | 文档释放 |
| 点击订阅与弱引用 token | Document/Element | Dispose、失效清理或文档释放 |
| 字体文件 | 应用/平台 | 应用保证路径与字形可用，不自动打包或移动文件 |

```csharp
document.Unload();
document.FontPath = absoluteFontPath;
document.Images = new[] { new ImageAsset("icons/coin.png", readableTexture) };
document.Load(markupAsset);
```

相对图片路径必须与 RML/RCSS 引用一致。不支持远程图片自动下载或异步资源 provider；RML/RCSS 推荐先内联。任何平台字体回退都不构成 Player 字体分发方案，发布应用应指定随应用交付且有授权的字体。
