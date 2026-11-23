# 开发与原生构建

使用方式见[Core 文档](../Packages/com.fujisheng.urui/README.md)和 [Effect 文档](../Packages/com.fujisheng.urui.effect/README.md)。修改公开 API 时同步更新专题指南、签名参考和样例，不以历史测试代替当前版本验证。

## Unity 开发

在独立 Unity 工程的 Package Manager 中从磁盘安装本仓库 `Packages/` 下的 Core、Effect 与 Authoring 包。

从本目录执行作者工具验证：

```sh
dotnet run --project Tools/Tests/Authoring/AuthoringTests.csproj
python3 Tools/verify-authoring.py --unity /absolute/path/to/Unity
python3 Tools/verify-authoring.py --unity /absolute/path/to/Unity --without-effect
```

`verify-authoring.py` 在 `Tests~/Authoring` 或 `Tests~/AuthoringCoreOnly` 创建隔离工程。使用 `--project` 选择外部目录，用 `--unity-version` 匹配所选 Editor。

在独立 Unity 工程中从磁盘安装包，配置 Surface、MainCamera 和输入。当前主要环境为 6000.3.22f1 / macOS ARM64 / Metal，平台范围见[支持矩阵](../Packages/com.fujisheng.urui/Documentation~/LIMITATIONS_ZH.md)。业务程序集引用 URUI.Runtime，使用 Effect 时额外引用 URUI.Effect。

Core 的 Editor 夹具、样例导入与 Player 入口见[Editor 检查](../Packages/com.fujisheng.urui/Tests~/Editor/README.md)，HTML/CSS 对照见[作者检查](../Packages/com.fujisheng.urui/Tests~/HtmlCss/README.md)。测试用可丢弃工程，不在开发中的主工程注入缺图等故障。构建、实际运行与性能测试分别记录。

## macOS 原生构建与全套契约检查

需要 CMake 3.21+、Apple 命令行构建工具及 Python 3。源码和第三方依赖随包提供，不需要构建时在线下载。从仓库根目录运行，输出目录放在包外：

```sh
cmake -S Packages/com.fujisheng.urui/Native~ -B /absolute/urui-native-build -DCMAKE_BUILD_TYPE=Release -DCMAKE_OSX_ARCHITECTURES=arm64 -DURUI_BUILD_TESTS=ON -DURUI_DEBUG=OFF
cmake --build /absolute/urui-native-build --parallel 2
ctest --test-dir /absolute/urui-native-build --output-on-failure
```

不限制 --target 才会构建当前注册的全部测试程序。支持清单检查使用仓库内 Tools/HtmlCss/check_support_manifest.py。输出中的 libURUIBridge.dylib 是构建产物，完成检查后再明确安装到可丢弃测试工程；不要直接覆盖正在运行的 Editor 已加载的插件。

## 其他平台与诊断

Native~/build-platform.sh 接受 macos-arm64、macos-x86_64、android-arm64、android-x86_64、windows-x86_64、ios-arm64、webgl，每个目标使用独立输出目录。工具配置为 URUI_CMAKE、ANDROID_NDK_ROOT、URUI_ZIG、EMSCRIPTEN；iOS 使用本机 Xcode/iphoneos SDK。具体配置和实际构建命令以该脚本为准。

`Tools/build-native-platforms.py --install` 使用 `URUI_UNITY_PLAYBACK_ENGINES`、`URUI_CMAKE`、`URUI_ZIG` 和 `URUI_EMSCRIPTEN_ROOT` 选择已安装工具。它重建并核对七平台，再以新 inode 安装。Windows 工具链从 `URUI_ZIG` 或 PATH 查找已安装的 Zig，不依赖工程外的固定目录；安装前会检查所有 Unity Editor 已正常退出。插件清单记录实际原生版本、源码哈希、导出集合与 Android 对齐；编译验证和实际运行证据分别记录。

URUI_DEBUG 默认关闭，包括 Editor 和 Development Build。诊断时必须同时设置原生 URUI_DEBUG=ON 与托管 URUI_DEBUG 定义；结束后恢复正式配套插件。必要的线程、身份、资源及 GPU 安全检查始终保留。

当前 ABI13 / render-plan9 / style4 / effect7 / filter3 / animation1。随包七平台插件的哈希和验证范围见[清单](../Packages/com.fujisheng.urui/Documentation~/PLUGINS.json)，编译完成不代表 Player 或真实设备运行通过。根目录中更早的 Tools/verify-*、平台汇总脚本保留历史验收工作流，使用前应核对其 ABI/场景前提；当前原生全套检查以上述 CMake/CTest 命令为准。

代码风格见[代码约定](../Packages/com.fujisheng.urui/Documentation~/CODE_STYLE_ZH.md)，分发时保留 Core/Effect 的版权与第三方许可文件。
