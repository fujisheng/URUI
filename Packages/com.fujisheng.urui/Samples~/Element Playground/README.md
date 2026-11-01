# Element Playground

导入样例后，在带 Main Camera 的空场景中添加 Document 与 ElementPlayground，将 `Playground.rml.txt` 指定给 Document.Source，然后进入 Play Mode。

按钮展示真实的创建、选中、移出、按索引重新挂载、销毁与 Clear。样式通过类名切换；状态文本由控制器显式写入，没有数据绑定。

`Clear` 和移出操作保留节点及其回调，直到重新挂载、显式 Destroy 或文档卸载。长期不再使用的子树应调用 Destroy。样例控制器只释放自己持有的订阅，不接管调用者资产。
