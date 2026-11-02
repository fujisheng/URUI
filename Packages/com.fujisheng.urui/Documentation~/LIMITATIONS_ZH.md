# 支持范围与限制

## 环境与平台

- Unity 6000.3.22f1；当前主要运行环境为 macOS ARM64 / Built-in / Metal。
- 一个活动 Document，节点与输入操作限主线程；需要 MainCamera。
- 内置输入使用 Input Manager (Old) / Both；其他输入来源可设置 UseUnityInput=false 并使用 Document.Input。
- 图片通过 ImageAsset 直接借用颜色 Texture2D（无需可读），字体仍需提供可读取且包含所需字形的文件。
- 原生 ABI3 的顶点、命令与帧布局保持不变，元素协议版本为 1，图片借用协议版本为 1，增量提供真实滚动视口查询。包内七份 Release / URUI_DEBUG=OFF 插件同源，均检查真实架构及 123 个正式导出；哈希见 PLUGINS.json。

| 目标 | 已执行检查 | Unity 运行范围 |
| --- | --- | --- |
| macOS ARM64 | 架构、导出、协议版本、真实原生契约 | 本次重编后只做 Editor 借用/渲染/生命周期及既有控件/场景回归；历史 ARM64 IL2CPP Player52项对应旧插件 |
| macOS x86_64 | 架构与全部正式导出 | 尚未运行 |
| Windows x64 | DLL 架构与全部正式导出，使用 Windows Universal CRT | 尚未运行 |
| Android ARM64 / x86_64 | ELF 架构、全部正式导出、API23+、16 KiB 对齐、静态 C++ 运行库 | 尚未运行或设备测试 |
| iOS 设备 ARM64 | 真实设备架构、全部正式导出、iphoneos SDK 完整链接 | 尚未运行或设备测试 |
| WebGL wasm32 | Wasm 对象、全部正式导出、完整链接、Node 中原生创建/加载/ABI 与64位借用 ID 经32位 Rml句柄转换检查 | 尚未运行 Unity WebGL Player |

Linux、iOS 模拟器、URP/HDRP、其他 Unity 版本及图形后端不在本次验证范围。

macOS Player 选择 ARM64 或 x64 单架构；目前随包分开提供这两个架构的插件，没有 Universal 插件。已打开的 Editor 需要重启后才能载入更新的原生动态库。

## 元素和控件

提供文档拥有的类型化节点、自定义控件工厂、内容容器、泛型查询、常用强类型 RCSS、布局/坐标读取及普通控件。支持 Label、Image、Button、Toggle、RadioButton、Slider、IntegerSlider、TextField、IntegerField、FloatField、Dropdown、ScrollView、Foldout、ProgressBar 和固定行高 ListView。

- RML/RCSS 是 RmlUi 的规则；普通 div/自定义标签需要明确设置 display/布局，不能假设浏览器或其他 UI 系统的默认样式。
- 这是 URUI 自己的 C# 接口，没有 Unity UIElements 源码/二进制兼容、UXML、UI Builder 或浏览器 JavaScript 环境。
- 保留 Document 所有权：通过 Create<T>() 创建，不支持文档外无参构造、跨文档移动或多个活动文档。
- Parent 保持真实 DOM 语义；内容区操作与真实 Hierarchy 分开。不要绕过复合控件管理其内部节点。
- 查询返回/执行有序快照，泛型筛选包含派生类；没有完整 CSS 选择器查询或无分配保证。
- ResolvedStyle 的任意属性可能保留相对单位；真实像素尺寸使用 Layout/ContentRect/WorldBound。布局读取不强制刷新整个文档。
- Style 强类型属性只覆盖明确映射的 RCSS；复杂表达式继续使用字符串接口。
- ListView 支持固定行高、单选与键盘选择/确认；数据原地修改需 RefreshItems，业务订阅必须在 UnbindItem 中释放。不支持多选、拖动重排和动态行高。
- IntegerSlider 使用原生 float 范围控件，不保证完整 int32 范围的精确刻度；IntegerField 的键盘步进覆盖 int32 并在边界饱和。
- 没有数据绑定、FUI 适配、通用异步资源下载、自动字体打包、RML 热重载或可视化调试器。

## 输入与事件

支持同步事件传播、指针捕获与取消、独立触摸编号、键盘/修饰键、焦点与 Tab 导航、表单值事件、Unicode 文本、剪贴板和组合串接口。事件按具体类型注册；默认行为取消只对 RmlUi 可中断事件生效。布局/挂载事件不冒泡；不提供事件池和完整的其他 UI 系统事件协议。

自动化验证覆盖组合预览/提交/取消、Unicode 选区、焦点切换与外部赋值、双滑块独立拖动、禁用子树清理、回调内重载和虚拟行重绑。注入这些真实接口不等于人工操作系统输入法或设备验收。

真实 macOS 系统 IME 尚需人工验收。Android/iOS 的系统软键盘尚未接入默认轮询适配器；应用可通过 Document.Input 接入平台键盘，设备运行仍待验证。无障碍、手柄导航和复杂编辑器控件不在本次交付范围。

## 调试与渲染

URUI_DEBUG 默认关闭，包括 Editor 和 Development Build。诊断版原生插件与托管宏必须同时匹配；线程、身份、生命周期与 GPU 资源安全校验始终保留。实验渲染路径仍是内部实现，不构成一般性能优势承诺，历史研究仍暂停。

图片借用的资源生命周期、颜色/采样约束及 active mipmap limit/纹理流送限制见 [生命周期与资源](LIFECYCLE_ZH.md)。借用省去图片的原生 RGBA 和额外 Unity 绘制纹理；仍存在必要的源 GPU、字体等生成纹理及驱动资源，不承诺固定减半或全链路零拷贝。
