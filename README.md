# URUI · UnityRmlUi

URUI 是 Unity 的 RmlUi 托管接入层，使用 RML/RCSS 描述界面，原生 RmlUi 处理布局与渲染数据，Unity 提交绘制。

当前预览：**0.1.0-preview.1**。基于已验收的 RmlUnity UPM 版本改名，默认渲染路径不变。新的易用托管 API 会在后续独立候选分支整理，不会覆盖此预览 tag。

## 安装

Unity Package Manager → Add package from git URL：

```text
https://github.com/fujisheng/URUI.git?path=/Packages/com.fujisheng.urui#v0.1.0-preview.1
```

仓库目前为私有，Git 安装需要你的 GitHub 访问权限；本机 Git 未认证时可将完整 `Packages/com.fujisheng.urui` 目录复制到项目 `Packages/`，或从磁盘选择其中的 `package.json`。不要同时安装旧包和新包。

## 开始使用

使用 `Tools > URUI > Create Minimal UI Scene`，或从 Package Manager 导入 Hello Rml 样例。运行前确认存在标记为 MainCamera 的相机，启用旧 Input Manager（或 Both），并使用可读的字体文件。兼容 API 仍为 `RmlUnity.RmlUnityHost`，旧程序集、GUID、shader 标识与 native ABI 保留，详见[命名迁移](docs/URUI_MIGRATION.md)。

## 验证范围

Unity **6000.3.22f1**、**macOS ARM64**、**Built-in / Metal**。底层基线曾通过干净 local/embedded 导入、Hello 编译、Editor 与 ARM64 IL2CPP 功能 smoke；包内源码离线重建的 dylib 与原发布库逐字节相同。改名后的 Editor 验证单独记录，不等同于重新做过 Player 或性能验收。

仅包含 Mac ARM64 原生库。Windows、Android、Linux、URP/HDRP 尚未验证。一次仅支持一个原生 context。NativeFrame 与其他实验路径需要显式开启；此预览不含未证明通用收益的研究候选，也不宣称生产就绪或 CI 通过。

## 包结构与文档

- `Runtime/`：兼容托管 API、shader、已验收 Mac 原生库。
- `Editor/`：菜单与 shader 资源校验。
- `Samples~/`：可选样例；`Tests~/`：独立测试源码与保留历史数据，不自动运行三消。
- `Native~/`：完整框架源码与固定版本 RmlUi 6.3 / FreeType 2.13.3、许可证、离线构建入口。
- [安装](Packages/com.fujisheng.urui/Documentation~/INSTALL_ZH.md) · [基线验收](Packages/com.fujisheng.urui/Documentation~/VALIDATION.md) · [原生来源](Packages/com.fujisheng.urui/Documentation~/SOURCE_AUDIT.md) · [迁移](docs/URUI_MIGRATION.md)

`~` 目录由 Unity 忽略资产导入，可直接用文件编辑器查看。第三方版权与许可证保留在包中。旧工程可从 `a29a15ebf55b1b26fcfb47bf73fc6105b1d0b563` 的独立 checkout 恢复；旧研究当前暂停保存。
