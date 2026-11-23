using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Rendering;

namespace URUI.Internal.Rendering
{
    /// <summary>
    /// 外部程序的不可变材质版本；按源材质实例哈希分桶加速命中，Unity 对象仅在录制与真实提交引用归零后复用。
    /// </summary>
    internal sealed class ExternalProgramResources : IDisposable
    {
        static readonly Unity.Profiling.ProfilerMarker CaptureMarker = new("URUI.ExternalProgram.Capture");
        static readonly Unity.Profiling.ProfilerMarker MatchMarker = new("URUI.ExternalProgram.Match");

        internal readonly struct Binding
        {
            internal readonly ulong Identity;
            internal readonly Entry Owner;
            internal readonly int Pass;
            internal Material Material => Owner.Material;
            internal readonly bool Dynamic;
            internal Binding(ulong identity, Entry owner, int pass, bool dynamic)
            {
                Identity = identity;
                Owner = owner;
                Pass = pass;
                Dynamic = dynamic;
            }
        }

        internal struct ProgramKey
        {
            internal RenderProgramDescriptor Descriptor;
            internal int Pass;
            internal int Components;
            internal ulong Identity;
        }

        sealed class ShaderSchema
        {
            internal readonly int[] FloatIds;
            internal readonly int[] ColorIds;
            internal readonly int[] VectorIds;
            internal readonly int[] IntIds;
            internal readonly int[] TextureIds;
            internal readonly bool[] TextureHasScaleOffset;
            internal readonly LocalKeyword[] Keywords;

            internal ShaderSchema(Shader shader)
            {
                var count = shader.GetPropertyCount();
                int floats = 0, colors = 0, vectors = 0, ints = 0, textures = 0;
                for (var i = 0; i < count; i++)
                {
                    switch (shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            floats++;
                            break;
                        case ShaderPropertyType.Color:
                            colors++;
                            break;
                        case ShaderPropertyType.Vector:
                            vectors++;
                            break;
                        case ShaderPropertyType.Int:
                            ints++;
                            break;
                        case ShaderPropertyType.Texture:
                            textures++;
                            break;
                    }
                }
                FloatIds = floats > 0 ? new int[floats] : Array.Empty<int>();
                ColorIds = colors > 0 ? new int[colors] : Array.Empty<int>();
                VectorIds = vectors > 0 ? new int[vectors] : Array.Empty<int>();
                IntIds = ints > 0 ? new int[ints] : Array.Empty<int>();
                TextureIds = textures > 0 ? new int[textures] : Array.Empty<int>();
                TextureHasScaleOffset = textures > 0 ? new bool[textures] : Array.Empty<bool>();
                floats = colors = vectors = ints = textures = 0;
                for (var i = 0; i < count; i++)
                {
                    var id = shader.GetPropertyNameId(i);
                    switch (shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            FloatIds[floats++] = id;
                            break;
                        case ShaderPropertyType.Color:
                            ColorIds[colors++] = id;
                            break;
                        case ShaderPropertyType.Vector:
                            VectorIds[vectors++] = id;
                            break;
                        case ShaderPropertyType.Int:
                            IntIds[ints++] = id;
                            break;
                        case ShaderPropertyType.Texture:
                            TextureIds[textures] = id;
                            TextureHasScaleOffset[textures] = (shader.GetPropertyFlags(i) & ShaderPropertyFlags.NoScaleOffset) == 0;
                            textures++;
                            break;
                    }
                }
                Keywords = shader.keywordSpace.keywords ?? Array.Empty<LocalKeyword>();
            }
        }

        static readonly Dictionary<Shader, ShaderSchema> shaderSchemas = new();

        static ShaderSchema SchemaFor(Shader shader)
        {
            if (!shaderSchemas.TryGetValue(shader, out var schema))
            {
                schema = new ShaderSchema(shader);
                shaderSchemas.Add(shader, schema);
            }
            return schema;
        }

        internal sealed class Entry
        {
            internal bool DynamicResources;
            internal Material Material;
            internal int References;
            internal ulong Used;
            internal ulong ResourceVersion;
            internal int SourceInstanceId;
            internal readonly WeakReference<Material> Source = new(null);
            internal Shader Shader;
            internal int Crc;
            ShaderSchema schema;
            internal readonly List<ProgramKey> Keys = new();
            float[] floats;
            Vector4[] colors;
            Vector4[] vectors;
            int[] integers;
            Texture[] textures;
            uint[] textureVersions;
            Vector4[] textureST;
            bool[] keywords;
            int renderQueue;
            bool instancing;
            bool doubleSided;
            MaterialGlobalIlluminationFlags illumination;
            bool retired;

            internal void RetainReference()
            {
                if (retired)
                {
                    throw new ObjectDisposedException(nameof(Entry));
                }
                checked
                {
                    References++;
                }
            }

            internal void ReleaseReference()
            {
                if (References == 0)
                {
                    throw new InvalidOperationException("程序版本引用被重复释放。");
                }
                References--;
                Collect();
            }

            internal void Retire()
            {
                retired = true;
                Collect();
            }

            void Collect()
            {
                if (retired && References == 0 && Material)
                {
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(Material);
                    Material = null;
                    Source.SetTarget(null);
                    if (textures != null)
                    {
                        Array.Clear(textures, 0, textures.Length);
                    }
                }
            }

            internal bool Matches(Material source, int crc, ulong resourceVersion)
            {
                using var sample = MatchMarker.Auto();
                if (Crc != crc || ResourceVersion != resourceVersion ||
                    !Source.TryGetTarget(out var previous) || !ReferenceEquals(previous, source) ||
                    !Material || Shader != source.shader ||
                    renderQueue != source.renderQueue || instancing != source.enableInstancing ||
                    doubleSided != source.doubleSidedGI || illumination != source.globalIlluminationFlags)
                {
                    return false;
                }
                var s = schema;
                var floatIds = s.FloatIds;
                for (var i = 0; i < floatIds.Length; i++)
                {
                    if (!floats[i].Equals(source.GetFloat(floatIds[i])))
                    {
                        return false;
                    }
                }
                var colorIds = s.ColorIds;
                for (var i = 0; i < colorIds.Length; i++)
                {
                    if (!colors[i].Equals((Vector4)source.GetColor(colorIds[i])))
                    {
                        return false;
                    }
                }
                var vectorIds = s.VectorIds;
                for (var i = 0; i < vectorIds.Length; i++)
                {
                    if (!vectors[i].Equals(source.GetVector(vectorIds[i])))
                    {
                        return false;
                    }
                }
                var intIds = s.IntIds;
                for (var i = 0; i < intIds.Length; i++)
                {
                    if (integers[i] != source.GetInteger(intIds[i]))
                    {
                        return false;
                    }
                }
                var texIds = s.TextureIds;
                var texHasST = s.TextureHasScaleOffset;
                for (var i = 0; i < texIds.Length; i++)
                {
                    var id = texIds[i];
                    var texture = source.GetTexture(id);
                    if ((texture ? texture.updateCount : 0) != textureVersions[i] || textures[i] != texture)
                    {
                        return false;
                    }
                    if (texHasST[i])
                    {
                        var offset = source.GetTextureOffset(id);
                        var scale = source.GetTextureScale(id);
                        if (!textureST[i].Equals(new Vector4(offset.x, offset.y, scale.x, scale.y)))
                        {
                            return false;
                        }
                    }
                }
                var kw = s.Keywords;
                for (var i = 0; i < kw.Length; i++)
                {
                    if (keywords[i] != source.IsKeywordEnabled(kw[i]))
                    {
                        return false;
                    }
                }
                return true;
            }

            internal void Capture(Material source, int sourceId, int crc, ulong resourceVersion)
            {
                if (References != 0)
                {
                    throw new InvalidOperationException("不能覆盖仍在使用的程序版本。");
                }
                var shader = source.shader;
                if (Shader != shader)
                {
                    Shader = shader;
                    var s = schema = SchemaFor(shader);
                    floats = s.FloatIds.Length > 0 ? new float[s.FloatIds.Length] : Array.Empty<float>();
                    colors = s.ColorIds.Length > 0 ? new Vector4[s.ColorIds.Length] : Array.Empty<Vector4>();
                    vectors = s.VectorIds.Length > 0 ? new Vector4[s.VectorIds.Length] : Array.Empty<Vector4>();
                    integers = s.IntIds.Length > 0 ? new int[s.IntIds.Length] : Array.Empty<int>();
                    textures = s.TextureIds.Length > 0 ? new Texture[s.TextureIds.Length] : Array.Empty<Texture>();
                    textureVersions = s.TextureIds.Length > 0 ? new uint[s.TextureIds.Length] : Array.Empty<uint>();
                    textureST = s.TextureIds.Length > 0 ? new Vector4[s.TextureIds.Length] : Array.Empty<Vector4>();
                    keywords = s.Keywords.Length > 0 ? new bool[s.Keywords.Length] : Array.Empty<bool>();
                }
                if (!Material)
                {
                    Material = new Material(source)
                    {
                        name = "URUI external program snapshot",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }
                else
                {
                    if (Material.shader != shader)
                    {
                        Material.shader = shader;
                    }
                    Material.CopyPropertiesFromMaterial(source);
                    // CopyPropertiesFromMaterial does not synchronize these 4 non-property Material states.
                    Material.renderQueue = source.renderQueue;
                    Material.enableInstancing = source.enableInstancing;
                    Material.doubleSidedGI = source.doubleSidedGI;
                    Material.globalIlluminationFlags = source.globalIlluminationFlags;
                }
                renderQueue = source.renderQueue;
                instancing = source.enableInstancing;
                doubleSided = source.doubleSidedGI;
                illumination = source.globalIlluminationFlags;
                DynamicResources = false;
                var currentSchema = schema;
                var floatIds = currentSchema.FloatIds;
                for (var i = 0; i < floatIds.Length; i++)
                {
                    floats[i] = source.GetFloat(floatIds[i]);
                }
                var colorIds = currentSchema.ColorIds;
                for (var i = 0; i < colorIds.Length; i++)
                {
                    colors[i] = source.GetColor(colorIds[i]);
                }
                var vectorIds = currentSchema.VectorIds;
                for (var i = 0; i < vectorIds.Length; i++)
                {
                    vectors[i] = source.GetVector(vectorIds[i]);
                }
                var intIds = currentSchema.IntIds;
                for (var i = 0; i < intIds.Length; i++)
                {
                    integers[i] = source.GetInteger(intIds[i]);
                }
                var texIds = currentSchema.TextureIds;
                var texHasST = currentSchema.TextureHasScaleOffset;
                for (var i = 0; i < texIds.Length; i++)
                {
                    var id = texIds[i];
                    var texture = source.GetTexture(id);
                    textures[i] = texture;
                    textureVersions[i] = texture ? texture.updateCount : 0;
                    if (texHasST[i])
                    {
                        var offset = source.GetTextureOffset(id);
                        var scale = source.GetTextureScale(id);
                        textureST[i] = new Vector4(offset.x, offset.y, scale.x, scale.y);
                    }
                    if (texture && texture is not Texture2D)
                    {
                        DynamicResources = true;
                    }
                }
                var kw = currentSchema.Keywords;
                for (var i = 0; i < kw.Length; i++)
                {
                    keywords[i] = source.IsKeywordEnabled(kw[i]);
                }
                SourceInstanceId = sourceId;
                Crc = crc;
                ResourceVersion = resourceVersion;
                Source.SetTarget(source);
                Keys.Clear();
            }

            internal Binding Program(ExternalGeometryProgram program)
            {
                foreach (var key in Keys)
                {
                    if (key.Descriptor.Equals(program.Descriptor) && key.Pass == program.Pass && key.Components == program.PositionComponents)
                    {
                        return new Binding(key.Identity, this, key.Pass, DynamicResources || !program.Descriptor.IsKnown || program.Descriptor.Time != RenderTimeInput.None);
                    }
                }
                if (Keys.Count >= 256)
                {
                    throw new InvalidOperationException("同一材质版本的程序声明超出预算。");
                }
                if (nextIdentity == ulong.MaxValue)
                {
                    throw new InvalidOperationException("程序身份已耗尽。");
                }
                var identity = ++nextIdentity;
                Keys.Add(new ProgramKey
                {
                    Descriptor = program.Descriptor,
                    Pass = program.Pass,
                    Components = program.PositionComponents,
                    Identity = identity
                });
                return new Binding(identity, this, program.Pass, DynamicResources || !program.Descriptor.IsKnown || program.Descriptor.Time != RenderTimeInput.None);
            }
        }

        static ulong nextIdentity;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            nextIdentity = 0;
            shaderSchemas.Clear();
        }

        const int MaximumVersions = 4096;
        readonly List<Entry> entries = new();
        readonly Dictionary<int, List<Entry>> buckets = new();
        readonly Stack<List<Entry>> idleBuckets = new();
        readonly List<Entry> recording = new();
        ulong generation;
        bool disposed;

        internal void Begin()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ExternalProgramResources));
            }
            if (generation == ulong.MaxValue)
            {
                throw new InvalidOperationException("程序窗口代际已耗尽。");
            }
            generation++;
        }

        void AddToBucket(int sourceId, Entry entry)
        {
            if (!buckets.TryGetValue(sourceId, out var list))
            {
                list = idleBuckets.Count > 0 ? idleBuckets.Pop() : new List<Entry>(2);
                buckets.Add(sourceId, list);
            }
            list.Add(entry);
        }

        void RemoveFromBucket(int sourceId, Entry entry)
        {
            if (sourceId != 0 && buckets.TryGetValue(sourceId, out var list))
            {
                list.Remove(entry);
                if (list.Count == 0)
                {
                    buckets.Remove(sourceId);
                    if (idleBuckets.Count < 128)
                    {
                        idleBuckets.Push(list);
                    }
                }
            }
        }

        internal Binding Capture(ExternalGeometryProgram program)
        {
            using var sample = CaptureMarker.Auto();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ExternalProgramResources));
            }
            var source = program.Material;
            int sourceId = source.GetInstanceID();
            var crc = source.ComputeCRC();

            // O(1) bucket lookup by source Material instance ID first.
            if (buckets.TryGetValue(sourceId, out var bucket))
            {
                for (int i = 0; i < bucket.Count; i++)
                {
                    var candidate = bucket[i];
                    if (candidate.Matches(source, crc, program.ResourceVersion))
                    {
                        candidate.Used = generation;
                        return candidate.Program(program);
                    }
                }
            }

            // Recycle oldest unreferenced entry if any exists.
            Entry available = null;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.References == 0 && entry.Used != generation && (available == null || entry.Used < available.Used))
                {
                    available = entry;
                }
            }
            if (available == null)
            {
                if (entries.Count >= MaximumVersions)
                {
                    throw new InvalidOperationException("外部材质版本仍被当前记录或在途 GPU 提交占用。");
                }
                available = new Entry();
                entries.Add(available);
            }
            else
            {
                RemoveFromBucket(available.SourceInstanceId, available);
            }
            available.Capture(source, sourceId, crc, program.ResourceVersion);
            available.Used = generation;
            AddToBucket(sourceId, available);
            return available.Program(program);
        }

        internal void CommitRecording()
        {
            EndRecording();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry.Used == generation)
                {
                    entry.RetainReference();
                    recording.Add(entry);
                }
            }
        }

        internal void EndRecording()
        {
            for (int i = 0; i < recording.Count; i++)
            {
                recording[i].ReleaseReference();
            }
            recording.Clear();
        }

        internal void PinSubmission(List<Entry> destination, HashSet<Entry> dedup)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ExternalProgramResources));
            }
            for (int i = 0; i < recording.Count; i++)
            {
                var entry = recording[i];
                if (!dedup.Add(entry))
                {
                    continue;
                }
                try
                {
                    entry.RetainReference();
                    destination.Add(entry);
                }
                catch
                {
                    dedup.Remove(entry);
                    throw;
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            EndRecording();
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].Retire();
            }
            entries.Clear();
            buckets.Clear();
        }
    }
}
