# 安装

当前本地包为 `com.fujisheng.urui` **`0.8.0-preview.1`**，程序集 `URUI.Runtime`。有 asmdef 的业务代码显式引用 URUI.Runtime；普通 Assembly-CSharp 自动引用。命名空间见 [API](API_ZH.md)。

## 从磁盘安装

Package Manager → Add package from disk，选择完整包中的 `package.json`。本工作区路径为 `URUI/Packages/com.fujisheng.urui/package.json`；独立库仓库中通常为 `Packages/com.fujisheng.urui/package.json`。也可以将完整包复制到 Unity 项目的 Packages 目录作为嵌入包。

本地开发的 manifest 可使用 `"com.fujisheng.urui": "file:<相对于项目 Packages 的包路径>"`。只选一种安装方式，避免 Assets、嵌入包和 Package Manager 中重复存在同一程序集。

## 从 Git 安装

远端确实包含所需版本的完整包和插件时，可在 Package Manager → Add package from git URL 使用：

```text
https://github.com/fujisheng/URUI.git?path=Packages/com.fujisheng.urui#<已验证提交SHA>
```

把占位符替换为实际提交；私有仓库需配置访问权限。当前工作区版本与远端已发布版本分别管理，不能由本地 package.json 推断远端已有该提交。本地尚未发布的修改使用磁盘安装。

## Unity 环境

| 配置 | 要求 |
| --- | --- |
| Unity | 包声明 6000.3 / 22f1；当前主要运行环境 6000.3.22f1 |
| 图形 | 主要验证 macOS ARM64 / Metal；其他目标见支持矩阵 |
| 相机 | 启用的 MainCamera；URP 使用相应宿主提交适配 |
| 输入 | Input Manager (Old/Both)，或关闭 UseUnityInput 后使用 Surface.Input 注入 |
| 宿主 | 一个活动 Surface，Document 显式关联 |
| 字体 | 提供可读字节资源，并包含使用的字形 |
| 图片 | Texture2D 无需 Read/Write，需完整常驻 mip0，不启用纹理流送或 active mipmap limit |

URP 的项目适配已有运行记录；HDRP、其他图形后端及非 macOS 设备运行范围不能据此推断，见[支持矩阵](LIMITATIONS_ZH.md)。HTML 不自动热重载，修改后重新加载文档或重新进入 Play Mode。

## 原生插件与版本

完整包包含 Runtime/Plugins 与相应源码。当前协议为 ABI13 / render-plan9 / element4 / style4 / effect7 / filter3 / animation1（172 个正式 C 导出，URUI_DEBUG 默认关闭）。构建与哈希清单见 [PLUGINS.json](PLUGINS.json)。

更新已被 Editor/Player 加载的动态库时，先关闭相应进程，替换完整配套包后重新启动。不要只更新 C# 或只替换某个平台插件；协议、导出与包版本必须匹配。macOS ARM64/x64 为分开提供的单架构插件。

## 样例与可选包

- **独立 URP 示范工程**：直接用 Unity `6000.3.22f1` 打开仓库下的 [SampleProject/](../../../SampleProject/)（含 URP 3D 舞台合成、Effect、Authoring 与 10k 军械库验证）。
- **包内 Core 样例**：在 Unity Package Manager 中选择 **URUI** → **Samples** → **Solstice** → **Import**，打开导入后的 `Solstice.unity`（标准路径 `Assets/Samples/URUI/<版本>/Solstice`）。也可通过菜单 `Tools → URUI → Open Solstice Sample` 一键导入并打开。

`Hello URUI`、`Element Playground`、`Runtime Controls` 为细分专题学习样例；`Documentation~`、`Samples~`、`Tests~`、`Native~` 默认不参与普通 Unity 资产导入，按需显式引入，接入项目无需任何外部工作区目录。

可选包 [Effect](../../com.fujisheng.urui.effect/README_ZH.md) 与 [Authoring](../../com.fujisheng.urui.authoring/README_ZH.md) 需配合同版本 Core 使用；若在通用 UI 框架中集成导航与屏幕管理，可通过对应适配包接入。最小接入流程见[快速开始](QUICK_START_ZH.md)。
