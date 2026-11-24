# 动画示例

通过 Package Manager Import 到 Assets。双击 Reward.html，切换“动画”，播放或拖动时间针。选中轨道后在 0..单轮时长内开启录制，用 Inspector 修改透明度/变换，再保存。示例只依赖 Core + Authoring。

正式运行时由业务安装 Document 的文本/图像资源，在页面加载并完成布局后，解析 Reward.urui-sequence.json 并构造 SequencePlayer(document.Body, definition)。页面不自动启动序列。
