using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Rendering.Effects;
using URUI.Rendering;
using URUI.Styles;

namespace URUI.Effect.Rendering
{
    internal sealed class UIEffectFactory : IEffectFactory
    {
        readonly IEffectResourceResolver resources;
        readonly Func<URUI.Elements.Element, EffectParameters> parameters;
        internal UIEffectFactory(IEffectResourceResolver resources, Func<URUI.Elements.Element, EffectParameters> parameters = null)
        {
            this.resources = resources;
            this.parameters = parameters;
        }
        internal static readonly StylePropertyDefinition[] Parameters = CombineParameters();
        static StylePropertyDefinition[] CombineParameters()
        {
            var fields = UIEffectParameters.Layout.Fields;
            var values = new StylePropertyDefinition[fields.Count];
            for (var i = 0; i < fields.Count; i++)
            {
                values[i] = fields[i].Property;
            }
            return values;
        }
        public IElementEffect Create(EffectContext context)
        {
            return new UIEffectInstance(context, resources, parameters);
        }
    }

    internal sealed class UIEffectInstance : IElementEffect, IEffectBounds, IParameterImageGeometry, IParameterImageRuntime, IParameterEffectLayout, IParameterImageResources
    {
        readonly EffectContext context;
        readonly EffectResources resources;
        readonly Func<URUI.Elements.Element, EffectParameters> parameterSource;
        readonly EffectParameters geometryParameters = new();
        static readonly Gradient defaultGradient = new();
        readonly Vector4[] runtime = new Vector4[14];
        UIEffectGeometry.Lease geometry;
        URUI.Rendering.Effects.EffectGeometry sourceTopology;
        ShadowMode mode;
        int iterations;
        EffectProgramCache.Lease program;
        (BlendMode, BlendMode) blendState;
        int quality = -1;
        bool complexPixel;
        Texture transition;
        Texture detail;
        Texture gradient;
        Texture atlas;
        EffectGradientCache.Lease gradientLut;
        Gradient gradientSource;
        Vector3 resourceKeys = new(float.NaN, float.NaN, float.NaN);
        Vector3 resourceOrigins;
        int resourceReadyMask;
        Texture cssTransition;
        Texture cssDetail;
        Texture cssGradient;
        public Rect InkBounds
        {
            get;
            private set;
        }
        public Material Program => program.Program.Material;
        public RenderTimeInput TimeInput
        {
            get;
            private set;
        }
        public ExternalGeometryInput Geometry => new(geometry.Mesh, 1);
        public Matrix4x4 GeometryTransform
        {
            get;
            private set;
        }
        public int RuntimeRowCount => runtime.Length;
        internal UIEffectInstance(EffectContext context, IEffectResourceResolver resolver, Func<URUI.Elements.Element, EffectParameters> parameterSource = null)
        {
            this.context = context;
            this.parameterSource = parameterSource;
            resources = resolver == null ? null : new EffectResources(context, resolver);
        }
        public void PrepareCoverage(in EffectProgramPreparation preparation, Span<Matrix4x4> transforms, out int count, out Vector4 expansion, out EffectComposite composite)
        {
            UIEffectCoverage.Read(preparation.Parameters, geometryParameters);
            expansion = UIEffectCoverage.CaptureExpansion(geometryParameters);
            count = UIEffectCoverage.Transforms(preparation.Coordinates, geometryParameters, transforms);
            var factors = EffectBlendState.Factors(geometryParameters);
            composite = new EffectComposite(factors.Item1, factors.Item2);
        }
        public void PrepareProgram(in EffectProgramPreparation preparation)
        {
            var v = preparation.Parameters;
            var coordinates = preparation.Coordinates;
            var source = preparation.Source;
            var preset = parameterSource?.Invoke(context.Element);
            UIEffectCoverage.Read(v, geometryParameters);
            if (geometry == null || !ReferenceEquals(sourceTopology, source.Topology) || mode != geometryParameters.ShadowMode || iterations != geometryParameters.ShadowIteration)
            {
                var next = context.Own(UIEffectGeometry.Acquire(source.Topology, geometryParameters.ShadowMode, geometryParameters.ShadowIteration));
                if (geometry != null)
                {
                    context.Retire(geometry);
                }
                geometry = next;
                sourceTopology = source.Topology;
                mode = geometryParameters.ShadowMode;
                iterations = geometryParameters.ShadowIteration;
            }
            var keys = new Vector3(UIEffectParameters.Number(v, UIEffectParameters.TransitionTexture), UIEffectParameters.Number(v, UIEffectParameters.DetailTexture), UIEffectParameters.Number(v, UIEffectParameters.GradationGradient));
            var origins = new Vector3(UIEffectParameters.Number(v, UIEffectParameters.TransitionTexture + 1), UIEffectParameters.Number(v, UIEffectParameters.DetailTexture + 1), UIEffectParameters.Number(v, UIEffectParameters.GradationGradient + 1));
            if (context.Element != null)
            {
                bool transitionChanged = keys.x != resourceKeys.x || origins.x != resourceOrigins.x || (resourceReadyMask & 1) == 0;
                bool detailChanged = keys.y != resourceKeys.y || origins.y != resourceOrigins.y || (resourceReadyMask & 2) == 0;
                bool gradientChanged = keys.z != resourceKeys.z || origins.z != resourceOrigins.z || (resourceReadyMask & 4) == 0;
                if (transitionChanged || detailChanged || gradientChanged)
                {
                    var style = preparation.Frame.Style;
                    if (transitionChanged)
                    {
                        if (PrepareImage(style, "--urui-fx-transition-texture"))
                        {
                            resourceKeys.x = keys.x;
                            resourceOrigins.x = origins.x;
                            resourceReadyMask |= 1;
                        }
                        else
                        {
                            resourceReadyMask &= ~1;
                        }
                        cssTransition = resources?.GetImage("--urui-fx-transition-texture");
                    }
                    if (detailChanged)
                    {
                        if (PrepareImage(style, "--urui-fx-detail-texture"))
                        {
                            resourceKeys.y = keys.y;
                            resourceOrigins.y = origins.y;
                            resourceReadyMask |= 2;
                        }
                        else
                        {
                            resourceReadyMask &= ~2;
                        }
                        cssDetail = resources?.GetImage("--urui-fx-detail-texture");
                    }
                    if (gradientChanged)
                    {
                        if (PrepareImage(style, "--urui-fx-gradation-gradient"))
                        {
                            resourceKeys.z = keys.z;
                            resourceOrigins.z = origins.z;
                            resourceReadyMask |= 4;
                        }
                        else
                        {
                            resourceReadyMask &= ~4;
                        }
                        cssGradient = resources?.GetImage("--urui-fx-gradation-gradient");
                    }
                }
            }
            var nextTransition = origins.x >= (float)StyleValueOrigin.Author ? cssTransition : preset?.TransitionTexture;
            var nextDetail = origins.y >= (float)StyleValueOrigin.Author ? cssDetail : preset?.DetailTexture;
            var nextGradient = cssGradient;
            var gradation = (GradationMode)UIEffectParameters.Number(v, UIEffectParameters.GradationMode);
            if (!nextGradient && (gradation == GradationMode.HorizontalGradient || gradation == GradationMode.VerticalGradient || gradation == GradationMode.AngleGradient))
            {
                var requested = preset?.GradationGradient ?? defaultGradient;
                if (gradientLut == null || !ReferenceEquals(gradientSource, requested))
                {
                    var next = context.Own(EffectGradientCache.Acquire(requested));
                    if (gradientLut != null)
                    {
                        context.Retire(gradientLut);
                    }
                    gradientLut = next;
                    gradientSource = requested;
                }
                nextGradient = gradientLut.Texture;
            }
            var nextAtlas = UIEffectGeometry.Texture;
            var nextQuality = EffectSampling.Quality(geometryParameters.SamplingFilter);
            // 算法族属于固定程序身份；强度、颜色与时间仍在参数表中，不能改写在途材质。
            var nextComplexPixel = UIEffectParameters.Number(v, UIEffectParameters.TransitionFilter) != 0 || UIEffectParameters.Number(v, UIEffectParameters.DetailFilter) != 0 || UIEffectParameters.Number(v, UIEffectParameters.EdgeMode) != 0 || gradation != GradationMode.None;
            var nextBlend = context.Element == null ? EffectBlendState.Factors(geometryParameters) : (BlendMode.One, BlendMode.OneMinusSrcAlpha);
            if (program == null || blendState != nextBlend || quality != nextQuality || complexPixel != nextComplexPixel || transition != nextTransition || detail != nextDetail || gradient != nextGradient || atlas != nextAtlas)
            {
                var next = context.Own(EffectProgramCache.Acquire(nextBlend.Item1, nextBlend.Item2, nextQuality, nextTransition, nextDetail, nextGradient, nextAtlas, nextComplexPixel));
                if (program != null)
                {
                    context.Retire(program);
                }
                program = next;
                blendState = nextBlend;
                quality = nextQuality;
                complexPixel = nextComplexPixel;
                transition = nextTransition;
                detail = nextDetail;
                gradient = nextGradient;
                atlas = nextAtlas;
            }
            var transitionMode = (TransitionFilter)UIEffectParameters.Number(v, UIEffectParameters.TransitionFilter);
            var timed = transitionMode != TransitionFilter.None && (UIEffectParameters.Number(v, UIEffectParameters.TransitionAutoPlaySpeed) != 0 || UIEffectParameters.Number(v, UIEffectParameters.TransitionTextureSpeedX) != 0 || UIEffectParameters.Number(v, UIEffectParameters.TransitionTextureSpeedY) != 0) ||
                UIEffectParameters.Number(v, UIEffectParameters.DetailFilter) != 0 && (UIEffectParameters.Number(v, UIEffectParameters.DetailTextureSpeedX) != 0 || UIEffectParameters.Number(v, UIEffectParameters.DetailTextureSpeedY) != 0) || UIEffectParameters.Number(v, UIEffectParameters.EdgeMode) == 2 && UIEffectParameters.Number(v, UIEffectParameters.EdgeShinyAutoPlaySpeed) != 0;
            TimeInput = timed ? RenderTimeInput.UIClock : RenderTimeInput.None;
            Vector4 RectRow(Rect b) => new(b.x, b.y, b.width, b.height);
            runtime[0] = RectRow(source.CaptureBounds);
            var uv = source.UvBounds;
            runtime[1] = new Vector4(uv.xMin, uv.yMin, uv.xMax, uv.yMax);
            runtime[2] = new Vector4(1f / source.PixelSize.x, 1f / source.PixelSize.y, source.PixelSize.x, source.PixelSize.y);
            runtime[3] = RectRow(coordinates.ContentRect);
            runtime[4] = RectRow(coordinates.RootContentRect);
            var inverse = coordinates.ElementToDocument.inverse;
            var rootInverse = coordinates.RootToDocument.inverse;
            for (var i = 0; i < 4; i++)
            {
                runtime[5 + i] = inverse.GetColumn(i);
                runtime[9 + i] = rootInverse.GetColumn(i);
            }
            uv = source.Topology.UvBounds;
            runtime[13] = new Vector4(uv.xMin, uv.yMin, uv.xMax, uv.yMax);
            GeometryTransform = source.IsCaptured ? coordinates.ElementToDocument : Matrix4x4.identity;
            InkBounds = UIEffectCoverage.Ink(coordinates, source, geometryParameters, geometry);
        }
        bool PrepareImage(ComputedStyleSnapshot style, string name)
        {
            var image = style.GetImage(name);
            if (resources != null)
            {
                return resources.PrepareImage(name, image);
            }
            if (image.Kind != ComputedImageKind.None)
            {
                throw new InvalidOperationException("效果纹理需要 EffectModule 的资源解析器：" + name);
            }
            return true;
        }
        public void CopyRuntimeParameters(Span<Vector4> values) => runtime.AsSpan().CopyTo(values);
        public void CommitResources() => UIEffectGeometry.Commit();
        public void GetPreviewParameters(Span<Vector4> values) => UIEffectParameters.Write(values, parameterSource?.Invoke(context.Element) ?? throw new InvalidOperationException("预览需要显式参数。"));
        public void Prepare(EffectFrame frame) => throw new InvalidOperationException("UI 程序使用固定参数准备。");
        public void Record(EffectRenderContext context) => throw new InvalidOperationException("UI 程序由原生计划直接提交。");
        public void Dispose()
        {
            geometry = null;
            program = null;
        }
    }
}
