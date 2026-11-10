using System;
using System.Collections.Generic;
using UnityEngine;

namespace URUI.Editor.Authoring
{
    /// <summary>在原生 AST 上核对模块依赖；全局选择器和同名关键帧覆盖遵循标准 CSS。</summary>
    public static class StyleDependencyValidator
    {
        public static IReadOnlyList<StyleDiagnostic> Validate(IEnumerable<KeyValuePair<string,TextAsset>> sources, IEnumerable<string> registeredImageEffects = null)
            => Validate(sources,registeredImageEffects,null);

        /// <summary>提供源图尺寸时，同时检查作者九宫格的中心填充声明。</summary>
        public static IReadOnlyList<StyleDiagnostic> Validate(IEnumerable<KeyValuePair<string,TextAsset>> sources,
            IEnumerable<string> registeredImageEffects, Func<string,Vector2Int?> imageSizeResolver)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var issues = new List<StyleDiagnostic>();
            var effects = registeredImageEffects == null ? null : new HashSet<string>(registeredImageEffects, StringComparer.Ordinal);
            foreach (var source in sources) {
                if (!source.Value) { issues.Add(new StyleDiagnostic("missing-style", "样式资源不存在。", source.Key)); continue; }
                if (effects != null || imageSizeResolver != null)
                    Scan(HtmlCssValidator.ValidateCss(source.Value.text, source.Key).Rules, source.Key, effects, imageSizeResolver, issues);
            }
            return issues.AsReadOnly();
        }
        static void Scan(IReadOnlyList<CssRuleInfo> rules, string uri, HashSet<string> effects, Func<string,Vector2Int?> imageSizeResolver, List<StyleDiagnostic> issues)
        {
            foreach (var rule in rules) {
                foreach (var value in rule.Declarations)
                    if (effects != null && value.Property == "--urui-image-effect" && !string.IsNullOrEmpty(value.Identifier) &&
                        !IsCssKeyword(value.Identifier) && !effects.Contains(value.Identifier))
                        issues.Add(new StyleDiagnostic("unknown-effect", "未注册 --urui-image-effect：" + value.Value + "；请安装对应模块或修正作者名称。", uri, value.Line, value.Column, value.ByteOffset));
                if(imageSizeResolver!=null){CheckBorderImage(rule,uri,imageSizeResolver,issues);}
                Scan(rule.Children, uri, effects, imageSizeResolver, issues);
            }
        }
        static void CheckBorderImage(CssRuleInfo rule,string uri,Func<string,Vector2Int?> resolve,List<StyleDiagnostic> issues)
        {
            CssDeclarationInfo source=null,slice=null;
            foreach(var value in rule.Declarations)
            {
                if(value.Property is "border-image" or "border-image-source")
                {
                    if(source==null||value.Important||!source.Important){source=value;}
                }
                if(value.Property is "border-image" or "border-image-slice")
                {
                    if(slice==null||value.Important||!slice.Important){slice=value;}
                }
            }
            var geometry=slice?.BorderImage;
            if(geometry==null||!geometry.Fill||source?.Urls.Count!=1){return;}
            var size=resolve(HtmlResourceDependencies.ResolveUrl(uri,source.Urls[0]));
            if(!size.HasValue||size.Value.x<=0||size.Value.y<=0){return;}
            var values=new float[4];
            for(var edge=0;edge<4;edge++)
            {
                var dimension=edge%2==0?size.Value.y:size.Value.x;
                values[edge]=Mathf.Min(dimension,geometry.Percentages[edge]?geometry.Slices[edge]*.01f*dimension:geometry.Slices[edge]);
            }
            if(values[1]+values[3]>=size.Value.x||values[0]+values[2]>=size.Value.y)
            {
                // 这是合法CSS的作者提示；其他规则仍可能有意覆盖切片，不能当解析错误拒绝。
                issues.Add(new StyleDiagnostic("empty-border-image-center","border-image声明fill，但当前源图切片中心为空；请核对切片与源图尺寸。",uri,slice.Line,slice.Column,slice.ByteOffset,false));
            }
        }
        static bool IsCssKeyword(string value)
        {
            switch (value.ToLowerInvariant()) {
                case "none": case "inherit": case "initial": case "unset": case "revert": case "revert-layer": return true;
                default: return false;
            }
        }
    }
}
