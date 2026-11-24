# 日冕旅团 Solstice — URUI Core 核心样例

[English](README.md) | **中文**

**《日冕旅团 · Solstice》** 是 URUI Core（`com.fujisheng.urui`）的零依赖旗舰示范样例：仅依赖核心包 `URUI.Runtime`，不引入 `URUI.Effect` 或 `URP`，同时兼容 Built-in RP 与 URP 管线。

## 快速打开

1. 在 Unity 编辑器中打开 **Window → Package Manager → URUI → Samples → Solstice → Import**，
   或直接点击顶部菜单 **Tools → URUI → Open Solstice Sample**（自动导入并打开 `Solstice.unity`）。
2. 点击 **Play** 体验完整多页面战术 RPG HUD：
   - **圣殿大厅（Lobby）**：夜曜靛蓝高对比 HUD 与全景圣殿背景、看板英灵切换（艾蕾莉亚 / 凯尔 / 莱拉 / 薇拉）、委托任务与斜切出征按钮。
   - **首领战斗（Combat）**：BOSS 血条/护盾 `ProgressBar`、1x/2x 倍速、自动战斗以及 4 个使用 `Image.FillMode = Radial360` 驱动环形冷却扇面的技能槽位。
   - **英灵殿（Heroes）**：英灵属性面板、等级强化升级与旅团主将部署。
   - **星盘祈愿（Summon）**：基于核心 `SequencePlayer` 多轨关键帧（星盘旋转 + 卡牌翻转揭示）与 80 抽保底进度条。
   - **万级军械库（Armory）**：基于 `ListView` 虚拟化池化渲染 10,000 件圣器，支持分类筛选、关键词实时检索与装配/锁定。
   - **系统设置与性能监控（Settings / Dev）**：`@property` 变量主题一键切换（夜曜靛蓝 / 烬火绯红 / 晨曦玉石），以及实时展示 `Surface.GetRenderingStatistics()` 指标的遥测面板。

## 完整 URP + Effect 版本

如需体验 URP 3D 视差背景合批（`UruiUrpRendererFeature`）、`TextureEffectSurface` 定制 HLSL 横幅、`<urui-particles>` 粒子特效、`ui-wave` 波浪特效、`ArcTextLayout` 弧形字带与 `CompiledDocumentAsset` 预编译，请直接打开仓库根目录 [`SampleProject/`](../../../../SampleProject)。
