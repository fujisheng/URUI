using System;
using System.Collections.Generic;
using UnityEngine;
namespace URUI.Editor.Authoring
{
    public sealed class StyleDiagnostic
    {
        public string Code { get; }
        public string Message { get; }
        public string Uri { get; }
        public int Line { get; }
        public int Column { get; }
        public int ByteOffset { get; }
        public bool IsError { get; }
        public StyleDiagnostic(string code, string message, string uri, int line = 0, int column = 0, int byteOffset = 0, bool isError = true)
        { Code = code; Message = message; Uri = uri; Line = line; Column = column; ByteOffset = byteOffset; IsError = isError; }
    }
    /// <summary>使用与运行时相同的 CSS 解析器和结构化诊断，不占用或替换活动文档。</summary>
    public static class NativeStyleValidator
    {
        public static IReadOnlyList<StyleDiagnostic> Validate(string uri, TextAsset source)
        {
            if (source == null) return new[] { new StyleDiagnostic("missing-style", "样式资源不存在。", uri) };
            return HtmlCssValidator.ValidateCss(source.text, uri).Diagnostics;
        }
    }
}
