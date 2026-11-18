using System;
using UnityEngine;
using URUI.Rendering.Effects;
using URUI.Rendering;
using URUI.Styles;
namespace URUI.Effect.Rendering
{
    /// <summary>单图调色/纹理/扫光共用不可变程序，节点尺寸、进度和时钟保存在独立属性块中。</summary>
    public static class TextureColorEffect
    {
        /// <summary>一个程序按不继承的资源键解析作者材质；项目只提供资源，不逐文档注册每份材质。</summary>
        public static EffectDefinition DefineResolved(string name,Func<string,Material> resolver,string keyProperty,string rateProperty=null,string saturationProperty=null,string brightnessProperty=null)
        {
            if(resolver==null)throw new ArgumentNullException(nameof(resolver));
            var parameters=new System.Collections.Generic.List<StylePropertyDefinition>(Parameters(rateProperty,saturationProperty,brightnessProperty));
            parameters.Add(new StylePropertyDefinition(keyProperty,StyleValueType.Keyword));
            return new EffectDefinition(name,EffectKind.Image,new ResolvedFactory(resolver,keyProperty,rateProperty,saturationProperty,brightnessProperty),"texture-image:resolved:v1","straight input; premultiplied output; document color space; HDR",parameters,true,renderProgram:EffectProgramDescriptors.Texture,renderParameters:Layout(rateProperty,saturationProperty,brightnessProperty,keyProperty));
        }
        public static EffectDefinition Define(string name,Material authored,string rateProperty=null,string saturationProperty=null,string brightnessProperty=null)
        {
            if(!authored)throw new ArgumentNullException(nameof(authored));
            return new EffectDefinition(name,EffectKind.Image,new Factory(authored,rateProperty,saturationProperty,brightnessProperty),"texture-image:v1","straight input; premultiplied output; document color space; HDR",
                Parameters(rateProperty,saturationProperty,brightnessProperty),
                authored.GetFloat("_Speed")!=0||authored.GetVector("_PatternSpeed")!=Vector4.zero,renderProgram:EffectProgramDescriptors.Texture,renderParameters:Layout(rateProperty,saturationProperty,brightnessProperty));
        }
        static System.Collections.Generic.IEnumerable<StylePropertyDefinition> Parameters(params string[] names)
        {foreach(var name in names)if(name!=null)yield return new StylePropertyDefinition(name,StyleValueType.Number);}
        static global::URUI.Rendering.RenderParameterLayout Layout(string rate,string saturation,string brightness,string key=null)
        {
            var fields=new System.Collections.Generic.List<global::URUI.Rendering.RenderParameterField>();
            if(rate!=null)fields.Add(new(new StylePropertyDefinition(rate,StyleValueType.Number),0,3));
            if(saturation!=null)fields.Add(new(new StylePropertyDefinition(saturation,StyleValueType.Number),1));
            if(brightness!=null)fields.Add(new(new StylePropertyDefinition(brightness,StyleValueType.Number),2));
            if(key!=null)fields.Add(new(new StylePropertyDefinition(key,StyleValueType.Keyword),4));
            return new global::URUI.Rendering.RenderParameterLayout(key==null?1:2,fields);
        }
        sealed class Program
        {
            readonly Material authored;readonly Vector4 fields;Material material;int references;RenderTimeInput timeInput;
            internal Program(Material authored,Vector4 fields){this.authored=authored;this.fields=fields;}
            internal sealed class Lease:IDisposable
            {
                Program owner;internal Material Material=>owner.material;internal RenderTimeInput TimeInput=>owner.timeInput;
                internal Lease(Program owner){this.owner=owner;}
                public void Dispose(){if(owner==null)return;var previous=owner;owner=null;if(--previous.references==0){EffectObjectLifetime.Destroy(previous.material);previous.material=null;}}
            }
            internal Lease Acquire()
            {
                if(!material){var shader=UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureImage");if(!shader||!shader.isSupported)throw new NotSupportedException("缺少单图效果 Shader。");material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};material.CopyMatchingPropertiesFromMaterial(authored);material.SetVector("_CssFields",fields);timeInput=material.GetFloat("_Speed")!=0||material.GetVector("_PatternSpeed")!=Vector4.zero?RenderTimeInput.UiClock:RenderTimeInput.None;}
                references++;return new Lease(this);
            }
        }
        sealed class Factory:IEffectFactory
        {
            readonly Program program;readonly string rate,saturation,brightness;readonly float baseSaturation,baseBrightness;
            internal Factory(Material authored,string rate,string saturation,string brightness){program=new Program(authored,new Vector4(rate!=null?1:0,saturation!=null?1:0,brightness!=null?1:0,0));this.rate=rate;this.saturation=saturation;this.brightness=brightness;baseSaturation=authored.GetFloat("_Saturation");baseBrightness=authored.GetFloat("_Brightness");}
            public IElementEffect Create(EffectContext context)=>new Instance(context.Own(program.Acquire()),rate,saturation,brightness,baseSaturation,baseBrightness);
        }
        sealed class ResolvedFactory:IEffectFactory
        {
            internal readonly Func<string,Material> Resolver;
            internal readonly string Key,Rate,Saturation,Brightness;
            readonly System.Collections.Generic.Dictionary<Material,Program> programs=new();
            internal ResolvedFactory(Func<string,Material> resolver,string key,string rate,string saturation,string brightness)
            {Resolver=resolver;Key=key;Rate=rate;Saturation=saturation;Brightness=brightness;}
            internal Program.Lease Acquire(Material authored)
            {
                foreach(var property in new[]{"_FilterMode","_ColorBlend","_PatternTex","_Rate","_Speed","_PatternSpeed","_Saturation","_Brightness"})
                    if(!authored.HasProperty(property))throw new ArgumentException("作者纹理效果材质缺少属性："+property);
                if(!programs.TryGetValue(authored,out var program))programs.Add(authored,program=new Program(authored,new Vector4(Rate!=null?1:0,Saturation!=null?1:0,Brightness!=null?1:0,1)));return program.Acquire();
            }
            public IElementEffect Create(EffectContext context)=>new ResolvedInstance(this,context);
        }
        sealed class ResolvedInstance:IElementEffect,IParameterImageEffect
        {
            readonly ResolvedFactory factory;readonly EffectContext context;
            public Material Program=>program?.Material;
            public RenderTimeInput TimeInput=>program?.TimeInput??RenderTimeInput.None;
            Program.Lease program;Material authored;string key;float baseSaturation,baseBrightness;
            bool hasPreparedKey;float preparedKey;
            internal ResolvedInstance(ResolvedFactory factory,EffectContext context){this.factory=factory;this.context=context;}
            public void PrepareProgram(in EffectProgramPreparation preparation)
            {
                var identity=preparation.Parameters[1].x;
                if(hasPreparedKey&&identity==preparedKey)return;
                Prepare(preparation.Frame);preparedKey=identity;hasPreparedKey=true;
            }
            public void Prepare(EffectFrame frame)
            {
                var requested=frame.Style.GetKeyword(factory.Key);
                if(requested!=key)
                {
                    var material=factory.Resolver(requested);
                    if(!material)throw new InvalidOperationException("纹理效果材质无法解析："+requested);
                    if(material!=authored)
                    {
                        var next=context.Own(factory.Acquire(material));if(program!=null)context.Retire(program);
                        program=next;authored=material;baseSaturation=material.GetFloat("_Saturation");baseBrightness=material.GetFloat("_Brightness");
                    }
                    key=requested;
                }
            }
            public void GetPreviewParameters(Span<Vector4> values){values[0]=new Vector4(Program.GetFloat("_Rate"),1,1,0);}
            public void Record(EffectRenderContext context)=>throw new InvalidOperationException("固定参数程序由宿主直接提交。");
            public void Dispose(){}
        }
        sealed class Instance:IElementEffect,IParameterImageEffect
        {
            readonly Program.Lease program;
            public Material Program=>program.Material;
            public RenderTimeInput TimeInput=>program.TimeInput;
            internal Instance(Program.Lease program,string rate,string saturation,string brightness,float baseSaturation,float baseBrightness){this.program=program;}
            public void PrepareProgram(in EffectProgramPreparation preparation){}
            public void Prepare(EffectFrame frame){}
            public void GetPreviewParameters(Span<Vector4> values){values[0]=new Vector4(Program.GetFloat("_Rate"),1,1,0);}
            public void Record(EffectRenderContext context)=>throw new InvalidOperationException("固定参数程序由宿主直接提交。");
            public void Dispose(){}
        }
    }
}
