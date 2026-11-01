# Hello URUI

导入样例，在空 GameObject 添加 HelloUrui 后进入 Play Mode。未配置 Source 时使用内置 RML；也可以为同一对象的 Document.Source 指定可编辑 TextAsset。

样例通过 Loaded 获取 Root，再用局部 Q 查询 label/action/panel；Text 显式写入文本，RegisterCallback<ClickEvent> 订阅点击，Style 写入 RCSS。控制器等待 Document 启用后才主动加载，避免组件启用顺序造成异常。

OnDisable 释放订阅并卸载文档，调用者提供的 TextAsset 不会被销毁。需要动态创建、重挂载和销毁示例时，导入 Element Playground。
