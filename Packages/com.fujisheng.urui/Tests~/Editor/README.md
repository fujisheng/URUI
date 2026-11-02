# Editor 功能检查

此目录由 Unity 默认忽略。在独立、可丢弃的 Unity **6000.3.22f1 / macOS ARM64 / Built-in / Metal** 工程中显式导入，检查当前公开 API 与编辑器入口。

1. 从本地磁盘安装 `com.fujisheng.urui`，导入 Hello URUI 样例。测试工程保留引擎自带的 `com.unity.modules.imageconversion` 1.0.0 模块，用于 PNG 截图。
2. 将 `Fixtures/ApiSmoke.cs`、`Fixtures/UsabilitySmoke.cs`、`Fixtures/ElementTreeSmoke.cs` 复制到工程 `Assets/URUIEditorChecks`。
3. 将 `Fixtures/Editor` 内脚本复制到 `Assets/URUIEditorChecks/Editor`；普通检查工程排除 `PlaygroundValidation.cs`，它属于 URUI-Dev 的专用场景检查。
4. 运行以下命令之一。Unity 在断言结束后退出，将报告保存到项目 `Evidence/`。命令应指向独立测试工程。

```sh
Unity -batchmode -projectPath /absolute/disposable-project -executeMethod URUI.Tests.Editor.ApiValidation.Run -logFile /absolute/api.log
Unity -batchmode -projectPath /absolute/disposable-project -executeMethod URUI.Tests.Editor.UsabilityValidation.Run -logFile /absolute/usability.log
```

`ApiValidation` 覆盖点击、样式像素、单 context、跨线程拒绝、无效输入、加载/卸载、重复注册/解绑、重载/删除失效、回调内重载和资源所有权。其 `PublicApiContract` 同时检查公开类型边界和事件 ABI 布局、原生帧结构布局，并在未定义 `URUI_DEBUG` 时确认诊断类型没有进入运行时程序集。`ProductionSafetyContract` 检查不确定的 GPU 提交状态始终报告故障，不创建真实 GPU 资源。`UsabilityValidation` 覆盖菜单资产保留、序列化 Source、Hello 点击与禁用恢复，并保存渲染 PNG。

`ElementTreeValidation.Run` 额外覆盖根与匿名节点、局部深度优先查询、可变名字、纯文本、属性/类名、样式覆盖移除、脱离/重挂载/销毁、回调路径快照和旧点击不误投替换节点。

URUI-Dev 本地工程中的 `PlaygroundValidation.Run` 操作真实序列化场景和按钮，检查创建、选中、移出、重挂载、销毁、Clear 与运行时异常，并保存截图。普通独立检查工程不需要导入 PlaygroundSmoke/PlaygroundValidation；运行该项时需显式导入 Element Playground 样例。

普通回归在默认关闭诊断的插件与托管配置下运行，不需要添加 `URUI_DEBUG`，也不会因为诊断关闭而跳过功能检查。需要单独检查诊断配置时，原生 CMake 的 `URUI_DEBUG=ON` 与托管 Scripting Define Symbols 中的 `URUI_DEBUG` 必须同时启用，见[开发指南](../../../../docs/DEVELOPMENT_ZH.md)。

这些检查用于有限的功能回归，不是完整 Unity Test Runner 套件、Player 构建验证或全平台性能测试。结果以实际生成的报告和日志为准。

## 元素运行时与控件

从 Source 运行 `python3 Tools/verify-element-runtime.py` 会准备独立的 Tests/ElementRuntime 工程，执行类型/事件/输入/控件/列表夹具并保存 Evidence/element-runtime.json。原生候选可通过 --native 指定；默认使用当前分发插件。`--method URUI.Tests.Editor.ElementRuntimeValidation.PreviewSample` 运行样例键盘保存、音量联动和列表选择并保存 PNG，`--method URUI.Tests.Editor.ElementRuntimeValidation.BuildPlayer` 构建 macOS ARM64 IL2CPP 验证 Player。Player 必须另行启动，可用 `--urui-evidence /absolute/evidence` 指定结果目录；构建成功不等于运行通过。旧 API、ElementTree 和 Usability 入口仍可分别显式运行，Playground 使用全新独立工程及 --playground，避免与先前导入的样例重复。

`ElementRuntimeSmoke` 故意触发一次 `EXPECTED_INVALIDATION_PROBE`，检验派生控件清理异常不会中断其他节点失效；报告单独记录 `expectedDiagnosticExceptions`。其他异常不能按此探针忽略。

## 控件分项验收

`--method URUI.Tests.Editor.ElementRuntimeValidation.VerifyControls` 执行 ControlCompletenessSmoke，覆盖默认控件样式与业务覆盖、只读/组合串、延后提交、数字无效输入与步进、单选组导航、滑块方向/反向步长、进度范围、滚动视口、列表键盘与重载解绑。各组独立记录，结果为 Evidence/control-completeness.json。`BuildControlsPlayer` 构建对应 ARM64 IL2CPP Player，仍需实际启动并读取报告。自动组合串注入不代表真实系统 IME 人工验收。
