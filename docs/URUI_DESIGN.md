# URUI 预览与托管 API 设计

基线：`afa24847342f16c64566fe005466aabd6b4c5109`，整树 `a44b70427f944b987658b9a2489358aba6dad5b1`。独立工作副本按此树重建并核对；原 UPM 快照、研究与旧数据只读保留。

## 发布边界与命名

1. 仓库 `fujisheng/URUI`，品牌 **URUI**，全称 **UnityRmlUi**。
2. 包目录与包 ID 为 `com.fujisheng.urui`，首个预览 `0.1.0-preview.1` / tag `v0.1.0-preview.1`。先发布改名后的已验收渲染基线，再做易用 API；不移动或覆盖 tag，不连续发第二个预览。
3. 新用户接口使用 `URUI` namespace 与 `URUI.Runtime` assembly。原 `RmlUnity` namespace、`RmlUnity.Runtime` assembly、脚本/资源 GUID、原生 `libRmlUnityBridge` 与 `rui_*` 作为明确的兼容层保留，避免伪造 ABI 迁移或破坏既有脚本、序列化资产。编辑器显示、文档、安装地址、示例采用 URUI。
4. 上游 RmlUi / FreeType、许可证、来源清单与历史验收记录不重写。NativeFrame 继续显式选择，默认保持安全路径。

## 用户接口

在兼容渲染器上增加小型托管外观，而不替换布局/渲染系统。`URUI.Document` 组件负责加载、就绪通知、卸载与生命周期；`Document.Q(id)` 返回 `Element`，找不到返回 null；`Element` 提供文本、属性、样式和点击订阅。调用限创建文档的 Unity 主线程。

元素采用文档版本 + 显式原生弱引用身份保护：卸载、重载、移除后旧元素失效；同 ID 新对象不能复活旧引用。现有 ABI 只对 transform 支持 token 更新；普通属性更新仍按 ID 寻址，不能证明原 ID 与对象的对应关系。因此唯一 ID、不能混用兼容 Host 改名是接口契约；新接口禁止修改 id。事件限定当前桥的点击通知，不冒充 UITK 的完整传播模型；兼容 PointerInput 保留但不包装成新事件系统。订阅支持明确解绑及 IDisposable，文档重载、禁用和销毁时释放。资源所有权明确，不暴露原生指针。

## 选择依据

保留字符串 ID 接口最兼容但易误用；全面克隆 UITK 控件/事件系统成本与语义差异过大。采用新增托管外观，保留旧接口，优先把加载、查询、更新、解绑和失效行为做清楚。此为本任务实施判断，未声称用户逐个选定签名。

风格证据：

- [FUI BindableProperty.cs](https://github.com/fujisheng/FUI/blob/23391f715535bbfada132944d1a94da51403fa06/Packages/com.fujisheng.fui/Runtime/Core/Bindable/BindableProperty.cs)：namespace、PascalCase 公共成员、camelCase 字段、中文 XML 注释、Dispose 清理。
- [FUI IElement.cs](https://github.com/fujisheng/FUI/blob/23391f715535bbfada132944d1a94da51403fa06/Packages/com.fujisheng.fui/Runtime/Core/Element/IElement.cs)：小接口、只读属性与文档注释。
- [UnityCli UnityCliToolBase.cs](https://github.com/fujisheng/UnityCli/blob/e782e97b0d0f3353a92d362e18b4a986989cccdb/Editor/Core/UnityCliToolBase.cs)：四空格、Allman 花括号、前置校验与明确错误。
- [UIDocument 源码](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/UIElements/Core/GameObjects/UIDocument.cs)、[官方 UIDocument](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.UIDocument.html)、[UQuery](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-UQuery.html)、[回调注册/解绑](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-Events-Handling.html)：参考组件承载文档、显式查询和成对解绑，不复制实现或宣称兼容。

## 验证与范围

改名做包解析、默认 shader 路径、原 GUID、旧 API 编译与 Editor 功能 smoke。API 做就绪/重载/卸载、失效、点击过滤与解绑、禁用恢复、线程与资源释放的短验证。仅 Unity 6000.3.22f1 / macOS ARM64 / Built-in Metal。复用旧原生与 IL2CPP 证据，新增改动不宣称重新做过 Player 验收；不跑三消、Android、长性能矩阵；特效粒子动画样本随研究暂停。
