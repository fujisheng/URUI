# 安装

包名 `com.fujisheng.urui`，程序集 `URUI.Runtime`，公开命名空间按 Documents、Elements、Events、Resources 分类，见[API](API_ZH.md)。

## 从 Git 安装

在 Package Manager 选择 **Add package from git URL**，输入：

```text
https://github.com/fujisheng/URUI.git?path=Packages/com.fujisheng.urui
```

需要固定依赖时，在 URL 末尾追加 `#` 和经过项目验证的提交 SHA。

## 本地开发

在 Package Manager 选择 **Add package from disk**，打开仓库中的：

```text
Packages/com.fujisheng.urui/package.json
```

也可将完整包目录复制到项目 Packages 作为嵌入包。一个项目选择一种安装方式；有 asmdef 的业务程序集引用 URUI.Runtime。开发工程可通过 manifest 的本地 file 路径引用包，修改包源码会由 Editor 重新编译。

## 环境与样例

运行验证环境为 Unity **6000.3.22f1 / macOS ARM64 Editor / Built-in / Metal**。包内七份平台插件均使用 ABI3、默认关闭 URUI_DEBUG；其他平台的编译、链接与运行验证范围见[支持矩阵](LIMITATIONS_ZH.md)。

准备 Input Manager (Old) 或 Both、启用的 MainCamera、一个活动 Document。字体路径需要可读，图片 Texture2D 开启 Read/Write。没有自动热重载 RML；修改文档资产后重新进入 Play Mode 或显式调用 Load。

默认推荐导入 **Game Flow**：Package Manager → URUI → Samples → Game Flow → Import，再打开导入目录内的 `GameFlow.unity`。也可使用 **Tools > URUI > Open Game Flow Sample**。Unity 标准目录为 `Assets/Samples/URUI/<包版本>/Game Flow`，实际路径由 Unity 决定；安装包不自动导入样例。所需资源均在包内，无需开发工程或 Artifacts。

Hello URUI、Element Playground、Runtime Controls 和 Create Minimal UI Scene 菜单保留为次要学习示例。

Documentation~、Samples~、Tests~、Native~ 默认不参与 Unity 资产导入，样例与测试需要显式导入。完整用法见[快速开始](QUICK_START_ZH.md)。
