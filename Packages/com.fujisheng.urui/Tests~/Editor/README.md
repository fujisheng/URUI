# Editor 功能检查

此目录由 Unity 默认忽略。在独立、可丢弃的 Unity **6000.3.22f1 / macOS ARM64 / Built-in / Metal** 工程中显式导入，检查当前公开 API 与编辑器入口。

## 默认包样例验收

URUI 的主要推荐样例是包内 `Samples~/Game Flow`。先只将 `Fixtures/Editor/PackageImport.cs` 复制到独立工程 `Assets/URUIEditorChecks/Editor`，调用 `URUI.Tests.Editor.PackageImport.ImportSamples`。该入口使用真实 `Sample.FindByPackage` / `Sample.Import(OverridePreviousImports)` 重新导入全部声明样例，覆盖独立工程中的样例副本，逐文件核对源字节；将路径和文件数量写入 `Evidence/sample-import.json`。之后重新启动 Unity，确认样例脚本编译通过；导入成功不能代替功能验收。

Game Flow 分项也可只复制 `GameFlowPackageImport.cs`，调用 `URUI.Tests.Editor.GameFlowPackageImport.Run`。`GameFlowValidation` 根据实际 Sample 元数据打开场景，不要求特定项目目录名。

完整 `GameFlowValidation.Run` 还验证两次缺图恢复：运行前将独立导入副本的 `Resources/URUI/GameFlow/Images/item-03.png`、`action-middle.png` 及各自 `.meta` 备份移出 Assets；进程结束后无论成功或失败，都恢复原始字节。不得对开发中的主工程执行这一故障注入。正常流程和恢复步骤的最终结果为 `Artifacts/GameFlow/Editor/game-flow.json`；失败细节为同目录 `failure.txt`。

下列 Hello/API/控件检查是分项回归入口，不是包的默认示例。

## 分项 API 与最小示例检查

1. 从本地磁盘安装 `com.fujisheng.urui`，按上述步骤导入样例。测试工程保留 `com.unity.modules.imageconversion` 1.0.0 模块，用于 PNG 截图；候选原生插件必须与当前托管 ABI 一致。
2. 将 `Fixtures/AcceptanceDocuments.cs` 和需要执行的运行时夹具复制到工程 `Assets/URUIEditorChecks`。辅助类只为测试场景显式配置 Surface、文档 URL、CSS 与原始字体，不转换 HTML。
3. 将对应分项的 Editor 夹具和它引用的契约检查复制到 `Assets/URUIEditorChecks/Editor`。导入全部四个样例后可复制完整 Fixtures 目录；只做分项时避免复制引用未导入样例的夹具。
4. 运行以下命令之一。Unity 在断言结束后退出，将报告保存到项目 `Evidence/`。命令应指向独立测试工程。

```sh
Unity -batchmode -projectPath /absolute/disposable-project -executeMethod URUI.Tests.Editor.ApiValidation.Run -logFile /absolute/api.log
Unity -batchmode -projectPath /absolute/disposable-project -executeMethod URUI.Tests.Editor.UsabilityValidation.Run -logFile /absolute/usability.log
```

`ApiValidation` 覆盖点击、样式像素、共享 Surface 上的独立文档、跨线程拒绝、无效输入、加载/卸载、重复注册/解绑、重载/删除失效、回调内重载和资源所有权。`PublicApiContract` 检查公开类型边界和当前 80 字节事件 ABI（64 位目标的 text 偏移 72）、原生帧结构布局，并检查默认关闭诊断。`ProductionSafetyContract` 检查不确定的 GPU 提交状态始终报告故障，不创建真实 GPU 资源。`PackageImport.Check` 只检查插件导入配置，不证明七平台新二进制或设备运行通过。`UsabilityValidation` 覆盖菜单资产保留、序列化 Source、Hello 点击与禁用恢复，并保存渲染 PNG。

`ElementTreeValidation.Run` 额外覆盖根与匿名节点、局部深度优先查询、可变名字、纯文本、属性/类名、样式覆盖移除、脱离/重挂载/销毁、回调路径快照和旧点击不误投替换节点。

URUI-Dev 本地工程中的 `PlaygroundValidation.Run` 操作真实序列化场景和按钮，检查创建、选中、移出、重挂载、销毁、Clear 与运行时异常，并保存截图。普通独立检查工程不需要导入 PlaygroundSmoke/PlaygroundValidation；运行该项时需显式导入 Element Playground 样例。

普通回归在默认关闭诊断的插件与托管配置下运行，不需要添加 `URUI_DEBUG`，也不会因为诊断关闭而跳过功能检查。需要单独检查诊断配置时，原生 CMake 的 `URUI_DEBUG=ON` 与托管 Scripting Define Symbols 中的 `URUI_DEBUG` 必须同时启用，见[诊断与支持范围](../../Documentation~/LIMITATIONS_ZH.md)。

这些检查用于有限的功能回归，不是完整 Unity Test Runner 套件、Player 构建验证或全平台性能测试。结果以实际生成的报告和日志为准。

## 元素运行时与控件

用同样的 Unity `-executeMethod` 方式调用 `URUI.Tests.Editor.ElementRuntimeValidation.Run`，执行类型、事件、输入、控件和列表夹具并保存 `Evidence/element-runtime.json`。`PreviewSample` 运行样例键盘保存、音量联动和列表选择并保存 PNG；`BuildPlayer` 构建 macOS ARM64 IL2CPP 验证 Player。Player 必须另行启动，可用 `--urui-evidence /absolute/evidence` 指定结果目录；构建成功不等于运行通过。ElementTree 和 Usability 入口可分别执行，Playground 必须来自当前样例导入路径，避免重复复制同一脚本。

`ElementRuntimeSmoke` 故意触发一次 `EXPECTED_INVALIDATION_PROBE`，检验派生控件清理异常不会中断其他节点失效；报告单独记录 `expectedDiagnosticExceptions`。其他异常不能按此探针忽略。

## 控件分项验收

`-executeMethod URUI.Tests.Editor.ElementRuntimeValidation.VerifyControls` 执行 ControlCompletenessSmoke，覆盖显式共享控件 CSS 与业务覆盖、只读/组合串、延后提交、数字无效输入与步进、单选组导航、滑块方向/反向步长、进度范围、滚动视口、列表键盘、严格文档结构和重载解绑。各组独立记录，结果为 Evidence/control-completeness.json。`BuildControlsPlayer` 构建对应 ARM64 IL2CPP Player，仍需实际启动并读取报告。自动组合串注入不代表真实系统 IME 人工验收。
