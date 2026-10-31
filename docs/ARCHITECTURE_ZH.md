# URUI 架构

URUI 将可读的界面文档和 Unity 业务代码连接起来。RML 描述结构，RCSS 描述布局与外观，C# 控制应用状态。

## 数据流

1. 应用为 `URUI.Document` 提供 RML、图片映射与字体路径。
2. `Document` 创建内部渲染宿主，通过 URUIBridge 加载 RmlUi 文档。
3. RmlUi 解析文档、计算布局并生成绘制数据。
4. Unity 使用包内 shader、mesh、material 与 command buffer 提交绘制。
5. Unity 输入交给 RmlUi 处理，点击通知再分发给公开的 `Element` 订阅。

`Document.Loaded` 标记文档可查询；布局和实际绘制由随后的 Unity 更新完成。当前一个活动文档对应一个原生 context，所有访问均在主线程进行。

## 边界

| 层 | 职责 |
| --- | --- |
| `URUI` / `URUI.Runtime` | `Document`、`Element`、`ElementStyle`、`ImageAsset`、`ClickEvent` 业务接口 |
| `URUI.Internal` | 渲染宿主、元素引用、资源管理和原生互操作；不作为业务 API |
| `URUI.Editor` 程序集 | 最小场景菜单与 shader 资源校验 |
| `URUIBridge` | `urui_*` C ABI；连接 RmlUi 与 Unity 的内部原生桥 |
| RmlUi / FreeType | 文档、样式、布局与字体处理 |

`URUI.Internal.RenderHost` 的生命周期由 `Document` 管理，业务代码不直接创建或操作它。内部实验渲染路径也不构成公开扩展接口。

公开元素包装关联原节点身份。原节点删除或文档重载后包装失效，相同 ID 的新节点需要重新查询。点击订阅遵守相同生命周期，不跨文档复用。

## 目录

- `Packages/com.fujisheng.urui/Runtime/`：托管接口、内部实现、shader 与原生插件。
- `Runtime/Debug/`：仅在显式定义 `URUI_DEBUG` 时参与编译的诊断、自测和故障注入；默认关闭。
- `Editor/`：编辑器集成。
- `Documentation~/`：产品和 API 文档、第三方许可证副本。
- `Samples~/`：显式导入的使用样例。
- `Tests~/`：显式导入的测试与验证工具。
- `Native~/`：桥接源码、原生测试和固定版本第三方源码。
- `Native~/Debug/`：由同名原生构建选项控制的诊断实现；发布插件不包含诊断导出。

带 `~` 的目录不参与 Unity 默认资产导入。原生构建和功能检查见[开发指南](DEVELOPMENT_ZH.md)，产品使用边界见[支持矩阵](../Packages/com.fujisheng.urui/Documentation~/LIMITATIONS_ZH.md)。

调试宏不控制线程、元素身份、资源所有权、借用窗口或 GPU 完成状态的检查。这些检查保证正式运行的正确性，始终保留。Editor 菜单、资产导入与 shader 构建校验也不依赖调试宏。
