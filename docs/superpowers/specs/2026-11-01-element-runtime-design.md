# URUI 类型化控件与交互底座

用户已在本聊天批准方案并要求实施。工作在现有 Source checkout 的功能分支进行，不提交、推送或发布。

## 目标与范围

在 RmlUi 6.3 / Unity 6000.3.22f1 上提供便于编写 Unity 运行时 UI 的操作方式：有类型且可继承的 Element、查询、内容容器、样式/几何、完整事件传播、输入和常用控件。RML/RCSS 仍是布局与外观事实源；不引入 Unity UIElements/uGUI 运行依赖、数据绑定框架或第三方依赖。

## 已选择的方案

选择扩展现有托管节点与原生桥，而非仅追加通用 Element 方法，或完整复制参考框架。复用现有文档所有权、稳定 token、主线程和渲染器；新增控件语义与输入边界。代价是仍由 Document.Create<T>() 创建节点，文档外的无参 new()、跨文档迁移、UXML/UI Builder 和编辑器专用控件不属于此次范围。

## 契约

- Document 拥有所有节点；移出仍有效，Destroy/替换/卸载失效。同一 token 始终返回同一个、类型确定的托管实例。
- Element 可继承，通过 ElementContext 构造；Document 注册 tag/类型工厂。内建 input 的类型在创建/首次包装时确定。
- Add/Insert/Children/Clear 作用于 ContentContainer；Hierarchy 暴露真实 DOM 操作；内部装饰节点不被业务 Clear 销毁。
- 保留非泛型 Query() 有序快照；新增 Q<T>() 与 Query<T>() 构造器。
- Style 是行内值与单位，ResolvedStyle/几何来自原生最近一次布局；不返回伪造的计算样式。不支持的属性/单位明确拒绝。
- 原生 UI 事件同步进入托管。EventBase 保存 Target/CurrentTarget/阶段；支持 trickle/target/bubble、停止传播。默认行为取消按 RmlUi 实际契约实现与说明，不宣称浏览器或其他 UI 框架全兼容。
- 文档卸载/资源销毁在原生回调退出前保持安全；异常不穿越 C ABI；回调增删采用快照。
- 指针命中不依赖元素 id。统一鼠标/触摸/滚轮、捕获、取消和失焦清理；并发手指不能复用同一鼠标状态。
- 键盘、修饰键、焦点、Tab、文本编辑、选区、剪贴板与 IME 形成完整链路。IME 与多指分别验收，未运行的平台必须如实标注。
- BaseField<T> 统一 Value、ChangeEvent<T> 和 SetValueWithoutNotify；复合控件文本更新不替换内部结构。
- 内建控件：Label、Image、Button、Toggle、RadioButton、Slider、IntegerSlider、TextField、IntegerField、FloatField、ScrollView、Dropdown、Foldout、ProgressBar、ListView（固定行高虚拟化与复用）。
- 所有公开 API 使用 PascalCase 与中文 XML 注释；新增 Unity 资产有 .meta，已有 GUID 不变；URUI_DEBUG 默认关闭。
- ABI3 现有布局不变。新增协议采用独立 element-runtime 版本检查；所有分发插件必须与当前源码一致。

## 验收

原生真实库契约测试、托管编译及真实 Unity Editor 场景分别记录。覆盖身份与类型、内容容器、百分比布局读取、查询、传播中断/重入、捕获/取消、焦点键盘、控件值变化、IME、列表复用与生命周期。已有 API/元素/Playground 回归继续运行。各平台编译/链接与 Player/设备运行分开陈述。
