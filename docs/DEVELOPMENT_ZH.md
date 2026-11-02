# 开发与原生构建

URUI 是 `Packages/com.fujisheng.urui` 下的 Unity Package。产品使用方式见[安装指南](../Packages/com.fujisheng.urui/Documentation~/INSTALL_ZH.md)，实现边界见[架构](ARCHITECTURE_ZH.md)。

## Unity 开发环境

使用 Unity **6000.3.22f1 / macOS ARM64 / Built-in / Metal**。在独立测试项目中从磁盘安装包，启用旧 Input Manager，准备 MainCamera，并显式导入 Hello URUI 样例。

- 运行时业务引用 `URUI.Runtime`，编辑器程序集为 `URUI.Editor`。
- 公共 API 按 `URUI.Documents`、`URUI.Elements`、`URUI.Controls`、`URUI.Events`、`URUI.Resources` 分类；内部实现按宿主、输入、渲染和互操作分区，见[代码组织](CODE_ORGANIZATION_ZH.md)。
- 文档、样例和菜单使用相同公开 API；修改生命周期或事件时同步检查这些入口。
- shader 标识为 `URUI/Premultiplied` 与 `URUI/ParameterTable`，随包的资源需要保留。

功能检查使用 `Tests~/Editor` 的独立夹具，导入步骤与命令见[Editor 检查说明](../Packages/com.fujisheng.urui/Tests~/Editor/README.md)。测试目录不自动参与项目编译，检查结果只覆盖实际运行的环境与场景。

## 代码风格与注释

C# 使用四空格缩进、Allman 花括号和块级命名空间。公开类型与成员采用 PascalCase，字段和局部变量采用 camelCase。简单属性保持紧凑，方法、条件分支和资源清理展开书写；较长的参数列表按行排列，空行用于区分状态、资源与操作阶段。

公开 API 使用中文 XML 注释，说明参数、返回值、主线程约束、资源所有权和失效条件。内部注释解释生命周期顺序、重入保护、失败回滚和原生互操作约束，避免逐行复述代码。`.editorconfig` 保存可由编辑器检查的格式规则。

这些约定参考 FUI 的 [BindableProperty](https://github.com/fujisheng/FUI/blob/23391f715535bbfada132944d1a94da51403fa06/Packages/com.fujisheng.fui/Runtime/Core/Bindable/BindableProperty.cs)、[ViewInstance.Factory](https://github.com/fujisheng/FUI/blob/23391f715535bbfada132944d1a94da51403fa06/Packages/com.fujisheng.fui/Runtime/Core/Infrastructure/ViewInstance.Factory.cs) 和 [Navigator.Operations](https://github.com/fujisheng/FUI/blob/23391f715535bbfada132944d1a94da51403fa06/Packages/com.fujisheng.fui/Runtime/Navigation/Navigator.Operations.cs) 中的组织与注释方式。

## 正式代码、诊断与测试

`URUI_DEBUG` 是统一的显式诊断开关，**默认关闭**。在 Unity Editor 和 Development Build 中也不会自动打开；`UNITY_EDITOR`、`DEVELOPMENT_BUILD` 不代替此开关。包内分发的七个平台原生插件均使用 `URUI_DEBUG=OFF`。

- 正式运行时保留加载、布局、绘制、事件、资源管理及其必要校验。
- 诊断实现与正式实现按文件分开，采样、计时、统计与探针调用由 `URUI_DEBUG` 条件编译；关闭时不进入运行时代码。
- 测试夹具放在 `Tests~/`，样例放在 `Samples~/`，由使用者显式导入。普通功能回归不依赖开启诊断。
- 参数、线程、生命周期和 shader 构建正确性检查始终保留。错误日志与用户回调异常报告也不属于可关闭的性能诊断。

需要诊断时，在独立工程中同时完成两侧配置：

1. 为目标平台的 **Scripting Define Symbols** 添加 `URUI_DEBUG`，使托管诊断代码参与编译。
2. 在独立输出目录构建匹配的原生插件，CMake 配置使用 `-DURUI_DEBUG=ON`。脚本方式如下：

```sh
URUI_DEBUG=ON bash Packages/com.fujisheng.urui/Native~/build-macos.sh /tmp/urui-native-debug
```

将调试插件放入独立测试工程的嵌入包后再运行诊断。托管符号与原生构建开关必须匹配；仅添加托管符号不能让默认插件提供诊断导出。结束诊断后删除 Scripting Define Symbols 中的 `URUI_DEBUG`，并恢复默认关闭诊断的插件。调试产物不替换包内正式分发插件。

`PublicApiContract` 在默认配置下检查运行时程序集内不存在诊断类型。Native 的 `debug_gate` 契约测试分别验证关闭和开启配置；普通 API 与交互回归仍按其功能执行，不以诊断开关跳过。

## macOS ARM64 原生构建

`Native~/` 包含桥接实现、RmlUi **6.3** 与 FreeType **2.13.3** 的固定源码，构建不需要在线获取第三方依赖。需要 Apple 命令行构建工具与 CMake 3.21 或更高版本。

从仓库根目录运行，建议使用包目录之外的独立输出目录：

```sh
bash Packages/com.fujisheng.urui/Native~/build-macos.sh /tmp/urui-native-build
```

脚本限制为 macOS ARM64，配置 Release 构建并运行原生契约测试。原生插件名为 `libURUIBridge.dylib`，导出 `urui_*` 接口。构建完成后，按脚本提示将插件安装到测试项目的嵌入包，再执行 Unity 功能检查；仅构建成功不能证明 Unity 侧运行正确。

URUIBridge、托管声明和插件必须来自同一实现版本。更改 ABI、资源生命周期或绘制数据时，同时核对托管边界与原生测试。其他目标的构建使用下述脚本；运行验证须单独完成。

## 许可

项目与第三方许可见包内 `LICENSE.md`、`Third Party Notices.md` 和 `Documentation~/Licenses/`。修改框架时保留第三方版权和许可文件。

## 多平台插件

```sh
bash Packages/com.fujisheng.urui/Native~/build-platform.sh macos-x86_64 /tmp/urui-macos-x64
bash Packages/com.fujisheng.urui/Native~/build-platform.sh android-arm64 /tmp/urui-android-arm64
bash Packages/com.fujisheng.urui/Native~/build-platform.sh android-x86_64 /tmp/urui-android-x64
bash Packages/com.fujisheng.urui/Native~/build-platform.sh windows-x86_64 /tmp/urui-windows-x64
bash Packages/com.fujisheng.urui/Native~/build-platform.sh ios-arm64 /tmp/urui-ios-device
bash Packages/com.fujisheng.urui/Native~/build-platform.sh webgl /tmp/urui-webgl
```

各目标使用独立输出目录。脚本通过平台标记拒绝复用其他目标的 CMake 缓存，并使用 `check-plugin.py` 检查产物的真实格式和架构。

- CMake 不在 PATH 时设置 `URUI_CMAKE`。
- Android：设置 `ANDROID_NDK_ROOT`；构建使用静态 C++ 运行库、API 23+ 和 16 KiB 页面对齐。
- Windows：设置 `URUI_ZIG` 指向 Zig 可执行文件，交叉目标为 x86_64-windows-gnu；工具链和缓存不随包分发。
- iOS：需要已由使用者阅读并接受许可的 Xcode 与 iphoneos SDK，必要时设置 `DEVELOPER_DIR`。当前库只包含设备 ARM64，不含模拟器。
- WebGL：设置 `EMSCRIPTEN` 指向 Unity 对应的 Emscripten 工具目录。库启用 `-fexceptions`，链接需保留匹配的异常支持；独立链接检查使用 `-sDISABLE_EXCEPTION_CATCHING=0`。Unity Player 链接和运行仍需应用单独验证。

静态 iOS/WebGL 库合并了桥接实现、RmlUi 与 FreeType 对象，托管声明使用 `__Internal`。动态平台使用 `URUIBridge`。平台与验证范围见[支持矩阵](../Packages/com.fujisheng.urui/Documentation~/LIMITATIONS_ZH.md)。
