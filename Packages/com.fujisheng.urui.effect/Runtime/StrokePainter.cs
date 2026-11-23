using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Elements;
using URUI.Rendering;

namespace URUI.Effect
{
    /// <summary>
    /// 通过公开绘制接口绘制局部坐标线段；线宽来自外置 --urui-line-width 样式。
    /// </summary>
    public sealed class StrokePainter : IDisposable
    {
        readonly Element element;
        Mesh mesh;
        Material material;
        IDisposable registration;
        Vector2[] endpoints = Array.Empty<Vector2>();
        Color[] colors = Array.Empty<Color>();
        readonly List<Vector3> meshVertices = new();
        readonly List<Color> meshColors = new();
        readonly List<int> meshIndices = new();
        bool dirty = true;
        bool disposed;
        float width;

        public StrokePainter(Element element)
        {
            this.element = element ?? throw new ArgumentNullException(nameof(element));
            try
            {
                var shader = UnityEngine.Resources.Load<Shader>("URUI.Effect/Stroke") ?? throw new InvalidOperationException("缺少线段绘制shader。");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                mesh = new Mesh { name = "URUI strokes", hideFlags = HideFlags.HideAndDontSave };
                mesh.MarkDynamic();
                registration = element.Document.AttachPainter(element, "strokes", Paint, Release);
            }
            catch
            {
                Release();
                throw;
            }
        }

        /// <summary>
        /// 端点按起点、终点成对提供，每段对应一个颜色；空数组清除线段。
        /// </summary>
        public void SetSegments(IReadOnlyList<Vector2> points, IReadOnlyList<Color> segmentColors)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(StrokePainter));
            }
            if (points == null || segmentColors == null || points.Count % 2 != 0 || points.Count / 2 != segmentColors.Count)
            {
                throw new ArgumentException("每条线段必须有两个端点和一个颜色。");
            }
            var next = new Vector2[points.Count];
            var nextColors = new Color[segmentColors.Count];
            for (var i = 0; i < next.Length; i++)
            {
                var point = points[i];
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y))
                {
                    throw new ArgumentOutOfRangeException(nameof(points));
                }
                next[i] = point;
            }
            for (var i = 0; i < nextColors.Length; i++)
            {
                var color = segmentColors[i];
                if (!float.IsFinite(color.r) || !float.IsFinite(color.g) || !float.IsFinite(color.b) || !float.IsFinite(color.a))
                {
                    throw new ArgumentOutOfRangeException(nameof(segmentColors));
                }
                nextColors[i] = color;
            }
            endpoints = next;
            colors = nextColors;
            dirty = true;
        }

        void Paint(ElementPaintContext context)
        {
            var raw = element.ResolvedStyle.Get("--urui-line-width") ?? "1px";
            var span = raw.AsSpan();
            if (span.EndsWith("px".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                span = span.Slice(0, span.Length - 2);
            }
            if (!float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var requested) || !float.IsFinite(requested) || requested <= 0)
            {
                throw new InvalidOperationException("--urui-line-width 必须为有限的正px长度。");
            }
            if (dirty || requested != width)
            {
                width = requested;
                BuildMesh();
                dirty = false;
            }
            if (mesh.vertexCount > 0)
            {
                context.DrawMesh(mesh, material, Matrix4x4.identity);
            }
        }

        void BuildMesh()
        {
            meshVertices.Clear();
            meshColors.Clear();
            meshIndices.Clear();
            int expectedVertices = colors.Length * 4;
            int expectedIndices = colors.Length * 6;
            if (meshVertices.Capacity < expectedVertices)
            {
                meshVertices.Capacity = expectedVertices;
            }
            if (meshColors.Capacity < expectedVertices)
            {
                meshColors.Capacity = expectedVertices;
            }
            if (meshIndices.Capacity < expectedIndices)
            {
                meshIndices.Capacity = expectedIndices;
            }
            for (var i = 0; i < colors.Length; i++)
            {
                var from = endpoints[i * 2];
                var to = endpoints[i * 2 + 1];
                var delta = to - from;
                if (delta.sqrMagnitude < .000001f)
                {
                    continue;
                }
                var normal = new Vector2(-delta.y, delta.x).normalized * (width * .5f);
                var start = meshVertices.Count;
                meshVertices.Add(from - normal);
                meshVertices.Add(from + normal);
                meshVertices.Add(to + normal);
                meshVertices.Add(to - normal);
                var color = colors[i];
                meshColors.Add(color);
                meshColors.Add(color);
                meshColors.Add(color);
                meshColors.Add(color);
                meshIndices.Add(start);
                meshIndices.Add(start + 1);
                meshIndices.Add(start + 2);
                meshIndices.Add(start);
                meshIndices.Add(start + 2);
                meshIndices.Add(start + 3);
            }
            mesh.Clear();
            mesh.indexFormat = meshVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(meshVertices);
            mesh.SetColors(meshColors);
            mesh.SetTriangles(meshIndices, 0);
            mesh.RecalculateBounds();
        }

        void Release()
        {
            if (mesh)
            {
                URUI.Resources.GpuRetirement.DestroyOwnedObject(mesh);
                mesh = null;
            }
            if (material)
            {
                URUI.Resources.GpuRetirement.DestroyOwnedObject(material);
                material = null;
            }
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            registration?.Dispose();
            registration = null;
        }
    }
}
