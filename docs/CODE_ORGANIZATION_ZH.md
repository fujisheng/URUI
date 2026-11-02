# 代码组织

运行时程序集为 `URUI.Runtime`，编辑器程序集为 `URUI.Editor`。程序集按运行环境区分，文件夹与命名空间按职责区分。

## 公开接口

| 目录 | 命名空间 | 职责 |
| --- | --- | --- |
| `Runtime/Documents/` | `URUI.Documents` | Document 配置、加载、生命周期、节点缓存与点击分发 |
| `Runtime/Elements/` | `URUI.Elements` | 元素身份、层级、查询、属性、样式及事件订阅 |
| `Runtime/Controls/` | `URUI.Controls` | 常用控件、值协议、组合容器与虚拟列表 |
| `Runtime/Events/` | `URUI.Events` | 传给业务回调的事件数据 |
| `Runtime/Resources/Images/` | `URUI.Resources` | 调用者持有的图片映射 |

`Document.cs` 保留组件声明、状态与生命周期事件；配置、加载、资源释放、线程检查和元素操作位于各自的 partial 文件。`Element` 的层级和事件实现也分别组织，使用者不需要了解原生句柄。

## 内部实现

以下目录均位于 `Runtime/Internal/`。内部类型不作为业务扩展接口。

| 目录 / 命名空间后缀 | 职责与入口 |
| --- | --- |
| `Hosting` | `RenderHost` 协调 Unity 组件生命周期、输入、文档、绘制与资源释放；各职责使用明确的 partial 文件 |
| `Input` | 指针阶段与输入数据，不持有 Unity 或原生资源 |
| `Resources` | 宿主使用的图片绑定记录 |
| `Interop` | C ABI 声明、原生库名、顶点/命令/版本戳结构；布局必须匹配原生定义 |
| `Rendering/Parameters` | 参数计划、资源缓存、批次、录制、提交与退役；`ParameterTableRenderer` 按这些阶段拆分 |
| `Rendering/Frames` | 同步借用的原生帧、帧录制及 GPU 资源退役 |
| `Rendering/Retention` | 保留记录的状态、键、资源独占约束与计划稳定性 |
| `Rendering/Meshes` | 网格批处理规则与临时缓冲 |
| `Rendering/Shaders` | Shader 查找、属性契约及能力校验 |

命名空间对应 `URUI.Internal.Hosting`、`URUI.Internal.Interop`、`URUI.Internal.Rendering.Parameters` 等目录职责。private 嵌套数据类型保留所属类型的访问边界，不为拆文件而扩大可见性。

partial 文件共享一个对象和同一套所有权规则。主声明集中保留原有字段的初始化顺序；拆分方法不能改变加载顺序、异常回滚、借用窗口或 GPU fence 判断。

## 诊断、编辑器与资产

- `Runtime/Debug/` 独立保存诊断实现，并按宿主、互操作与渲染职责分类；`URUI_DEBUG` 默认关闭。诊断 partial 与对应正式类型使用同一个命名空间，独立诊断帮助类位于 `URUI.Internal.Diagnostics`。
- `Editor/Menus/` 使用 `URUI.Editor.Menus`，提供场景创建菜单；`Editor/Validation/` 使用 `URUI.Editor.Validation`，保存资产与构建校验。
- `Runtime/Resources/URUI/` 保存随包加载的 shader；`Runtime/Plugins/` 保存各平台原生插件。
- `Samples~/` 和 `Tests~/` 需要显式导入；样例展示公开接口，验证夹具可以检查内部协议。
- `Native~/` 保存原生桥、契约测试与第三方源码，其 C ABI 和固定布局与托管目录组织相互独立。

移动 Unity 脚本、资源和目录时应同时移动对应 `.meta`；已有组件脚本保留 GUID、类名、程序集与序列化字段。新增 partial 文件使用新的 GUID，不替换组件脚本的身份。
