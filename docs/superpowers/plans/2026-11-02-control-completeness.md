# URUI 现有控件补全计划

> REQUIRED SUB-SKILL: superpowers:executing-plans。沿用本聊天已批准的运行时控件方案，由主代理执行，不提交或推送。

**Goal:** 补齐已加入控件的基本交互与独立使用能力，不以类型存在或旧冒烟测试通过作为完整性的判据。

**Architecture:** 保持 Document 工厂、稳定 token、原生表单载体和 ABI3。复用 RmlUi 布局/编辑；托管层补全键盘、提交、选择和生命周期语义。基础 RCSS 在业务样式前加载，业务样式可以覆盖。新增名称只使用 URUI 自身术语。

**Spec:** `docs/superpowers/specs/2026-11-01-element-runtime-design.md`。本轮是原方案中“常用控件可用”的补全；上一轮完成记录仅对应其基础范围。

## 验收与实施

- [x] 增加独立真实 Unity 组件行为夹具，分别记录所有失败；旧代码上先复现。
- [x] Button、Toggle、RadioButton：键盘按下/释放、组内导航、禁用/取消、回调重载后停止后续通知。
- [x] BaseField、TextField、IntegerField、FloatField：公共文本输入能力、只读（含组合串）、延后提交、提交通知、无效数值恢复与键盘步进；保留程序赋值和静默写入契约。
- [x] Slider/IntegerSlider、ProgressBar、Foldout：方向/反向、步长变更生效、范围变化、统一值通知和折叠键盘操作。
- [x] ScrollView、ListView：键盘滚动/选择/提交、ScrollTo、数据缩短/取消选择、行解绑使用对应绑定的清理委托、焦点及可见性。
- [x] Label/Image/原生表单与组合控件的基础 RCSS：默认可见的尺寸、焦点/禁用/选中状态、滑块、滚动条和下拉面板；业务样式覆盖有效。
- [x] 综合示例展示全部现有控件和上述交互，使用包内基础样式，不能靠示例私有皮肤掩盖缺口。
- [x] 真实 Editor 和 ARM64 IL2CPP Player、原有 API/场景回归、截图检查、默认/DEBUG 编译、命名/.meta/差异审查；更新组件能力表与证据。

## 审查重点

1. 单次按键只提交/变更一次，组合输入与只读不能绕过提交规则。
2. 不完整数字、非有限值和边界值不抛出业务输入异常，不破坏已提交值。
3. 子控件正在编辑时，列表/滚动容器不截获其键盘操作。
4. 回调重载、行重绑与回调委托替换后不执行旧业务逻辑。
5. 默认样式不覆盖业务显式布局；新增客户区查询需要更新匹配的原生插件，ABI3布局保持不变，七平台同源。

真实系统 IME、移动默认软键盘和其他设备仍按现有支持矩阵陈述，不用自动注入冒充设备证据。

## 验收记录（2026-11-02）

Editor 与真实 ARM64 IL2CPP Player 均通过23组组件场景（40断言）和原有52断言；API、ElementTree、Usability、Playground回归通过。七平台同源插件120导出、iOS/WebGL完整链接与Node原生Wasm检查通过；Native节点7项、交互12项和真实帧生命周期/线程契约通过。默认/DEBUG托管编译、命名、.meta/GUID、插件和Player插件哈希、差异检查通过。主代理完成审查，未委派审查，未提交或推送。

基础样式的23组验收包括业务覆盖和无head文档，截图展示全部15类控件。原RML进度上下限另有失败复现及修正。旧回归工程残留样例重复、以及旧Tree夹具未建Evidence目录均明确作为验证环境/夹具问题纠正，已在干净工程重新通过。

证据：开发工程根 Artifacts/ElementRuntime/Controls/validation.json；组件能力和已知边界见包内API与支持矩阵。仍保留固定行高/单选、原生float滑块精度及既有平台边界，不承诺参考框架完整API兼容。
