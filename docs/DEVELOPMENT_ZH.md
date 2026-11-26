# 开发指南、原生构建与测试验证

[English](DEVELOPMENT.md) | **中文**

Unity 验证基线：**`6000.3.22f1`**。原生桥接协议：**`ABI13 / render-plan9 / element4 / 172 个导出`**。

## 1. 仓库结构

- `Packages/com.fujisheng.urui/` — Core 运行时（`URUI.Runtime`）、Editor 工具（`URUI.Editor`）、7 平台原生二进制（`Runtime/Plugins`）、原生 C++ 源码（`Native~`）及测试套件（`Tests/`、`Tests~/`）。
- `Packages/com.fujisheng.urui.effect/` — 着色器滤镜、预设控制器、`WaveEffect`、`TextureEffectSurface` 与粒子节点（`URUI.Effect`）。
- `Packages/com.fujisheng.urui.authoring/` — 纯 Editor 可视化工作台、时间轴与本机 Agent HTTP 接口（`URUI.Authoring.Editor`）。
- `SampleProject/` — 独立 URP 完整示范工程（**《潮汐信标》**）。
- `Tools/` — 七平台原声构建脚本、文档规范检查、托管端测试与静态校验工具。

## 2. 重新编译七平台原生库（`URUIBridge`）

通过统一构建脚本从 `Native~` 同源构建七个目标平台（`macOS ARM64/x86_64`、`Windows x86_64`、`Android ARM64/x86_64`、`iOS ARM64`、`WebGL wasm32`）：

```sh
python3 Tools/build-native-platforms.py
```

构建后会自动更新 `Runtime/Plugins/**` 与 `Packages/com.fujisheng.urui/Documentation~/PLUGINS.json` 中的源码哈希、二进制哈希与导出列表（当前为 172 个正式导出、`required_abi_version == 13`）。重新启动或重载对应 Unity 工程后加载新插件。

## 3. 执行自动化回归验证

### Authoring 托管测试套件（无需打开 Unity）
```sh
dotnet run --project Tools/Tests/Authoring/AuthoringTests.csproj
```

### 文档、链接与脚本自检
```sh
python3 Tools/check-docs.py
python3 Tools/check-static-links.py
python3 -m unittest discover -s Tools/Tests -p "test_*.py"
```

### Unity EditMode 与 PlayMode 测试套件
- 在纯净 Built-in 渲染管线的 Unity `6000.3.22f1` 验证工程中运行 `URUI.Editor.Tests`、`URUI.Runtime.Tests.PlayMode` 与 `URUI.Effect.Tests`（导入 `Samples~/EffectGallery`，对应 `Tools/Effects/validate.py` 标准验证流），全面回归内存泄漏、`SurfaceRenderingStatisticsTests`、原生渲染计划与着色彩效果。
- 在 `SampleProject` 打开潮汐信标，试玩登录、拖动模组选装、英雄预演和试炼结算，检查粒子和 URP 合成。
- 外置存储开发请统一设置 `TMPDIR`；涉及真实显卡采样、`ReadPixels` 或截图的测试（包括含 GPU 预览的 EditMode 与全部 PlayMode）请运行真实硬件设备，**不要传 `-nographics`**。
