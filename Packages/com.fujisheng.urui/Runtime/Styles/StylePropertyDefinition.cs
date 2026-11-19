using System;
namespace URUI.Styles
{
    public enum StyleValueType
    {
        Number,
        Pixels,
        Color,
        Keyword,
        Image
    }
    /// <summary>
    /// 效果所需的计算样式类型；CSS 仍拥有默认值和继承规则。
    /// </summary>
    public sealed class StylePropertyDefinition
    {
        public string Name
        {
            get;
        }
        public StyleValueType Type
        {
            get;
        }
        public StylePropertyDefinition(string name, StyleValueType type)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("样式参数名不能为空。", nameof(name));
            }
            if (!Enum.IsDefined(typeof(StyleValueType), type))
            {
                throw new ArgumentOutOfRangeException(nameof(type));
            }
            Name = name;
            Type = type;
        }
    }
}
