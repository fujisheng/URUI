# URUI 架构

URUI 将可读的界面文档和 Unity 业务代码连接起来。RML 描述结构，RCSS 描述布局与外观，C# 控制应用状态。

## 数据流

1. 应用为 `URUI.Documents.Document` 提供 RML、图片映射与字体路径。
2. `Document` 创建内部渲染宿主，通过 URUIBridge 加载 RmlUi 文档。
3. RmlUi 解析文档、计算布局并生成绘制数据。
4. Unity 使用包内 shader、mesh、material 与 command buffer 提交绘制。
5. Unity 输入经 Document.Input 归一化后交给 RmlUi，原生事件同步进入托管传播与控件处理。

`Document.Loaded` 标记文档可查询；布局和实际绘制由随后的 Unity 更新完成。当前一个活动文档对应一个原生 context，所有访问均在主线程进行。

## 边界

| 层 | 职责 |
| --- | --- |
| `URUI.Documents` / `URUI.Elements` / `URUI.Controls` / `URUI.Events` / `URUI.Resources` | `URUI.Runtime` 程序集中的业务接口 |
| `URUI.Internal.Hosting` / `Input` / `Resources` / `Interop` | 宿主、输入、图片映射与原生边界；不作为业务 API |
| `URUI.Internal.Rendering.*` | 参数计划、帧借用、保留记录、网格与 shader 资源 |
| `URUI.Editor` 程序集 | 最小场景菜单与 shader 资源校验 |
| `URUIBridge` | `urui_*` C ABI；连接 RmlUi 与 Unity 的内部原生桥 |
| RmlUi / FreeType | 文档、样式、布局与字体处理 |

`URUI.Internal.Hosting.RenderHost` 的生命周期由 `Document` 管理，业务代码不直接创建或操作它。内部实验渲染路径也不构成公开扩展接口。

Element 按稳定对象身份包装节点，Document 按令牌复用包装。文档根、Parent 与局部查询来自真实 DOM；Name 变化和重挂载不会改换身份。附着节点由 RmlUi DOM 拥有，脱离的根由文档的原生注册表持有。Destroy、结构替换或文档卸载后，相应身份与订阅失效。

Document 按注册表首次确定节点的托管类型；RML、类型化查询与 C# 创建共用稳定实例。组合控件通过 ContentContainer 接收业务节点，Hierarchy 保留真实 DOM 操作。常用表单控件统一值变化协议；TextInputField/NumericField 复用只读、选区、提交与数值步进，ListView 采用固定行高和可见行复用。基础 RCSS 在业务样式之前注入，滚动布局通过原生客户区快照扣除实际滚动条。

原生事件通过文档 capture 回调同步进入托管；托管层在调用业务代码前固定目标到根的路径与订阅快照，一次完成 capture/target/bubble。停止传播与取消默认行为分别处理；卸载、重载和指针取消延后到原生调用安全退出后执行。旧原生点击队列仅保留 C ABI 兼容，不再向托管重复投递。核心包不依赖 FUI，也没有数据绑定。

托管与原生接口需要 ABI3 和元素协议 v1，包内七份平台插件均按相同源码构建。运行验证范围以支持矩阵和 PLUGINS.json 为准；编译/链接检查不代替 Unity Player 或设备验证。

详细的职责、目录和类型定位见[代码组织](CODE_ORGANIZATION_ZH.md)。

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
