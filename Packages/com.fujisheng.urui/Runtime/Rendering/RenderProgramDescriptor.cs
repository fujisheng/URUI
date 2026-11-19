using System;
using UnityEngine.Rendering;

namespace URUI.Rendering
{
    public enum RenderTextureEncoding
    {
        Unknown,
        TextureDeclared,
        EncodedPremultiplied,
        LinearPremultiplied,
        LinearStraight
    }
    public enum RenderColorSpace
    {
        Unknown,
        EncodedSrgb,
        Linear,
        Active,
        Target
    }
    public enum RenderAlpha
    {
        Unknown,
        Premultiplied,
        Straight,
        Opaque,
        PartialPremultiplied
    }
    public enum RenderBlendDomain
    {
        Unknown,
        EncodedSrgb,
        Linear,
        Active,
        Target
    }
    public enum RenderTargetCapability
    {
        Unknown,
        Ldr,
        Hdr
    }
    public enum RenderSampling
    {
        Unknown,
        BilinearClamp,
        LocalAddressing,
        Neighborhood
    }
    public enum RenderGeometry
    {
        Unknown,
        Local,
        VertexDisplacement,
        Expanded,
        Captured
    }
    public enum RenderTimeInput
    {
        None,
        UIClock,
        External
    }

    /// <summary>
    /// 程序的机器可读渲染约定；默认值表示未知用户程序，必须保持外部边界。
    /// </summary>
    public readonly struct RenderProgramDescriptor : IEquatable<RenderProgramDescriptor>
    {
        public RenderTextureEncoding Input
        {
            get;
        }
        public RenderColorSpace Computation
        {
            get;
        }
        public RenderColorSpace Output
        {
            get;
        }
        public RenderAlpha Alpha
        {
            get;
        }
        public RenderBlendDomain Blend
        {
            get;
        }
        public BlendMode SourceBlend
        {
            get;
        }
        public BlendMode DestinationBlend
        {
            get;
        }
        public BlendMode SourceAlphaBlend
        {
            get;
        }
        public BlendMode DestinationAlphaBlend
        {
            get;
        }
        public CullMode Cull
        {
            get;
        }
        public ColorWriteMask ColorMask
        {
            get;
        }
        public RenderTargetCapability Target
        {
            get;
        }
        public RenderSampling Sampling
        {
            get;
        }
        public RenderGeometry Geometry
        {
            get;
        }
        public RenderTimeInput Time
        {
            get;
        }
        public bool IsKnown => Input != RenderTextureEncoding.Unknown;

        public RenderProgramDescriptor(RenderTextureEncoding input, RenderColorSpace computation,
            RenderColorSpace output, RenderAlpha alpha, RenderBlendDomain blend, BlendMode sourceBlend,
            BlendMode destinationBlend, RenderTargetCapability target, RenderSampling sampling,
            RenderGeometry geometry, RenderTimeInput time = RenderTimeInput.None,
            BlendMode? sourceAlphaBlend = null, BlendMode? destinationAlphaBlend = null,
            CullMode cull = CullMode.Off, ColorWriteMask colorMask = ColorWriteMask.All)
        {
            Require(input, true);
            Require(computation, true);
            Require(output, true);
            Require(alpha, true);
            Require(blend, true);
            Require(target, true);
            Require(sampling, true);
            Require(geometry, true);
            Require(time, false);
            Require(cull, false);
            var alphaSource = sourceAlphaBlend ?? sourceBlend;
            var alphaDestination = destinationAlphaBlend ?? destinationBlend;
            if ((int)alphaSource < 0 || (int)alphaSource > 10 || (int)alphaDestination < 0 || (int)alphaDestination > 10 || ((int)colorMask & ~15) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceAlphaBlend));
            }
            if ((int)sourceBlend < 0 || (int)sourceBlend > 10 || (int)destinationBlend < 0 || (int)destinationBlend > 10)
            {
                throw new ArgumentOutOfRangeException(nameof(sourceBlend));
            }
            Input = input;
            Computation = computation;
            Output = output;
            Alpha = alpha;
            Blend = blend;
            SourceBlend = sourceBlend;
            DestinationBlend = destinationBlend;
            Target = target;
            SourceAlphaBlend = alphaSource;
            DestinationAlphaBlend = alphaDestination;
            Cull = cull;
            ColorMask = colorMask;
            Sampling = sampling;
            Geometry = geometry;
            Time = time;
        }
        static void Require<T>(T value, bool nonzero) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value) || nonzero && Convert.ToInt32(value) == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "已知程序必须完整声明渲染约定。");
            }
        }
        /// <summary>
        /// 共享栅格区间的必要条件；实际计划仍须核对目标、资源和有序依赖。
        /// </summary>
        public bool CanShareRaster(RenderProgramDescriptor other)
            => IsKnown && other.IsKnown && Input == other.Input && Computation == other.Computation &&
               Output == other.Output && Alpha == other.Alpha && Blend == other.Blend &&
               SourceBlend == other.SourceBlend && DestinationBlend == other.DestinationBlend &&
               Target == other.Target && Sampling == other.Sampling && SourceAlphaBlend == other.SourceAlphaBlend && DestinationAlphaBlend == other.DestinationAlphaBlend && Cull == other.Cull && ColorMask == other.ColorMask;
        public bool Equals(RenderProgramDescriptor other)
            => IsKnown == other.IsKnown && Input == other.Input && Computation == other.Computation && Output == other.Output &&
               Alpha == other.Alpha && Blend == other.Blend && SourceBlend == other.SourceBlend && DestinationBlend == other.DestinationBlend &&
               Target == other.Target && Sampling == other.Sampling && Geometry == other.Geometry && Time == other.Time && SourceAlphaBlend == other.SourceAlphaBlend && DestinationAlphaBlend == other.DestinationAlphaBlend && Cull == other.Cull && ColorMask == other.ColorMask;
        public override bool Equals(object obj) => obj is RenderProgramDescriptor other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(HashCode.Combine(Input, Computation, Output, Alpha, Blend, SourceBlend),
            HashCode.Combine(DestinationBlend, Target, Sampling, Geometry, Time, SourceAlphaBlend, DestinationAlphaBlend, HashCode.Combine(Cull, ColorMask)));
    }
}
