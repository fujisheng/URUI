using System;
using UnityEngine;
using URUI.Rendering.Effects;
namespace URUI.Effect.Rendering
{
    internal static class UIEffectCoverage
    {
        static readonly Vector2[][] directions = { Array.Empty<Vector2>(), new[] { Vector2.one }, new[] { Vector2.one, Vector2.right, Vector2.up }, new[] { Vector2.one, -Vector2.one, new Vector2(1, -1), new Vector2(-1, 1) }, new[] { Vector2.one, -Vector2.one, new Vector2(1, -1), new Vector2(-1, 1), Vector2.right, Vector2.up, Vector2.left, Vector2.down } };
        internal static void Read(ReadOnlySpan<Vector4> v, EffectParameters p)
        {
            p.SamplingFilter = (SamplingFilter)UIEffectParameters.Number(v, UIEffectParameters.SamplingFilter);
            p.TransitionFilter = (TransitionFilter)UIEffectParameters.Number(v, UIEffectParameters.TransitionFilter);
            p.AllowToModifyMeshShape = UIEffectParameters.Number(v, UIEffectParameters.AllowToModifyMeshShape) != 0;
            p.Flip = (Flip)Mathf.Clamp(Mathf.RoundToInt(UIEffectParameters.Number(v, UIEffectParameters.Flip)), 0, 15);
            p.ShadowMode = (ShadowMode)UIEffectParameters.Number(v, UIEffectParameters.ShadowMode);
            p.ShadowDistance = new Vector2(Mathf.Clamp(UIEffectParameters.Number(v, UIEffectParameters.ShadowDistanceX), -600, 600), Mathf.Clamp(UIEffectParameters.Number(v, UIEffectParameters.ShadowDistanceY), -600, 600));
            p.ShadowIteration = Mathf.Clamp(Mathf.RoundToInt(UIEffectParameters.Number(v, UIEffectParameters.ShadowIteration)), 1, 5);
            p.ShadowMirrorScale = Mathf.Clamp(UIEffectParameters.Number(v, UIEffectParameters.ShadowMirrorScale), 0, 2);
            p.BlendType = (BlendType)UIEffectParameters.Number(v, UIEffectParameters.BlendType);
            p.SrcBlendMode = (UnityEngine.Rendering.BlendMode)UIEffectParameters.Number(v, UIEffectParameters.SrcBlendMode);
            p.DstBlendMode = (UnityEngine.Rendering.BlendMode)UIEffectParameters.Number(v, UIEffectParameters.DstBlendMode);
        }
        internal static Vector2 Distance(EffectParameters p)
        {
            var d = p.ShadowDistance;
            if ((p.Flip & Flip.Shadow) != 0)
            {
                if ((p.Flip & Flip.Horizontal) != 0)
                {
                    d.x = -d.x;
                }
                if ((p.Flip & Flip.Vertical) != 0)
                {
                    d.y = -d.y;
                }
            }
            return d;
        }
        internal static Vector4 Expansion(EffectParameters p)
        {
            if (!p.AllowToModifyMeshShape)
            {
                return Vector4.zero;
            }
            var e = p.SamplingFilter switch
            {
                SamplingFilter.BlurFast => Vector4.one * 10,
                SamplingFilter.BlurMedium => Vector4.one * 15,
                SamplingFilter.BlurDetail => Vector4.one * 20,
                SamplingFilter.RgbShift => new Vector4(40, 0, 40, 0),
                _ => Vector4.zero
            };
            if (p.TransitionFilter == TransitionFilter.Melt)
            {
                e.w += 40;
            }
            else if (p.TransitionFilter == TransitionFilter.Burn)
            {
                e.y += 40;
            }
            return e;
        }
        internal static Vector4 CaptureExpansion(EffectParameters p)
        {
            var e = Expansion(p);
            if ((p.Flip & (Flip.Horizontal | Flip.Vertical)) != 0)
            {
                e = Vector4.one * Mathf.Max(e.x, e.y, e.z, e.w);
            }
            for (var i = 0; i < 4; i++)
            {
                e[i] = e[i] > 0 ? e[i] * .5f + .002f : 0;
            }
            return e;
        }
        internal static int Transforms(in EffectProgramCoordinates layout, EffectParameters p, Span<Matrix4x4> result)
        {
            var flags = p.Flip;
            var distance = Distance(p);
            var flip = (flags & (Flip.Horizontal | Flip.Vertical)) != 0;
            var shadow = p.ShadowMode != ShadowMode.None && distance != Vector2.zero;
            if (!flip && !shadow)
            {
                return 0;
            }
            var center = layout.ContentRect.center;
            var reflection = Matrix4x4.identity;
            if (flip)
            {
                reflection = Matrix4x4.Translate(center) * Matrix4x4.Scale(new Vector3((flags & Flip.Horizontal) != 0 ? -1 : 1, (flags & Flip.Vertical) != 0 ? -1 : 1, 1)) * Matrix4x4.Translate(-center);
            }
            var matrix = layout.ElementToDocument;
            var inverse = matrix.inverse;
            var index = 0;
            if (flip)
            {
                result[index++] = matrix * reflection * inverse;
            }
            if (shadow && p.ShadowMode == ShadowMode.Mirror)
            {
                var mirror = Matrix4x4.identity;
                mirror[1, 1] = -p.ShadowMirrorScale;
                mirror[1, 3] = 2 * layout.ContentRect.y + (layout.ContentRect.height + layout.RootContentRect.height) * .5f * (1 + p.ShadowMirrorScale) - distance.y;
                result[index++] = matrix * mirror * reflection * inverse;
            }
            else if (shadow)
            {
                var offset = Vector2.zero;
                for (var iteration = 0; iteration < p.ShadowIteration; iteration++)
                {
                    offset += distance / (iteration + 1);
                    foreach (var direction in directions[(int)p.ShadowMode])
                    {
                        result[index++] = matrix * Matrix4x4.Translate(new Vector3(offset.x * direction.x, -offset.y * direction.y, 0)) * reflection * inverse;
                    }
                }
            }
            return index;
        }
        internal static Rect Project(Rect bounds, Matrix4x4 transform)
        {
            var low = Vector2.positiveInfinity;
            var high = Vector2.negativeInfinity;
            for (var corner = 0; corner < 4; corner++)
            {
                var p = transform.MultiplyPoint(new Vector3((corner & 1) == 0 ? bounds.xMin : bounds.xMax, (corner & 2) == 0 ? bounds.yMin : bounds.yMax));
                low = Vector2.Min(low, p);
                high = Vector2.Max(high, p);
            }
            return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }
        internal static int ExpansionIndex(EffectParameters p)
        {
            if (!p.AllowToModifyMeshShape)
            {
                return 0;
            }
            var family = p.SamplingFilter switch
            {
                SamplingFilter.BlurFast => 1,
                SamplingFilter.BlurMedium => 2,
                SamplingFilter.BlurDetail => 3,
                SamplingFilter.RgbShift => 4,
                _ => 0
            };
            var transition = p.TransitionFilter == TransitionFilter.Melt ? 1 : p.TransitionFilter == TransitionFilter.Burn ? 2 : 0;
            if (transition != 0 && (p.Flip & Flip.Effect) == 0 && (p.Flip & Flip.Vertical) != 0)
            {
                transition = 3 - transition;
            }
            return family * 3 + transition;
        }
        internal static Rect Ink(in EffectProgramCoordinates layout, in EffectProgramSource source, EffectParameters p, UIEffectGeometry.Lease geometry)
        {
            var bounds = source.IsCaptured ? source.Topology.Bounds : geometry.ExpandedBounds(ExpansionIndex(p));
            if (source.IsCaptured)
            {
                var inverse = layout.ElementToDocument.inverse;
                if (inverse.Equals(Matrix4x4.zero))
                {
                    return layout.ContentRect;
                }
                var capture = source.CaptureBounds;
                var uv = source.UvBounds;
                var e = Expansion(p);
                if ((p.Flip & Flip.Effect) == 0 && (p.Flip & Flip.Vertical) != 0)
                {
                    (e.y, e.w) = (e.w, e.y);
                }
                var distance = Distance(p);
                var modified = e != Vector4.zero || (p.Flip & (Flip.Horizontal | Flip.Vertical)) != 0 || p.ShadowMode != ShadowMode.None && distance != Vector2.zero;
                var world = modified ? new Rect(capture.x + uv.x * capture.width, capture.y + (1 - uv.yMax) * capture.height, uv.width * capture.width, uv.height * capture.height) : capture;
                if (modified)
                {
                    var sx = world.width * .5f / Mathf.Max(.00001f, world.width - .002f);
                    var sy = world.height * .5f / Mathf.Max(.00001f, world.height - .002f);
                    world = Rect.MinMaxRect(world.xMin - e.x * sx, world.yMin - e.y * sy, world.xMax + e.z * sx, world.yMax + e.w * sy);
                }
                bounds = Project(world, inverse);
            }
            var content = layout.ContentRect;
            if ((p.Flip & Flip.Horizontal) != 0)
            {
                bounds = Rect.MinMaxRect(2 * content.center.x - bounds.xMax, bounds.yMin, 2 * content.center.x - bounds.xMin, bounds.yMax);
            }
            if ((p.Flip & Flip.Vertical) != 0)
            {
                bounds = Rect.MinMaxRect(bounds.xMin, 2 * content.center.y - bounds.yMax, bounds.xMax, 2 * content.center.y - bounds.yMin);
            }
            var d = Distance(p);
            if (p.ShadowMode == ShadowMode.None || d == Vector2.zero)
            {
                return bounds;
            }
            var original = bounds;
            if (p.ShadowMode == ShadowMode.Mirror)
            {
                var height = layout.RootContentRect.height;
                var halfSum = (content.height + height) * .5f;
                var threshold = content.y + halfSum - height * Mathf.Clamp01(d.x);
                if (bounds.yMax < threshold)
                {
                    return bounds;
                }
                var origin = 2 * content.y + halfSum * (1 + p.ShadowMirrorScale) - d.y;
                bounds = Rect.MinMaxRect(bounds.xMin, origin - p.ShadowMirrorScale * bounds.yMax, bounds.xMax, origin - p.ShadowMirrorScale * Mathf.Max(bounds.yMin, threshold));
            }
            else
            {
                var harmonic = 0f;
                for (var iteration = 0; iteration < p.ShadowIteration; iteration++)
                {
                    harmonic += 1f / (iteration + 1);
                }
                var offset = d * harmonic;
                if (p.ShadowMode == ShadowMode.Shadow || p.ShadowMode == ShadowMode.Shadow3)
                {
                    bounds = Rect.MinMaxRect(bounds.xMin + Mathf.Min(0, offset.x), bounds.yMin + Mathf.Min(0, -offset.y), bounds.xMax + Mathf.Max(0, offset.x), bounds.yMax + Mathf.Max(0, -offset.y));
                }
                else
                {
                    bounds = Rect.MinMaxRect(bounds.xMin - Mathf.Abs(offset.x), bounds.yMin - Mathf.Abs(offset.y), bounds.xMax + Mathf.Abs(offset.x), bounds.yMax + Mathf.Abs(offset.y));
                }
            }
            return Rect.MinMaxRect(Mathf.Min(original.xMin, bounds.xMin), Mathf.Min(original.yMin, bounds.yMin), Mathf.Max(original.xMax, bounds.xMax), Mathf.Max(original.yMax, bounds.yMax));
        }
    }
}
