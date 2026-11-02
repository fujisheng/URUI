# URUI 组件完整性审查（2026-11-02）

结论：当前是具备完整一条业务 UI 流程能力的 preview，并非完整的 Unity UI Toolkit/Web UI 替代品。以下以 c077e63 的原生实现、当前工作树的 managed 运行时和可复核验收为基础。本轮修复 ScrollView 的焦点/边缘触控状态问题；Game Flow 仅使用公开 API，未改变原生 ABI 或七平台插件。

| 范围 | 状态 | 实际边界与证据 |
| --- | --- | --- |
| Document | 已实现，有约束 | 同步加载/重载/卸载、通知、主线程校验；一个活动文档。Document.Loading.cs、Document.Lifecycle.cs。 |
| Element 与所有权 | 已实现 | 规范化身份、有类型工厂、查询快照、真实 DOM/内容容器、脱离/重挂载/销毁、世代失效；跨文档移动不支持。Document.Elements.cs、Element.Hierarchy.cs。 |
| 常用控件 | 已实现 | Label、Image、Button、Toggle、RadioButton、Slider、IntegerSlider、TextField、IntegerField、FloatField、Dropdown、ScrollView、Foldout、ProgressBar、ListView；不是其他 UI 系统的接口兼容实现。ControlFactories.cs。 |
| 列表 | 部分实现，明确范围 | 固定行高虚拟化、单选、键盘、绑定/解绑；没有动态行高、多选、拖动重排、TreeView。ListView.cs。 |
| 输入、焦点、事件 | 已实现，有缺口 | 同步传播/捕获/默认取消、Tab/按键、独立指针、剪贴板、Unicode/组合串注入；真实系统 IME 仍需人工验收，移动默认软键盘、手柄和无障碍缺失。Document.Input.cs、Element.Events.cs。 |
| 生命周期清理 | 已实现 | 旧包装和订阅失效、脱离节点回收、回调内重载延后、调用者纹理/TextAsset 不由文档销毁；一个文档限制仍在。Document.Events.cs、Document.Loading.cs。 |
| RML/RCSS 资源 | 部分实现 | 原生能解析外部 RCSS，但公开 Load 没有 base URL/资源提供者，native 固定 demo.rml。Game Flow 使用有限资源适配器；不等于通用 Resources/Addressables/异步下载接口。 |
| 字体与图片 | 部分实现 | 一个可读字体文件、显式可读 Texture2D 映射；缺少自动打包、字体回退列表、异步解析。Game Flow 打包 OFL 字体并声明真实 family。 |
| 样式与布局 | 已实现子集 | 常用强类型 RCSS、原始属性接口、真实布局与坐标/滚动视口；完整浏览器 CSS、任意选择器和无分配查询不在支持范围。 |
| 裁剪与高级效果 | 部分实现/缺失 | 矩形 scissor、变换、普通实色边框/纹理可用；clip mask、layer/compositing、filter 和 shader decorator 默认 unsupported/no-op。圆角溢出裁剪、渐变装饰和 blur 均有原生失败证据。bridge.cpp:149–210。 |
| Unity 集成 | 部分实现 | Built-in、MainCamera、Old/Both 输入，或用户自接 Document.Input；URP/HDRP、多个活动文档和其他图形后端没有本轮支持承诺。 |
| 平台 | 部分验证 | 七份同源 ABI3/元素协议1插件；Unity 实际运行证据限 Unity6000.3.22f1/macOS ARM64 Editor 与 ARM64 IL2CPP/Built-in/Metal。其他平台编译/链接不等于设备运行通过。 |

## 复核的具体问题

1. `Native~/element_events.inl:243` 的捕获释放事件使用最后一份全局输入。指针11已捕获、指针22随后输入，再释放11，会向正确元素发送 PointerCaptureOut，但事件携带22的编号和坐标。会影响依赖该数据的多指业务清理；本样例没有并行多指捕获业务，不依赖错误事件载荷。本轮未改原生以避免七平台插件失配。
2. `Runtime/Elements/ElementQuery.cs:50` 的 Fluent `Query<T>().Children()` 直接比较 Parent 与 root；组合控件的业务子节点实际位于 ContentContainer，可能漏查或取到内部节点。`Element.Children()` 的重定向正常；样例不使用有问题的 Fluent 操作。
3. 外部 RCSS 缺失不会使原生 LoadDocumentFromMemory 失败，可能得到未样式化文档。RmlUi JoinPath 把单个 POSIX 前导斜杠解释成资源根，不能把普通绝对路径直接塞入 href。样例先验证资产并以 RmlUi 资源根前缀保留运行时绝对链接路径；验收读取实际布局和样式证明加载成功。
4. native urui_create 未把 LoadFontFace 失败转成失败返回；已存在但无效的字体可能得到缺字文档。样例分发已知字体；通用错误处理仍是待补能力。
5. 圆角 overflow、渐变 decorator、filter:blur 的原生检查标记 unsupported；加载成功不能作为这些效果可用的证据。**滚动区域上方存在 transform 祖先时，普通矩形裁剪也会走缺失的 clip-mask 路径，真实截图出现列表行越过视口。** 这是组件的实际功能缺口，不是仅缺少测试。样例以 opacity 淡入和局部 relative top 位移避免它，且用视口外像素回归确认恢复矩形 scissor；没有宣称修复组件的变换裁剪。

6. 本轮触控验收复现并修复 `Runtime/Controls/ScrollView.cs` 的 managed 问题：pointer down 导致子项焦点 Blur 冒泡时，不能据此清掉刚开始的拖动；未越过4px阈值就离开视口的未捕获手势必须清理，否则下一根不同编号手指无法开始拖动。Editor 先验证失败，再验证修复通过。取消/丢失捕获仍清理，视口内子项之间移动不清理。本修复无需原生重编译。

## 验证边界

既有 Artifacts/ElementRuntime/Controls/validation.json 记录 40 项控件和 52 项运行时断言及 API/树/场景回归。本轮只执行窄范围 native 复核、Game Flow 模型/资源检查及短 Editor 验收；不重新宣称 Player、Android、跨平台设备或性能通过。Game Flow 的真实验收结果与截图位于工作区 Artifacts/GameFlow；README 解释 mock 服务、资源加载和运行方式。

## Current verified design-first result (2026-11-02)

The user approved Home v1 and Inventory v2 before asset extraction and reconstruction. All six complete design masters preceded their screen implementation. The importable sample now contains 40 independent production PNGs, real external RML/RCSS, live typed controls and explicit mock services. No whole UI master is used as a runtime image.

Editor: 94 assertions PASS. Model: 18 PASS. Resource contracts: 40 PNGs, hashes, import metadata and every authored image reference PASS. Actual 1280x720 and680x900 screenshots were inspected. Geometry snapshots and master-vs-Editor comparisons are local in Artifacts/GameFlow/Editor. Item and shared action-image deletion/recovery are exercised. The shared-image validation review finding was reproduced RED, then fixed GREEN. No other final review blocker remained.

The bounded artwork helper uses public Element/Image/style/geometry APIs for9-slice panel and3-slice action composition. Explicit pixel dimensions avoid replaced-image intrinsic-size overflow. This does not add a general native nine-slice API or fix advanced clip-mask/layer/filter support. Native source and all seven plugins remain unchanged relative to HEAD. The earlier managed ScrollView focus/edge touch fixes remain covered.

Reconstruction is not pixel-identical: Lato replaces generated lettering; real editable form/dropdown controls and some secondary states differ from the concept. Transform clipping, native multi-pointer capture payload, composite fluent Children query, general resource providers/font fallback, device/IME/mobile keyboard/accessibility gaps documented above remain. No new Player/device/performance claims and no publication.

Scene: Assets/Samples/URUI/0.2.0-preview.1/Game Flow/GameFlow.unity. Source: Source/Packages/com.fujisheng.urui/Samples~/Game Flow. Evidence: Artifacts/GameFlow/verification.json and Editor/game-flow.json. Design/mask/crop lineage: FUI-CLI/GameFlowDesign. Source/docs/GAME_FLOW_ASSET_WORKFLOW_ZH.md records the actual URUI adaptation and deviations; no claim of full FUI Web-to-UGUI execution or separate user approval for each bbox. Screenshots remain local.

Package-entry correction: Game Flow is the first recommended package sample. Unity Sample.Import actually produced Assets/Samples/URUI/0.2.0-preview.1/Game Flow/GameFlow.unity; all62 non-meta sample files matched and Editor94 assertions passed there. Main package README/Quick Start/install docs and Tools > URUI > Open Game Flow Sample use the package entry. Old generated development copy was archived outside Assets; no second source is maintained.
