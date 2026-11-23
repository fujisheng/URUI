using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Rendering;
using URUI.Rendering.Effects;
using URUI.Styles;
namespace URUI.Effect.Rendering
{
    /// <summary>
    /// 共享网格只随细分变化，波形、颜色和时钟由 GPU 参数表提供。
    /// </summary>
    public static class WaveEffect
    {
        static readonly StylePropertyDefinition[] parameters ={
            new("--urui-wave-fixed-edge",StyleValueType.Keyword),new("--urui-wave-displacement",StyleValueType.Keyword),new("--urui-wave-amplitude",StyleValueType.Pixels),
            new("--urui-wave-frequency",StyleValueType.Number),new("--urui-wave-speed",StyleValueType.Number),new("--urui-wave-secondary",StyleValueType.Number),
            new("--urui-wave-fixed-part",StyleValueType.Number),new("--urui-wave-cross-influence",StyleValueType.Number),new("--urui-wave-cross-scale",StyleValueType.Number),
            new("--urui-wave-columns",StyleValueType.Number),new("--urui-wave-rows",StyleValueType.Number),new("--urui-wave-time-offset",StyleValueType.Number),new("--urui-image-color",StyleValueType.Color)};
        static readonly RenderParameterLayout layout = new(4, new RenderParameterField[]{
            new RenderParameterField(parameters[2],0),new(parameters[3],1),new(parameters[4],2),new(parameters[5],3),
            new(parameters[6],4),new(parameters[7],5),new(parameters[8],6),new(parameters[11],7),
            new(parameters[0],8,keywords:new RenderKeywordMapping[]{new RenderKeywordMapping("left",0),new("right",1),new("top",2),new("bottom",3),new("center",4)}),
            new(parameters[1],9,keywords:new RenderKeywordMapping[]{new RenderKeywordMapping("horizontal",0),new("vertical",1)}),
            new(parameters[9],10),new(parameters[10],11),new(parameters[12],12)});
        public static EffectDefinition Define(string name = "ui-wave") => new(name, EffectKind.Image, new Factory(), "wave:v2", "straight texture input; premultiplied HDR output", parameters, true, renderProgram: EffectProgramDescriptors.Wave, renderParameters: layout);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            MaterialResource.ResetStatics();
            Grid.ResetStatics();
        }
        sealed class Factory : IEffectFactory
        {
            public IElementEffect Create(EffectContext context) => new Instance(context);
        }
        sealed class MaterialResource : IDisposable
        {
            static Material shared;
            static int references;

            internal static void ResetStatics()
            {
                if (shared)
                {
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(shared);
                }
                shared = null;
                references = 0;
            }
            bool disposed;
            internal Material Material => shared;
            internal MaterialResource()
            {
                if (!shared)
                {
                    var shader = UnityEngine.Resources.Load<Shader>("URUI.Effect/WaveImage");
                    if (!shader || !shader.isSupported)
                    {
                        throw new NotSupportedException("缺少 Wave Shader。");
                    }
                    shared = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                references++;
            }
            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                if (--references == 0)
                {
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(shared);
                    shared = null;
                }
            }
        }
        sealed class Grid : IDisposable
        {
            sealed class Entry
            {
                internal Mesh mesh;
                internal int count;
            }
            static readonly Dictionary<(int, int), Entry> entries = new();

            internal static void ResetStatics()
            {
                foreach (var item in entries.Values)
                {
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(item.mesh);
                }
                entries.Clear();
            }

            readonly (int, int) key;
            Entry entry;
            internal Mesh Mesh => entry.mesh;
            internal Grid(int columns, int rows)
            {
                key = (columns, rows);
                if (!entries.TryGetValue(key, out entry))
                {
                    var vertices = new Vector3[(columns + 1) * (rows + 1)];
                    var uv = new Vector2[vertices.Length];
                    var indices = new int[columns * rows * 6];
                    for (int y = 0; y <= rows; y++)
                    {
                        for (int x = 0; x <= columns; x++)
                        {
                            var v = new Vector2((float)x / columns, (float)y / rows);
                            vertices[y * (columns + 1) + x] = v;
                            uv[y * (columns + 1) + x] = v;
                        }
                    }
                    for (int y = 0; y < rows; y++)
                    {
                        for (int x = 0; x < columns; x++)
                        {
                            int v = y * (columns + 1) + x;
                            int i = (y * columns + x) * 6;
                            indices[i] = v;
                            indices[i + 1] = v + columns + 1;
                            indices[i + 2] = v + 1;
                            indices[i + 3] = v + 1;
                            indices[i + 4] = v + columns + 1;
                            indices[i + 5] = v + columns + 2;
                        }
                    }
                    var mesh = new Mesh { name = "URUI shared wave grid", hideFlags = HideFlags.HideAndDontSave };
                    mesh.vertices = vertices;
                    mesh.uv = uv;
                    mesh.uv2 = new Vector2[vertices.Length];
                    mesh.triangles = indices;
                    mesh.RecalculateBounds();
                    entry = new Entry { mesh = mesh };
                    entries.Add(key, entry);
                }
                entry.count++;
            }
            public void Dispose()
            {
                if (entry == null)
                {
                    return;
                }
                var previous = entry;
                entry = null;
                if (--previous.count == 0)
                {
                    entries.Remove(key);
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(previous.mesh);
                }
            }
        }
        sealed class Instance : IElementEffect, IEffectBounds, IParameterImageGeometry
        {
            readonly EffectContext owner;
            readonly MaterialResource material;
            Grid grid;
            int columns;
            int rows;
            float amplitude;
            float speed;
            public Rect InkBounds
            {
                get;
                private set;
            }
            public Material Program => material.Material;
            public RenderTimeInput TimeInput => amplitude != 0 && speed != 0 ? RenderTimeInput.UIClock : RenderTimeInput.None;
            public ExternalGeometryInput Geometry => new(grid.Mesh, 1);
            public Matrix4x4 GeometryTransform
            {
                get;
                private set;
            }
            internal Instance(EffectContext owner)
            {
                this.owner = owner;
                material = owner.Own(new MaterialResource());
            }
            static float Range(float value, float minimum, float maximum, string name)
            {
                if (!float.IsFinite(value) || value < minimum || value > maximum)
                {
                    throw new InvalidOperationException("Wave 参数超出范围：" + name);
                }
                return value;
            }
            public void PrepareProgram(in EffectProgramPreparation preparation)
            {
                var values = preparation.Parameters;
                var motion = values[0];
                var shape = values[1];
                var axis = values[2];
                var c = Range(axis.z, 2, 128, "columns");
                var r = Range(axis.w, 1, 64, "rows");
                if (c != Mathf.Floor(c) || r != Mathf.Floor(r))
                {
                    throw new InvalidOperationException("Wave 细分必须为整数。");
                }
                amplitude = Range(motion.x, 0, float.MaxValue, "amplitude");
                Range(motion.y, 0, float.MaxValue, "frequency");
                speed = Range(motion.z, -float.MaxValue, float.MaxValue, "speed");
                var secondary = Range(motion.w, 0, 1, "secondary");
                Range(shape.x, 0, .95f, "fixed-part");
                var cross = Range(shape.y, 0, 1, "cross-influence");
                var crossScale = Range(shape.z, -float.MaxValue, float.MaxValue, "cross-scale");
                Range(shape.w, -float.MaxValue, float.MaxValue, "time-offset");
                if (grid == null || columns != (int)c || rows != (int)r)
                {
                    var next = owner.Own(new Grid((int)c, (int)r));
                    if (grid != null)
                    {
                        owner.Retire(grid);
                    }
                    grid = next;
                    columns = (int)c;
                    rows = (int)r;
                }
                var size = preparation.Size;
                var offset = preparation.ContentOffset;
                var edge = axis.x;
                var direction = axis.y;
                var vertical = edge == 2 || edge == 3 || edge == 4 && direction == 0;
                var extent = amplitude * (1 + secondary + cross * .5f * (vertical ? size.x : size.y) * Mathf.Abs(crossScale));
                InkBounds = new Rect(offset - (direction == 0 ? new Vector2(extent, 0) : new Vector2(0, extent)), size + (direction == 0 ? new Vector2(2 * extent, 0) : new Vector2(0, 2 * extent)));
                GeometryTransform = Matrix4x4.Translate(new Vector3(offset.x, offset.y, 0));
            }
            public void GetPreviewParameters(Span<Vector4> values)
            {
                values[0] = new Vector4(18, 6, 1.3f, .5f);
                values[1] = new Vector4(.65f, .25f, .01f, 0);
                values[2] = new Vector4(2, 0, 24, 8);
                values[3] = Vector4.one;
            }
            public void Prepare(EffectFrame frame) => throw new InvalidOperationException("Wave 使用固定程序参数准备。");
            public void Record(EffectRenderContext context) => throw new InvalidOperationException("固定参数程序由宿主直接提交。");
            public void Dispose()
            {
                grid = null;
            }
        }
    }
}
