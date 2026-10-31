# Editor 功能检查

此目录由 Unity 默认忽略。在独立、可丢弃的 Unity **6000.3.22f1 / macOS ARM64 / Built-in / Metal** 工程中显式导入，检查当前公开 API 与编辑器入口。

1. 从本地磁盘安装 `com.fujisheng.urui`，导入 Hello URUI 样例。测试工程保留引擎自带的 `com.unity.modules.imageconversion` 1.0.0 模块，用于 PNG 截图。
2. 将 `Fixtures/ApiSmoke.cs`、`Fixtures/UsabilitySmoke.cs` 复制到工程 `Assets/URUIEditorChecks`。
3. 将 `Fixtures/Editor` 内脚本复制到 `Assets/URUIEditorChecks/Editor`。
4. 运行以下命令之一。Unity 在断言结束后退出，将报告保存到项目 `Evidence/`。命令应指向独立测试工程。

```sh
Unity -batchmode -projectPath /absolute/disposable-project -executeMethod URUI.Tests.Editor.ApiValidation.Run -logFile /absolute/api.log
Unity -batchmode -projectPath /absolute/disposable-project -executeMethod URUI.Tests.Editor.UsabilityValidation.Run -logFile /absolute/usability.log
```

`ApiValidation` 覆盖点击、样式像素、单 context、跨线程拒绝、无效输入、加载/卸载、重复注册/解绑、重载/删除失效、回调内重载和资源所有权。其 `PublicApiContract` 同时检查五个公开类型、原生帧结构布局，并在未定义 `URUI_DEBUG` 时确认诊断类型没有进入运行时程序集。`ProductionSafetyContract` 检查不确定的 GPU 提交状态始终报告故障，不创建真实 GPU 资源。`UsabilityValidation` 覆盖菜单资产保留、序列化 Source、Hello 点击与禁用恢复，并保存渲染 PNG。

普通回归在默认关闭诊断的插件与托管配置下运行，不需要添加 `URUI_DEBUG`，也不会因为诊断关闭而跳过功能检查。需要单独检查诊断配置时，原生 CMake 的 `URUI_DEBUG=ON` 与托管 Scripting Define Symbols 中的 `URUI_DEBUG` 必须同时启用，见[开发指南](../../../../docs/DEVELOPMENT_ZH.md)。

这些检查用于有限的功能回归，不是完整 Unity Test Runner 套件、Player 构建验证或全平台性能测试。结果以实际生成的报告和日志为准。
