# Element Playground

导入样例后，用 `Tools/URUI/Create Minimal UI Scene` 创建场景，将 `Playground.html` 指定给 Document.Source，SourceUrl 设为 `samples/element-playground/Playground.html`，再添加 ElementPlayground 并进入 Play Mode。菜单已建立 Surface 和显式的 base.css/字体依赖；多个文档可以关联同一 Surface。

浏览器预览直接读取这份 HTML，并将包内 `Runtime/Resources/URUI` 挂载为 `/urui`。节点操作按钮由 C# 控制器执行。

按钮展示真实的创建、选中、移出、按索引重新挂载、销毁与 Clear。样式通过类名切换；状态文本由控制器显式写入，没有数据绑定。

`Clear` 和移出操作保留节点及其回调，直到重新挂载、显式 Destroy 或文档卸载。长期不再使用的子树应调用 Destroy。样例控制器只释放自己持有的订阅，不接管调用者资产。
