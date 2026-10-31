# RmlUnity → URUI

| 项目 | 旧名 | 当前名 |
| --- | --- | --- |
| 品牌 | RmlUnity | URUI / UnityRmlUi |
| GitHub | fujisheng/RmlUnity | fujisheng/URUI |
| UPM ID / 目录 | com.fujisheng.rmlunity | com.fujisheng.urui |
| 编辑器菜单 | Tools/RmlUnity | Tools/URUI |
| 新用户接口 | 兼容 Host | 当前未发布候选的 URUI namespace / URUI.Runtime |
| 兼容接口 / 程序集 | RmlUnity / RmlUnity.Runtime | 保留 |
| 原生 ABI / shader 标识 / GUID | 原值 | 保留 |

先提交或备份应用及场景，移除 manifest 中旧包依赖（或旧 embedded 包），再添加新包。**不能同时安装**，因为保留了相同脚本 GUID 与兼容类型。迁移 UPM 锁文件时让 Unity Package Manager 重新解析，不手改缓存。

旧脚本 `using RmlUnity;`、`RmlUnityHost`、asmdef 的 `RmlUnity.Runtime` 引用及序列化字段继续有效。保留这些名字是有意的兼容边界；普通用户的新入口已在候选的 URUI namespace 中提供，避免把底层 native 生命周期暴露出去。原生 `libRmlUnityBridge.dylib`、`rui_*` 导出、`__RMLUNITY_FONT__` 字体占位符及既有 shader 标识保持原值，不将其当作新的平台 ABI。

新安装地址必须使用 URUI 仓库和新包路径。仓库网页改名重定向不意味着旧 UPM 子目录仍存在。旧 tag / 历史提交的路径按各自内容处理。没有改动第三方 RmlUi、FreeType 名称或历史验收文件。

此预览基于 `afa24847342f16c64566fe005466aabd6b4c5109`，只做分发身份、路径和品牌迁移，不接入暂停的性能研究。后续 API 候选提交与此 tag 分开，安装固定 tag 不会自动取得候选接口。

## 新业务代码迁移到候选外观

- `RmlUnityHost.LoadMarkup(rml)` → `URUI.Document.LoadMarkup(rml)`。
- `host.SetText("label", value)` → `document.Q("label").SetText(value)`；先处理 Q 返回 null。
- `host.SetStyle(id, property, value)` → `document.Q(id).Style.Set(property, value)`。
- `host.Clicked += Action<string>` → 元素 RegisterClickCallback；使用返回的 IDisposable 或成对 Unregister。
- 不把兼容 Host 的实验渲染开关搬到新 Document；新入口保留安全默认。
- 使用新 API 的业务 asmdef 引用 URUI.Runtime；旧代码仍引用 RmlUnity.Runtime。不要把两个旧/新包同时安装。

旧序列化 Host 场景继续加载旧组件；不会偷偷替换成新 Document 或改变字段所有权。需要采用新组件时，在应用副本中移除旧 Host，再添加 URUI.Document 并迁移 Source、字体与图片配置。RmlUnityHost 的旧点击和更新方法仍可使用，不强制迁移。
