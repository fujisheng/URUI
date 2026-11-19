using System;

namespace URUI.Styles
{
    public enum ComputedImageKind
    {
        None,
        Url,
        Gradient
    }

    /// <summary>
    /// 由原生 CSS image 解析器验证的不可变图像值，保留声明来源用于相对资源解析。
    /// </summary>
    public sealed class ComputedImageValue
    {
        public ComputedImageKind Kind
        {
            get;
        }
        public string Url
        {
            get;
        }
        public string CssText
        {
            get;
        }
        public string SourceUrl
        {
            get;
        }

        internal ComputedImageValue(int kind, string value, string sourceUrl)
        {
            if (kind < 0 || kind > 2)
            {
                throw new NotSupportedException("未知的原生图像类型。");
            }
            Kind = (ComputedImageKind)kind;
            SourceUrl = sourceUrl;
            Url = Kind == ComputedImageKind.Url ? value : null;
            CssText = Kind == ComputedImageKind.None ? "none" : Kind == ComputedImageKind.Gradient ? value
                : "url(\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")";
        }
    }
}
