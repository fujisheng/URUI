# Hello URUI

在 Package Manager → URUI → Samples 中导入，将 `HelloUrui` 添加到空 GameObject，进入 Play Mode。点击界面按钮，观察计数和背景色变化。

样例使用 `Document.LoadMarkup` 加载内联 RML/RCSS，在 `Loaded` 中查询元素和注册点击，在 `OnDisable` 中释放订阅并卸载文档。重新加载后重新查询，避免持有失效元素。

已有 MainCamera 时复用；没有时创建相机，并在销毁样例时清理。一次只运行一个 URUI 文档。使用环境为 Unity 6000.3.22f1 / macOS ARM64 / Built-in / Metal，Active Input Handling 设为 Input Manager (Old) 或 Both。

完整步骤与自行创建文档的示例见[快速开始](../../Documentation~/QUICK_START_ZH.md)。
