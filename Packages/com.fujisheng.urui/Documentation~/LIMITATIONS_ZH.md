# 当前限制

- 固定预览 `v0.1.0-preview.1` 与候选 `urui/managed-api-docs` 分开。新 Document API 只在候选中。
- 功能验收范围是 Unity 6000.3.22f1 / macOS ARM64 / Built-in / Metal；仅带对应 dylib。Windows、Android、Linux、URP/HDRP、WebGL 不在已验证支持范围。
- 单个活动 context，使用主线程、MainCamera 与旧 Input Manager；没有多面板或全套键盘/表单/IME/无障碍控件承诺。
- 只支持 RmlUi 实际实现的 RML/RCSS。托管 Q 按唯一 ID，不能代替 UITK 泛型 UQuery 或 CSS 选择器。
- 点击只有最近带 ID 的目标，不含 UITK 的捕获/冒泡/事件池协议。普通 API 暂未包装完整 PointerInput。
- 必须保持 ID 唯一，不能混用旧 Host 改名后继续用旧 Element。没有自动检测所有外部 DOM 变更的保证。
- NativeFrame、prepared batches、parameter table 等保留为兼容层显式实验选项；新 Document 不会自动开启。
- 没有新增通用性能优势结论、Windows/Android 验收或绿色 CI 结论。日常验证为受影响 Editor 检查，没有重新打 Player。
- 三消源码与旧数据仅保留存档，不作为测试运行。特效/粒子/动画样本尚未实施，随旧研究暂停，不属于本次发布功能。
- 历史 Android workflow 仍为旧工程配置，不是当前 UPM 包的有效构建入口。本次没有修改/执行这些工作流。
