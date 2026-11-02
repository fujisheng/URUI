using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>普通文本标签，不解析 RML。</summary>
    public class Label : Element
    {
        /// <summary>文档工厂构造入口。</summary>
        protected internal Label(ElementContext context) : base(context) { }
        /// <summary>关联输入控件的 Name；点击标签由原生 label 转交给该控件。</summary>
        public string For { get => GetAttribute("for") ?? ""; set => SetAttribute("for", value); }
    }
}
