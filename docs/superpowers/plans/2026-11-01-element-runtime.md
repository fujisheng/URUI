# URUI Element Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** 完成已批准的类型化 Element、输入事件与常用控件方案，并保留可复现验证。

**Architecture:** 复用 Document 所有权与 RmlUi DOM/布局；注册表为 token 创建唯一托管控件。原生桥补充同步事件、输入、样式/几何和值接口，C# 实现查询、组合控件与列表复用。

**Tech Stack:** Unity 6000.3.22f1、C#、C++17、RmlUi 6.3、FreeType 2.13.3、CMake、Python ctypes。

**Spec:** `docs/superpowers/specs/2026-11-01-element-runtime-design.md`

## Global Constraints

- 当前 Source checkout 的功能分支；不提交、推送、发布；不修改历史研究和基线。
- 主线程、单活动 Document、文档所有权、稳定 token、默认 URUI_DEBUG=OFF。
- 保留原 ABI3 布局，新增 element API 独立版本检查；七平台同源插件。
- C# PascalCase、四空格/Allman、中文 XML；Unity .meta 同步。
- 无新增第三方依赖、UIElements/uGUI 或数据绑定。

## Review Focus

- 事件回调中销毁/卸载不能产生原生悬空指针，后续回调不得接收失效节点（任务2）。
- 未命名节点命中及捕获后的越界移动/释放，应用失焦后不能保留按下状态（任务2）。
- 同一 token 从 RML、泛型查询及父子遍历取得的类型和对象身份一致（任务3）。
- 静默设值与用户输入的通知数量正确，组合串取消不修改已提交值（任务4）。
- 列表滚动、重绑、删除数据及文档重载后不投递到旧行/旧订阅（任务5）。

## Task 1: 原生 元素读写、状态与控件协议

**Files:** 新增 `Native~/element_runtime.h`、`Native~/element_runtime.inl`、`Native~/tests/test_element_runtime.py`；修改 `bridge.cpp`、`urui.h`。

**Interfaces:** element API v1；基于 token 的样式字符串读取、几何快照、焦点、表单值读写、真实命中与输入接口。既有元素协议不变。

- [x] 写真实 ctypes 行为测试：百分比计算、行内属性移除、焦点、表单值、无效/跨线程 token；运行确认缺失接口失败。
- [x] 实现原生接口，字符串采用长度探测/复制，几何采用固定布局快照，所有导出捕获异常。
- [x] 构建真实插件并运行既有 ctest、test_elements.py、test_native_frame.py 与新测试，必须全部通过。

## Task 2: 同步事件与输入

**Files:** `Native~/element_events.inl`、`Runtime/Events/*`、`Runtime/Internal/Interop/NativeMethods.Interaction.cs`、`Runtime/Internal/Hosting/RenderHost.Input.cs`、`Runtime/Documents/Document.Events.cs`、`Runtime/Elements/Element.Events.cs` 与验证夹具。

**Interfaces:** EventBase/ClickEvent/Pointer*/Key*/Focus*/ChangeEvent<T>；RegisterCallback<T>(Action<T>, TrickleDown)；CapturePointer/ReleasePointer；原生回调描述固定布局并同步返回停止标志。

- [x] 写传播顺序、中断、异常隔离、删除/卸载、多指/取消、无 id 命中、焦点键盘与文本测试；先观察失败。
- [x] 接入 AOT 安全回调；清理旧点击链路以防双通知；延后危险卸载；实现输入归一化及焦点/文本/IME。
- [x] 原生契约、托管编译、真实 Unity 输入注入验证通过。

## Task 3: 类型化 Element、查询、样式与布局

**Files:** `Runtime/Elements/Element*.cs`、`ElementContext.cs`、`ElementQuery.cs`、`ElementStyle*.cs`、`Element.Geometry.cs`、`Runtime/Documents/Document.Elements.cs`、`Document.Loading.cs`。

**Interfaces:** Document.Create<T>()/RegisterElementType<T>()；ElementContext；Q<T>/Query<T>；ContentContainer/Hierarchy；Style 强类型值；ResolvedStyle/Layout/WorldBound。

- [x] 写 Unity 真实文档测试：RML/C# 类型与身份、组合容器 Clear、泛型查询筛选、样式单位/计算结果和状态继承；确认失败。
- [x] 按注册表首次确定包装类型，在加载完毕初始化控件，保留 Query() 快照；几何由原生快照读取。
- [x] 新夹具和既有 ElementTree/Api/Playground 检查通过；更新公开 API 合同，保留所有权和线程校验。

## Task 4: 常用控件与表单

**Files:** `Runtime/Controls/*`、`Samples~/Runtime Controls/*`、`Tests~/Editor/Fixtures/ElementRuntimeSmoke.cs` 与 Editor 入口。

**Interfaces:** Label/Image/Button；BaseField<T>/INotifyValueChanged<T>；Toggle/RadioButton/Slider/IntegerSlider/TextField/IntegerField/FloatField；ScrollView/Dropdown/Foldout/ProgressBar。

- [x] 写真实控件测试：Value/静默赋值/用户输入只通知一次、范围夹取、单选互斥、键盘激活、IME 提交取消、组合容器；先失败。
- [x] 实现原生表单值适配和托管组合控件；RML 与 C# 创建均可用。
- [x] 运行真实设置页/登录表单验证，保存 JSON/PNG；记录 IME 的自动化和实际平台验证范围。

## Task 5: ListView 与综合验证

**Files:** `Runtime/Controls/ListView.cs`、Runtime Controls、ElementRuntimeSmoke、用户/API/开发文档及插件清单。

**Interfaces:** 固定行高 ItemsSource/MakeItem/BindItem/UnbindItem、RefreshItems/Rebuild、SelectionChanged；行复用受文档所有权管理。

- [x] 写长列表滚动、可见行上限、数据替换/缩短、订阅解绑与重载测试，先失败。
- [x] 实现虚拟化和复用，加入示例与使用说明。
- [x] 构建七平台插件并更新检查/哈希清单；区分架构/ABI/链接与 Unity Player/设备结果。
- [x] 执行全套相关回归、差异审查和公开文档核对；更新进度与剩余限制。

## 完成记录（2026-11-02）

- 全部五阶段已实现；新增产品代码/API/文件使用 URUI 自身命名。未提交、推送或发布。
- 正式插件：CTest 5/5、Element 7/7、ElementRuntime 12/12，真实 native_frame 生命周期/线程契约通过。七平台同源构建、架构和119导出检查通过；iOS/WebGL完整链接、Node原生Wasm检查分别通过。
- Unity 6000.3.22f1 / macOS ARM64 / Built-in / Metal：Editor与ARM64 IL2CPP Player均通过52项断言。另通过Api、ElementTree、Usability、Playground回归；样例键盘保存/音量联动/列表点击通过并检查1100x720截图。
- 主代理按方案和代码审查清单完成审查。已修正同步回调重载边界、动态工厂重入、单元素后续回调、指针子树取消、行绑定版本/解绑、滚动内容宽度和Player插件打包等问题；无已知阻断交付问题。未进行独立子代理审查。
- 置于本次运行验收之外的行为：真实系统IME人工操作、其他平台Unity Player/设备、移动默认软键盘接入。前两项缺少真实环境证据；移动软键盘适配尚未实现，已在支持矩阵明示。保留单文档、固定行高与当前渲染管线边界。
- 汇总证据：开发工程根 Artifacts/ElementRuntime/validation.json；干净示例场景 Assets/URUI/Scenes/RuntimeControls.unity。
