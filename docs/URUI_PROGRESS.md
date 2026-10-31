# URUI 工作记录

- 原优化研究按用户要求暂停，原目录与数据只读保留；未续做 CQ/paint、特效、粒子、动画或三消测试。
- 仓库成功改名 URUI，默认分支仍为 main。旧 GitHub API 路径回读解析到规范 URUI；新安装使用新包子目录。
- `v0.1.0-preview.1` 已作为 prerelease 发布，固定提交 `a5506d79718f31c5cfc159877ab26416c12992bd`，整树 `b30cc5f234e9f24acb2e3b444c9fe85df5b0f8a8`。回读 prerelease=true、draft=false；发布提交 Actions runs=0，不是 CI pass。
- 独立候选增加 URUI.Document/Element/ElementStyle/ImageAsset/ClickEvent，URUI.Runtime assembly；保留原 RmlUnity namespace/程序集/GUID/native ABI 兼容边界。这是避免破坏既有脚本、场景和库的实施判断。
- 代码风格证据来自实际读取的 FUI/UnityCli；仅整理新外观、样例、菜单和触及的公共引用类，没有机械改写底层渲染器或第三方。
- 新增中文安装、快速开始、API、事件、资源生命周期、限制、风格与验证说明；主 README 和包 README 区分固定预览与候选。
- 独立审查的两个 Important 和两个 Minor 均有针对性的失败复现和修复，最终 Editor 检查通过。没有保留未处理的审查问题。
- 决定保持唯一 ID 与禁止外部 ID 改名为明确契约：现有 ABI 的普通属性更新按字符串 ID；没有隐藏声称完整对象身份写入或 UITK 兼容。代价是应用必须遵守该规则，违反时可能错误寻址。
- 实际 API/样例/旧 Host Editor smoke 分别 24/18/24 帧通过，静态 GUID/原生源码/库/链接检查通过。新 API 没有新的 Player/其他平台/性能结论。
- 原始证据在独立 task-3 工作目录；可复现 Editor fixture 随候选存档于 Tests~/Editor。保留工作分支用于审查，不合并，不修改 tag，不连续发布第二预览。
