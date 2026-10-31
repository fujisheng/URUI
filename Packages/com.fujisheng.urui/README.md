# URUI · UnityRmlUi

版本 **0.1.0-preview.1**。Unity RmlUi 托管接入包，已验证 Unity 6000.3.22f1 / macOS ARM64 / Built-in Metal。

从 Package Manager 导入 Hello Rml，或使用 `Tools > URUI > Create Minimal UI Scene`。

兼容 API、程序集名、脚本 GUID 与原生 ABI 有意保留。不要与旧 `com.fujisheng.rmlunity` 同时安装。参考仓库 `docs/URUI_MIGRATION.md`；完整使用说明见 [安装](Documentation~/INSTALL_ZH.md)、[已验收基线](Documentation~/VALIDATION.md)、[原生源码审计](Documentation~/SOURCE_AUDIT.md)。

目前一次支持一个 context，依赖 MainCamera、旧 Input Manager 与字体文件，仅带 Mac ARM64 原生库。NativeFrame 和实验路径默认不开启，不宣称其他平台、SRP、CI 或生产就绪。

`Native~` 含完整离线重建源码及第三方许可证；`Samples~`、`Tests~` 为可选内容，三消仅存档，不作为测试运行。
