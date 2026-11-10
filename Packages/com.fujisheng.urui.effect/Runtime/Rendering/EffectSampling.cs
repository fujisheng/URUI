using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Rendering.Effects;
using URUI.Styles;

namespace URUI.Effect.Rendering
{
    /// <summary>采样输入与扩边几何；只在原始几何或结构改变时创建网格。</summary>
    internal sealed class EffectSampling
    {
        sealed class GeometryLease : IDisposable
        {
            internal readonly Mesh Mesh;
            internal GeometryLease(Mesh mesh) { Mesh = mesh; }
            public void Dispose() { if (Mesh) { EffectObjectLifetime.Destroy(Mesh); } }
        }
        readonly EffectContext context;
        GeometryLease expanded;
        ulong sourceGeometry;
        Vector4 currentExpansion;
        internal Rect InkBounds { get; private set; }
        internal Mesh Geometry => expanded?.Mesh;
        internal EffectSampling(EffectContext context) { this.context = context; }
        internal static SamplingFilter ReadMode(string value) => value switch
        {
            "none" => SamplingFilter.None, "blur-fast" => SamplingFilter.BlurFast, "blur-medium" => SamplingFilter.BlurMedium,
            "blur-detail" => SamplingFilter.BlurDetail, "pixelation" => SamplingFilter.Pixelation, "rgb-shift" => SamplingFilter.RgbShift,
            "edge-luminance" => SamplingFilter.EdgeLuminance, "edge-alpha" => SamplingFilter.EdgeAlpha,
            _ => throw new InvalidOperationException("URUI 采样模式无效：" + value)
        };
        internal static int Quality(SamplingFilter mode) => mode >= SamplingFilter.BlurFast && mode <= SamplingFilter.BlurDetail ? (int)mode : 0;
        internal static Vector4 Expansion(EffectLayout layout)
        {
            var style = layout.Style;
            if (style.GetKeyword("--urui-fx-allow-to-modify-mesh-shape") == "false") { return Vector4.zero; }
            var expansion = SourceExpansion(ReadMode(style.GetKeyword("--urui-fx-sampling-filter")), style.GetKeyword("--urui-fx-transition-filter"));
            if ((Mathf.RoundToInt(style.GetNumber("--urui-fx-flip")) & 3) != 0)
            { expansion=Vector4.one*Mathf.Max(expansion.x,expansion.y,expansion.z,expansion.w); }
            // 源网格扩展按每个三角形中心放大，实际外边缘约移动一半；留出浮点收缩的余量。
            return new Vector4(expansion.x > 0 ? expansion.x * .5f + .002f : 0, expansion.y > 0 ? expansion.y * .5f + .002f : 0,
                expansion.z > 0 ? expansion.z * .5f + .002f : 0, expansion.w > 0 ? expansion.w * .5f + .002f : 0);
        }
        static Vector4 SourceExpansion(SamplingFilter mode, string transition)
        {
            var expansion = mode switch
            {
                SamplingFilter.BlurFast => Vector4.one * 10,
                SamplingFilter.BlurMedium => Vector4.one * 15,
                SamplingFilter.BlurDetail => Vector4.one * 20,
                SamplingFilter.RgbShift => new Vector4(40, 0, 40, 0),
                _ => Vector4.zero
            };
            // 源坐标向上，URUI 局部坐标向下：Melt 向底部扩边，Burn 向顶部扩边。
            if (transition == "melt") expansion.w += 40;
            else if (transition == "burn") expansion.y += 40;
            return expansion;
        }
        internal void Prepare(EffectFrame frame, EffectParameters parameters)
        {
            var source = frame.SourceGeometry;
            var expansion = frame.SourceIsCaptured ? Vector4.zero : GeometryExpansion(parameters);
            InkBounds = frame.SourceIsCaptured ? new Rect(frame.ContentOffset, frame.Size) : source.Bounds;
            if (expanded != null && sourceGeometry == source.GeometryId && expansion == currentExpansion)
            { var b = expanded.Mesh.bounds; InkBounds = new Rect(b.min.x, b.min.y, b.size.x, b.size.y); return; }
            GeometryLease next = null;
            if (expansion != Vector4.zero)
            {
                var mesh = Expand(source, expansion);
                try { next = context.Own(new GeometryLease(mesh)); }
                catch { EffectObjectLifetime.Destroy(mesh); throw; }
                var bounds = mesh.bounds; InkBounds = new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
            }
            if (expanded != null) { context.Retire(expanded); }
            expanded = next; sourceGeometry = source.GeometryId; currentExpansion = expansion;
        }
        internal static Vector4 GeometryExpansion(EffectParameters parameters)
        {
            var expansion=parameters.AllowToModifyMeshShape ? SourceExpansion(parameters.SamplingFilter,
                parameters.TransitionFilter == TransitionFilter.Melt ? "melt" : parameters.TransitionFilter == TransitionFilter.Burn ? "burn" : "none") : Vector4.zero;
            if((parameters.Flip&Flip.Effect)==0)
            {
                if((parameters.Flip&Flip.Horizontal)!=0)(expansion.x,expansion.z)=(expansion.z,expansion.x);
                if((parameters.Flip&Flip.Vertical)!=0)(expansion.y,expansion.w)=(expansion.w,expansion.y);
            }
            return expansion;
        }
        static Mesh Expand(global::URUI.Rendering.Effects.EffectGeometry source, Vector4 expansion)
        {
            var count = source.Indices.Count;
            var positions = new Vector3[count]; var uv = new Vector2[count]; var colors = new Color32[count]; var indices = new int[count];
            var outer = source.Bounds;
            for (var start = 0; start < count; start += 3)
            {
                var low = new Vector2(float.MaxValue, float.MaxValue); var high = new Vector2(float.MinValue, float.MinValue);
                var uvLow = low; var uvHigh = high;
                for (var offset = 0; offset < 3; offset++)
                {
                    var id = source.Indices[start + offset]; var point = source.Positions[id]; var coord = source.UVs[id];
                    low = Vector2.Min(low, point); high = Vector2.Max(high, point); uvLow = Vector2.Min(uvLow, coord); uvHigh = Vector2.Max(uvHigh, coord);
                }
                var center = (low + high) * .5f; var uvCenter = (uvLow + uvHigh) * .5f;
                var size = high - low - Vector2.one * .002f;
                for (var offset = 0; offset < 3; offset++)
                {
                    var index = start + offset; var id = source.Indices[index]; var point = source.Positions[id]; var coord = source.UVs[id];
                    if (point.x < outer.xMin + .001f) { var scale = 1 + expansion.x / Mathf.Max(.00001f, Mathf.Abs(size.x)); point.x = center.x + (point.x - center.x) * scale; coord.x = uvCenter.x + (coord.x - uvCenter.x) * scale; }
                    else if (point.x > outer.xMax - .001f) { var scale = 1 + expansion.z / Mathf.Max(.00001f, Mathf.Abs(size.x)); point.x = center.x + (point.x - center.x) * scale; coord.x = uvCenter.x + (coord.x - uvCenter.x) * scale; }
                    if (point.y < outer.yMin + .001f) { var scale = 1 + expansion.y / Mathf.Max(.00001f, Mathf.Abs(size.y)); point.y = center.y + (point.y - center.y) * scale; coord.y = uvCenter.y + (coord.y - uvCenter.y) * scale; }
                    else if (point.y > outer.yMax - .001f) { var scale = 1 + expansion.w / Mathf.Max(.00001f, Mathf.Abs(size.y)); point.y = center.y + (point.y - center.y) * scale; coord.y = uvCenter.y + (coord.y - uvCenter.y) * scale; }
                    positions[index] = point; uv[index] = coord; colors[index] = source.Colors[id]; indices[index] = index;
                }
            }
            var mesh = new Mesh { name = "URUI Effect Sampling", indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.vertices = positions; mesh.uv = uv; mesh.colors32 = colors; mesh.triangles = indices; mesh.RecalculateBounds(); return mesh;
        }
    }
}
