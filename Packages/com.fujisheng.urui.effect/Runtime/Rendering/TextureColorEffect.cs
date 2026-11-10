using System;
using UnityEngine;
using URUI.Rendering.Effects;
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
            return new EffectDefinition(name,EffectKind.Image,new ResolvedFactory(resolver,keyProperty,rateProperty,saturationProperty,brightnessProperty),"texture-image:resolved:v1","straight input; premultiplied output; document color space; HDR",parameters,true);
        }
        public static EffectDefinition Define(string name,Material authored,string rateProperty=null,string saturationProperty=null,string brightnessProperty=null)
        {
            if(!authored)throw new ArgumentNullException(nameof(authored));
            return new EffectDefinition(name,EffectKind.Image,new Factory(authored,rateProperty,saturationProperty,brightnessProperty),"texture-image:v1","straight input; premultiplied output; document color space; HDR",
                Parameters(rateProperty,saturationProperty,brightnessProperty),
                authored.GetFloat("_Speed")!=0||authored.GetVector("_PatternSpeed")!=Vector4.zero);
        }
        static System.Collections.Generic.IEnumerable<StylePropertyDefinition> Parameters(params string[] names)
        {foreach(var name in names)if(name!=null)yield return new StylePropertyDefinition(name,StyleValueType.Number);}
        sealed class Program
        {
            readonly Material authored;Material material;int references;
            internal Program(Material authored){this.authored=authored;}
            internal sealed class Lease:IDisposable
            {
                Program owner;internal Material Material=>owner.material;
                internal Lease(Program owner){this.owner=owner;}
                public void Dispose(){if(owner==null)return;var previous=owner;owner=null;if(--previous.references==0){EffectObjectLifetime.Destroy(previous.material);previous.material=null;}}
            }
            internal Lease Acquire()
            {
                if(!material){var shader=UnityEngine.Resources.Load<Shader>("URUI.Effect/TextureImage");if(!shader||!shader.isSupported)throw new NotSupportedException("缺少单图效果 Shader。");material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};material.CopyMatchingPropertiesFromMaterial(authored);}
                references++;return new Lease(this);
            }
        }
        sealed class Factory:IEffectFactory
        {
            readonly Program program;readonly string rate,saturation,brightness;readonly float baseSaturation,baseBrightness;
            internal Factory(Material authored,string rate,string saturation,string brightness){program=new Program(authored);this.rate=rate;this.saturation=saturation;this.brightness=brightness;baseSaturation=authored.GetFloat("_Saturation");baseBrightness=authored.GetFloat("_Brightness");}
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
                if(!programs.TryGetValue(authored,out var program))programs.Add(authored,program=new Program(authored));return program.Acquire();
            }
            public IElementEffect Create(EffectContext context)=>new ResolvedInstance(this,context);
        }
        sealed class ResolvedInstance:IElementEffect
        {
            readonly ResolvedFactory factory;readonly EffectContext context;readonly MaterialPropertyBlock properties=new();
            Program.Lease program;Material authored;string key;float baseSaturation,baseBrightness;
            internal ResolvedInstance(ResolvedFactory factory,EffectContext context){this.factory=factory;this.context=context;}
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
                // 初始进度保留作者材质；只有明确 CSS/动画声明才覆盖它。
                var rate=factory.Rate!=null&&frame.Style.GetOrigin(factory.Rate)>StyleValueOrigin.UserAgent?factory.Rate:null;
                Bind(properties,frame,rate,factory.Saturation,factory.Brightness,baseSaturation,baseBrightness);
            }
            public void Record(EffectRenderContext context)=>context.Draw(program.Material,properties);
            public void Dispose(){}
        }
        static void Bind(MaterialPropertyBlock properties,EffectFrame frame,string rate,string saturation,string brightness,float baseSaturation,float baseBrightness)
        {
            properties.Clear();properties.SetVector("_ReferenceSize",new Vector4(frame.Size.x,frame.Size.y,0,0));
            var bounds=frame.SourceGeometry?.Bounds??new Rect(frame.ContentOffset,frame.Size);
            properties.SetVector("_EffectBounds",new Vector4(bounds.x,bounds.y,
                bounds.width>0?1f/bounds.width:0,bounds.height>0?1f/bounds.height:0));
            properties.SetFloat("_EffectTime",(float)frame.Time);properties.SetFloat("_InputBottomLeft",frame.SourceIsBottomLeft?1:0);properties.SetFloat("_InputPremultiplied",frame.SourceIsPremultiplied?1:0);
            if(rate!=null)properties.SetFloat("_Rate",frame.Style.GetNumber(rate));
            if(saturation!=null)properties.SetFloat("_Saturation",baseSaturation*frame.Style.GetNumber(saturation));
            if(brightness!=null)properties.SetFloat("_Brightness",baseBrightness*frame.Style.GetNumber(brightness));
        }
        sealed class Instance:IElementEffect
        {
            readonly Program.Lease program;readonly string rate,saturation,brightness;readonly float baseSaturation,baseBrightness;readonly MaterialPropertyBlock properties=new();
            internal Instance(Program.Lease program,string rate,string saturation,string brightness,float baseSaturation,float baseBrightness){this.program=program;this.rate=rate;this.saturation=saturation;this.brightness=brightness;this.baseSaturation=baseSaturation;this.baseBrightness=baseBrightness;}
            public void Prepare(EffectFrame frame)
            {
                Bind(properties,frame,rate,saturation,brightness,baseSaturation,baseBrightness);
            }
            public void Record(EffectRenderContext context)=>context.Draw(program.Material,properties);
            public void Dispose(){}
        }
    }
}
