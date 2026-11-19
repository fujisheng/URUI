using System.Collections.Generic;

namespace URUI.Editor.Authoring
{
    /// <summary>
    /// 原生CSS解析器展开的上、右、下、左切片；避免作者工具重复解析简写。
    /// </summary>
    public sealed class CssBorderImageInfo
    {
        public bool Fill
        {
            get;
        }
        public IReadOnlyList<float> Slices
        {
            get;
        }
        public IReadOnlyList<bool> Percentages
        {
            get;
        }
        internal CssBorderImageInfo(bool fill, float[] slices, bool[] percentages)
        {
            Fill = fill;
            Slices = System.Array.AsReadOnly(slices);
            Percentages = System.Array.AsReadOnly(percentages);
        }
    }
    /// <summary>
    /// 来自原生 CSS AST 的声明副本；位置指向作者原始字节。
    /// </summary>
    public sealed class CssDeclarationInfo
    {
        public string Property
        {
            get;
        }
        public string Value
        {
            get;
        }
        public string Identifier
        {
            get;
        }
        public IReadOnlyList<string> Urls
        {
            get;
        }
        public CssBorderImageInfo BorderImage
        {
            get;
        }
        public bool Important
        {
            get;
        }
        public int Line
        {
            get;
        }
        public int Column
        {
            get;
        }
        public int ByteOffset
        {
            get;
        }
        internal CssDeclarationInfo(string property, string value, string identifier, bool important, int line, int column, int offset, string[] urls, CssBorderImageInfo borderImage = null)
        {
            BorderImage = borderImage;
            Urls = System.Array.AsReadOnly(urls ?? System.Array.Empty<string>());
            Property = property;
            Value = value;
            Identifier = identifier;
            Important = important;
            Line = line;
            Column = column;
            ByteOffset = offset;
        }
    }
    /// <summary>
    /// 有明确子规则和声明的 CSS 规则，不在 C# 中再次拆分规则块。
    /// </summary>
    public sealed class CssRuleInfo
    {
        public string AtRule
        {
            get;
        }
        public string Prelude
        {
            get;
        }
        public int Line
        {
            get;
        }
        public IReadOnlyList<CssDeclarationInfo> Declarations
        {
            get;
        }
        public IReadOnlyList<CssRuleInfo> Children
        {
            get;
        }
        internal CssRuleInfo(string atRule, string prelude, int line, List<CssDeclarationInfo> declarations, List<CssRuleInfo> children)
        {
            AtRule = atRule;
            Prelude = prelude;
            Line = line;
            Declarations = declarations.AsReadOnly();
            Children = children.AsReadOnly();
        }
    }
    /// <summary>
    /// 独立的校验结果，不持有活动文档或原生解析内存。
    /// </summary>
    public sealed class CssValidationResult
    {
        public IReadOnlyList<StyleDiagnostic> Diagnostics
        {
            get;
        }
        public IReadOnlyList<CssRuleInfo> Rules
        {
            get;
        }
        internal CssValidationResult(List<StyleDiagnostic> diagnostics, List<CssRuleInfo> rules)
        {
            Diagnostics = diagnostics.AsReadOnly();
            Rules = rules.AsReadOnly();
        }
    }
}
