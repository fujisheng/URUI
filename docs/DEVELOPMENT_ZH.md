# 开发与原生构建

URUI 是 `Packages/com.fujisheng.urui` 下的 Unity Package。产品使用方式见[安装指南](../Packages/com.fujisheng.urui/Documentation~/INSTALL_ZH.md)，实现边界见[架构](ARCHITECTURE_ZH.md)。

## Unity 开发环境

使用 Unity **6000.3.22f1 / macOS ARM64 / Built-in / Metal**。在独立测试项目中从磁盘安装包，启用旧 Input Manager，准备 MainCamera，并显式导入 Hello URUI 样例。

- 运行时业务引用 `URUI.Runtime`，编辑器程序集为 `URUI.Editor`。
- 公共 API 维护在 namespace `URUI`；渲染和互操作实现位于 `URUI.Internal`。
- 文档、样例和菜单使用相同公开 API；修改生命周期或事件时同步检查这些入口。
- shader 标识为 `URUI/Premultiplied` 与 `URUI/ParameterTable`，随包的资源需要保留。

功能检查使用 `Tests~/Editor` 的独立夹具，导入步骤与命令见[Editor 检查说明](../Packages/com.fujisheng.urui/Tests~/Editor/README.md)。测试目录不自动参与项目编译，检查结果只覆盖实际运行的环境与场景。

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
