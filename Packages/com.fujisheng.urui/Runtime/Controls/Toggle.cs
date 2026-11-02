using System;
using System.Globalization;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>有类型的 Toggle 表单控件。</summary>
    public class Toggle : CheckableField
    {
        /// <summary>文档工厂构造入口。</summary>
        protected internal Toggle(ElementContext context) : base(context) { }
        /// <summary>一次完整键盘激活切换当前值。</summary>
        protected override void Activate() => Value = !Value;
    }
}
