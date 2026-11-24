# 性能优化、渲染合批与内存安全机制

[English](PERFORMANCE_AND_MEMORY.md) | **中文**

URUI 的核心设计目标是在保持完整 HTML/CSS 表达力的同时，将稳态 CPU、GC 分配与 GPU/原生内存控制在严格可预测的范围内。

## 1. 零节点膨胀与托管低分配路径

- **零逐节点 `MonoBehaviour`**：无论页面有多少层嵌套节点，每个画面只需挂一个 `Document`，彻底避免 UGUI `LayoutRebuilder` 级联重建与 `GameObject` 层级同步开销。
- **稳态缓存与缓冲复用**：`DocumentsSnapshot()` 仅在成员集合变更时更新；事件派发路径快照与 UTF-8 原生读取缓冲跨帧复用；延后队列采用值类型记录，避免每帧分配闭包对象。
- **单调安全 UI 时钟（`UnityUIClock`）**：内置亚微秒舍入保护，兼容 Editor 停顿、Frame Debugger 重放以及 `FixedUpdate` / `Update` 时钟推进，避免因单调误差抖动触发时间反转误报。

## 2. `render-plan9` 渲染合批与参数页机制

- **单批 8 纹理合批槽**：内置 Shader 在单次 Draw Call 中可同时绑定最多 8 张纹理，允许字体图集、SVG/PNG 图标、借用贴图自由混排合批，无需离线强行拼合成整张大合图。
- **静态几何 + 参数页（Parameter Pages）**：普通静态 UI 编译一次顶点拓扑后，后续缩放、透明淡入淡出与色偏修改仅增量上传参数纹理的少数像素行，避免每帧重传整页 Mesh 顶点。
- **有界空闲 Mesh 池淘汰**：针对动态顶点数高频抖动的场景（动态曲线、文本动画等），`RenderPlanResources` 会自动上限裁剪过期小容量 `Mesh` 桶，防止长时间运行产生显存缓慢爬升。

## 3. 严格 GPU Fence 退役与防泄漏契约

- **真实 GPU Fence 保护**：任何尺寸变更、图片卸载或离屏捕获纹理（`RenderTexture` / `Mesh` / `Material`）均通过 GPU Fence 追踪所有正在执行的指令队列（每个 `Surface` 上限保留 8 份在途提交），待最后一个读取者完成后自动销毁或回池。
- **空输入与零输出早期剪枝**：若裁剪组无实质像素或无外部消费者，原生计划直接跳过对应的离屏 `RenderTexture` 分配与拷贝通行。

## 4. 公开实时统计接口（`Surface.GetRenderingStatistics`）

自 `0.8.0-preview.1` 起，无需开启 `URUI_DEBUG`，即可在 Editor 与正式发布的 Player 中直接读取真实渲染与原生内存统计（详见《日冕旅团 · SOLSTICE》右上角 DEV 遥测抽屉）：

```csharp
var stats = surface.GetRenderingStatistics();
// 可读字段：
// Ready / LoadedDocuments / RecordedGeometryDraws / RecordedExternalGeometryDraws
// NativePlanCommandCount / NativePlanGeometryBytes / NativePlanFrameBytes
// NativePlanAllocations / NativePlanFrees / NativePlanAllocatedBytes
// GeometryUploadBytes / ParameterUploadBytes / ParameterCopyBytes
```

## 5. 实战调优清单

1. **长列表必用固定行高 `ListView`**：如《日冕旅团 · SOLSTICE》10,000 件军械库示例，实时实例化的行节点数（`RealizedItemCount`）也始终维持在 15 行左右。
2. **正式页面使用 AOT `CompiledDocumentAsset`**：通过 `CompiledDocumentCompiler.CompileAndSave` 将 HTML + CSS 闭包预编译为二进制计划资产（如 `Backpack.compiled.asset`），彻底跳过启动时源码词法与 CSS 解析开销。
3. **主题换肤优先使用 CSS 变量**：利用 `@property` 切换根容器类名（如 `theme-obsidian` / `theme-crimson` / `theme-alabaster`），避免重构 DOM 或卸载重建纹理。
4. **字体按需子集化（Font Subsetting）与贴图禁用 Mipmap**：UI 贴图关闭 Streaming / Mipmap；中文字体推荐按实际文案子集化（例如 SOLSTICE 将 10 MB 完整中文字体子集化压缩至 ~200 KB，显著降低 FreeType 字形缓存内存占用）。
