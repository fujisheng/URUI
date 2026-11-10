using System;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace URUI.Effect.Editor.Import
{
    /// <summary>输出现有 HTML/CSS 子集能表达的声明；HDR 和任意 Gradient 保留在预设中，不能静默截断。</summary>
    public static class EffectPresetCssWriter
    {
        static string Kebab(string value)=>System.Text.RegularExpressions.Regex.Replace(value,"(?<=[a-z0-9])([A-Z])","-$1").ToLowerInvariant();
        static string Number(float value)
        {if(!float.IsFinite(value))throw new InvalidOperationException("CSS 数值必须有限。");return value.ToString("R",CultureInfo.InvariantCulture);}
        static string Quote(string value)
        {if(value==null||value.IndexOf('\0')>=0)throw new InvalidOperationException("CSS URL 必须有效。");return "\""+value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\d ").Replace("\n","\\a ")+"\"";}
        public static string Write(EffectParameters parameters,string selector,EffectImportContext references=null)
        {
            if(parameters==null)throw new ArgumentNullException(nameof(parameters));
            if(string.IsNullOrWhiteSpace(selector)||selector.IndexOfAny(new[]{'{','}',';','\r','\n','\0'})>=0)throw new ArgumentException("请提供单个有效的作者选择器。",nameof(selector));
            var output=new StringBuilder(selector+" {\n  --urui-effect: ui;\n");
            void Emit(string key,string value)=>output.Append("  --urui-fx-").Append(key).Append(": ").Append(value).Append(";\n");
            foreach(var field in typeof(EffectParameters).GetFields(BindingFlags.Public|BindingFlags.Instance))
            {
                var key=Kebab(field.Name);var value=field.GetValue(parameters);
                switch(value)
                {
                    case float number:Emit(key,Number(number));break;
                    case int integer:Emit(key,integer.ToString(CultureInfo.InvariantCulture));break;
                    case bool flag:Emit(key,flag?"true":"false");break;
                    case Flip flags:Emit(key,((int)flags).ToString(CultureInfo.InvariantCulture));break;
                    case Enum mode:Emit(key,Kebab(mode.ToString()));break;
                    case Vector2 vector:Emit(key+"-x",Number(vector.x));Emit(key+"-y",Number(vector.y));break;
                    case EffectRange range:Emit(key+"-min",Number(range.Min));Emit(key+"-max",Number(range.Max));break;
                    case Color color:
                        var colorMode=field.Name switch {"Color"=>parameters.ColorFilter,"TransitionColor"=>parameters.TransitionColorFilter,"ShadowColor"=>parameters.ShadowColorFilter,"EdgeColor"=>parameters.EdgeColorFilter,_=>ColorFilter.None};
                        var extended=field.Name is "Color" or "TransitionColor" or "ShadowColor" or "EdgeColor";
                        if(colorMode is ColorFilter.HsvModifier or ColorFilter.Contrast)
                        {
                            if(colorMode==ColorFilter.HsvModifier)
                            {
                                if(color.r<-.5f||color.r>.5f||color.g<-1||color.g>1||color.b<-1||color.b>1)throw new NotSupportedException("有符号颜色超出 CSS 标量子集，请保留 EffectPreset："+field.Name);
                                Emit(key+"-hue-shift",Number(color.r));Emit(key+"-saturation-shift",Number(color.g));Emit(key+"-value-shift",Number(color.b));
                            }
                            else
                            {
                                if(color.r<-1||color.r>1||color.g<-1||color.g>1)throw new NotSupportedException("Contrast 超出 CSS 标量子集，请保留 EffectPreset。");
                                Emit(key+"-contrast-shift",Number(color.r));Emit(key+"-brightness-shift",Number(color.g));
                            }
                            Emit(key,"white");Emit(key+"-alpha",Number(color.a));break;
                        }
                        for(var i=0;i<3;i++)if(color[i]<0||color[i]>1||!float.IsFinite(color[i]))throw new NotSupportedException("HDR/有符号 RGB 不能由当前 CSS 色值子集精确表达，请使用 EffectPreset："+field.Name);
                        if(!extended&&(color.a<0||color.a>1))throw new NotSupportedException("Alpha 超出 CSS 色值子集："+field.Name);
                        Emit(key,"rgba("+Number(color.r*255)+", "+Number(color.g*255)+", "+Number(color.b*255)+", "+Number(Mathf.Clamp01(color.a))+")");
                        if(extended)Emit(key+"-alpha",Number(color.a));break;
                    case Texture texture:
                        var url=references?.TextureUrl!=null?references.TextureUrl(texture):AssetDatabase.GetAssetPath(texture);
                        if(string.IsNullOrWhiteSpace(url))throw new InvalidOperationException("纹理没有 CSS URL 去向："+texture.name);
                        if(url.StartsWith("Assets/",StringComparison.Ordinal))url="/"+url;
                        if(url.StartsWith("Packages/",StringComparison.Ordinal))throw new InvalidOperationException("包纹理需要映射到已挂载的 CSS URL："+texture.name);
                        Emit(key,"url("+Quote(url)+")");break;
                    case Gradient gradient:
                        var needed=parameters.GradationMode is GradationMode.HorizontalGradient or GradationMode.VerticalGradient or GradationMode.AngleGradient;
                        if(needed)
                        {
                            var lut=references?.GradientUrl?.Invoke(gradient);
                            if(string.IsNullOrWhiteSpace(lut))throw new InvalidOperationException("任意 Gradient 需要明确的 LUT URL；完整键值仍保存在 EffectPreset 中。");
                            Emit(key,"url("+Quote(lut)+")");
                        }
                        break;
                    case null:
                        if(typeof(Texture).IsAssignableFrom(field.FieldType))Emit(key,"none");
                        break;
                    default:throw new NotSupportedException("CSS 导出尚未映射字段："+field.Name);
                }
            }
            return output.Append("}\n").ToString();
        }
    }
}
