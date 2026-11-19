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
                mesh = new Mesh { name = "URUI strokes" };
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
            var text = element.ResolvedStyle.Get("--urui-line-width") ?? "1px";
            if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - 2);
            }
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var requested) || !float.IsFinite(requested) || requested <= 0)
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
            var vertices = new List<Vector3>(colors.Length * 4);
            var tint = new List<Color>(colors.Length * 4);
            var indices = new List<int>(colors.Length * 6);
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
                var start = vertices.Count;
                vertices.Add(from - normal);
                vertices.Add(from + normal);
                vertices.Add(to + normal);
                vertices.Add(to - normal);
                for (var n = 0; n < 4; n++)
                {
                    tint.Add(colors[i]);
                }
                indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetColors(tint);
            mesh.SetTriangles(indices, 0);
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
