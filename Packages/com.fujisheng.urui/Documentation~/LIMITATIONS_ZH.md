# 支持范围与限制

## 环境与平台

- Unity 6000.3.22f1；当前主要运行环境为 macOS ARM64 / Built-in / Metal。
- 一个活动 Surface/原生上下文可承载多个独立 Document；节点与输入操作限主线程，需要 MainCamera。
- 内置输入使用 Input Manager (Old) / Both；其他输入来源可设置 UseUnityInput=false 并使用 Surface.Input。
- 图片通过 ImageAsset 借用或 ImageResolver 同步按需取得颜色 Texture2D（无需可读），字体仍需提供可读取且包含所需字形的文件。
- 原生 ABI11 / render-plan8，正式 C 导出集合由头文件与二进制核对生成；style4 / effect7 / filter3 / animation1 等协议独立版本。候选与已安装插件必须区分，实际哈希、构建和运行状态见 PLUGINS.json。

| 目标 | 已执行检查 | Unity 运行范围 |
| --- | --- | --- |
| macOS ARM64 | 架构、导出、协议版本、真实原生契约 | 本轮主工程 Metal Editor、独立 Gamma/Linear 运行套件及同插件哈希的 ARM64 IL2CPP Player；浏览器只验证共享作者文件的约定范围 |
| macOS x86_64 | 架构与全部正式导出 | 尚未运行 |
| Windows x64 | DLL 架构与全部正式导出，使用 Windows Universal CRT | 尚未运行 |
| Android ARM64 / x86_64 | ELF 架构、全部正式导出、API23+、16 KiB 对齐、静态 C++ 运行库 | 尚未运行或设备测试 |
| iOS 设备 ARM64 | iphoneos SDK 编译、静态归档架构、正式导出与内嵌依赖核对 | 尚未运行或设备测试 |
| WebGL wasm32 | 当前插件 Wasm 对象、全部176正式导出；本轮为格式、导出与静态依赖验证，历史完整链接与多文档 Node 冒烟不作为本轮设备验收 | 当前插件尚未运行 Unity WebGL Player |

先前 SRP 图片提交生命周期的定向结果不代表完整 SRP 兼容，也不替代本轮控件验收。Linux、iOS 模拟器、HDRP、其他 Unity 版本及图形后端仍未验证；非 macOS ARM64 目标尚无本轮真实设备/Player 运行证据；每份候选编译/链接状态以清单为准。

macOS Player 选择 ARM64 或 x64 单架构；目前随包分开提供这两个架构的插件，没有 Universal 插件。已打开的 Editor 需要重启后才能载入更新的原生动态库。

## 元素和控件

提供文档拥有的类型化节点、自定义控件工厂、内容容器、泛型查询、常用强类型 CSS、布局/坐标读取及普通控件。支持 Label、Image、Button、Toggle、RadioButton、Slider、IntegerSlider、TextField、IntegerField、FloatField、Dropdown、ScrollView、Foldout、ProgressBar 和固定行高 ListView。

- HTML/CSS 使用标准语法及受支持子集；严格作者规则不允许隐式闭合/修复。标准元素有基础语义，游戏自定义元素需通过 urui-* 与配套 CSS 明确布局。支持取值以 HtmlCssSupport.json 为准。
- 这是 URUI 自己的 C# 接口，没有 Unity UIElements 源码/二进制兼容、UXML、UI Builder 或浏览器 JavaScript 环境。
- 保留 Document 所有权：通过 Create<T>() 创建，不支持文档外无参构造或跨文档移动。
- Parent 保持真实 DOM 语义；内容区操作与真实 Hierarchy 分开。不要绕过复合控件管理其内部节点。
- 查询返回/执行有序快照，泛型筛选包含派生类；没有完整 CSS 选择器查询或无分配保证。
- ResolvedStyle 的任意属性可能保留相对单位；真实像素尺寸使用 Layout/ContentRect/WorldBound。布局读取不强制刷新整个文档。
- Style 强类型属性只覆盖明确映射的 CSS；复杂表达式继续使用字符串接口。
- ListView 支持固定行高、单选与键盘选择/确认；数据原地修改需 RefreshItems，业务订阅必须在 UnbindItem 中释放。不支持多选、拖动重排和动态行高。
- IntegerSlider 使用原生 float 范围控件，不保证完整 int32 范围的精确刻度；IntegerField 的键盘步进覆盖 int32 并在边界饱和。
- Core 不内置业务数据绑定；独立 FUI 适配包负责窗口和绑定。没有通用异步下载、HTML 自动热重载或浏览器 JavaScript 环境。

## 输入与事件

支持同步事件传播、指针捕获与取消、独立触摸编号、键盘/修饰键、焦点与 Tab 导航、表单值事件、Unicode 文本、剪贴板和组合串接口。事件按具体类型注册；默认行为取消只对 URUI 可中断事件生效。布局/挂载事件不冒泡；不提供事件池和完整的其他 UI 系统事件协议。

自动化验证覆盖组合预览/提交/取消、Unicode 选区、焦点切换与外部赋值、双滑块独立拖动、禁用子树清理、回调内重载和虚拟行重绑。注入这些真实接口不等于人工操作系统输入法或设备验收。

真实 macOS 系统 IME 尚需人工验收。Android/iOS 的系统软键盘尚未接入默认轮询适配器；应用可通过 Surface.Input 接入平台键盘，设备运行仍待验证。无障碍、手柄导航和复杂编辑器控件不在本次交付范围。

## 调试与渲染

URUI_DEBUG 默认关闭，包括 Editor 和 Development Build。诊断版原生插件与托管宏必须同时匹配；线程、身份、生命周期与 GPU 资源安全校验始终保留。实验渲染路径仍是内部实现，不构成一般性能优势承诺，历史研究仍暂停。

本轮默认控件的相同输入性能对照中，macOS ARM64 / Metal / Gamma 的 Release IL2CPP Player 八项同步/提交 P95 门槛通过，图片对照一致、RT 数量相同。Editor PlayMode 的 1000 个图片按钮连续切换焦点场景仍有同步长尾回归：P95 7.3620→9.5225ms，未通过该项内部门槛；其余七项通过。两类结果分别保留在 `artifacts/urui-default-controls-20261110/performance/report.md`，没有将 Player 结果作为 Editor 无回归的证明，也没有零 GC 或可比 GPU 耗时结论。

图片借用的资源生命周期、颜色/采样约束及 active mipmap limit/纹理流送限制见 [生命周期与资源](LIFECYCLE_ZH.md)。借用省去图片的原生 RGBA 和额外 Unity 绘制纹理；仍存在必要的源 GPU、字体等生成纹理及驱动资源，不承诺固定减半或全链路零拷贝。
