using System;
using System.Collections.Generic;
using UnityEngine;

namespace URUI.Rendering.Effects
{
    /// <summary>原始绘制几何的不可变副本；同一原生几何身份复用，不依赖 Record 阶段的 DOM。</summary>
    public sealed class EffectGeometry
    {
        internal readonly Vector3[] PositionData;
        internal readonly Vector2[] UvData;
        internal readonly Color32[] ColorData;
        internal readonly int[] IndexData;
        public ulong GeometryId { get; }
        public IReadOnlyList<Vector3> Positions { get; }
        public IReadOnlyList<Vector2> UVs { get; }
        public IReadOnlyList<Color32> Colors { get; }
        public IReadOnlyList<int> Indices { get; }
        public Rect Bounds { get; }
        /// <summary>URUI 原始 UV 坐标范围；借用 Unity 纹理的 Y 翻转另由 EffectFrame 声明。</summary>
        public Rect UvBounds { get; }
        internal EffectGeometry(ulong id, Vector3[] positions, Vector2[] uv, Color32[] colors, int[] indices)
        {
            if (positions.Length == 0 || positions.Length != uv.Length || positions.Length != colors.Length)
            { throw new ArgumentException("效果原始几何数据不完整。"); }
            GeometryId = id; PositionData = positions; UvData = uv; ColorData = colors; IndexData = indices;
            Positions = Array.AsReadOnly(positions); UVs = Array.AsReadOnly(uv);
            Colors = Array.AsReadOnly(colors); Indices = Array.AsReadOnly(indices);
            var low = new Vector2(positions[0].x, positions[0].y); var high = low;
            var uvLow = uv[0]; var uvHigh = uvLow;
            for (var index = 1; index < positions.Length; index++)
            {
                var point = new Vector2(positions[index].x, positions[index].y);
                low = Vector2.Min(low, point); high = Vector2.Max(high, point);
                uvLow = Vector2.Min(uvLow, uv[index]); uvHigh = Vector2.Max(uvHigh, uv[index]);
            }
            Bounds = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
            UvBounds = Rect.MinMaxRect(uvLow.x, uvLow.y, uvHigh.x, uvHigh.y);
        }
        internal static readonly EffectGeometry CapturedQuad = new(0,
            new[] { Vector3.zero, Vector3.right, new Vector3(1, 1), Vector3.up },
            new[] { Vector2.up, Vector2.one, Vector2.right, Vector2.zero },
            new Color32[] { Color.white, Color.white, Color.white, Color.white }, new[] { 0, 1, 2, 0, 2, 3 });
    }
}
