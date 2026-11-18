using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Rendering.Effects;
using URUI.Rendering;
using URUI.Styles;

namespace URUI.Effect.Rendering
{
    internal sealed class UiEffectFactory : IEffectFactory
    {
        readonly IEffectResourceResolver resources;
        readonly Func<URUI.Elements.Element,EffectParameters> parameters;
        internal UiEffectFactory(IEffectResourceResolver resources,Func<URUI.Elements.Element,EffectParameters> parameters=null) { this.resources = resources;this.parameters=parameters; }
        static readonly StylePropertyDefinition[] baseParameters = {
            new("--urui-fx-tone-filter", StyleValueType.Keyword), new("--urui-fx-tone-intensity", StyleValueType.Number),
            new("--urui-fx-color-filter", StyleValueType.Keyword), new("--urui-fx-color-intensity", StyleValueType.Number),
            new("--urui-fx-color", StyleValueType.Color), new("--urui-fx-color-glow", StyleValueType.Keyword),
            new("--urui-fx-target-mode", StyleValueType.Keyword), new("--urui-fx-target-color", StyleValueType.Color),
            new("--urui-fx-target-range", StyleValueType.Number), new("--urui-fx-target-softness", StyleValueType.Number),
            new("--urui-fx-sampling-filter", StyleValueType.Keyword), new("--urui-fx-sampling-intensity", StyleValueType.Number),
            new("--urui-fx-sampling-width", StyleValueType.Number), new("--urui-fx-sampling-scale", StyleValueType.Number),
            new("--urui-fx-allow-to-modify-mesh-shape", StyleValueType.Keyword)
        };
        internal static readonly StylePropertyDefinition[] Parameters = CombineParameters();
        static StylePropertyDefinition[] CombineParameters()
        {
            var values = new System.Collections.Generic.List<StylePropertyDefinition>(baseParameters);
            values.AddRange(EffectVisualProperties.Parameters); values.AddRange(EffectGradation.Parameters);
            values.AddRange(EffectShadowProperties.Parameters); values.AddRange(EffectBlendState.Parameters); return values.ToArray();
        }
        public IElementEffect Create(EffectContext context) { return new UiEffectInstance(context, resources,parameters); }
    }

    internal sealed class UiEffectInstance : IElementEffect, IEffectBounds, IParameterImageGeometry, IParameterImageRuntime, IParameterEffectLayout, IParameterImageResources
    {
        readonly EffectContext context;readonly EffectResources resources;
        readonly Func<URUI.Elements.Element,EffectParameters> parameterSource;
        readonly EffectParameters geometryParameters=new();readonly Gradient defaultGradient=new();
        readonly Vector4[] runtime=new Vector4[14];
        UiEffectGeometry.Lease geometry;URUI.Rendering.Effects.EffectGeometry sourceTopology;ShadowMode mode;int iterations;
        EffectProgramCache.Lease program;(BlendMode,BlendMode) blendState;int quality=-1;bool complexPixel;Texture transition,detail,gradient,atlas;
        EffectGradientCache.Lease gradientLut;Gradient gradientSource;
        Vector3 resourceKeys=new(float.NaN,float.NaN,float.NaN);Vector3 resourceOrigins;
        Texture cssTransition,cssDetail,cssGradient;
        public Rect InkBounds {get;private set;}
        public Material Program=>program.Program.Material;
        public RenderTimeInput TimeInput {get;private set;}
        public ExternalGeometryInput Geometry=>new(geometry.Mesh,1);
        public Matrix4x4 GeometryTransform {get;private set;}
        public int RuntimeRowCount=>runtime.Length;
        internal UiEffectInstance(EffectContext context,IEffectResourceResolver resolver,Func<URUI.Elements.Element,EffectParameters> parameterSource=null)
        {this.context=context;this.parameterSource=parameterSource;resources=resolver==null?null:new EffectResources(context,resolver);}
        public void PrepareCoverage(in EffectProgramPreparation preparation,Span<Matrix4x4> transforms,out int count,out Vector4 expansion,out EffectComposite composite)
        {
            UiEffectCoverage.Read(preparation.Parameters,geometryParameters);expansion=UiEffectCoverage.CaptureExpansion(geometryParameters);
            count=UiEffectCoverage.Transforms(preparation.Coordinates,geometryParameters,transforms);
            var factors=EffectBlendState.Factors(geometryParameters);composite=new EffectComposite(factors.Item1,factors.Item2);
        }
        public void PrepareProgram(in EffectProgramPreparation preparation)
        {
            var v=preparation.Parameters;var coordinates=preparation.Coordinates;var source=preparation.Source;var preset=parameterSource?.Invoke(context.Element);
            UiEffectCoverage.Read(v,geometryParameters);
            if(geometry==null||!ReferenceEquals(sourceTopology,source.Topology)||mode!=geometryParameters.ShadowMode||iterations!=geometryParameters.ShadowIteration) {
                var next=context.Own(UiEffectGeometry.Acquire(source.Topology,geometryParameters.ShadowMode,geometryParameters.ShadowIteration));
                if(geometry!=null)context.Retire(geometry);geometry=next;sourceTopology=source.Topology;mode=geometryParameters.ShadowMode;iterations=geometryParameters.ShadowIteration;
            }
            var keys=new Vector3(UiEffectParameters.Number(v,UiEffectParameters.TransitionTexture),UiEffectParameters.Number(v,UiEffectParameters.DetailTexture),UiEffectParameters.Number(v,UiEffectParameters.GradationGradient));
            var origins=new Vector3(UiEffectParameters.Number(v,UiEffectParameters.TransitionTexture+1),UiEffectParameters.Number(v,UiEffectParameters.DetailTexture+1),UiEffectParameters.Number(v,UiEffectParameters.GradationGradient+1));
            if(context.Element!=null && (keys!=resourceKeys||origins!=resourceOrigins)) {
                var style=preparation.Frame.Style;
                var ready=PrepareImage(style,"--urui-fx-transition-texture") & PrepareImage(style,"--urui-fx-detail-texture") & PrepareImage(style,"--urui-fx-gradation-gradient");
                cssTransition=resources?.GetImage("--urui-fx-transition-texture");cssDetail=resources?.GetImage("--urui-fx-detail-texture");cssGradient=resources?.GetImage("--urui-fx-gradation-gradient");if(ready){resourceKeys=keys;resourceOrigins=origins;}
            }
            var nextTransition=origins.x>=(float)StyleValueOrigin.Author?cssTransition:preset?.TransitionTexture;
            var nextDetail=origins.y>=(float)StyleValueOrigin.Author?cssDetail:preset?.DetailTexture;
            var nextGradient=cssGradient;var gradation=(GradationMode)UiEffectParameters.Number(v,UiEffectParameters.GradationMode);
            if(!nextGradient&&(gradation==GradationMode.HorizontalGradient||gradation==GradationMode.VerticalGradient||gradation==GradationMode.AngleGradient)) {
                var requested=preset?.GradationGradient??defaultGradient;
                if(gradientLut==null||!ReferenceEquals(gradientSource,requested)){var next=context.Own(EffectGradientCache.Acquire(requested));if(gradientLut!=null)context.Retire(gradientLut);gradientLut=next;gradientSource=requested;}
                nextGradient=gradientLut.Texture;
            }
            var nextAtlas=UiEffectGeometry.Texture;var nextQuality=EffectSampling.Quality(geometryParameters.SamplingFilter);
            // 算法族属于固定程序身份；强度、颜色与时间仍在参数表中，不能改写在途材质。
            var nextComplexPixel=UiEffectParameters.Number(v,UiEffectParameters.TransitionFilter)!=0||UiEffectParameters.Number(v,UiEffectParameters.DetailFilter)!=0||UiEffectParameters.Number(v,UiEffectParameters.EdgeMode)!=0||gradation!=GradationMode.None;
            var nextBlend=context.Element==null?EffectBlendState.Factors(geometryParameters):(BlendMode.One,BlendMode.OneMinusSrcAlpha);
            if(program==null||blendState!=nextBlend||quality!=nextQuality||complexPixel!=nextComplexPixel||transition!=nextTransition||detail!=nextDetail||gradient!=nextGradient||atlas!=nextAtlas) {
                var next=context.Own(EffectProgramCache.Acquire(nextBlend.Item1,nextBlend.Item2,nextQuality,nextTransition,nextDetail,nextGradient,nextAtlas,nextComplexPixel));
                if(program!=null)context.Retire(program);program=next;blendState=nextBlend;quality=nextQuality;complexPixel=nextComplexPixel;transition=nextTransition;detail=nextDetail;gradient=nextGradient;atlas=nextAtlas;
            }
            var transitionMode=(TransitionFilter)UiEffectParameters.Number(v,UiEffectParameters.TransitionFilter);
            var timed=transitionMode!=TransitionFilter.None&&(UiEffectParameters.Number(v,UiEffectParameters.TransitionAutoPlaySpeed)!=0||UiEffectParameters.Number(v,UiEffectParameters.TransitionTextureSpeedX)!=0||UiEffectParameters.Number(v,UiEffectParameters.TransitionTextureSpeedY)!=0)||
                UiEffectParameters.Number(v,UiEffectParameters.DetailFilter)!=0&&(UiEffectParameters.Number(v,UiEffectParameters.DetailTextureSpeedX)!=0||UiEffectParameters.Number(v,UiEffectParameters.DetailTextureSpeedY)!=0)||UiEffectParameters.Number(v,UiEffectParameters.EdgeMode)==2&&UiEffectParameters.Number(v,UiEffectParameters.EdgeShinyAutoPlaySpeed)!=0;
            TimeInput=timed?RenderTimeInput.UiClock:RenderTimeInput.None;
            Vector4 RectRow(Rect b)=>new(b.x,b.y,b.width,b.height);
            runtime[0]=RectRow(source.CaptureBounds);var uv=source.UvBounds;runtime[1]=new Vector4(uv.xMin,uv.yMin,uv.xMax,uv.yMax);
            runtime[2]=new Vector4(1f/source.PixelSize.x,1f/source.PixelSize.y,source.PixelSize.x,source.PixelSize.y);
            runtime[3]=RectRow(coordinates.ContentRect);runtime[4]=RectRow(coordinates.RootContentRect);
            var inverse=coordinates.ElementToDocument.inverse;var rootInverse=coordinates.RootToDocument.inverse;
            for(var i=0;i<4;i++){runtime[5+i]=inverse.GetColumn(i);runtime[9+i]=rootInverse.GetColumn(i);}
            uv=source.Topology.UvBounds;runtime[13]=new Vector4(uv.xMin,uv.yMin,uv.xMax,uv.yMax);
            GeometryTransform=source.IsCaptured?coordinates.ElementToDocument:Matrix4x4.identity;
            InkBounds=UiEffectCoverage.Ink(coordinates,source,geometryParameters,geometry);
        }
        bool PrepareImage(ComputedStyleSnapshot style,string name)
        {
            var image=style.GetImage(name);if(resources!=null)return resources.PrepareImage(name,image);
            if(image.Kind!=ComputedImageKind.None)throw new InvalidOperationException("效果纹理需要 EffectModule 的资源解析器："+name);
            return true;
        }
        public void CopyRuntimeParameters(Span<Vector4> values)=>runtime.AsSpan().CopyTo(values);
        public void CommitResources()=>UiEffectGeometry.Commit();
        public void GetPreviewParameters(Span<Vector4> values)=>UiEffectParameters.Write(values,parameterSource?.Invoke(context.Element)??throw new InvalidOperationException("预览需要显式参数。"));
        public void Prepare(EffectFrame frame)=>throw new InvalidOperationException("UI 程序使用固定参数准备。");
        public void Record(EffectRenderContext context)=>throw new InvalidOperationException("UI 程序由原生计划直接提交。");
        public void Dispose(){geometry=null;program=null;}
    }
}
