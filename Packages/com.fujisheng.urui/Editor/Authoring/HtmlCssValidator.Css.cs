using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using URUI.Internal.Interop;
using URUI.Internal.Serialization;

namespace URUI.Editor.Authoring
{
    public static partial class HtmlCssValidator
    {
        const int ExpectedAuthoringProtocolVersion = 2;

        [DllImport("URUIBridge", CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_authoring_protocol_version();
        [DllImport("URUIBridge", CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_validate_stylesheet([MarshalAs(UnmanagedType.LPUTF8Str)] string uri,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source, [Out] byte[] output, int capacity, out int required);

        /// <summary>
        /// 编译独立属性表副本并取得原生 AST，不替换活动 Surface 或文档。
        /// </summary>
        public static CssValidationResult ValidateCss(string text, string sourceUrl)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            if (string.IsNullOrWhiteSpace(sourceUrl))
            {
                throw new ArgumentException("CSS 校验需要来源 URL。", nameof(sourceUrl));
            }
            var diagnostics = new List<StyleDiagnostic>();
            var rules = new List<CssRuleInfo>();
            if (text.IndexOf('\0') >= 0)
            {
                diagnostics.Add(new StyleDiagnostic("CSS_NULL", "CSS 不能包含 NUL。", sourceUrl));
                return new CssValidationResult(diagnostics, rules);
            }
            try
            {
                if (urui_authoring_protocol_version() != ExpectedAuthoringProtocolVersion)
                {
                    throw new NotSupportedException($"需要作者校验协议 v{ExpectedAuthoringProtocolVersion}。");
                }
                var bytes = new byte[4096];
                var status = urui_validate_stylesheet(sourceUrl, text, bytes, bytes.Length, out var required);
                if (status == NativeStatus.BufferTooSmall && required > bytes.Length)
                {
                    bytes = new byte[required];
                    status = urui_validate_stylesheet(sourceUrl, text, bytes, bytes.Length, out required);
                }
                if (status < 0)
                {
                    diagnostics.Add(new StyleDiagnostic("native-css-failure", "原生 CSS 校验失败：" + status, sourceUrl));
                }
                else
                {
                    // 使用有界递归 ValueJson 替代 Unity JsonUtility，避免 10 层序列化深度截断嵌套规则 AST。
                    var json = Encoding.UTF8.GetString(bytes, 0, required - 1);
                    if (ValueJson.Parse(json) is Dictionary<string, object> packet)
                    {
                        if (packet.TryGetValue("diagnostics", out var diagObj) && diagObj is List<object> diagList)
                        {
                            for (int i = 0; i < diagList.Count; i++)
                            {
                                if (diagList[i] is Dictionary<string, object> value)
                                {
                                    diagnostics.Add(new StyleDiagnostic(
                                        JsonString(value, "code"),
                                        JsonString(value, "message"),
                                        JsonString(value, "uri"),
                                        JsonInt(value, "line"),
                                        JsonInt(value, "column"),
                                        JsonInt(value, "byteOffset"),
                                        JsonString(value, "severity") == "error"));
                                }
                            }
                        }
                        if (packet.TryGetValue("rules", out var rulesObj) && rulesObj is List<object> ruleList)
                        {
                            ParseRules(ruleList, rules);
                        }
                    }
                }
            }
            catch (Exception error) when (error is DllNotFoundException || error is EntryPointNotFoundException || error is BadImageFormatException || error is NotSupportedException)
            {
                diagnostics.Add(new StyleDiagnostic("plugin-capability", "已安装插件缺少 CSS 作者校验协议：" + error.Message, sourceUrl));
            }
            return new CssValidationResult(diagnostics, rules);
        }

        static void ParseRules(List<object> source, List<CssRuleInfo> destination)
        {
            if (source == null)
            {
                return;
            }
            for (int i = 0; i < source.Count; i++)
            {
                if (!(source[i] is Dictionary<string, object> rule))
                {
                    continue;
                }
                var declarations = new List<CssDeclarationInfo>();
                if (rule.TryGetValue("declarations", out var declsObj) && declsObj is List<object> declList)
                {
                    for (int d = 0; d < declList.Count; d++)
                    {
                        if (!(declList[d] is Dictionary<string, object> value))
                        {
                            continue;
                        }
                        string[] urls = null;
                        if (value.TryGetValue("urls", out var urlsObj) && urlsObj is List<object> urlList && urlList.Count > 0)
                        {
                            urls = new string[urlList.Count];
                            for (int u = 0; u < urlList.Count; u++)
                            {
                                urls[u] = urlList[u] as string ?? "";
                            }
                        }
                        CssBorderImageInfo borderImage = null;
                        if (value.TryGetValue("borderImage", out var biObj) && biObj is Dictionary<string, object> bi
                            && bi.TryGetValue("slices", out var slicesObj) && slicesObj is List<object> sliceList && sliceList.Count == 4
                            && bi.TryGetValue("percentages", out var pctObj) && pctObj is List<object> pctList && pctList.Count == 4)
                        {
                            var slices = new float[4];
                            var percentages = new bool[4];
                            for (int k = 0; k < 4; k++)
                            {
                                slices[k] = ToFloat(sliceList[k]);
                                percentages[k] = pctList[k] is bool b && b;
                            }
                            borderImage = new CssBorderImageInfo(JsonBool(bi, "fill"), slices, percentages);
                        }
                        declarations.Add(new CssDeclarationInfo(
                            JsonString(value, "property"),
                            JsonString(value, "value"),
                            JsonString(value, "identifier"),
                            JsonBool(value, "important"),
                            JsonInt(value, "line"),
                            JsonInt(value, "column"),
                            JsonInt(value, "byteOffset"),
                            urls,
                            borderImage));
                    }
                }
                var children = new List<CssRuleInfo>();
                if (rule.TryGetValue("children", out var childrenObj) && childrenObj is List<object> childList)
                {
                    ParseRules(childList, children);
                }
                destination.Add(new CssRuleInfo(
                    JsonString(rule, "atRule"),
                    JsonString(rule, "prelude"),
                    JsonInt(rule, "line"),
                    declarations,
                    children));
            }
        }

        static string JsonString(Dictionary<string, object> map, string key)
            => map.TryGetValue(key, out var v) ? v as string : null;
        static int JsonInt(Dictionary<string, object> map, string key)
            => map.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
        static bool JsonBool(Dictionary<string, object> map, string key)
            => map.TryGetValue(key, out var v) && v is bool b && b;
        static float ToFloat(object v)
            => v != null ? Convert.ToSingle(v, CultureInfo.InvariantCulture) : 0f;
    }
}
