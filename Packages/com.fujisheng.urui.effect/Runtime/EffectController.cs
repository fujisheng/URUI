using System;
using System.Collections.Generic;
using System.Threading;
using URUI.Animation;
using URUI.Elements;
using URUI.Styles;
using URUI.Rendering.Effects;
using URUI.Effect.Rendering;
namespace URUI.Effect
{
    public enum EffectApplyMode { Replace,MergeActiveGroups }
    public enum TransitionInputPolicy { Standard,DisableWhenComplete }
    /// <summary>作者节点的预设、独立播放与快照联动；不拥有 DOM 或每节点 MonoBehaviour。</summary>
    public sealed class EffectController:IDisposable
    {
        readonly EffectModule module;
        readonly IDisposable binding;
        EffectParameters parameters=new(),lastSnapshot;
        Dictionary<string,StyleOverride> presetValues,linkedValues,playbackValues;
        EffectPlayback playback;
        EffectLink link;
        EffectLayout rawLayout,resolvedLayout;
        EffectParameters resolvedParameters;
        EffectParameters computedParameters,computedBasis,lastSnapshotSource;
        ComputedStyleSnapshot computedAuthor;
        long revision,computedRevision=-1;
        (bool capture,bool input)? runtimeState;
        double previousTime;
        IUiClock previousClock;
        bool disposed;
        bool available;
        public Element Element {get;}
        public TransitionInputPolicy InputPolicy {get;set;}
        internal bool Alive=>!disposed&&Element.IsValid&&Element.IsAttached;
        internal EffectController(EffectModule module,Element element)
        {this.module=module;Element=element;available=true;binding=element.Document.Effects.BindParameters(element,ResolveLayout);}
        void RequireValid(){if(!Alive)throw new ObjectDisposedException(nameof(EffectController));}
        public void Apply(EffectPreset preset,EffectApplyMode mode=EffectApplyMode.Replace)
        {
            RequireValid();if(!preset)throw new ArgumentNullException(nameof(preset));
            if(!Enum.IsDefined(typeof(EffectApplyMode),mode))throw new ArgumentOutOfRangeException(nameof(mode));
            var next=preset.CreateParameters();next=mode==EffectApplyMode.Replace?next:EffectParameterMerge.Merge(parameters,next);
            var values=EffectParameterStyle.Write(next);
            Unlink();parameters=next;presetValues=values;Invalidate();
        }
        public EffectParameters CreateParameters()
        {RequireValid();return (resolvedParameters??Compute(Element.Document.Styles.GetComputed(Element))).Clone();}
        public void SaveTo(EffectPreset preset)
        {if(!preset)throw new ArgumentNullException(nameof(preset));preset.SetParameters(CreateParameters());}
        public EffectPlayback Play(EffectTweenDefinition definition,CancellationToken cancellationToken=default)
        {
            RequireValid();var next=new EffectPlayback(definition,Invalidate,cancellationToken);playback?.Cancel();playback=next;
            previousClock=null;Invalidate();return playback;
        }
        public IDisposable Link(EffectController source,bool useTargetRoot=true)
        {
            RequireValid();if(source==null||!source.Alive)throw new ArgumentException("联动来源必须有效。",nameof(source));
            if(useTargetRoot&&!ReferenceEquals(Element.Document,source.Element.Document))throw new ArgumentException("根坐标不能跨文档联动。",nameof(useTargetRoot));
            for(var current=source;current!=null;current=current.link?.Source)if(ReferenceEquals(current,this))throw new InvalidOperationException("效果联动不能形成循环。");
            Unlink();link=new EffectLink(this,source,useTargetRoot);Invalidate();return link;
        }
        internal void Release(EffectLink value)
        {if(!ReferenceEquals(link,value))return;parameters=(resolvedParameters??lastSnapshot??parameters).Clone();presetValues=EffectParameterStyle.Write(parameters);link=null;Invalidate();}
        void Unlink(){link?.Dispose();}
        void Invalidate(){revision++;resolvedLayout=null;resolvedParameters=null;}
        internal void Freeze(){resolvedLayout=null;available=Alive;if(available)rawLayout=Element.Document.Effects.GetLayout(Element);}
        internal void Tick()
        {
            if(!Alive){Dispose();return;}
            if(playback!=null)
            {
                var surfaceClock=Element.Document.Surface.UiClock;
                var clock=playback.UpdateMode==EffectUpdateMode.Unscaled?UnityUiClock.Unscaled:UnityUiClock.Scaled;
                // A caller-supplied Surface clock is authoritative in deterministic simulations and previews.
                if(!ReferenceEquals(surfaceClock,UnityUiClock.Scaled)&&!ReferenceEquals(surfaceClock,UnityUiClock.Unscaled))clock=surfaceClock;
                var now=clock.Now;var delta=ReferenceEquals(clock,previousClock)?now-previousTime:0;
                previousClock=clock;previousTime=now;
                playback.Advance(playback.UpdateMode==EffectUpdateMode.Manual?0:delta);
                if(!Alive)return;
            }
            var p=Compute(Element.Document.Styles.GetComputed(Element),true);
            var rejecting=p.TransitionFilter is TransitionFilter.Fade or TransitionFilter.Cutoff or TransitionFilter.Dissolve or TransitionFilter.Melt or TransitionFilter.Burn;
            var state=(p.BlendType!=BlendType.AlphaBlend,InputPolicy==TransitionInputPolicy.DisableWhenComplete&&rejecting&&p.TransitionProgress>=1);
            if(runtimeState!=state){Element.Document.Effects.SetRuntimeState(Element,state.Item1,state.Item2);runtimeState=state;}
        }
        EffectParameters Compute(ComputedStyleSnapshot author,bool freshSources=false)
        {
            var basis=parameters;
            if(link!=null)
            {
                if(link.Source.disposed||!link.Source.available){Release(link);basis=parameters;}
                else
                {
                    basis=freshSources?link.Source.Compute(link.Source.Element.Document.Styles.GetComputed(link.Source.Element),true):link.Source.ResolveParameters();
                }
            }
            if(ReferenceEquals(computedAuthor,author)&&ReferenceEquals(computedBasis,basis)&&computedRevision==revision)return computedParameters;
            var p=basis.CopyForRendering();
            if(link!=null){p.SamplingScale=parameters.SamplingScale;p.AllowToModifyMeshShape=parameters.AllowToModifyMeshShape;}
            var values=link!=null?EffectParameterStyle.Write(p,linkedValues??=new(StringComparer.Ordinal)):presetValues;
            var style=values==null?author:author.WithOverrides(values,StyleValueOrigin.UserAgent);
            EffectParameterReader.Read(style,p);
            if(playback!=null&&!playback.IsCancelled)
            {
                var rate=playback.EvaluatedProgress;var channels=playback.Channels;
                var animated=playbackValues??=new(StringComparer.Ordinal);animated.Clear();
                void Set(EffectTweenChannels channel,bool active,string key,float value)
                {if(active&&(channels&channel)!=0)animated["--urui-fx-"+key]=StyleOverride.Number(value);}
                Set(EffectTweenChannels.Tone,p.ToneFilter!=ToneFilter.None,"tone-intensity",rate);
                Set(EffectTweenChannels.Color,p.ColorFilter!=ColorFilter.None,"color-intensity",rate);
                Set(EffectTweenChannels.Sampling,p.SamplingFilter!=SamplingFilter.None,"sampling-intensity",rate);
                Set(EffectTweenChannels.Transition,p.TransitionFilter!=TransitionFilter.None,"transition-progress",rate);
                Set(EffectTweenChannels.GradationOffset,p.GradationMode!=GradationMode.None,"gradation-offset",-1+2*rate);
                Set(EffectTweenChannels.GradationRotation,p.GradationMode is GradationMode.Angle or GradationMode.AngleGradient,"gradation-rotation",360*rate);
                Set(EffectTweenChannels.EdgeShiny,p.EdgeMode==EdgeMode.Shiny,"edge-shiny-progress",rate);
                style=style.WithOverrides(animated,StyleValueOrigin.Author);EffectParameterReader.Read(style,p);
            }
            // Keep original CSS image declarations in the style view. Direct preset textures stay in the pure snapshot.
            lastStyle=style;computedAuthor=author;computedBasis=basis;computedRevision=revision;computedParameters=p;return p;
        }
        ComputedStyleSnapshot lastStyle;
        internal EffectParameters ResolveParameters()
        {
            if(rawLayout==null)return lastSnapshot??parameters;
            var author=rawLayout.Style;
            resolvedParameters=Compute(author);
            if(!ReferenceEquals(lastSnapshotSource,resolvedParameters)){lastSnapshot=resolvedParameters;lastSnapshotSource=resolvedParameters;}
            return resolvedParameters;
        }
        EffectLayout ResolveLayout(EffectLayout current)
        {
            if(resolvedLayout!=null)return resolvedLayout;
            ResolveParameters();var result=current.WithStyle(lastStyle);
            if(link is {UseTargetRoot:true}&&link.Source.rawLayout!=null)result=result.WithRoot(link.Source.ResolveLayout(link.Source.rawLayout));
            return resolvedLayout=result;
        }
        internal EffectParameters RenderingParameters=>resolvedParameters;
        public void Dispose()
        {if(disposed)return;disposed=true;playback?.Cancel();link?.Dispose();binding.Dispose();rawLayout=null;resolvedLayout=null;module.Release(this);}
    }
}
