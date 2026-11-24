# AI 辅助 UI 开发工作流

[English](AI_WORKFLOW.md) | **中文**

URUI 从底层协议到编辑器工作台均围绕**纯文本高可读性、明确状态契约与闭环视觉自检**设计，让 AI Coding Agent 能像编写 Web 前端一样准确、稳定地构建与迭代 Unity 界面。

## 为什么文本驱动比传统 Prefab 更适合 AI

| 传统 UGUI / Prefab 模式 | URUI 工作流 |
| --- | --- |
| 复杂的 `.prefab` YAML 序列化与密集组件 GUID，极易冲突 | 干净的 `.html`、`.css`、`.urui-sequence.json` 与纯托管 C# |
| Agent 难以直观推导深层 `RectTransform` 与锚点联动结果 | 明确 DOM 树、CSS 层叠计算值、来源标识（Origin）与像素坐标 |
| 换肤或调字常导致 Prefab 变脏甚至序列化引用损坏 | `@property` 主题 Token 与外部 CSS 修改安全且极易 Code Review |

## 推荐的 AI 闭环工作流

```text
  [1] 编写语义化 HTML / CSS / .urui-sequence.json
               |
               v
  [2] 桌面浏览器快速初检，或启动 Authoring 本机 HTTP v1 接口
      （读取 Library/URUI/Authoring/endpoint.json）
               |
               v
  [3] Agent 调用 preview.capture 获取真实引擎渲染截图进行视觉检查与精细微调
               |
               v
  [4] 正式上线页面通过 CompiledDocumentCompiler 编译为 CompiledDocumentAsset
```

### 核心协作方式

1. **标准 HTML/CSS 快速草案**：利用 Flexbox + 设计 Token 编写页面草案，直接在桌面浏览器确认配色、留白与折行策略。
2. **Authoring 本机 HTTP Agent 接口 v1**：由人工在 Unity 编辑器按需启用后，Agent 读取 `endpoint.json` 并携带会话修订版本号安全操作：
   - 查询节点：`hierarchy.find`、`hierarchy.children`、`properties.read`
   - 增量编辑：`properties.set`、`style.rule`、`structure.insert / move / remove`
   - 动画编排：`animation.open`、`animation.preset`、`animation.key`、`animation.sample`
   - 实时验图与显式落盘：`preview.capture` 获取渲染路径审阅，确认无误后再触发 `document.save`
   完整协议字段见 [本机 Agent API 规范](../Packages/com.fujisheng.urui.authoring/Documentation~/agent-api_ZH.md)。
3. **写给 Agent 的黄金准则**：
   - 将视觉表现彻底留在 CSS（`tokens.css` / `studio.css`）中，C# 仅通过稳定 `id` 驱动数据与交互订阅。
   - 牢记文档重载会使旧 `Element` 句柄失效，在 `Loaded` / 重新查询中重新绑定订阅并在卸载时 `Dispose()`。
   - 修改布局或字体后务必执行真实截图目视检查（不使用 `-nographics`），核对缺字、遮挡与对比度。
