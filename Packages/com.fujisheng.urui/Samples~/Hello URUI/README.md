# Hello URUI

从候选包的 Package Manager Samples 导入，将 HelloUrui 添加到空 GameObject，进入 Play Mode。已有 MainCamera 时复用；没有时创建并在销毁示例时清理。

示例在 Loaded 中重新查询节点和注册点击，点击后更新计数和背景色，在 OnDisable 中解绑并卸载。一次仅运行一个 URUI 文档。

此示例使用候选 URUI.Document API，固定 v0.1.0-preview.1 仍包含旧兼容 Hello Rml。见 Documentation~/QUICK_START_ZH.md。
