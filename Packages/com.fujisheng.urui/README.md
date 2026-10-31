# URUI · UnityRmlUi

当前工作树是 **未发布托管 API 候选**。固定预览为 v0.1.0-preview.1；其包版本元数据仍保留，候选不构成第二次发布。使用候选时固定对应分支/提交，不要把候选接口当作预览 tag 已提供。

从候选 Package Manager 导入 Hello URUI，或运行 `Tools > URUI > Create Minimal UI Scene`。业务接口为 `URUI.Document` → `Q(id)` → `Element`，支持普通文本、属性、RCSS 样式、变换、结构和可释放点击订阅。

- [快速开始](Documentation~/QUICK_START_ZH.md)
- [API](Documentation~/API_ZH.md)
- [事件与解绑](Documentation~/EVENTS_ZH.md)
- [资源和生命周期](Documentation~/LIFECYCLE_ZH.md)
- [限制](Documentation~/LIMITATIONS_ZH.md)
- [风格参考](Documentation~/CODE_STYLE_ZH.md)
- [预览验证](Documentation~/URUI_PREVIEW_VALIDATION.md)
- [原生来源](Documentation~/SOURCE_AUDIT.md)

环境为 Unity 6000.3.22f1 / macOS ARM64 / Built-in Metal。兼容 namespace、程序集、native ABI、shader 标识和原资产 GUID 有意保留；不要同时安装旧 com.fujisheng.rmlunity。第三方许可证保持原样。三消仅存档，特效/粒子/动画样本随旧研究暂停。
