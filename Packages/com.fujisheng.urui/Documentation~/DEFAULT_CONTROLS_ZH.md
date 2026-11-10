# 默认控件与作者皮肤

每份完整 HTML 文档显式链接 `/urui/base.css` 和 `/urui/controls.css`。默认样式提供接近 Unity UGUI 的白灰外观与状态反馈，仍使用标准 HTML/CSS 的受支持子集，无 UGUI 运行依赖。

| 状态 | 图形倍率 | 过渡 |
| --- | --- | --- |
| Normal | `#ffffffff` | 0.1 秒 |
| Highlighted / Selected | `#f5f5f5ff` | 0.1 秒 |
| Pressed | `#c8c8c8ff` | 0.1 秒 |
| Disabled | `#c8c8c880` | 0.1 秒 |

普通按钮默认 160×30，Toggle/Radio 20×20，Slider 160×20。作者的字体、尺寸、背景和特殊状态声明仍按 CSS 层叠生效；没有强制最小按钮尺寸。

```html
<button type="button">确定</button>
<button type="button" data-urui-skin="image" style="width:48px;height:48px;">
  <img src="icon.png" data-urui-graphic>
  <span class="badge">3</span>
</button>
```

同一文档的内置和注册属性合计上限为 511 个（保留无效 ID）；共享控件、完整 URUI.Effect 与作者注册可同时加载，超限会使候选文档加载失败并保留原文档。

`data-urui-skin="image"` 去掉默认背景、边框和内边距；`data-urui-graphic`（或 `.urui-control-graphic`）只指定自身的反馈图形。原生对图片、CSS 背景、边框和 border-image 乘以独立的 `--urui-graphic-tint`，保留作者原有颜色与透明度。子文字、未标记图片、装饰、粒子和遮罩输入不跟随乘色。简单图形通过已有顶点颜色绘制，不额外创建 RenderTexture。

选择最小的图形节点作为目标，避免给包含文字或特效的整个容器打标记。需要保持某个目标不变色时，可在作者 CSS 中设置 `--urui-graphic-tint:white`；自定义状态也可直接覆盖状态变量或图形变量。已有作者背景会覆盖默认按钮背景，特殊皮肤应显式指定自己的反馈图形。

键盘 Space 在按住期间显示 Pressed，释放时激活；Enter 显示短暂反馈。禁用、失焦、隐藏或克隆不保留临时键盘按压状态。

作者下拉皮肤（`urui-select` / `.urui-select-options`）默认 0.15 秒淡入淡出。关闭时立即取消命中，淡出只保留绘制；退出中重开会取消旧完成回调。自定义弹出层可设置 `--urui-select-fade:0` 关闭共享淡入淡出。普通 `<select>` 保留原生基础弹出列表；跨浏览器一致的选中标记和可控淡入淡出使用上述作者皮肤，示例见 Runtime Controls。输入框 placeholder 使用独立的 50% Alpha 和斜体；选区保留作者文字颜色，底色为 `#a8ceffc0`，caret 为 1px、0.85Hz。readonly 保持相同皮肤。

浏览器读取同一份文件。默认 CSS 在声明型 `@supports (property:value)` / `not (property:value)` 子集中选择浏览器路径；URUI 不实现复合布尔 feature query。现代浏览器用标准 brightness/opacity 滤镜预览图片或空图形槽的反馈，原生使用顶点乘色。对包含子节点的 CSS 图形槽，浏览器不会对整个子树施加滤镜。HDR、Linear 色彩空间、游戏 Shader 和粒子不作为浏览器逐像素等价目标。交互式 `urui-*` 皮肤可使用现有 `/urui/preview.js` 预览适配器。
