using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using URUI.Documents;
using URUI.Elements;
using URUI.Rendering;
using URUI.Effect.Rendering;
namespace URUI.Effect
{
    /// <summary>持久注册一个作者节点；粒子源独立准备、原子替换并经宿主 GPU fence 退役。</summary>
    public sealed class ParticlePainter:IDisposable
    {
        sealed class Emitter
        {
            internal ParticleSystem System;
            internal ParticleSystemRenderer Renderer;
            internal Mesh Mesh,Trails;
            internal Material[] Materials;
            internal readonly MaterialPropertyBlock Properties=new();
            internal ParticleSystem.Particle[] Particles=Array.Empty<ParticleSystem.Particle>();
            internal int Frame=-1,Version=-1,Count;
        }
        sealed class SourceState:IDisposable
        {
            internal GameObject Instance,CameraObject;
            internal Camera Camera;
            internal readonly List<Emitter> Emitters=new();
            internal int Version;
            bool disposed;
            internal SourceState(GameObject source)
            {
                if(!source)throw new ArgumentNullException(nameof(source));
                foreach(var component in source.GetComponentsInChildren<Component>(true))
                    if(!(component is Transform||component is ParticleSystem||component is ParticleSystemRenderer))
                        throw new ArgumentException("粒子源含有非粒子组件："+component?.GetType().Name,nameof(source));
                try
                {
                    Instance=UnityEngine.Object.Instantiate(source);Instance.name="URUI particles: "+source.name;Instance.hideFlags=HideFlags.HideAndDontSave;Instance.transform.position=Vector3.zero;
                    CameraObject=new GameObject("URUI particle baking camera"){hideFlags=HideFlags.HideAndDontSave};
                    Camera=CameraObject.AddComponent<Camera>();Camera.enabled=false;Camera.orthographic=true;Camera.aspect=1;Camera.transform.position=new Vector3(0,0,-1000);
                    foreach(var system in Instance.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var renderer=system.GetComponent<ParticleSystemRenderer>();if(!renderer)throw new ArgumentException("粒子源缺少 Renderer。",nameof(source));
                        if(system.main.simulationSpace==ParticleSystemSimulationSpace.Custom)throw new NotSupportedException("自定义模拟空间需先声明坐标映射。");
                        var materials=renderer.sharedMaterials;
                        if(materials.Length==0)throw new ArgumentException("粒子源没有材质。",nameof(source));
                        foreach(var material in materials)
                            if(!material||!material.HasProperty("_UruiPaintOpacity")||!material.HasProperty("_UruiClipEnabled")||!material.HasProperty("_UruiClipRect"))
                                throw new ArgumentException("粒子材质未声明完整 URUI 绘制契约。",nameof(source));
                        if(system.trails.enabled&&materials.Length<2)throw new ArgumentException("开启尾迹的粒子缺少尾迹材质。",nameof(source));
                        renderer.enabled=false;renderer.minParticleSize=0;renderer.maxParticleSize=1;
                        var main=system.main;main.cullingMode=ParticleSystemCullingMode.AlwaysSimulate;
                        system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                        Emitters.Add(new Emitter{System=system,Renderer=renderer,Materials=materials,Mesh=new Mesh{name="URUI particle mesh"},Trails=new Mesh{name="URUI particle trails"}});
                    }
                    if(Emitters.Count==0)throw new ArgumentException("粒子源没有粒子系统。",nameof(source));
                }
                catch{Dispose();throw;}
            }
            internal void Apply(ParticlePlaybackState state,bool hidden,bool pauseWhenHidden,bool restart)
            {
                foreach(var emitter in Emitters)
                {
                    var system=emitter.System;
                    if(state==ParticlePlaybackState.Stopped||restart)system.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                    if(state==ParticlePlaybackState.Playing&&(!hidden||!pauseWhenHidden))system.Play(false);
                    else if(state!=ParticlePlaybackState.Stopped)system.Pause(false);
                }
                Version++;
            }
            public void Dispose()
            {
                if(disposed)return;disposed=true;
                foreach(var emitter in Emitters){EffectObjectLifetime.Destroy(emitter.Mesh);EffectObjectLifetime.Destroy(emitter.Trails);}
                Emitters.Clear();EffectObjectLifetime.Destroy(Instance);EffectObjectLifetime.Destroy(CameraObject);
            }
        }
        readonly Element element;
        readonly Document document;
        readonly bool observeLayout;
        SourceState state;
        GameObject source;
        ParticlePlaybackOptions options;
        IDisposable registration;
        bool disposed,visible=true;
        long bodyBakes,trailBakes,particleReads,drawCalls,readTicks,bodyTicks,trailTicks,recordTicks;
        public ParticlePlaybackState PlaybackState {get;private set;}
        public int ParticleCount {get;private set;}
        public GameObject Source=>source;
        public ParticlePainter(Element element,GameObject source,bool restartOnShow=true):this(element,source,new ParticlePlaybackOptions{RestartOnShow=restartOnShow}){}
        public ParticlePainter(Element element,GameObject source,ParticlePlaybackOptions options):this(element,source,options,true){}
        internal ParticlePainter(Element element,GameObject source,ParticlePlaybackOptions options,bool observeLayout)
        {
            this.element=element??throw new ArgumentNullException(nameof(element));document=element.Document;this.options=(options??throw new ArgumentNullException(nameof(options))).Clone();this.observeLayout=observeLayout;
            state=new SourceState(source);this.source=source;PlaybackState=this.options.PlayOnLoad?ParticlePlaybackState.Playing:ParticlePlaybackState.Paused;
            try
            {
                registration=document.AttachPainter(element,"particles",Paint,Release);element.AddToClassList("has-particles");
                if(observeLayout)document.LayoutUpdated+=Update;
                state.Apply(PlaybackState,false,this.options.PauseWhenHidden,true);
            }
            catch{if(registration!=null)registration.Dispose();else Release();throw;}
        }
        void Require(){if(disposed||!element.IsValid)throw new ObjectDisposedException(nameof(ParticlePainter));}
        public void SetSource(GameObject value)
        {
            Require();if(source==value)return;
            var next=new SourceState(value);
            try
            {
                next.Apply(PlaybackState,!visible,options.PauseWhenHidden,PlaybackState==ParticlePlaybackState.Playing);
                document.RetirePainterResources(state);
                foreach(var emitter in state.Emitters)emitter.System.Pause(false);
                state=next;source=value;ParticleCount=0;
            }
            catch{next.Dispose();throw;}
        }
        public void SetOptions(ParticlePlaybackOptions value)
        {Require();options=(value??throw new ArgumentNullException(nameof(value))).Clone();state.Apply(PlaybackState,!visible,options.PauseWhenHidden,false);}
        public void Play(){Require();PlaybackState=ParticlePlaybackState.Playing;state.Apply(PlaybackState,!visible,options.PauseWhenHidden,false);}
        public void Pause(){Require();PlaybackState=ParticlePlaybackState.Paused;state.Apply(PlaybackState,!visible,options.PauseWhenHidden,false);}
        public void Stop(){Require();PlaybackState=ParticlePlaybackState.Stopped;ParticleCount=0;state.Apply(PlaybackState,!visible,options.PauseWhenHidden,false);}
        public void Restart(){Require();PlaybackState=ParticlePlaybackState.Playing;state.Apply(PlaybackState,!visible,options.PauseWhenHidden,true);}
        void Update(Document owner)=>UpdateVisibility();
        internal void UpdateVisibility()
        {
            if(disposed||!element.IsValid)return;
            var displayed=element.IsAttached&&element.HasLayout&&element.IsVisibleInHierarchy;
            if(displayed==visible)return;visible=displayed;
            state.Apply(PlaybackState,!visible,options.PauseWhenHidden,displayed&&options.RestartOnShow&&PlaybackState==ParticlePlaybackState.Playing);
        }
        static double Milliseconds(long ticks)=>ticks*1000.0/Stopwatch.Frequency;
        public ParticlePainterStatistics GetStatistics()=>new(bodyBakes,trailBakes,particleReads,drawCalls,Milliseconds(readTicks),Milliseconds(bodyTicks),Milliseconds(trailTicks),Milliseconds(recordTicks));
        void Paint(ElementPaintContext context)
        {
            if(context.IsClippedOut)return;
            ParticleCount=0;
            foreach(var emitter in state.Emitters)
            {
                if(!emitter.System.gameObject.activeInHierarchy)continue;
                if(emitter.Frame!=Time.frameCount||emitter.Version!=state.Version)
                {
                    var start=Stopwatch.GetTimestamp();FitBakingCamera(emitter);readTicks+=Stopwatch.GetTimestamp()-start;particleReads++;
                    start=Stopwatch.GetTimestamp();emitter.Renderer.BakeMesh(emitter.Mesh,state.Camera,true);bodyTicks+=Stopwatch.GetTimestamp()-start;bodyBakes++;
                    if(emitter.System.trails.enabled){start=Stopwatch.GetTimestamp();emitter.Renderer.BakeTrailsMesh(emitter.Trails,state.Camera,true);trailTicks+=Stopwatch.GetTimestamp()-start;trailBakes++;}
                    emitter.Frame=Time.frameCount;emitter.Version=state.Version;
                }
                ParticleCount+=emitter.Count;
                var offset=emitter.System.main.simulationSpace==ParticleSystemSimulationSpace.Local?emitter.System.transform.position:Vector3.zero;offset.z=0;
                var matrix=Matrix4x4.Scale(new Vector3(1,-1,1))*Matrix4x4.Translate(offset);
                var record=Stopwatch.GetTimestamp();
                if(emitter.Mesh.vertexCount>0){context.DrawMesh(emitter.Mesh,emitter.Materials[0],matrix,emitter.Properties);drawCalls++;}
                if(emitter.System.trails.enabled&&emitter.Trails.vertexCount>0){context.DrawMesh(emitter.Trails,emitter.Materials[1],matrix,emitter.Properties);drawCalls++;}
                recordTicks+=Stopwatch.GetTimestamp()-record;
            }
        }
        void FitBakingCamera(Emitter emitter)
        {
            var count=emitter.System.particleCount;
            if(emitter.Particles.Length<count)emitter.Particles=new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];
            var alive=emitter.System.GetParticles(emitter.Particles);emitter.Count=alive;
            var scale=emitter.System.transform.lossyScale;var maximumScale=Mathf.Max(1,Mathf.Max(Mathf.Abs(scale.x),Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z))));var extent=1f;
            for(var i=0;i<alive;i++)extent=Mathf.Max(extent,emitter.Particles[i].GetCurrentSize3D(emitter.System).magnitude*maximumScale);
            // 保留原保守尺寸公式；不因二维节点盒或裁剪范围缩小大粒子的烘焙视口。
            state.Camera.orthographicSize=extent;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;if(observeLayout)document.LayoutUpdated-=Update;
            if(element.IsValid)element.RemoveFromClassList("has-particles");
            foreach(var emitter in state.Emitters)emitter.System.Pause(false);
            registration?.Dispose();registration=null;
        }
        void Release(){disposed=true;if(observeLayout)document.LayoutUpdated-=Update;state?.Dispose();state=null;}
    }
}
