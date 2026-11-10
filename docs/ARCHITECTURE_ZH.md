# URUI 架构

当前架构由共享宿主、独立文档、托管节点、原生排版与 Unity 渲染组成。HTML/CSS 拥有结构、样式和关键帧，C# 拥有业务意图；具体接口以[Core API](../Packages/com.fujisheng.urui/Documentation~/API_ZH.md)为准。

## 数据流

1. Surface 管理共享原生上下文、视口、时钟和原始输入。
2. 每个 Document 加载自己的 HTML、CSS、字体与图片资源，创建独立 DOM/样式和资源代际。
3. 原生引擎解析、布局并生成有序绘制命令；托管 Element 包装按所属文档与 token 保持身份。
4. Unity 宿主同步网格/参数，按透明混合依赖合批并提交；GPU 资源等待真实 fence 后退役。
5. Surface.Input 分发输入，原生事件同步进入托管 capture/target/bubble 与控件默认行为。

所有节点操作限 Unity 主线程。Loaded 表示 DOM 可查询；布局、绘制和提交完成分别有自己的时点。成功重载使旧节点失效，失败候选保留旧文档。页面关闭不卸载其他文档。

## 模块边界

| 模块 | 职责 |
| --- | --- |
| Core 公开 API | Hosting、Documents、Elements、Controls、Events、Resources、Styles、Animation、Media、Rendering |
| Core Internal | RenderHost、互操作、几何缓存、批次、图层与退役；不作为业务接口 |
| URUIBridge / Native Engine | 带版本的 C ABI、HTML/CSS、布局、字形及绘制协议 |
| URUI.Effect | 可选效果、预设、播放、粒子和纹理处理；只引用 URUI.Runtime |
| 外部框架适配 | 页面路由、数据绑定、资源管理及具体管线接入；Core 不依赖 FUI |

## 所有权与扩展

Document 拥有附着和脱离节点。Remove/Clear 保留身份，Destroy/结构替换/卸载使相应节点和订阅失效。组合控件通过 ContentContainer 接收业务内容，Hierarchy 表示真实 DOM。

效果在加载前注册，Prepare 消费不可变帧并准备资源，Record 只录制绘制。绘制窗口离开回调后永久失效，不在其中修改 DOM。自定义效果和资源租约见[渲染扩展](../Packages/com.fujisheng.urui/Documentation~/RENDERING_ZH.md)及[生命周期](../Packages/com.fujisheng.urui/Documentation~/LIFECYCLE_ZH.md)。

当前协议 ABI8 / style4 / effect7 / filter3 / animation1，正式 C 导出 176 个。诊断由 URUI_DEBUG 显式开启且默认关闭，线程、生命周期与资源安全校验始终保留。平台验证范围见[支持矩阵](../Packages/com.fujisheng.urui/Documentation~/LIMITATIONS_ZH.md)。
