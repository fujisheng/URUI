# 代码风格与组织

参考用户实际仓库的共同习惯，应用于新托管接口、示例和本次触及的公共边界：四空格、Allman 花括号、PascalCase 公共成员、camelCase 字段、小文件职责、中文 XML 接口注释。没有对底层渲染器或第三方做全库格式化。

证据（实际读取的文件与固定树引用）：

- [FUI BindableProperty.cs](https://github.com/fujisheng/FUI/blob/23391f715535bbfada132944d1a94da51403fa06/Packages/com.fujisheng.fui/Runtime/Core/Bindable/BindableProperty.cs)：公共属性/方法、字段、事件、Dispose 和中文文档。
- [FUI IElement.cs](https://github.com/fujisheng/FUI/blob/23391f715535bbfada132944d1a94da51403fa06/Packages/com.fujisheng.fui/Runtime/Core/Element/IElement.cs)：窄接口和只读属性。
- [UnityCli UnityCliToolBase.cs](https://github.com/fujisheng/UnityCli/blob/e782e97b0d0f3353a92d362e18b4a986989cccdb/Editor/Core/UnityCliToolBase.cs)：缩进、命名空间、校验、清晰错误。

FUI 与 UnityCli 细节并非完全统一，本次选取共同模式，不声称存在统一团队规范，也未修改这些参考仓库。

`Runtime/Managed/` 的 URUI.Runtime 是新用户外观；原 RmlUnity.Runtime 和 RmlUnity namespace 是兼容实现边界。未来维护优先从新外观进入，不把 native 指针、令牌、借用内存或实验开关扩展到普通业务接口。保留原脚本 GUID、shader 标识、原生文件名和许可证。
