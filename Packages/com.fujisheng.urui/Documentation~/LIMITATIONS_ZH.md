# 支持矩阵与限制

## 环境

| 项目 | 当前范围 |
| --- | --- |
| Unity | 6000.3.22f1 |
| Unity 运行验证 | macOS ARM64 Editor |
| 渲染 | Built-in Render Pipeline / Metal |
| 相机 | 场景中启用并标记为 MainCamera 的相机 |
| 输入后端 | Input Manager (Old)，或 Active Input Handling 为 Both |
| 文档并发 | 一个活动原生 context，即一个活动 Document |
| 线程 | Unity 主线程 |
| 图片 | 通过 ImageAsset 映射的可读 Texture2D，开启 Read/Write |
| 字体 | 可读取的字体文件路径，包含界面需要的字形 |

原生接口使用 **URUIBridge ABI3**。七份插件均由当前源码构建为 Release / URUI_DEBUG=OFF，并核对架构、ABI3 与相同的 89 个正式导出。

| 目标 | 产物检查 | Unity 运行验证 |
| --- | --- | --- |
| macOS ARM64 | dylib，架构/ABI/导出、原生契约测试 | Editor / Built-in / Metal |
| macOS x86_64 | dylib，架构/ABI/导出 | 未执行 |
| Windows x64 | DLL，架构/ABI/导出与依赖；需要 Windows Universal CRT | 未执行 |
| Android ARM64 / x86_64 | so，架构/ABI/导出、API23+、16 KiB 对齐、静态 C++ 运行库 | 未执行 |
| iOS 设备 ARM64 | 合并静态库，iphoneos SDK 完整链接 | 未执行 |
| WebGL wasm32 | 合并静态库，全部正式导出链接、Node 下 ABI 入口检查 | 未执行 |

插件明细见 PLUGINS.json。iOS 不含模拟器。WebGL 使用 Unity 对应的 Emscripten 与匹配的异常支持；原生 wasm 入口检查不代表 Unity WebGL Player 验证。本轮没有 Player/APK/App 打包或设备测试，跨平台应用仍需在目标运行环境验证。

Linux、URP/HDRP、其他图形后端及其他 Unity 版本不在已验证范围内。

## 界面与交互

当前提供文档根、按 Name/Class 局部查询、节点创建与可重用层级操作、显式销毁、纯文本/属性、类名与行内 RCSS、二维变换、图片映射及 ClickEvent。

- Q/Query 包含自身，按 DOM 深度优先顺序返回首项/快照；不是 CSS 选择器或 UQueryBuilder，没有泛型控件查询。
- 名字与身份分离；移出仍有效，Destroy/结构替换/文档卸载后失效。脱离的树由文档继续持有，长期不再使用需 Destroy。
- Text 是纯文本；读取含结构化子节点的 Text 会抛异常。SetInnerMarkup 需要已挂载节点，使用 RmlUi 容错解析，不提供事务性验证。
- 点击使用实际弱身份和分发时快照的父级通知路径；没有捕获、StopPropagation、指针捕获或完整 UITK 事件协议。
- 样式使用 RmlUi RCSS，不提供完整浏览器/HTML/CSS/JavaScript、已计算样式 getter 或 UITK StyleLength 类型体系。
- 完整键盘导航、文本输入、IME、表单交互和无障碍尚未完成验证。
- 没有通用异步资源加载器、远程图片下载、自动字体打包、RML 自动热重载或可视化调试器。
- 没有数据绑定、FUI 依赖或 FUI 适配实现；仅保留可由未来适配器使用的具体节点职责边界。

渲染器的实验路径仍属内部细节，没有面向业务的配置承诺，不构成通用性能优势结论。未来方向见[路线图](ROADMAP_ZH.md)。
