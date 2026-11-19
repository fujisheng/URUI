using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SourceGeometry = URUI.Rendering.Effects.EffectGeometry;
namespace URUI.Effect.Rendering
{
    // Triangle facts are immutable while their leases are alive. Slots are reused
    // only after EffectContext retires the last GPU use, including queued submits.
    internal static class UIEffectGeometry
    {
        internal const int Capacity = 65536;
        internal sealed class Entry
        {
            internal Mesh Mesh;
            internal int References;
            internal int Offset;
            internal int Length;
            internal SourceGeometry Source;
            internal (ulong, ShadowMode, int) SharedKey;
        }
        internal sealed class Alias
        {
            internal Entry Entry;
            internal int References;
            internal Rect[] Bounds;
            internal int BoundsMask;
        }
        internal sealed class Lease : IDisposable
        {
            readonly (SourceGeometry, ShadowMode, int) key;
            Alias alias;
            internal Mesh Mesh => alias.Entry.Mesh;
            internal Lease((SourceGeometry, ShadowMode, int) key, Alias alias)
            {
                this.key = key;
                this.alias = alias;
            }
            internal Rect ExpandedBounds(int index)
            {
                if (index == 0)
                {
                    return key.Item1.Bounds;
                }
                if (alias.Bounds == null)
                {
                    alias.Bounds = new Rect[15];
                }
                if ((alias.BoundsMask & (1 << index)) == 0)
                {
                    alias.Bounds[index] = BuildBounds(key.Item1, alias.Entry, index);
                    alias.BoundsMask |= 1 << index;
                }
                return alias.Bounds[index];
            }
            public void Dispose()
            {
                if (alias == null)
                {
                    return;
                }
                var previous = alias;
                alias = null;
                if (--previous.References == 0)
                {
                    entries.Remove(key);
                }
                var entry = previous.Entry;
                if (--entry.References != 0)
                {
                    return;
                }
                var bucket = shared[entry.SharedKey];
                bucket.Remove(entry);
                if (bucket.Count == 0)
                {
                    shared.Remove(entry.SharedKey);
                }
                EffectObjectLifetime.Destroy(entry.Mesh);
                Free(entry.Offset, entry.Length);
                if (entries.Count == 0)
                {
                    EffectObjectLifetime.Destroy(texture);
                    texture = null;
                    data = null;
                    free.Clear();
                    dirty = false;
                }
            }
        }
        static readonly Dictionary<(SourceGeometry, ShadowMode, int), Alias> entries = new();
        static readonly Dictionary<(ulong, ShadowMode, int), List<Entry>> shared = new();
        static readonly List<(int Offset, int Length)> free = new();
        static Vector4[] data;
        static Texture2D texture;
        static bool dirty;
        internal static Texture Texture => texture;
        static int Rent(int count)
        {
            if (count <= 0 || count > Capacity)
            {
                throw new InvalidOperationException("UI 效果三角形事实超出参数容量。");
            }
            if (!texture)
            {
                texture = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true)
                {
                    name = "URUI effect triangle facts",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                data = new Vector4[Capacity];
                free.Add((0, Capacity));
            }
            for (var i = 0; i < free.Count; i++)
            {
                if (free[i].Length >= count)
                {
                    var range = free[i];
                    if (range.Length == count)
                    {
                        free.RemoveAt(i);
                    }
                    else
                    {
                        free[i] = (range.Offset + count, range.Length - count);
                    }
                    return range.Offset;
                }
            }
            throw new InvalidOperationException("UI 效果在途三角形事实超出参数容量。");
        }
        static void Free(int offset, int length)
        {
            var index = 0;
            while (index < free.Count && free[index].Offset < offset)
            {
                index++;
            }
            free.Insert(index, (offset, length));
            if (index > 0 && free[index - 1].Offset + free[index - 1].Length == offset)
            {
                var a = free[index - 1];
                free[index - 1] = (a.Offset, a.Length + length);
                free.RemoveAt(index);
                index--;
            }
            if (index + 1 < free.Count && free[index].Offset + free[index].Length == free[index + 1].Offset)
            {
                var a = free[index];
                free[index] = (a.Offset, a.Length + free[index + 1].Length);
                free.RemoveAt(index + 1);
            }
        }
        internal static void Commit()
        {
            if (!dirty)
            {
                return;
            }
            texture.SetPixelData(data, 0);
            texture.Apply(false, false);
            dirty = false;
        }
        static Rect BuildBounds(SourceGeometry source, Entry entry, int index)
        {
            // There are five expansion families and three transition directions.
            // Each source alias owns its size-dependent bounds. Shared GPU facts
            // stay normalized and immutable; numeric animation never walks vertices.
            var family = index / 3;
            var transition = index % 3;
            var ex = family == 1 ? 10f : family == 2 ? 15f : family == 3 ? 20f : family == 4 ? 40f : 0;
            var ey = family == 4 ? 0 : ex;
            var top = ey + (transition == 2 ? 40 : 0);
            var bottom = ey + (transition == 1 ? 40 : 0);
            var b = source.Bounds;
            var size = new Vector2(b.width > 0 ? 1 / (1 / b.width) : 0, b.height > 0 ? 1 / (1 / b.height) : 0);
            var low = Vector2.positiveInfinity;
            var high = Vector2.negativeInfinity;
            for (var start = 0; start < source.Indices.Count; start += 3)
            {
                var row = entry.Offset + start / 3 * 8;
                var center = data[row];
                var span = data[row + 1];
                var pointCenter = b.position + new Vector2(center.x * size.x, center.y * size.y);
                var denominator = new Vector2(Mathf.Max(.00001f, Mathf.Abs(span.x * size.x - .002f)), Mathf.Max(.00001f, Mathf.Abs(span.y * size.y - .002f)));
                for (var corner = 0; corner < 3; corner++)
                {
                    var normalized = data[row + 2 + corner];
                    var point = b.position + new Vector2(normalized.x * size.x, normalized.y * size.y);
                    var x = point.x < b.x + .001f || point.x > b.x + size.x - .001f ? ex : 0;
                    var y = point.y < b.y + .001f ? top : point.y > b.y + size.y - .001f ? bottom : 0;
                    point = pointCenter + Vector2.Scale(point - pointCenter, new Vector2(1 + x / denominator.x, 1 + y / denominator.y));
                    low = Vector2.Min(low, point);
                    high = Vector2.Max(high, point);
                }
            }
            // Preserve the existing subpixel coverage margin at integer RT edges.
            return Rect.MinMaxRect(low.x - .002f, low.y - .002f, high.x + .002f, high.y + .002f);
        }
        static int Directions(ShadowMode mode) => mode switch { ShadowMode.Shadow => 1, ShadowMode.Shadow3 => 3, ShadowMode.Outline => 4, ShadowMode.Outline8 => 8, _ => 0 };
        static Vector2 Position(SourceGeometry source, int id)
        {
            var b = source.Bounds;
            var p = source.Positions[id];
            return new Vector2(b.width == 0 ? 0 : (p.x - b.x) / b.width, b.height == 0 ? 0 : (p.y - b.y) / b.height);
        }
        static Vector2 Coordinate(SourceGeometry source, int id)
        {
            var b = source.UvBounds;
            var p = source.UVs[id];
            return new Vector2(b.width == 0 ? 0 : (p.x - b.x) / b.width, b.height == 0 ? 0 : (p.y - b.y) / b.height);
        }
        static ulong Hash(SourceGeometry source)
        {
            ulong hash = 14695981039346656037;
            void Mix(uint word)
            {
                hash = unchecked((hash ^ word) * 1099511628211);
            }
            Mix((uint)source.Indices.Count);
            foreach (var id in source.Indices)
            {
                var p = Position(source, id);
                var uv = Coordinate(source, id);
                var c = source.Colors[id];
                Mix((uint)BitConverter.SingleToInt32Bits(p.x));
                Mix((uint)BitConverter.SingleToInt32Bits(p.y));
                Mix((uint)BitConverter.SingleToInt32Bits(uv.x));
                Mix((uint)BitConverter.SingleToInt32Bits(uv.y));
                Mix((uint)c.r | ((uint)c.g << 8) | ((uint)c.b << 16) | ((uint)c.a << 24));
            }
            return hash;
        }
        static bool SameTopology(SourceGeometry a, SourceGeometry b)
        {
            if (a.Indices.Count != b.Indices.Count)
            {
                return false;
            }
            for (var index = 0; index < a.Indices.Count; index++)
            {
                var ai = a.Indices[index];
                var bi = b.Indices[index];
                if (!Position(a, ai).Equals(Position(b, bi)) || !Coordinate(a, ai).Equals(Coordinate(b, bi)))
                {
                    return false;
                }
                var ac = a.Colors[ai];
                var bc = b.Colors[bi];
                if (ac.r != bc.r || ac.g != bc.g || ac.b != bc.b || ac.a != bc.a)
                {
                    return false;
                }
            }
            return true;
        }
        internal static Lease Acquire(SourceGeometry source, ShadowMode mode, int iterations)
        {
            if (source == null || source.Indices.Count % 3 != 0)
            {
                throw new InvalidOperationException("UI 效果需要有效三角形源拓扑。");
            }
            var key = (source, mode, iterations);
            if (entries.TryGetValue(key, out var existing))
            {
                existing.References++;
                existing.Entry.References++;
                return new Lease(key, existing);
            }
            var sharedKey = (Hash(source), mode, iterations);
            if (shared.TryGetValue(sharedKey, out var bucket))
            {
                foreach (var candidate in bucket)
                {
                    if (SameTopology(candidate.Source, source))
                    {
                        var alias = new Alias
                        {
                            Entry = candidate,
                            References = 1
                        };
                        candidate.References++;
                        entries.Add(key, alias);
                        return new Lease(key, alias);
                    }
                }
            }
            var count = source.Indices.Count;
            var length = checked(count / 3 * 8);
            var offset = Rent(length);
            Mesh mesh = null;
            try
            {
                var copies = Directions(mode) * iterations;
                var mirror = mode == ShadowMode.Mirror;
                var vertexCount = checked(count * (mirror ? 3 : copies + 1));
                var positions = new Vector3[vertexCount];
                var uv = new Vector2[vertexCount];
                var colors = new Color32[vertexCount];
                var indices = new int[vertexCount];
                var outer = source.Bounds;
                var uvBounds = source.UvBounds;
                Vector2 Position(int id)
                {
                    var p = source.Positions[id];
                    return new Vector2(outer.width == 0 ? 0 : (p.x - outer.x) / outer.width, outer.height == 0 ? 0 : (p.y - outer.y) / outer.height);
                }
                Vector2 Coordinate(int id)
                {
                    var p = source.UVs[id];
                    return new Vector2(uvBounds.width == 0 ? 0 : (p.x - uvBounds.x) / uvBounds.width, uvBounds.height == 0 ? 0 : (p.y - uvBounds.y) / uvBounds.height);
                }
                for (var start = 0; start < count; start += 3)
                {
                    var row = offset + start / 3 * 8;
                    var low = Vector2.positiveInfinity;
                    var high = Vector2.negativeInfinity;
                    var uvLow = low;
                    var uvHigh = high;
                    for (var corner = 0; corner < 3; corner++)
                    {
                        var id = source.Indices[start + corner];
                        var p = Position(id);
                        var t = Coordinate(id);
                        low = Vector2.Min(low, p);
                        high = Vector2.Max(high, p);
                        uvLow = Vector2.Min(uvLow, t);
                        uvHigh = Vector2.Max(uvHigh, t);
                        data[row + 2 + corner] = new Vector4(p.x, p.y, t.x, t.y);
                        data[row + 5 + corner] = (Color)source.Colors[id];
                    }
                    var center = (low + high) * .5f;
                    var uvCenter = (uvLow + uvHigh) * .5f;
                    data[row] = new Vector4(center.x, center.y, uvCenter.x, uvCenter.y);
                    data[row + 1] = new Vector4(high.x - low.x, high.y - low.y, 0, 0);
                    void Add(int target, int corner, int code)
                    {
                        var id = source.Indices[start + corner];
                        var p = Position(id);
                        positions[target] = new Vector3(p.x, p.y, row + code * Capacity);
                        uv[target] = Coordinate(id);
                        colors[target] = source.Colors[id];
                        indices[target] = target;
                    }
                    if (mirror)
                    {
                        var order = new[] { 0, 1, 2, 0, 2, 3 };
                        for (var i = 0; i < 6; i++)
                        {
                            Add(start * 2 + i, i % 3, 128 + order[i]);
                        }
                        for (var i = 0; i < 3; i++)
                        {
                            Add(count * 2 + start + i, i, i);
                        }
                    }
                    else
                    {
                        for (var copy = 1; copy <= copies; copy++)
                        {
                            for (var i = 0; i < 3; i++)
                            {
                                Add((copy - 1) * count + start + i, i, copy * 3 + i);
                            }
                        }
                        for (var i = 0; i < 3; i++)
                        {
                            Add(copies * count + start + i, i, i);
                        }
                    }
                }
                mesh = new Mesh
                {
                    name = "URUI effect GPU template",
                    indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
                };
                mesh.vertices = positions;
                mesh.uv = uv;
                mesh.uv2 = new Vector2[vertexCount];
                mesh.colors32 = colors;
                mesh.triangles = indices;
                mesh.RecalculateBounds();
                var entry = new Entry
                {
                    Mesh = mesh,
                    References = 1,
                    Offset = offset,
                    Length = length,
                    Source = source,
                    SharedKey = sharedKey
                };
                var alias = new Alias
                {
                    Entry = entry,
                    References = 1
                };
                if (bucket == null)
                {
                    bucket = new List<Entry>();
                    shared.Add(sharedKey, bucket);
                }
                bucket.Add(entry);
                entries.Add(key, alias);
                dirty = true;
                return new Lease(key, alias);
            }
            catch
            {
                if (mesh)
                {
                    EffectObjectLifetime.Destroy(mesh);
                }
                Free(offset, length);
                throw;
            }
        }
    }
}
