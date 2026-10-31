# 未发布托管 API 候选验证

日期：2026-10-31。候选基于已发布 `v0.1.0-preview.1` / `a5506d79718f31c5cfc159877ab26416c12992bd`，分支 `urui/managed-api-docs`。本文件描述后续候选，不扩大固定预览的功能。没有发布第二个版本。

环境为 Unity 6000.3.22f1 / macOS ARM64 / Built-in Metal，独立 local-file 安装工程。没有构建 Player，没有运行 Android、三消或性能矩阵，没有声称 CI 通过。

## 最终通过的检查

- `ApiValidation.Run`，24 帧，退出 0：包解析、兼容 API 编译、URUI.Runtime 依赖闭包（不含 uGUI）、原 shader 契约/插件过滤；同步加载/Loaded、缺少节点、缓存包装、文字/属性/样式、绿色像素、唯一 context、后台线程拒绝、NUL 拒绝、未激活对象配置、TextAsset 显式卸载、生命周期回调禁用、重复订阅/解绑、DOM 替换/同 ID 新对象、回调内重载、禁用恢复和调用者资源保留。报告加载/卸载各 4 次，实际 native 点击 1 次。
- `UsabilityValidation.Run`，18 帧，退出 0：菜单两次创建保持唯一场景/文档路径，已编辑文本保留；序列化 Source 加载、禁用恢复、Hello URUI 实际按钮点击 2 次、样例恢复与销毁、背景色像素读回。按钮布局边界为 `(44,96.8,220,50)`，保存实际渲染 PNG。
- 兼容 `UPMValidation.EditorSmoke`，24 帧，退出 0：旧 Host 的文本/属性/样式、弱引用、默认绘制、绿色像素、显式 NativeFrame 与 teardown 均通过。兼容 fixture 的旧 camera 字段有既存 CS0108 警告，不属于新库编译错误。
- 静态检查：原有 meta 全部字节不变；GUID 冲突 0、孤立 meta 0；Native~ 源文件和 dylib 未改变；文档相对链接存在；生产包依赖仍只有内置 jsonserialize。dylib SHA256 `0c9df11f988f807de559f6407a7c6f4e59414b6fd749a8b8bb0dd88792761a8a`。

测试 fixture 随包置于 `Tests~/Editor/`，默认不编译，显式复制到独立项目运行。截图测试需要已安装引擎内置 imageconversion 模块，不是生产包依赖。测试没有公开给业务层任何注入事件或原生指针接口。

## 审查和失败记录

独立只读审查发现：Unloaded 禁用后仍创建 renderer、TextAsset 显式卸载被 Start 撤销、嵌入 NUL 被 native 截断、未激活组件误报线程错误。均先在真实 Editor 断言中复现，再修复；最终回归通过。身份设计文档改为准确契约：普通属性写入仍按唯一 ID 寻址，不声称能检测兼容 Host 的外部改名。

首轮重载点击测试在新 context 首次布局前注入输入，修正为布局完成后点击。最小验证工程首次截图编译缺少 imageconversion 内置模块，在验证工程启用后通过；没有下载或增加库依赖。截图进一步发现 Hello 的 div 默认行内布局，补充块布局的尺寸断言先失败，再在样例 RCSS 显式声明 display:block；按钮尺寸与交互随后通过。

原始成功和失败日志、JSON、PNG 保存在本次 task-3 的 `preview-validation/Evidence/`，根目录有 `PREVIEW_PUBLICATION.json`、`preview-evidence.json` 与 `candidate-static.json`。旧 UPM 原生重建、embedded 导入与 IL2CPP 证据只按历史基线复用；新候选没有重新做这些 Player/全平台验收。

本机 Git 没有该私有仓库的认证，未直接从 Git URL 安装；使用磁盘安装验证，远端 tag、包内容与提交回读验证。独立工作副本由已验证快照和 GitHub commit/tree 重建，基线整树 hash 与远端一致；没有创建凭据或修改访问权限。
