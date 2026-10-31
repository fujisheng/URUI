# Editor 功能检查

此目录由 Unity 默认忽略。只在独立、可丢弃的 Unity 6000.3.22f1 / macOS ARM64 / Built-in Metal 工程中显式导入；不编译 Player、不运行三消或性能矩阵。

1. 从本地磁盘安装候选 `com.fujisheng.urui`，导入 Hello URUI 样例。为 PNG 截图证据保留引擎自带的 com.unity.modules.imageconversion 1.0.0 模块；这是测试工程依赖，不是库依赖。
2. 将 Fixtures 中的 `ApiSmoke.cs`、`UsabilitySmoke.cs` 复制到工程 Assets/URUIEditorChecks。
3. 将 Fixtures/Editor 内脚本复制到 Assets/URUIEditorChecks/Editor。
4. 从工程根目录运行以下之一，Unity 会在断言结束后退出，报告保存在项目 Evidence/。请勿指向有未保存工作的应用工程。

```sh
Unity -batchmode -projectPath /absolute/disposable-project -executeMethod ApiValidation.Run -logFile /absolute/api.log
Unity -batchmode -projectPath /absolute/disposable-project -executeMethod UsabilityValidation.Run -logFile /absolute/usability.log
```

ApiValidation 验证真实 native click、样式像素、单 context、跨线程、NUL、未激活配置、Source 显式卸载、生命周期回调禁用、重复注册/解绑、重载/删除失效、回调内重载和资源所有权。UsabilityValidation 验证菜单资产保留、序列化 Source、Hello 点击与禁用恢复，保存实际渲染 PNG。

这些是有限的功能 smoke，不是完整 Unity Test Runner 套件或性能/全平台验收。历史兼容 Host 的 Editor/IL2CPP 证据另见 Documentation~/VALIDATION.md。回归失败和修正原因见 Documentation~/URUI_CANDIDATE_VALIDATION.md。
