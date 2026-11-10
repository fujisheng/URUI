# Changelog

## 0.7.1-preview.1 — 视觉缺陷修复

- 共享文本布局保留 HTML hidden 状态。
- 标准单层 border-image 复用图片效果解析、网格和缓存，无需额外覆盖图。
- 字体 dilate 使用文本颜色替代单色基字层；outline/dilate 支持小数 px 与字号相对 em。
- 作者 AST 附带原生解析的切片信息，资源尺寸校验提示空中心填充。
- C ABI8、176个正式导出保持不变；本轮验证记录见 artifacts/urui-visual-bugs-20261110。

## 0.7.0-preview.1 — Profiler 驱动的通用性能优化

- 绘制回调窗口改为带 generation 守卫的只读值类型；公开旧窗口仍永久失效。EffectFrame 改为只读值快照，属于源码 API 调整。
- 延后执行队列保存私有值记录，避免每次 RunAfterEvent 再创建两层闭包；保留片段回滚、执行顺序和异常规则。
- 合并原生元素有效性、挂载与布局读取，删除显式同步的重复文档刷新；复用精确 UTF-8 读取缓冲和稳定动画成员快照。
- 普通命令与变换由原生 memcmp 始终维护精确版本，复用内部几何与分组，动态纹理每次重绑；外部 CommandBuffer 每次清空重录。
- 空 painter 和仅 DrawOriginal 的图片效果不再主动分割普通批次。私有退役容器、pin 和材质租约仅在真实 GPU fence 完成后复用。
- 默认 Unity 时钟向原生同步时吸收不超过1微秒的采样舍入回退，支持 Frame Debugger 暂停重放；自定义时钟和较大回退继续严格拒绝。
- ABI8，新增 urui_element_refresh_state，删除 urui_render_plan_enable；176 个正式导出，七平台同源构建。当前运行验证范围以 PLUGINS.json 为准。

## 0.6.1-preview.1 — 默认性能路径

- 删除宿主性能开关、参数表/原生帧实验模式及整段命令保留模式；普通渲染固定使用集合和属性块复用、属性 ID、裁剪与自动合批。
- 非相交普通几何按空间依赖整理纹理批次，相交透明图形保留 source-over 顺序；paint、图层、裁剪、变换及未知 Shader 保留正确性边界。排序拓扑有界且可缓存。
- 内置 Shader 固定每批最多绑定 8 张原纹理，顶点携带纹理槽；图片与生成字体可同批，保留各自尺寸、翻转、寻址和预乘语义，不制作图集或复制图片。透明相交图形也按三角形顺序混合，超出绑定预算自动分批。
- 文档元素及布局通知只在成员变化时替换快照；回调新增元素、订阅变化和重载继续使用稳定遍历语义。
- 私有 CPU 几何和批次 staging 复用，GPU Mesh 按需创建并保留独立版本；SRP 提交记录与图片 pin 集合只在真实 fence 完成后复用。
- 横向内容测量、滚动和效果使用稳定类型化样式；空闲滚动不再排队写入相同位置。公共效果帧和回调窗口保持独立生命周期。
- 原生源码与 ABI 不变。本版本仍可能因效果回调快照、输入、业务逻辑和动态 GPU Mesh 版本产生分配；不承诺任意页面全局零 GC 或与 UGUI 相同批次。

## 0.6.0-preview.1 — 默认控件

- 默认按钮、输入、切换、滑块、下拉和滚动条使用中性白灰皮肤；反馈色与时长对齐 UGUI 默认 ColorBlock。
- 显式图形目标支持图片、CSS 背景和 border-image 的 RGBA 乘色，保留作者颜色/Alpha，避免影响非目标装饰与遮罩，不新增简单图形的离屏目标。
- 补齐键盘按压、下拉 0.15 秒淡入淡出及中途重开、独立 placeholder 半透明斜体、保留选区文字色和 0.85Hz 光标。
- 增加声明型 `@supports` / `not` 子集，供同源浏览器预览选择自身支持的标准样式。
- 内部 CSS 属性 ID 扩为 16 位、注册总量上限 512，支持共享控件、完整效果和作者注册同时存在；超限保留加载失败回滚。
- 属性集合按非空字迭代；重复的数值/颜色动画采样不再脏化样式；图片直绘策略按样式版本与捕获要求复用，保持参数变动及捕获切换的失效。
- ABI7 / style4 / effect7 / filter3 / animation1 保持不变；176 个正式 C 导出。各平台实际验证范围见插件清单与支持矩阵。

## 0.4.0-preview.1 — HTML/CSS 候选

- 原生派生核心、头文件、构建目标与命名统一为 URUI；保留上游版权和依赖溯源。
- 严格 HTML DTO 建树和标准 CSS 词法、层叠、变量、文档级注册/字体匹配；作者文件为真实 HTML/CSS，非空元素显式闭合。
- 共享 Surface 承载独立 Document；显式文档身份、候选事务、输入路由、模态与焦点恢复，FUI 顶层页面各自加载完整文档。
- 标准控件、图像、边框、渐变、透明组合与小数字号接入；游戏绘制、滤镜、资源租约与 GPU 退役保留独立扩展。
- 移除旧解析/模板/原生数据绑定/脚本钩子、无消费者包装和未链接分发模块；不提供旧接口别名。
- 原生 ABI6，168 个正式 C 导出。实际版本及各平台构建/运行范围以候选清单为准；不把编译成功作为设备运行验收。


## 0.2.0-preview.1

- 补全控件基础样式、布尔选择按键状态/单选导航、文本/数值只读与延后提交、数值步进、滑块方向/反向及进度范围。
- 补全滚动客户区、ScrollTo、列表键盘选择/确认和绑定清理边界；修复回调内重载、反向步长和多指焦点问题。
- 新增文档工厂、可继承 Element、有类型查询、内容容器、样式与几何读取。
- 新增同步事件传播、指针捕获/取消、多指状态、焦点导航、键盘和文本组合接口。
- 新增常用控件、统一值变化协议、固定行高 ListView 与 Runtime Controls 样例。
- ClickEvent 升级为 EventBase 派生类；文档回调重载在原生调用返回后执行。
- 保持 ABI3 布局，新增元素协议版本 1，更新七平台同源插件；各平台实际验证范围见支持矩阵。

## 0.3.0-preview.1 — 既有版本记录

- ImageAsset 直接借用现有 Unity Texture2D，支持不可读颜色纹理及同图多路径共享；原生只保留元数据，取消图片 RGBA 与额外 GPU 纹理副本。字体等生成纹理继续由 URUI 拥有。
- 添加 texture protocol v1、WebGL32句柄转换与七平台同步插件；渲染正确处理借用图片 UV/预乘/颜色，不修改调用者 sampler。完整常驻 mip0 约束及生命周期见资源文档。


- Fix touch scrolling being cancelled by a bubbled keyboard-focus Blur event during pointer down.
- Make Game Flow the recommended package sample: six external HTML pages, shared CSS, original bundled resources, cancellable mock services, preferences, inventory and rankings.
- Add standard Unity Sample import/open menu and a continuous Editor gameplay-UI recording in the package README.

- Provide `URUI.Documents.Document`, `Element`, `ElementStyle`, `ImageAsset` and `ClickEvent` in `URUI.Runtime`.
- Support HTML loading, ID queries, text and structure updates, CSS styles, 2D transforms, image mappings and disposable click subscriptions.
- Define main-thread access, one active document, element invalidation and resource ownership.
- Separate hosting, input, rendering and native interop into internal responsibility namespaces, with the URUIBridge native library and `urui_*` exports.
- Include Hello URUI, the minimal scene menu, API guides, the support matrix and the roadmap for AI-assisted Unity UI development.
- Bundle native plugins for Android ARM64/x86_64, Windows x86_64, macOS ARM64/x86_64, iOS device ARM64 and WebGL wasm32; distinguish compilation/link checks from Unity runtime validation.
