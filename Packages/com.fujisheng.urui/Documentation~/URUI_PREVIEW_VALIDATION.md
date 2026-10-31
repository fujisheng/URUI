# URUI 0.1.0-preview.1 验证

基于 `afa24847342f16c64566fe005466aabd6b4c5109` 的分发身份改名。2026-10-31 使用 Unity 6000.3.22f1 / macOS ARM64 / Built-in Metal 在独立工程完成 local-file 包解析、兼容 API 与 Hello 样例编译、两种 shader 契约检查、Mac ARM64 插件过滤，以及 24 帧 Editor Play Mode smoke：文本/样式/属性、弱引用、默认绘制、绿色像素读回、显式 NativeFrame 和销毁均通过，进程退出码 0。

运行时代码仅修正两个包内 shader 源码路径；原生源码、dylib、GUID、默认路径与兼容程序集不变。原生库 SHA256 为 `0c9df11f988f807de559f6407a7c6f4e59414b6fd749a8b8bb0dd88792761a8a`。Editor 菜单显示改为 URUI。

首次受沙箱限制无法连接本机 Unity 服务，正常停止后以本机服务访问权限运行通过。原始日志和报告保留在本次独立 task-3 工作目录 `preview-validation/Evidence/` 与 `preview-evidence.json`。历史原生离线构建、embedded 导入与 Mac ARM64 IL2CPP 证据见 VALIDATION.md；本次没有重新构建 Player。

没有执行 Android、三消、性能矩阵；没有扩大到 Windows/Linux/SRP，CI 未运行且不声明通过。新的 URUI 托管外观将是后续独立候选，未包含在此固定预览。
