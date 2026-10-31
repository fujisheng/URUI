# 安装

当前文档对应仓库 `main` 分支。包名为 `com.fujisheng.urui`，在 Unity 中显示为 **URUI**。

## 环境

当前验证范围为 **Unity 6000.3.22f1 / macOS ARM64 / Built-in / Metal**。其他环境见[支持矩阵](LIMITATIONS_ZH.md)。

在项目中确认：

- **Active Input Handling** 为 **Input Manager (Old)** 或 **Both**。
- 场景中有启用并标记为 **MainCamera** 的相机。
- 同时只启用一个 URUI 文档。
- 加载的字体文件可读取，图片纹理开启 **Read/Write**。

## 通过 Git 安装

Unity Package Manager → **Add package from git URL**：

```text
https://github.com/fujisheng/URUI.git?path=/Packages/com.fujisheng.urui#main
```

Git 安装需要已有仓库访问权限。`main` 会随开发更新；需要固定项目依赖时，可将地址中的 `main` 替换为经过项目验证的提交 ID。

## 从磁盘安装

取得完整仓库后，在 Package Manager 中选择 **Add package from disk**，打开：

```text
Packages/com.fujisheng.urui/package.json
```

也可将完整的 `com.fujisheng.urui` 目录复制到项目的 `Packages/` 下作为嵌入包。一个项目选择一种安装方式，保留包内目录结构。

## 创建第一个界面

在 Package Manager 中导入 **Hello URUI**，将 `HelloUrui` 添加到空 GameObject 并进入 Play Mode。或者使用 **Tools > URUI > Create Minimal UI Scene** 创建文档资产和场景。

业务脚本使用 `using URUI;`。有 asmdef 的业务程序集添加对 **URUI.Runtime** 的引用；编辑器工具位于 **URUI.Editor** 程序集。

完整操作见[快速开始](QUICK_START_ZH.md)。`Documentation~`、`Samples~`、`Tests~`、`Native~` 由 Unity 默认忽略资产导入，可直接用文件编辑器查看；样例和测试需要显式导入。
