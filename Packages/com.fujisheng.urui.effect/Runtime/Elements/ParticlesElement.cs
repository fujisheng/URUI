using System;
using URUI.Elements;
using URUI.Events;
namespace URUI.Effect.Elements
{
    /// <summary>声明式 Unity 粒子节点；HTML 的 src 由模块资源解析器映射，模拟仍由 Unity 驱动。</summary>
    public sealed class ParticlesElement:Element
    {
        readonly IEffectResourceResolver resources;
        readonly Action<ParticlesElement> invalidated;
        readonly Func<bool> moduleDisposed;
        ParticlePainter painter;
        IDisposable detached;
        string currentSource=string.Empty;
        internal ParticlesElement(ElementContext context,IEffectResourceResolver resources,Action<ParticlesElement> invalidated,Func<bool> moduleDisposed):base(context)
        {this.resources=resources;this.invalidated=invalidated;this.moduleDisposed=moduleDisposed;}
        public string Source {get=>GetAttribute("src")??string.Empty;set=>SetAttribute("src",value??throw new ArgumentNullException(nameof(value)));}
        public string CurrentSource=>currentSource;
        public int ParticleCount=>painter?.ParticleCount??0;
        public ParticlePlaybackState PlaybackState=>painter?.PlaybackState??ParticlePlaybackState.Stopped;
        public ParticlePainterStatistics GetStatistics()=>painter?.GetStatistics()??default;
        ParticlePainter RequirePainter()
        {if(moduleDisposed())throw new ObjectDisposedException(nameof(EffectModule));return painter??throw new InvalidOperationException("请先为粒子节点提供有效 src。");}
        public void Play()=>RequirePainter().Play();
        public void Pause()=>RequirePainter().Pause();
        public void Stop()=>RequirePainter().Stop();
        public void Restart()=>RequirePainter().Restart();
        protected override void OnInitialize()
        {
            detached=RegisterCallback<DetachFromDocumentEvent>(_=>painter?.UpdateVisibility());
            ApplySource(Source);
        }
        static bool Flag(string value,bool fallback)
        {if(value==null)return fallback;if(value.Length==0)return true;if(bool.TryParse(value,out var result))return result;throw new ArgumentException("粒子 data 属性只接受 true/false。");}
        ParticlePlaybackOptions Options()=>new(){
            PlayOnLoad=Flag(GetAttribute("data-urui-play-on-load"),true),
            PauseWhenHidden=Flag(GetAttribute("data-urui-pause-when-hidden"),true),
            RestartOnShow=Flag(GetAttribute("data-urui-restart-on-show"),true)};
        void ApplySource(string key)
        {
            if(moduleDisposed())throw new ObjectDisposedException(nameof(EffectModule));
            if(key==currentSource)return;
            if(key.Length==0){Release();return;}
            var source=resources?.ResolveParticles(key);
            if(!source)throw new InvalidOperationException("粒子源无法解析："+key);
            if(painter==null)painter=new ParticlePainter(this,source,Options(),false);
            else painter.SetSource(source);
            currentSource=key;
        }
        protected override void OnAttributeChanged(string name)
        {
            if(name=="src")
            {
                var requested=Source;
                Document.RunAfterEvent(()=>{if(IsValid&&Source==requested)ApplySource(requested);});
            }
            else if(name is "data-urui-play-on-load" or "data-urui-pause-when-hidden" or "data-urui-restart-on-show")
            {if(painter!=null)painter.SetOptions(Options());}
        }
        protected override void OnUpdate()=>painter?.UpdateVisibility();
        internal void Release(){painter?.Dispose();painter=null;currentSource=string.Empty;}
        protected override void OnInvalidated(){detached?.Dispose();detached=null;Release();invalidated(this);}
    }
}
