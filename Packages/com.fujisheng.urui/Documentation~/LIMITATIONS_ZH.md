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

下列原生插件已编译并随包提供。编译、符号和链接检查与 Unity 应用运行验证是不同阶段。

| 平台 | 架构 / 产物 | 当前验证 |
| --- | --- | --- |
| macOS | ARM64 `.dylib` | 原生构建、契约测试与 Unity Editor 功能验证 |
| macOS | x86_64 `.dylib` | 编译、架构与符号检查；未运行验证 |
| Android | ARM64、x86_64 `.so`，API 23+、16 KiB 对齐 | 编译与 ELF/依赖/符号检查；未运行验证 |
| Windows | x86_64 `.dll` | PE32+ 编译与导出检查；未运行验证 |
| iOS | 设备 ARM64 `.a`，iOS 13+ | 编译与设备架构完整链接检查；未运行验证，不含模拟器 |
| WebGL | wasm32 `.a` | 编译与完整 Wasm 链接检查；未在 Unity Player 中运行验证 |

WebGL 库使用 Emscripten C++ 异常支持（`-fexceptions`）；链接配置须匹配，独立链接检查启用了 `-sDISABLE_EXCEPTION_CATCHING=0`。包不自动修改应用的 Player 构建设置。iOS/WebGL 静态库通过 `__Internal` 调用，字体文件需由应用配置和交付。

没有执行这些平台的 Player/APK/App 构建或设备运行。Linux、URP/HDRP、其他图形后端和其他 Unity 版本不在已验证范围内。

## 界面与交互

当前公开接口提供 RML 文档加载、按唯一 ID 查询、普通文本与属性更新、内部结构替换、元素追加/删除、RCSS 行内样式、二维位移/缩放、图片映射和点击订阅。

- RML/RCSS 使用 RmlUi 的语法与布局规则；不提供完整浏览器、HTML/CSS/JavaScript 兼容。
- `Q(id)` 只按 ID 查询，不支持 CSS 选择器、泛型控件查询或 Unity UI Toolkit 控件体系。
- ID 必须唯一；节点替换和文档重载后需要重新查询，旧元素与订阅不会复用。
- 点击通知以最近带 ID 的目标为准；公开 API 没有捕获、冒泡和完整指针事件协议。
- 完整键盘导航、文本输入、IME、表单交互与无障碍能力尚未完成接入和验证。
- 没有通用资源加载器、远程图片下载、自动字体打包或已计算样式读取接口。
- 当前不提供自动热重载或可视化 UI 调试器。

渲染器中的实验实现属于内部细节，没有面向业务的配置承诺。当前功能范围也不构成对其他 Unity UI 方案的通用性能优势结论。

未来方向见[路线图](ROADMAP_ZH.md)。
