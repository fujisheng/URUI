using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Rendering.Effects;
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

    internal sealed class UiEffectInstance : IElementEffect, IEffectBounds
    {
        static readonly EffectColorProperties colorProperties = new("--urui-fx-color");
        readonly MaterialPropertyBlock properties = new();
        readonly EffectContext context;
        readonly EffectSampling sampling;
        readonly EffectGeometry geometry;
        readonly EffectResources resources;
        EffectGradientCache.Lease gradientLut;
        Gradient gradientSource;
        readonly Gradient defaultGradient=new();
        EffectProgramCache.Lease program;
        int quality = -1;
        (BlendMode,BlendMode) blendState;
        public Rect InkBounds => geometry.Mesh ? geometry.InkBounds : sampling.InkBounds;
        EffectParameters parameters = new();
        readonly Func<URUI.Elements.Element,EffectParameters> parameterSource;
        internal UiEffectInstance(EffectContext context, IEffectResourceResolver resolver,Func<URUI.Elements.Element,EffectParameters> parameterSource=null)
        { this.context = context;this.parameterSource=parameterSource; sampling = new EffectSampling(context); geometry = new EffectGeometry(context); resources = resolver == null ? null : new EffectResources(context, resolver); }

        public void Prepare(EffectFrame frame)
        {
            var style = frame.Style;
            var preset=parameterSource?.Invoke(context.Element);
            parameters.GradationGradient=preset?.GradationGradient??defaultGradient;
            if(style!=null)
            {
                EffectParameterReader.Read(style,parameters);
                PrepareImage(style,"--urui-fx-transition-texture");PrepareImage(style,"--urui-fx-detail-texture");PrepareImage(style,"--urui-fx-gradation-gradient");
                parameters.TransitionTexture=style.GetOrigin("--urui-fx-transition-texture")>=StyleValueOrigin.Author?resources?.GetImage("--urui-fx-transition-texture"):preset?.TransitionTexture;
                parameters.DetailTexture=style.GetOrigin("--urui-fx-detail-texture")>=StyleValueOrigin.Author?resources?.GetImage("--urui-fx-detail-texture"):preset?.DetailTexture;
            }
            else
            {
                if(preset==null)throw new InvalidOperationException("无 DOM 预览必须显式提供参数快照。");
                parameters=preset;
            }
            sampling.Prepare(frame, parameters);
            geometry.Prepare(frame, parameters, sampling.Geometry);
            var requestedQuality = EffectSampling.Quality(parameters.SamplingFilter);
            var blend=style==null?EffectBlendState.Factors(parameters):(BlendMode.One,BlendMode.OneMinusSrcAlpha);
            if (quality != requestedQuality || blendState!=blend)
            {
                var next = context.Own(EffectProgramCache.Acquire(blend.Item1, blend.Item2, requestedQuality));
                if (program != null) { context.Retire(program); } program = next; quality = requestedQuality;blendState=blend;
            }
            properties.Clear();
            properties.SetTexture("_MainTex", frame.Source ? frame.Source : Texture2D.whiteTexture);
            properties.SetInt("_ToneFilter", (int)parameters.ToneFilter); properties.SetFloat("_ToneIntensity", parameters.ToneIntensity);
            properties.SetInt("_ColorFilter", (int)parameters.ColorFilter); properties.SetFloat("_ColorIntensity", parameters.ColorIntensity);
            properties.SetVector("_ColorValue", parameters.Color); properties.SetInt("_ColorGlow", parameters.ColorGlow ? 1 : 0);
            properties.SetInt("_TargetMode", (int)parameters.TargetMode); properties.SetVector("_TargetColor", parameters.TargetColor);
            properties.SetFloat("_TargetRange", parameters.TargetRange); properties.SetFloat("_TargetSoftness", parameters.TargetSoftness);
            properties.SetFloat("_InputBottomLeft", frame.SourceIsBottomLeft ? 1 : 0);
            properties.SetFloat("_InputPremultiplied", frame.SourceIsPremultiplied ? 1 : 0);
            properties.SetFloat("_InputCaptured", frame.SourceIsCaptured ? 1 : 0);
            properties.SetFloat("_UseVertexColor", frame.SourceIsCaptured && !geometry.Mesh ? 0 : 1);
            properties.SetFloat("_UseEffectGeometry", geometry.Mesh ? 1 : 0);
            var uv = frame.SourceUvBounds;
            properties.SetVector("_SourceUvBounds", new Vector4(uv.xMin, uv.yMin, uv.xMax, uv.yMax));
            properties.SetVector("_SourceTexel", new Vector4(1f / frame.SourcePixelSize.x, 1f / frame.SourcePixelSize.y, frame.SourcePixelSize.x, frame.SourcePixelSize.y));
            properties.SetInt("_SamplingFilter", (int)parameters.SamplingFilter); properties.SetFloat("_SamplingIntensity", parameters.SamplingIntensity);
            properties.SetFloat("_SamplingWidth", parameters.SamplingWidth); properties.SetFloat("_SamplingScale", parameters.SamplingScale);
            EffectVisualProperties.Bind(properties, frame, parameters);
            var gradient = resources?.GetImage("--urui-fx-gradation-gradient");
            if (!gradient && (parameters.GradationMode == GradationMode.HorizontalGradient || parameters.GradationMode == GradationMode.VerticalGradient || parameters.GradationMode == GradationMode.AngleGradient))
            {
                if (gradientLut == null || !ReferenceEquals(gradientSource, parameters.GradationGradient))
                {
                    var next = context.Own(EffectGradientCache.Acquire(parameters.GradationGradient));
                    if (gradientLut != null) context.Retire(gradientLut);
                    gradientLut = next; gradientSource = parameters.GradationGradient;
                }
                gradient = gradientLut.Texture;
            }
            EffectGradation.Bind(properties, frame, parameters, gradient);
            EffectShadowProperties.Bind(properties, parameters);
            EffectGeometry.BindMirror(properties, frame, parameters);
        }
        void PrepareImage(ComputedStyleSnapshot style, string name)
        {
            var image = style.GetImage(name);
            if (resources != null) { resources.PrepareImage(name, image); }
            else if (image.Kind != ComputedImageKind.None) { throw new InvalidOperationException("效果纹理需要 EffectModule 的资源解析器：" + name); }
        }
        public void Record(EffectRenderContext context)
        {
            if (geometry.Mesh) { context.DrawGeometry(geometry.Mesh, program.Program.Material, Matrix4x4.identity, properties); }
            else if (sampling.Geometry) { context.DrawGeometry(sampling.Geometry, program.Program.Material, Matrix4x4.identity, properties); }
            else { context.Draw(program.Program.Material, properties); }
        }
        public void Dispose() { }
    }
}
