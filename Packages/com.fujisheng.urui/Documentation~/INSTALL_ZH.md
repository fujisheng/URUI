# 安装指南：版本与入口

固定预览 `v0.1.0-preview.1` 使用兼容 Host / Hello Rml。当前候选使用 `URUI.Document` / Hello URUI，安装和新接口请读 [QUICK_START_ZH.md](QUICK_START_ZH.md)。以下是兼容 Host 的使用与离线源码说明，不能与新 Document 同时创建 context。

# 安装与源码位置

把整个 `com.fujisheng.urui` 文件夹放进你的 Unity 工程的 `Packages/` 目录；也可在 Package Manager 中选择 Add package from disk，打开 package.json。不要同时保留旧 Assets/RmlUnity 安装，避免同名程序集和 GUID 冲突。

当前验证范围是 Unity 6000.3.22f1、macOS ARM64、Built-in/Metal。没有承诺 Windows、Android、Linux 或 SRP 支持。输入后端仍使用引擎自带的旧 Input API。

最小示例可从 Package Manager 的 Samples 导入 Hello Rml，或者调用 Tools > URUI > Create Minimal UI Scene。用户继续使用 RmlUnityHost 的 SetText、SetStyle、SetTranslateScale2D、AcquireElementReference 与事件等托管 API。NativeFrame 保持显式可选。

完整 C++ 在 Native~/bridge.cpp 和同目录的实现头文件；Native~/ThirdParty 包含固定版本依赖源码，Native~/build-macos.sh 是离线构建入口。带 ~ 的目录不参与 Unity 资产与 C# 导入，可在文件管理器或代码编辑器里直接打开。Tests~ 也不参与默认程序集。Runtime 中没有 Match3、uGUI 对照和 CPU 采样器。旧项目和研究代码仍保存在原位置，没有混入当前包。

从全新目录重建得到的 dylib 与发布库逐字节一致。详见 SOURCE_AUDIT.md 与 VALIDATION.md。

此包已更名 URUI；兼容 namespace、程序集、native ABI 与 GUID 保留。不要与旧包同时安装。Git 安装须固定预览 tag，详见仓库 README。
