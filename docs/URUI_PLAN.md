# URUI 实施计划

目标和设计见 [URUI_DESIGN.md](URUI_DESIGN.md)。本会话直接实施用户已授权的预览、改名、整理和友好 API。

- [x] 读取实时主干、包版本、tags/releases、工作流和既有证据；读取其他真实 C# 仓库与官方 UITK 资料。
- [x] 仓库重命名 URUI 并回读，独立副本与远端整树校验。
- [x] 将 UPM 身份、路径、编辑器显示、安装文档改为 URUI；保留兼容符号、GUID 与 native ABI。Editor 导入/旧调用/shader/渲染 smoke 通过后提交为预览基线。
- [x] 发布一次 prerelease `v0.1.0-preview.1`；回读 tag 指向、Release 状态和安装路径。CI 不声明通过。
- [x] 新建 `Runtime/Managed/URUI.Runtime.asmdef`（引用兼容 `RmlUnity.Runtime`），新增 `Document.cs`、`Element.cs`、`ElementStyle.cs`、事件订阅类型。先写行为断言验证缺少接口，再实现并在 Editor 运行。旧调用编译保留。
- [x] 重写 Hello 示例与菜单面向 Document；整理 README、安装/快速开始/资源生命周期/事件/API/迁移/限制文档，以及公共接口 XML 注释。
- [x] 核对重载后旧节点、同 ID 替换、事件内修改和解绑、无效加载、禁用/恢复、主线程约束和单 context。受影响 Editor 测试通过后在独立分支形成可审查提交，保持已发布 tag 固定。

风险检查：单个原生 context；跨线程；旧元素失效；订阅泄漏与回调重入；资源所有权；旧 asmdef 和序列化迁移。所有命令与结果写入任务证据，不将历史已测平台扩大。
