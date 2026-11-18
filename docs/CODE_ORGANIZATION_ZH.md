# 代码组织

主包位于 Packages/com.fujisheng.urui，可选效果包位于 Packages/com.fujisheng.urui.effect，Editor-only 作者工具位于 Packages/com.fujisheng.urui.authoring。命名空间按职责组织，程序集区分 Runtime 与 Editor。

## Core

| 相对主包的目录 | 职责 |
| --- | --- |
| Runtime/Hosting | Surface、共享输入与提交 |
| Runtime/Documents | Document 配置、加载、节点工厂、事件及生命周期 |
| Runtime/Elements | 节点身份、查询、层级、样式与坐标 |
| Runtime/Controls | 类型化控件和作者控件组合 |
| Runtime/Events | 同步传播及输入/值/布局事件 |
| Runtime/Resources | TextResource、图片、租约和 GPU 退役帮助接口 |
| Runtime/Styles、Runtime/Animation | 计算样式、命名动画、时钟与通用行为 |
| Runtime/Media、Runtime/Rendering | 视频输出与公开绘制/效果扩展 |
| Runtime/Internal | RenderHost、互操作、几何、批次、图层与资源实现 |
| Runtime/Debug | 由 URUI_DEBUG 控制的诊断实现 |
| Runtime/Resources/URUI、Runtime/Plugins | 共享 CSS/Shader/字体与七平台原生插件 |
| Editor | 导入、菜单及作者/资源校验 |
| Native~/Engine、Native~/ThirdParty | 原生引擎和固定第三方源码 |

## Effect

Runtime 根目录提供 EffectModule、EffectController、EffectPreset、EffectPlayback 和参数；Runtime/Elements 提供声明式粒子节点；Runtime/Rendering 提供纹理/表面及内部效果实现；Editor 提供资产与预览工具。

各包的 Documentation~ 为使用事实源，Samples~ 为可导入样例，Tests/ 与 Tests~/ 为相应测试入口。完整 API 清单见 [Core 接口参考](../Packages/com.fujisheng.urui/Documentation~/REFERENCE_ZH.md)、[Effect 接口参考](../Packages/com.fujisheng.urui.effect/Documentation~/REFERENCE_ZH.md)与 [Authoring 接口说明](../Packages/com.fujisheng.urui.authoring/Documentation~/editor-api.md)。

移动脚本、资源及目录时同时处理对应 .meta，保留已有组件身份。partial 文件共享同一对象的状态和所有权，不应因拆分改变初始化、通知或退役顺序。格式与 XML 注释要求见[代码约定](../Packages/com.fujisheng.urui/Documentation~/CODE_STYLE_ZH.md)。
