# North / 07 整稿到资源的制作记录

用户于2026-11-02 07:19:54确认 Home.design-master.v1.png / Inventory.design-master.v2.png 的整体方向，随后授权切图和本地实现。本轮严格沿用这两张整稿，四张延伸页面也先生成完整设计再实现；此前独立素材方案保留在 Artifacts/GameFlow/PreviousDesign，没有继续使用。

实际读取的参考是 [fujisheng/fui-cli](https://github.com/fujisheng/fui-cli/tree/0e09b7fbf5ba814bf53175ea550f7af7775cdf75) 的 `Skills/fui-cli/SKILL.md`、`references/asset-generation-workflow.md`、`scripts/bbox-review/README.md`、`scripts/process-ui-assets/README.md`。

## 已实际执行

1. 固定两张已确认整稿；原始工具画布均为1672×941，未伪称请求的2048×1152已经输出。运行时横屏验收明确设为1280×720。
2. 按图中真实视觉边界制定20个初始资源框；发现人物框贴近扫描器后先扩框，再生成其SourceCrop/mask。另补6个排行榜头像的精确裁框。裁框不是旧RML点击框。
3. 保存 layer_plan.json、bbox-review-data.json、1:1可调整检查页、overlay、sources、visible/repair mask和edit target。被遮挡世界保存occluder mask与hole source。框由执行者在获批设计和继续实施授权下审阅，未伪称用户单独确认过每个框。掩码是direct-repaired指导，不声称逐像素锁定或全自动语义分割。
4. 以SourceCrop实际进入imageGen：纯世界、完整人物、空按钮、空面板、选中框、原图标strip及十件物品。没有传入商业游戏资产或工程截图。保留原始输出、提示词、尺寸、失败和重试记录。断线期间物品09保留原始返回数据，恢复后落盘未重画；物品10在已知未生成的情况下重试。
5. 原生Alpha检查前后均执行。生产资源显式按Alpha>8 bbox裁边、contain缩放和留边；原图不覆盖。面板九切片、动作三切片、图标strip裁切是确定性处理。图标小噪点移除、货币边界连通matte分支均明确记录，未静默扣色或冒充原生透明。实际看过深/浅底和Editor组合。
6. 包内交付40张独立PNG和可读Texture2D导入设置。所有运行时文本/控件仍由独立RML/RCSS/C#公开API承担。完整设计稿不在runtime Images目录。
7. 在迁移导入目录执行真实Editor组合，对照整稿检查布局/比例/材质/层次/间距和控件状态。保存 `.layout.json`、`*.master-vs-editor.png` 和机器报告。修复实际发现的切片固有尺寸越界、引导重试纹理、标题绘制层级、排名条样式污染与共享缺图未验证。

## 记录位置

- 完整整稿：Artifacts/GameFlow/DesignFirst/20261102（含Extensions四页）。
- 制作链：FUI-CLI/GameFlowDesign；主要为layer_plan.json、bbox-review-data.json、sources/、debug/、asset_requests/、ai_alpha_sources/、assets_png/、shared-generation-log.json、item-generation-log.json、asset-processing-report.json。
- 1:1检查页：previews/{Home,Inventory,Leaderboard}.bbox-review.html。页面调整只导出草稿；改框后必须重新生成对应裁图/掩码/输出，不能直接拉伸旧资产冒充新链。
- 包内清单：Samples~/Game Flow/Resources/URUI/GameFlow/asset-manifest.json.txt；完整资源提示词在Images/SOURCE_DESIGN_FIRST.txt。
- 最终组合/布局/功能证据：Artifacts/GameFlow/Editor及verification.json。

## URUI适配及未宣称部分

没有安装或依赖FUI，没有生成UGUI Prefab，也没有手写一个名为visual-ui.json的第二套布局真相。RML/RCSS是唯一运行时布局来源。实际Editor组合及公开API布局JSON替代Web→UGUI手交步骤；不宣称上游全部Web/UGUI门禁已执行。制作记录保留在本工作区，运行不依赖这些记录或任何本机绝对路径。

这次还原不是逐像素复制；真实字体、可输入字段/下拉、筛选与错误状态会与生成稿不同。已有renderer的变换裁剪、高级layer/filter等边界保持如实记录，没有为视觉工作改原生ABI或七份插件。所有截图只留本地，未上传到会话/Space；新原创imageGen结果由工具正常展示。
