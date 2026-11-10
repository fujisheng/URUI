using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Elements;
using URUI.Rendering;
using URUI.Rendering.Effects;
namespace URUI.Documents
{
    public sealed partial class Document
    {
        EffectRegistry effects;
        int effectCallbackDepth;
        Dictionary<string,ILayerFilterProgram> filterPrograms => content.FilterPrograms;
        Dictionary<(ulong token, EffectKind kind, ulong input), EffectEntry> effectInstances => content.EffectInstances;
        readonly HashSet<(ulong token, EffectKind kind, ulong input)> preparedEffects = new();
        readonly List<(ulong token,EffectKind kind,ulong input)> removedEffects=new();
        readonly float[] effectLayoutScratch = new float[40];
        Action<Action> effectSubmissionSink;
        internal Action<Action> EffectSubmissionSink => effectSubmissionSink ??= QueueEffectSubmission;
        void QueueEffectSubmission(Action callback) => host.QueueEffectSubmission(this,callback);
        EffectLayout FreezeEffectLayout(Element element, URUI.Styles.ComputedStyleSnapshot style)
        {
            if (URUI.Internal.Interop.NativeMethods.urui_effect_layout(NativeHandle, element.Token, effectLayoutScratch, effectLayoutScratch.Length, out var root) != 1)
                throw new InvalidOperationException("效果坐标快照已失效。");
            var data=effectLayoutScratch;var own=Matrix4x4.identity;var rootMatrix=Matrix4x4.identity;
            for (var index=0;index<16;index++) { own[index]=data[4+index];rootMatrix[index]=data[24+index]; }
            var rectangle=new Rect(data[0],data[1],data[2],data[3]);
            var rootRectangle=new Rect(data[20],data[21],data[22],data[23]);
            if(content.EffectLayouts.TryGetValue(element.Token,out var previous)&&ReferenceEquals(previous.Style,style)&&
                previous.ContentRect.Equals(rectangle)&&previous.ElementToDocument.Equals(own)&&previous.RootToken==root&&
                previous.RootContentRect.Equals(rootRectangle)&&previous.RootToDocument.Equals(rootMatrix))return previous;
            var result=new EffectLayout(style,rectangle,own,root,rootRectangle,rootMatrix);
            content.EffectLayouts[element.Token]=result;
            return result;
        }
        public EffectRegistry Effects { get { CheckThread(); return effects ??= new EffectRegistry(this); } }
        internal void UpdateEffectParameters() { effects?.UpdateParameters(); }
        internal void FreezeEffectParameters() { effects?.FreezeParameters(); }
        internal EffectLayout RawEffectLayout(Element element)
        {
            CheckThread();RequireEffectDomAccess();
            if(element==null||!element.IsValid||!element.IsAttached||!ReferenceEquals(element.Document,this))throw new ArgumentException("参数需要当前文档的有效节点。");
            element.AcceptRenderedLayout();return FreezeEffectLayout(element,Styles.GetComputed(element));
        }
        EffectLayout ParameterLayout(Element element, URUI.Styles.ComputedStyleSnapshot style)
        {
            var raw=FreezeEffectLayout(element,style);effectCallbackDepth++;surface.BeginEffect();
            try{return effects?.ApplyParameters(element,raw)??raw;}
            finally{effectCallbackDepth--;surface.EndEffect();}
        }
        internal void RequireEffectDomAccess() { if (effectCallbackDepth!=0 || surface && surface.IsEffectCallback) throw new InvalidOperationException("Effect Prepare/Record 中请使用不可变 EffectFrame，不能访问 DOM。"); }
        internal bool HasEffects => effects != null && effects.Count != 0;
        sealed class EffectEntry : IDisposable
        {
            internal EffectContext context; internal IElementEffect instance; internal EffectFrame frame; internal string name; internal Rect bounds;
            public void Dispose() { try { instance?.Dispose(); } finally { context?.Dispose(); instance=null; context=null; frame=default; } }
        }
        internal Rect EffectInkBounds(ulong token, EffectKind kind = EffectKind.Image, ulong input=0)=>effectInstances[(token, kind, input)].bounds;
        internal EffectKind EffectKindForName(string name)
        {
            if (effects == null || !effects.TryGet(name, out var definition)) { throw new InvalidOperationException("效果未注册：" + name); }
            return definition.Kind;
        }
        internal void BeginEffectPreparation() { preparedEffects.Clear(); }
        internal void EndEffectPreparation()
        {
            removedEffects.Clear();
            foreach (var pair in effectInstances) { if (!preparedEffects.Contains(pair.Key)) { removedEffects.Add(pair.Key); } }
            foreach (var key in removedEffects) { RetireEffect(effectInstances[key]); effectInstances.Remove(key); }
        }
        internal bool TryGetEffectFrame(Element element,out EffectFrame frame)
        {
            CheckThread();frame=default;
            if(element==null||!ReferenceEquals(element.Document,this)||!element.IsValid)return false;
            if(effectInstances.TryGetValue((element.Token, EffectKind.Element, 0),out var entry))
            { frame=entry.frame;return frame.Layout!=null; }
            foreach(var pair in effectInstances)
            {
                if(pair.Key.token==element.Token)
                { frame=pair.Value.frame;return frame.Layout!=null; }
            }
            return false;
        }
        internal Vector4 ResolveEffectOutput(ulong token, string name, out EffectComposite composite, out Matrix4x4[] transforms)
        {
            var element = WrapElement(token);
            if (element == null || !element.IsAttached || effects == null || !effects.TryGet(name, out var definition))
            { throw new InvalidOperationException("效果扩边所属节点或程序已失效：" + name); }
            element.AcceptRenderedLayout(); var layout = ParameterLayout(element, Styles.GetComputed(element));
            effectCallbackDepth++; surface.BeginEffect();
            try { composite=definition.ResolveComposite(layout);transforms=definition.ResolveBoundsTransforms(layout);return definition.ResolveExpansion(layout); }
            finally { effectCallbackDepth--; surface.EndEffect(); }
        }
        internal void PrepareEffects(IReadOnlyList<EffectInput> uses,uint? sourceVersion=null)
        {
            if (uses.Count==0 && effectInstances.Count==0) return;
            for(var useIndex=0;useIndex<uses.Count;useIndex++)
            {
                var use=uses[useIndex];
                var element=WrapElement(use.token);
                if (element == null || !element.IsAttached) continue;
                if (effects == null || !effects.TryGet(use.name,out var definition))
                    throw new InvalidOperationException($"{Source?.name ?? name}: img #{element.Name}, --urui-image-effect '{use.name}' 未注册。");
                var key = (use.token, definition.Kind, use.input);
                preparedEffects.Add(key);
                if (effectInstances.TryGetValue(key,out var entry) && entry.name!=use.name)
                { RetireEffect(entry); effectInstances.Remove(key); entry=null; }
                element.AcceptRenderedLayout();
                var snapshot=Styles.GetComputed(element);
                for(var parameterIndex=0;parameterIndex<definition.Parameters.Count;parameterIndex++)
                {
                    var parameter=definition.Parameters[parameterIndex];
                    if (!snapshot.Contains(parameter))
                    throw new InvalidOperationException($"{Source?.name ?? name}: img #{element.Name}, {use.name} 参数缺失或类型错误：{parameter.Name}");
                }
                var frame=new EffectFrame(use.token,ParameterLayout(element,snapshot),use.source,use.borrowed,effects.ClockFor(element).Now,sourceVersion,use.geometry,use.captureBounds,use.sourceInkBounds);
                effectCallbackDepth++; surface.BeginEffect();
                try
                {
                    if (entry == null)
                    {
                        entry=new EffectEntry {name=use.name, context=new EffectContext(definition,element,use.token,RetireEffect)};
                        try { entry.instance=definition.Factory.Create(entry.context) ?? throw new InvalidOperationException("效果工厂返回 null："+use.name); }
                        catch { RetireEffect(entry); throw; }
                        effectInstances.Add(key,entry);
                    }
                    entry.instance.Prepare(frame); entry.frame=frame;
                    var content=new Rect(frame.ContentOffset,frame.Size);var e=definition.ResolveExpansion(frame.Layout);
                    entry.bounds=Rect.MinMaxRect(content.xMin-e.x,content.yMin-e.y,content.xMax+e.z,content.yMax+e.w);
                    if(entry.instance is IEffectBounds ink){var b=ink.InkBounds;if(!float.IsFinite(b.x)||!float.IsFinite(b.y)||!float.IsFinite(b.width)||!float.IsFinite(b.height)||b.width<0||b.height<0)throw new InvalidOperationException("效果绘制范围无效："+definition.Name);entry.bounds=Rect.MinMaxRect(Mathf.Min(entry.bounds.xMin,b.xMin),Mathf.Min(entry.bounds.yMin,b.yMin),Mathf.Max(entry.bounds.xMax,b.xMax),Mathf.Max(entry.bounds.yMax,b.yMax));}
                }
                catch
                {
                    if (effectInstances.Remove(key,out var failed)) RetireEffect(failed);
                    throw;
                }
                finally { effectCallbackDepth--; surface.EndEffect(); }
            }
        }
        internal bool RecordEffect(ulong token,ElementPaintContext paint,Mesh geometry,Action<Action> submitted,EffectKind kind=EffectKind.Image,ulong input=0)
        {
            if (!effectInstances.TryGetValue((token,kind,input),out var entry)) throw new InvalidOperationException("效果没有已准备的实例。");
            var context=new EffectRenderContext(paint,geometry,entry.frame,submitted);
            effectCallbackDepth++; surface.BeginEffect(); entry.context.Recording=true;
            try { entry.instance.Record(context); return !context.UsesOriginal; }
            finally { entry.context.Recording=false; effectCallbackDepth--; surface.EndEffect(); context.Close(); }
        }
        internal void EffectSubmitted(Action callback)
        {
            effectCallbackDepth++; surface.BeginEffect();
            try{callback();}finally{effectCallbackDepth--; surface.EndEffect();}
        }
        void RetireEffect(IDisposable resource) { host.RetirePaintResource(resource); }
        internal bool IsFilterCacheable(string name)=>URUI.Internal.Rendering.BuiltinFilterPrograms.IsBuiltin(name)||effects!=null&&effects.IsFilterCacheable(name);
        internal void PrepareFilter(FilterInvocation invocation)
        {
            effectCallbackDepth++; surface.BeginEffect();
            try {
                if(!filterPrograms.TryGetValue(invocation.Name,out var program)) {
                    if (URUI.Internal.Rendering.BuiltinFilterPrograms.IsBuiltin(invocation.Name))
                        program = URUI.Internal.Rendering.BuiltinFilterPrograms.Create(invocation.Name);
                    else {
                        if(effects==null||!effects.TryGetFilter(invocation.Name,out var factory))throw new InvalidOperationException($"{Source?.name ?? name}: filter '{invocation.Name}' 未注册。");
                        program=factory()??throw new InvalidOperationException("滤镜工厂返回 null："+invocation.Name);
                    }
                    filterPrograms.Add(invocation.Name,program);
                }
                program.Prepare(invocation);
            } finally {effectCallbackDepth--; surface.EndEffect();}
        }
        internal void RecordFilter(FilterRenderContext context,FilterInvocation invocation)
        {
            effectCallbackDepth++; surface.BeginEffect();
            try {filterPrograms[invocation.Name].Record(context,invocation);}
            finally {effectCallbackDepth--; surface.EndEffect();context.Close();}
        }
        void ReleaseEffects()
        {
            var resources=new List<IDisposable>(effectInstances.Values);resources.AddRange(filterPrograms.Values);
            if(resources.Count==0)return;
            host.RetirePaintResources(resources);effectInstances.Clear();filterPrograms.Clear();
        }
    }
}
