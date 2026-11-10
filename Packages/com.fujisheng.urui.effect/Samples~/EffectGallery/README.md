# URUI Effect Gallery

独立效果示例，使用 URUI Core `0.7.1-preview.1` 和 Effect `0.4.1-preview.1`，协议 ABI8 / style4 / effect7 / filter3 / animation1。样例程序集只引用 URUI.Runtime 与 URUI.Effect。

## 导入和运行

在 Package Manager 选择 URUI Effect → Samples → Declarative Effect Gallery → Import。创建启用的 MainCamera 和带 EffectGallery 组件的对象，按 Inspector 配置资源：

| 字段 | 样例资源 |
| --- | --- |
| html / style | Gallery.html / Gallery.css |
| white / shader | white.png / Gallery.shader |
| source / pattern / gradient | input.png / pattern.png / gradient-lut.png |
| 粒子源 | 可选纯 ParticleSystem prefab；未指定时使用固定种子的示例粒子源 |

进入 Play Mode。节点没有逐个挂载 MonoBehaviour，布局、皮肤和静态参数来自作者资源。完整安装、CSS 挂载和模块调用顺序见[快速开始](../../Documentation~/QUICK_START_ZH.md)。

## 观察和操作

图库覆盖 13 组、83 个枚举条目，提供默认/极值/关闭三类示例。GalleryCoverage.json 将覆盖 ID 关联到声明及成本说明。标签上的采样数、捕获数描述算法工作量，不代表 GPU 耗时。

Play/Pause/Resume/Stop/Restart 和 range 输入对应 EffectController/EffectPlayback。第二个目标使用独立联动快照；手动 Seek 不发送自然成功。粒子使用 urui-particles、src 与 data-urui-* 显隐策略，可通过 ParticlesElement 显式控制。

浏览器预览使用同一 Gallery.html/Gallery.css/input.png，需要将 `/urui/base.css` 和 `/urui-effect/Effect.css` 映射到包资源。开发仓库 `URUI/Tools/HtmlCss/serve.py` 提供映射；单独安装的包不依赖该开发脚本。浏览器用于标准布局与滤镜对照，扩展效果保留原图，粒子显示有尺寸的占位。

参数接口见 [C# 使用](../../Documentation~/API_ZH.md)、[CSS 参数](../../Documentation~/CSS_ZH.md)和[渲染资源](../../Documentation~/RENDERING_ZH.md)。自带纹理不依赖游戏美术资源；分发时保留包内 [ThirdPartyNotices.md](../../ThirdPartyNotices.md) 和原许可证。
