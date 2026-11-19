using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Rendering;

namespace URUI.Internal.Rendering
{
    /// <summary>
    /// 外部程序的不可变材质版本；Unity 对象仅在录制与真实提交引用归零后复用。
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
        internal sealed class Entry
        {
            internal bool DynamicResources;
            internal Material Material;
            internal int References;
            internal ulong Used;
            internal ulong ResourceVersion;
            internal readonly WeakReference<Material> Source = new(null);
            internal Shader Shader;
            internal int Crc;
            internal int[] Properties;
            internal ShaderPropertyType[] Types;
            internal uint[] TextureVersions;
            internal LocalKeyword[] Keywords;
            internal readonly List<ProgramKey> Keys = new();
            Vector4[] values;
            int[] integers;
            Texture[] textures;
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
                // CRC 只是快速拒绝；ShaderLab 可反射值逐项核对，不把哈希相等当作内容证明。
                for (var i = 0; i < Properties.Length; i++)
                {
                    var id = Properties[i];
                    switch (Types[i])
                    {
                        case ShaderPropertyType.Color:
                            if (!values[i].Equals((Vector4)source.GetColor(id)))
                            {
                                return false;
                            }
                            break;
                        case ShaderPropertyType.Vector:
                            if (!values[i].Equals(source.GetVector(id)))
                            {
                                return false;
                            }
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            if (!values[i].x.Equals(source.GetFloat(id)))
                            {
                                return false;
                            }
                            break;
                        case ShaderPropertyType.Int:
                            if (integers[i] != source.GetInteger(id))
                            {
                                return false;
                            }
                            break;
                        case ShaderPropertyType.Texture:
                            var texture = source.GetTexture(id);
                            var offset = source.GetTextureOffset(id);
                            var scale = source.GetTextureScale(id);
                            if ((texture ? texture.updateCount : 0) != TextureVersions[i] || textures[i] != texture || !values[i].Equals(new Vector4(offset.x, offset.y, scale.x, scale.y)))
                            {
                                return false;
                            }
                            break;
                        default:
                            return false;
                    }
                }
                for (var i = 0; i < Keywords.Length; i++)
                {
                    if (keywords[i] != source.IsKeywordEnabled(Keywords[i]))
                    {
                        return false;
                    }
                }
                return true;
            }
            internal void Capture(Material source, int crc, ulong resourceVersion)
            {
                if (References != 0)
                {
                    throw new InvalidOperationException("不能覆盖仍在使用的程序版本。");
                }
                var shader = source.shader;
                if (Shader != shader)
                {
                    Shader = shader;
                    var count = shader.GetPropertyCount();
                    Properties = new int[count];
                    Types = new ShaderPropertyType[count];
                    TextureVersions = new uint[count];
                    for (var i = 0; i < count; i++)
                    {
                        Properties[i] = shader.GetPropertyNameId(i);
                        Types[i] = shader.GetPropertyType(i);
                    }
                    Keywords = shader.keywordSpace.keywords;
                    values = new Vector4[count];
                    integers = new int[count];
                    textures = new Texture[count];
                    keywords = new bool[Keywords.Length];
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
                }
                // 快照一旦发布便不可写，保存其精确属性值，避免每次匹配再次跨 Unity 边界读同一快照。
                renderQueue = source.renderQueue;
                instancing = source.enableInstancing;
                doubleSided = source.doubleSidedGI;
                illumination = source.globalIlluminationFlags;
                DynamicResources = false;
                for (var i = 0; i < Properties.Length; i++)
                {
                    var id = Properties[i];
                    textures[i] = null;
                    switch (Types[i])
                    {
                        case ShaderPropertyType.Color:
                            values[i] = source.GetColor(id);
                            break;
                        case ShaderPropertyType.Vector:
                            values[i] = source.GetVector(id);
                            break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            values[i] = new Vector4(source.GetFloat(id), 0, 0, 0);
                            break;
                        case ShaderPropertyType.Int:
                            integers[i] = source.GetInteger(id);
                            break;
                        case ShaderPropertyType.Texture:
                            var texture = source.GetTexture(id);
                            var offset = source.GetTextureOffset(id);
                            var scale = source.GetTextureScale(id);
                            textures[i] = texture;
                            TextureVersions[i] = texture ? texture.updateCount : 0;
                            values[i] = new Vector4(offset.x, offset.y, scale.x, scale.y);
                            if (texture && texture is not Texture2D)
                            {
                                DynamicResources = true;
                            }
                            break;
                    }
                }
                for (var i = 0; i < Keywords.Length; i++)
                {
                    keywords[i] = source.IsKeywordEnabled(Keywords[i]);
                }
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
        const int MaximumVersions = 4096;
        readonly List<Entry> entries = new();
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
        internal Binding Capture(ExternalGeometryProgram program)
        {
            using var sample = CaptureMarker.Auto();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ExternalProgramResources));
            }
            var source = program.Material;
            var crc = source.ComputeCRC();
            Entry available = null;
            foreach (var entry in entries)
            {
                if (entry.Matches(source, crc, program.ResourceVersion))
                {
                    entry.Used = generation;
                    return entry.Program(program);
                }
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
            available.Capture(source, crc, program.ResourceVersion);
            available.Used = generation;
            return available.Program(program);
        }
        internal void CommitRecording()
        {
            EndRecording();
            foreach (var entry in entries)
            {
                if (entry.Used == generation)
                {
                    entry.RetainReference();
                    recording.Add(entry);
                }
            }
        }
        internal void EndRecording()
        {
            foreach (var entry in recording)
            {
                entry.ReleaseReference();
            }
            recording.Clear();
        }
        internal void PinSubmission(List<Entry> destination)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ExternalProgramResources));
            }
            foreach (var entry in recording)
            {
                if (!destination.Contains(entry))
                {
                    entry.RetainReference();
                    destination.Add(entry);
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
            foreach (var entry in entries)
            {
                entry.Retire();
            }
            entries.Clear();
        }
    }
}
