using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace URUI.Editor.Authoring
{
    public sealed class HtmlDiagnostic
    {
        public string Code
        {
            get;
        }
        public string Message
        {
            get;
        }
        public string Uri
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
        public bool IsError
        {
            get;
        }
        internal HtmlDiagnostic(string code, string message, string uri, int line, int column, int byteOffset, bool error)
        {
            Code = code;
            Message = message;
            Uri = uri;
            Line = line;
            Column = column;
            ByteOffset = byteOffset;
            IsError = error;
        }
    }
    public sealed class HtmlNodeInfo
    {
        public string Tag
        {
            get;
        }
        public string Id
        {
            get;
        }
        public string Classes
        {
            get;
        }
        public string Source
        {
            get;
        }
        public string Href
        {
            get;
        }
        public string Rel
        {
            get;
        }
        public string InlineStyle
        {
            get;
        }
        public string StyleText
        {
            get;
        }
        public int Line
        {
            get;
        }
        public bool IsInert
        {
            get;
        }
        internal HtmlNodeInfo(string tag, string id, string classes, string source, string href, string rel, int line, bool inert, string inlineStyle, string styleText)
        {
            InlineStyle = inlineStyle;
            StyleText = styleText;
            Tag = tag;
            Id = id;
            Classes = classes;
            Source = source;
            Href = href;
            Rel = rel;
            Line = line;
            IsInert = inert;
        }
    }
    public sealed class HtmlValidationResult
    {
        public IReadOnlyList<HtmlDiagnostic> Diagnostics
        {
            get;
        }
        public IReadOnlyList<HtmlNodeInfo> Nodes
        {
            get;
        }
        public bool IsValid
        {
            get
            {
                foreach (var diagnostic in Diagnostics)
                {
                    if (diagnostic.IsError)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        internal HtmlValidationResult(List<HtmlDiagnostic> diagnostics, List<HtmlNodeInfo> nodes)
        {
            Diagnostics = diagnostics.AsReadOnly();
            Nodes = nodes.AsReadOnly();
        }
    }
    /// <summary>
    /// 与运行时共用严格 HTML 解析器，不维护另一套 C# 标记语法。
    /// </summary>
    public static partial class HtmlCssValidator
    {
        [Serializable]
        sealed class Packet
        {
            public Diagnostic[] diagnostics;
            public Node[] nodes;
        }
        [Serializable]
        sealed class Diagnostic
        {
            public string code;
            public string message;
            public string uri;
            public string severity;
            public int line;
            public int column;
            public int byteOffset;
        }
        [Serializable]
        sealed class Node
        {
            public string tag;
            public string id;
            public string classes;
            public string source;
            public string href;
            public string rel;
            public string inlineStyle;
            public string styleText;
            public int line;
            public bool inert;
        }
        [DllImport("URUIBridge", CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_validate_html([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceUrl, int fragment, [Out] byte[] output, int capacity, out int required);
        public static HtmlValidationResult Validate(string text, string sourceUrl, bool isFragment)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            if (string.IsNullOrWhiteSpace(sourceUrl))
            {
                throw new ArgumentException("HTML 校验需要来源 URL。", nameof(sourceUrl));
            }
            var diagnostics = new List<HtmlDiagnostic>();
            var nodes = new List<HtmlNodeInfo>();
            if (text.IndexOf('\0') >= 0)
            {
                diagnostics.Add(new HtmlDiagnostic("HTML_NULL", "HTML 不能包含 NUL。", sourceUrl, 1, 1, 0, true));
                return new HtmlValidationResult(diagnostics, nodes);
            }
            try
            {
                var bytes = new byte[4096];
                var status = urui_validate_html(text, sourceUrl, isFragment ? 1 : 0, bytes, bytes.Length, out var required);
                if (status == -3 && required > bytes.Length)
                {
                    bytes = new byte[required];
                    status = urui_validate_html(text, sourceUrl, isFragment ? 1 : 0, bytes, bytes.Length, out required);
                }
                if (status < 0)
                {
                    diagnostics.Add(new HtmlDiagnostic("native-html-failure", "原生 HTML 校验失败：" + status, sourceUrl, 0, 0, 0, true));
                }
                else
                {
                    var packet = JsonUtility.FromJson<Packet>(Encoding.UTF8.GetString(bytes, 0, required - 1));
                    foreach (var value in packet.diagnostics ?? Array.Empty<Diagnostic>())
                    {
                        diagnostics.Add(new HtmlDiagnostic(value.code, value.message, value.uri, value.line, value.column, value.byteOffset, value.severity == "error"));
                    }
                    foreach (var value in packet.nodes ?? Array.Empty<Node>())
                    {
                        nodes.Add(new HtmlNodeInfo(value.tag, value.id, value.classes, value.source, value.href, value.rel, value.line, value.inert, value.inlineStyle, value.styleText));
                    }
                }
            }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException || error is BadImageFormatException)
            {
                diagnostics.Add(new HtmlDiagnostic("plugin-capability", "已安装插件缺少 HTML 作者校验入口：" + error.Message, sourceUrl, 0, 0, 0, true));
            }
            return new HtmlValidationResult(diagnostics, nodes);
        }
    }
}
