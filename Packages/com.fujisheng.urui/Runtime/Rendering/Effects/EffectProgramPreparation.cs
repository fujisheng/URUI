using System;
using UnityEngine;
namespace URUI.Rendering.Effects
{
    /// <summary>程序准备时的冻结坐标事实；不包含样式对象。</summary>
    public readonly struct EffectProgramCoordinates
    {
        public Rect ContentRect {get;}
        public Rect RootContentRect {get;}
        public Matrix4x4 ElementToDocument {get;}
        public Matrix4x4 RootToDocument {get;}
        /// <summary>使用另一个布局的根坐标，保留自身内容与变换。</summary>
        public EffectProgramCoordinates WithRoot(EffectLayout root)
        {if(root==null)throw new ArgumentNullException(nameof(root));return new(ContentRect,root.RootContentRect,ElementToDocument,root.RootToDocument);}
        internal EffectProgramCoordinates(Rect content,Rect root,Matrix4x4 element,Matrix4x4 parent)
        {ContentRect=content;RootContentRect=root;ElementToDocument=element;RootToDocument=parent;}
    }
    /// <summary>冻结采样事实；Topology 为原始拓扑，统一乘色和 UV 变换仍由原生基础参数提供。</summary>
    public readonly struct EffectProgramSource
    {
        public Texture Texture {get;}
        public EffectGeometry Topology {get;}
        public bool IsCaptured {get;}
        public bool IsBottomLeft {get;}
        public bool IsPremultiplied {get;}
        public Rect CaptureBounds {get;}
        public Rect UvBounds {get;}
        public Vector2Int PixelSize {get;}
        internal EffectProgramSource(Texture texture,EffectGeometry topology,bool captured,bool bottomLeft,Rect capture,Rect uv)
        {Texture=texture;Topology=topology;IsCaptured=captured;IsBottomLeft=bottomLeft;IsPremultiplied=!bottomLeft;CaptureBounds=capture;UvBounds=uv;PixelSize=texture?new Vector2Int(texture.width,texture.height):Vector2Int.one;}
    }
    /// <summary>固定参数程序的私有绑定。连续值与坐标按同步回调解析；公开布局仅在请求快照时生成。</summary>
    public interface IEffectParameterBinding
    {
        /// <summary>生成可保留的公开布局；返回值及其样式必须不可变。</summary>
        EffectLayout ResolveLayout(EffectLayout layout);
        /// <summary>解析冻结坐标事实，不得访问或修改 DOM。</summary>
        EffectProgramCoordinates ResolveCoordinates(EffectProgramCoordinates coordinates);
        /// <summary>覆盖当前程序的固定布局值；Span 仅在本次同步回调中有效，不得保留或修改 DOM。</summary>
        void ResolveProgramParameters(string program,Span<Vector4> values);
    }
    internal interface IPreparedEffectInputs
    {
        bool IsActive(ulong version);
        Vector2 Size {get;}
        Vector2 ContentOffset {get;}
        double Time {get;}
        EffectProgramCoordinates Coordinates {get;}
        EffectProgramSource Source {get;}
        EffectFrame Materialize();
    }
    /// <summary>仅在 PrepareProgram 回调中有效的连续参数视图；Frame 按需生成独立公开快照。</summary>
    public readonly ref struct EffectProgramPreparation
    {
        readonly IPreparedEffectInputs inputs;
        readonly ulong version;
        readonly ReadOnlySpan<Vector4> values;
        internal EffectProgramPreparation(IPreparedEffectInputs inputs,ulong version,ReadOnlySpan<Vector4> values)
        {this.inputs=inputs;this.version=version;this.values=values;}
        void Require(){if(inputs==null||!inputs.IsActive(version))throw new InvalidOperationException("程序准备视图已过期。");}
        public Vector2 Size {get{Require();return inputs.Size;}}
        public Vector2 ContentOffset {get{Require();return inputs.ContentOffset;}}
        public double Time {get{Require();return inputs.Time;}}
        /// <summary>声明布局中的数值；只在当前同步回调中使用，不得保存该 Span。</summary>
        public ReadOnlySpan<Vector4> Parameters {get{Require();return values;}}
        public EffectProgramCoordinates Coordinates {get{Require();return inputs.Coordinates;}}
        public EffectProgramSource Source {get{Require();return inputs.Source;}}
        public EffectFrame Frame {get{Require();return inputs.Materialize();}}
    }
    internal readonly struct ImageProgramInput
    {
        internal readonly Rect Bounds;
        internal readonly Vector2 Size;
        internal readonly double Time;
        internal readonly ulong Clock;
        internal readonly uint SourceFlags;
        internal readonly URUI.Styles.ComputedStyleSnapshot Overrides;
        internal readonly ReadOnlyMemory<Vector4> BoundValues;
        internal ImageProgramInput(Rect bounds,Vector2 size,double time,ulong clock,uint flags,URUI.Styles.ComputedStyleSnapshot overrides,ReadOnlyMemory<Vector4> boundValues)
        {Bounds=bounds;Size=size;Time=time;Clock=clock;SourceFlags=flags;Overrides=overrides;BoundValues=boundValues;}
    }
}
