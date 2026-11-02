using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityResources = UnityEngine.Resources;

namespace URUI.Internal.Resources
{
    // 默认样式先于文档业务规则，避免控件只能在带私有皮肤的样例中可见。
    internal static class ControlStyleSheet
    {
        static readonly Regex openingTags = new Regex(@"<!--[\s\S]*?-->|<(?<tag>rml|head)\b(?:""[^""]*""|'[^']*'|[^'""<>])*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static string css;
        internal static string Apply(string markup)
        {
            if (css == null)
            {
                var asset = UnityResources.Load<TextAsset>("URUI/Controls.rcss");
                if (!asset)
                {
                    throw new InvalidOperationException("URUI 控件基础样式资源缺失。");
                }

                css = asset.text;
            }
            string style = "<style>" + css + "</style>";
            int rootEnd = -1;
            foreach (Match match in openingTags.Matches(markup))
            {
                string tag = match.Groups["tag"].Value;
                if (tag.Equals("head", StringComparison.OrdinalIgnoreCase))
                {
                    return markup.Insert(match.Index + match.Length, style);
                }

                if (tag.Equals("rml", StringComparison.OrdinalIgnoreCase) && rootEnd < 0)
                {
                    rootEnd = match.Index + match.Length;
                }
            }
            return rootEnd < 0 ? markup : markup.Insert(rootEnd, "<head>" + style + "</head>");
        }
    }
}
