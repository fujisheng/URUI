# Changelog

## 0.2.0-preview.1

- 补全控件基础样式、布尔选择按键状态/单选导航、文本/数值只读与延后提交、数值步进、滑块方向/反向及进度范围。
- 补全滚动客户区、ScrollTo、列表键盘选择/确认和绑定清理边界；修复回调内重载、反向步长和多指焦点问题。
- 新增文档工厂、可继承 Element、有类型查询、内容容器、样式与几何读取。
- 新增同步事件传播、指针捕获/取消、多指状态、焦点导航、键盘和文本组合接口。
- 新增常用控件、统一值变化协议、固定行高 ListView 与 Runtime Controls 样例。
- ClickEvent 升级为 EventBase 派生类；文档回调重载在原生调用返回后执行。
- 保持 ABI3 布局，新增元素协议版本 1，更新七平台同源插件；各平台实际验证范围见支持矩阵。

## Unreleased

- ImageAsset 直接借用现有 Unity Texture2D，支持不可读颜色纹理及同图多路径共享；原生只保留元数据，取消图片 RGBA 与额外 GPU 纹理副本。字体等生成纹理继续由 URUI 拥有。
- 添加 texture protocol v1、WebGL32句柄转换与七平台同步插件；渲染正确处理借用图片 UV/预乘/颜色，不修改调用者 sampler。完整常驻 mip0 约束及生命周期见资源文档。


- Fix touch scrolling being cancelled by a bubbled keyboard-focus Blur event during pointer down.
- Make Game Flow the recommended package sample: six external RML pages, shared RCSS, original bundled resources, cancellable mock services, preferences, inventory and rankings.
- Add standard Unity Sample import/open menu and a continuous Editor gameplay-UI recording in the package README.

- Provide `URUI.Documents.Document`, `Element`, `ElementStyle`, `ImageAsset` and `ClickEvent` in `URUI.Runtime`.
- Support RML loading, ID queries, text and structure updates, RCSS styles, 2D transforms, image mappings and disposable click subscriptions.
- Define main-thread access, one active document, element invalidation and resource ownership.
- Separate hosting, input, rendering and native interop into internal responsibility namespaces, with the URUIBridge native library and `urui_*` exports.
- Include Hello URUI, the minimal scene menu, API guides, the support matrix and the roadmap for AI-assisted Unity UI development.
- Bundle native plugins for Android ARM64/x86_64, Windows x86_64, macOS ARM64/x86_64, iOS device ARM64 and WebGL wasm32; distinguish compilation/link checks from Unity runtime validation.
