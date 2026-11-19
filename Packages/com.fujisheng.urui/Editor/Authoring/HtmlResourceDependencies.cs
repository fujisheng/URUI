using System;
using System.Collections.Generic;
using System.Text;
using URUI.Internal.Interop;
using URUI.Resources;
using UnityEngine;

namespace URUI.Editor.Authoring
{
    /// <summary>
    /// 从原生 HTML/CSS AST 收集页面依赖；不引入另一套作者解析器。
    /// </summary>
    public sealed class HtmlResourceDependencies
    {
        enum FileKind
        {
            StyleSheet,
            Fragment,
            Font
        }
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        readonly List<TextResource> textFiles = new List<TextResource>();
        readonly SortedSet<string> imageUrls = new SortedSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, FileKind> visited = new Dictionary<string, FileKind>(StringComparer.Ordinal);
        readonly Func<string, TextAsset> load;
        readonly string documentUrl;
        public IReadOnlyList<TextResource> TextFiles => textFiles.AsReadOnly();
        public IReadOnlyCollection<string> ImageUrls => imageUrls;

        HtmlResourceDependencies(string documentUrl, Func<string, TextAsset> load)
        {
            this.documentUrl = documentUrl;
            this.load = load;
        }

        /// <summary>
        /// 片段中的资源相对所属文档；样式和字体相对所在 CSS 文件。
        /// </summary>
        public static HtmlResourceDependencies Collect(string htmlUri, Func<string, TextAsset> load)
        {
            if (load == null)
            {
                throw new ArgumentNullException(nameof(load));
            }
            var source = ResolveUrl("/", htmlUri);
            RequireExtension(source, ".html");
            var result = new HtmlResourceDependencies(source, load);
            result.InspectHtml(result.Load(source), source, false);
            result.textFiles.Sort((a, b) => string.CompareOrdinal(a.Source, b.Source));
            return result;
        }

        static void RequireExtension(string uri, string extension)
        {
            if (!uri.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("作者依赖必须使用真实 " + extension + " 文件：" + uri);
            }
        }

        internal static string ResolveUrl(string baseUrl, string source)
        {
            if (string.IsNullOrWhiteSpace(source) || source.IndexOf('\0') >= 0)
            {
                throw new InvalidOperationException("作者依赖 URL 不能为空或包含 NUL。");
            }
            var required = NativeMethods.urui_resolve_resource_url(baseUrl, source, null, 0);
            if (required <= 0)
            {
                throw new InvalidOperationException("作者依赖 URL 无效或越出本地资源域：" + source);
            }
            var bytes = new byte[required];
            if (NativeMethods.urui_resolve_resource_url(baseUrl, source, bytes, bytes.Length) != required)
            {
                throw new InvalidOperationException("作者依赖 URL 解析失败：" + source);
            }
            var uri = StrictUtf8.GetString(bytes, 0, required - 1);
            if (!uri.StartsWith("/", StringComparison.Ordinal) || uri.StartsWith("//", StringComparison.Ordinal) || uri.IndexOf('?') >= 0 || uri.IndexOf('#') >= 0)
            {
                throw new InvalidOperationException("作者依赖必须是无查询和片段标记的本地资源 URL：" + uri);
            }
            return uri;
        }

        /// <summary>
        /// 片段的标准资源依赖；相对路径使用片段的作者来源作为独立编译基准。
        /// </summary>
        public static HtmlResourceDependencies CollectFragment(string htmlUri, Func<string, TextAsset> load)
        {
            if (load == null)
            {
                throw new ArgumentNullException(nameof(load));
            }
            var source = ResolveUrl("/", htmlUri);
            RequireExtension(source, ".html");
            var result = new HtmlResourceDependencies(source, load);
            result.InspectHtml(result.Load(source), source, true);
            return result;
        }

        TextAsset Load(string uri)
        {
            var asset = load(uri);
            if (!asset)
            {
                throw new InvalidOperationException("缺少作者依赖：" + uri);
            }
            return asset;
        }

        static string ReadText(TextAsset asset, string uri)
        {
            try
            {
                return StrictUtf8.GetString(asset.bytes);
            }
            catch (DecoderFallbackException error)
            {
                throw new InvalidOperationException("作者文件必须使用 UTF-8：" + uri, error);
            }
        }

        void AddFile(string baseUrl, string reference, FileKind kind)
        {
            var uri = ResolveUrl(baseUrl, reference);
            if (kind == FileKind.StyleSheet)
            {
                RequireExtension(uri, ".css");
            }
            if (kind == FileKind.Fragment)
            {
                RequireExtension(uri, ".html");
            }
            if (visited.TryGetValue(uri, out var previous))
            {
                if (previous != kind)
                {
                    throw new InvalidOperationException("作者依赖角色冲突：" + uri);
                }
                return;
            }
            visited.Add(uri, kind);
            var asset = Load(uri);
            textFiles.Add(new TextResource(uri, asset));
            if (kind == FileKind.StyleSheet)
            {
                InspectCss(ReadText(asset, uri), uri);
            }
            if (kind == FileKind.Fragment)
            {
                InspectHtml(asset, uri, true);
            }
        }

        void AddImage(string baseUrl, string reference)
        {
            if (!string.IsNullOrEmpty(reference))
            {
                imageUrls.Add(ResolveUrl(baseUrl, reference));
            }
        }

        void InspectHtml(TextAsset asset, string uri, bool fragment)
        {
            var result = HtmlCssValidator.Validate(ReadText(asset, uri), uri, fragment);
            foreach (var issue in result.Diagnostics)
            {
                if (issue.IsError)
                {
                    throw new InvalidOperationException(uri + ":" + issue.Line + " " + issue.Code + ": " + issue.Message);
                }
            }
            // SetInnerHtml 没有独立资源域，所有片段都在原文档内解析 URL。
            var baseUrl = fragment ? documentUrl : uri;
            foreach (var node in result.Nodes)
            {
                if (node.Tag == "link")
                {
                    var stylesheet = HasRel(node.Rel, "stylesheet");
                    var ownedFragment = HasRel(node.Rel, "urui-fragment");
                    if (stylesheet && ownedFragment)
                    {
                        throw new InvalidOperationException("链接不能同时是样式和片段：" + uri);
                    }
                    if (stylesheet)
                    {
                        AddFile(baseUrl, node.Href, FileKind.StyleSheet);
                    }
                    if (ownedFragment)
                    {
                        AddFile(baseUrl, node.Href, FileKind.Fragment);
                    }
                }
                if (node.Tag == "img" || node.Tag == "input" || node.Tag == "source" || node.Tag == "video" || node.Tag == "audio")
                {
                    AddImage(baseUrl, node.Source);
                }
                if (!string.IsNullOrEmpty(node.InlineStyle))
                {
                    InspectCss("urui-inline{" + node.InlineStyle + "}", baseUrl);
                }
                if (!string.IsNullOrEmpty(node.StyleText))
                {
                    InspectCss(node.StyleText, baseUrl);
                }
            }
        }

        static bool HasRel(string value, string expected)
        {
            foreach (var token in (value ?? "").Split(new[] { ' ', '\t', '\n', '\r', '\f' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(token, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        void InspectCss(string text, string uri)
        {
            var result = HtmlCssValidator.ValidateCss(text, uri);
            foreach (var issue in result.Diagnostics)
            {
                // 运行时按 CSS 规则忽略无效声明；打包门禁必须阻止这些视觉缺失被静默分发。
                throw new InvalidOperationException(uri + ":" + issue.Line + " " + issue.Code + ": " + issue.Message);
            }
            InspectRules(result.Rules, uri);
        }

        void InspectRules(IReadOnlyList<CssRuleInfo> rules, string uri)
        {
            foreach (var rule in rules)
            {
                foreach (var declaration in rule.Declarations)
                {
                    foreach (var reference in declaration.Urls)
                    {
                        if (rule.AtRule == "font-face")
                        {
                            AddFile(uri, reference, FileKind.Font);
                        }
                        else
                        {
                            AddImage(uri, reference);
                        }
                    }
                }
                InspectRules(rule.Children, uri);
            }
        }
    }
}
