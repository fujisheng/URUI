using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Events;
using URUI.Resources;
using URUI.Rendering.Effects;
using URUI.Styles;
using URUI.Hosting;
namespace URUI.Effect.Examples
{
    /// <summary>独立效果图库；一个宿主管理全部节点，示例不依赖 Game、FUI 或 Coffee。</summary>
    public sealed class EffectGallery:MonoBehaviour,IEffectResourceResolver
    {
        public TextAsset html,style;
        public Texture2D white,source,pattern,gradient;
        public Shader shader;
        public GameObject particlePrefab;
        public URUI.Animation.IUiClock UiClock;
        Document document;
        EffectModule module;
        EffectPreset preset;
        Material particleMaterial;
        GameObject ownedParticleSource;
        readonly List<IDisposable> subscriptions=new();
        public EffectPlayback Playback {get;private set;}
        public Document Document=>document;
        public EffectModule Module=>module;
        void Start()
        {
            if(!html||!style||!white||!shader)throw new InvalidOperationException("请配置 Gallery HTML、CSS、纹理和自定义 Shader。");
            source=source?source:white;pattern=pattern?pattern:white;gradient=gradient?gradient:white;
            if(!particlePrefab)particlePrefab=CreateParticleSource();
            var surface=gameObject.AddComponent<Surface>();
            if(UiClock!=null)surface.UiClock=UiClock;
            document=gameObject.AddComponent<Document>();document.Surface=surface;
            document.Images=new[]{new ImageAsset("white.png",white),new ImageAsset("input.png",source)};
            var files=new List<TextResource>{
                new("/urui/base.css",UnityEngine.Resources.Load<TextAsset>("URUI/base")),
                new("/urui-effect/Effect.css",UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")),
                new("/samples/urui-effects/Gallery.css",style)};
            foreach(var family in new[]{"Serif","Sans","Mono"})
                foreach(var face in new[]{"Regular","Italic","Bold","BoldItalic"})
                {
                    var name="Liberation"+family+"-"+face+".ttf";
                    files.Add(new TextResource("/urui/Fonts/"+name+".bytes",UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/"+name)));
                }
            document.TextResources=files.ToArray();module=new EffectModule(document,this);
            document.Effects.Register(new EffectDefinition("sample-tint",EffectKind.Image,new Factory(shader),"sample-tint:v1","straight input; premultiplied output",new[]{new StylePropertyDefinition("--sample-progress",StyleValueType.Number)}));
            document.Load(html,"/samples/urui-effects/Gallery.html");
            preset=ScriptableObject.CreateInstance<EffectPreset>();
            preset.SetParameters(new EffectParameters{TransitionFilter=TransitionFilter.Dissolve,TransitionTexture=pattern,TransitionWidth=.2f});
            var controller=module.GetController(document.Q("controlled"));controller.Apply(preset);
            subscriptions.Add(module.GetController(document.Q("linked")).Link(controller));
            Playback=controller.Play(new EffectTweenDefinition{Channels=EffectTweenChannels.Transition,Duration=2,WrapMode=EffectWrapMode.PingPongLoop});
            void Button(string id,Action action)=>subscriptions.Add(document.Q(id).RegisterCallback<ClickEvent>(_=>action()));
            Button("play",()=>Playback.Restart());Button("pause",()=>Playback.Pause());Button("resume",()=>Playback.Resume());Button("stop",()=>Playback.Stop());Button("restart",()=>Playback.Restart());
            subscriptions.Add(document.Q<Slider>("seek").RegisterValueChangedCallback(value=>{Playback.Pause();Playback.SetTime(value.NewValue*2);}));
        }
        GameObject CreateParticleSource()
        {
            ownedParticleSource=new GameObject("URUI Gallery pure particle prefab");
            var system=ownedParticleSource.AddComponent<ParticleSystem>();system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            system.useAutoRandomSeed=false;system.randomSeed=1234;
            var main=system.main;main.startLifetime=1;main.startSpeed=20;main.startSize=5;main.startColor=new Color(.3f,.7f,1,.8f);main.maxParticles=128;main.playOnAwake=false;
            var emission=system.emission;emission.rateOverTime=64;
            particleMaterial=new Material(UnityEngine.Resources.Load<Shader>("URUI.Effect/Particle")){hideFlags=HideFlags.HideAndDontSave};
            var renderer=ownedParticleSource.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=particleMaterial;renderer.enabled=false;
            return ownedParticleSource;
        }
        public ImageLease AcquireImage(ComputedImageValue image)
        {
            if(image.Kind!=ComputedImageKind.Url)return null;
            var name=image.Url;var texture=name.EndsWith("pattern.png",StringComparison.Ordinal)?pattern:name.EndsWith("gradient-lut.png",StringComparison.Ordinal)?gradient:null;
            return texture?new ImageLease(texture):null;
        }
        public GameObject ResolveParticles(string key)=>key=="sample-particles"?particlePrefab:null;
        void OnDestroy()
        {
            foreach(var subscription in subscriptions)subscription.Dispose();subscriptions.Clear();
            if(document)document.Unload();module?.Dispose();
            if(preset)UnityEngine.Object.Destroy(preset);if(ownedParticleSource)UnityEngine.Object.Destroy(ownedParticleSource);
            if(particleMaterial)GpuRetirement.Retire(()=>UnityEngine.Object.Destroy(particleMaterial));
        }
        sealed class Factory:IEffectFactory
        {
            readonly Shader shader;internal Factory(Shader shader){this.shader=shader;}
            public IElementEffect Create(EffectContext context)=>new Instance(shader);
        }
        sealed class Instance:IElementEffect
        {
            readonly Material material;
            readonly MaterialPropertyBlock parameters=new();
            internal Instance(Shader shader){material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};}
            public void Prepare(EffectFrame frame){parameters.SetFloat("_Progress",frame.Style.GetNumber("--sample-progress"));}
            public void Record(EffectRenderContext context)=>context.Draw(material,parameters);
            public void Dispose(){if(material)UnityEngine.Object.Destroy(material);}
        }
    }
}
