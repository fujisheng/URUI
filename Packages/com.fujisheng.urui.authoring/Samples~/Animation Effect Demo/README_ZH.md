# UI + Effect + 粒子统一动画

需要安装 Effect，并将本示例 Import 到 Assets。打开 Reward.html → 动画 → 播放：弹窗、效果参数和粒子共享主时间。粒子 Prefab/Material 使用 URUI 粒子材质契约，原始资源只在明确 Apply 后修改。

正式运行时安装 EffectModule(document, resources)，让 resources 解析 confetti.prefab；页面需加载 /urui-effect/Effect.css，再创建 SequencePlayer。Core/Authoring 不自动依赖 Effect。
