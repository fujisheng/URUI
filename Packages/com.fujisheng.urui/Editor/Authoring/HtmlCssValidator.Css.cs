using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace URUI.Editor.Authoring
{
    public static partial class HtmlCssValidator
    {
        [Serializable] sealed class CssPacket { public Diagnostic[] diagnostics; public CssRule[] rules; }
        [Serializable] sealed class CssRule { public string atRule, prelude; public int line; public CssDeclaration[] declarations; public CssRule[] children; }
        [Serializable] sealed class BorderImage { public bool fill; public float[] slices; public bool[] percentages; }
        [Serializable] sealed class CssDeclaration { public string property, value, identifier; public string[] urls; public bool important; public int line, column, byteOffset; public BorderImage borderImage; }
        [DllImport("URUIBridge", CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_authoring_protocol_version();
        [DllImport("URUIBridge", CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_validate_stylesheet([MarshalAs(UnmanagedType.LPUTF8Str)] string uri,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source, [Out] byte[] output, int capacity, out int required);

        /// <summary>编译独立属性表副本并取得原生 AST，不替换活动 Surface 或文档。</summary>
        public static CssValidationResult ValidateCss(string text, string sourceUrl)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (string.IsNullOrWhiteSpace(sourceUrl)) throw new ArgumentException("CSS 校验需要来源 URL。", nameof(sourceUrl));
            var diagnostics = new List<StyleDiagnostic>(); var rules = new List<CssRuleInfo>();
            if (text.IndexOf('\0') >= 0) {
                diagnostics.Add(new StyleDiagnostic("CSS_NULL", "CSS 不能包含 NUL。", sourceUrl));
                return new CssValidationResult(diagnostics, rules);
            }
            try {
                if (urui_authoring_protocol_version() != 1) throw new NotSupportedException("需要作者校验协议 v1。");
                var bytes = new byte[4096];
                var status = urui_validate_stylesheet(sourceUrl, text, bytes, bytes.Length, out var required);
                if (status == -3 && required > bytes.Length) {
                    bytes = new byte[required]; status = urui_validate_stylesheet(sourceUrl, text, bytes, bytes.Length, out required);
                }
                if (status < 0) diagnostics.Add(new StyleDiagnostic("native-css-failure", "原生 CSS 校验失败：" + status, sourceUrl));
                else {
                    var packet = JsonUtility.FromJson<CssPacket>(Encoding.UTF8.GetString(bytes, 0, required - 1));
                    foreach (var value in packet.diagnostics ?? Array.Empty<Diagnostic>())
                        diagnostics.Add(new StyleDiagnostic(value.code, value.message, value.uri, value.line, value.column, value.byteOffset, value.severity == "error"));
                    rules.AddRange(CopyRules(packet.rules));
                }
            } catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException || error is BadImageFormatException || error is NotSupportedException) {
                diagnostics.Add(new StyleDiagnostic("plugin-capability", "已安装插件缺少 CSS 作者校验协议：" + error.Message, sourceUrl));
            }
            return new CssValidationResult(diagnostics, rules);
        }
        static List<CssRuleInfo> CopyRules(CssRule[] source)
        {
            var result = new List<CssRuleInfo>();
            foreach (var rule in source ?? Array.Empty<CssRule>()) {
                var declarations = new List<CssDeclarationInfo>();
                foreach (var value in rule.declarations ?? Array.Empty<CssDeclaration>())
                    declarations.Add(new CssDeclarationInfo(value.property, value.value, value.identifier, value.important, value.line, value.column, value.byteOffset, value.urls,
                        value.borderImage?.slices?.Length==4 && value.borderImage.percentages?.Length==4
                            ? new CssBorderImageInfo(value.borderImage.fill,value.borderImage.slices,value.borderImage.percentages) : null));
                result.Add(new CssRuleInfo(rule.atRule, rule.prelude, rule.line, declarations, CopyRules(rule.children)));
            }
            return result;
        }
    }
}
