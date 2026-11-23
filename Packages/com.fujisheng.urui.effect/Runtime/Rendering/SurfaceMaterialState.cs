using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace URUI.Effect.Rendering
{
    /// <summary>
    /// 精确比较处理材质的实际输入；版本来自值变化，不使用可能冲突的参数哈希。
    /// </summary>
    internal sealed class SurfaceMaterialState
    {
        struct Value : IEquatable<Value>
        {
            internal Vector4 vector;
            internal Texture texture;
            internal uint update;
            internal int width;
            internal int height;
            internal int format;
            internal int filter;
            internal int wrapU;
            internal int wrapV;
            internal int wrapW;
            internal int aniso;
            internal float bias;
            public bool Equals(Value value) => vector.Equals(value.vector) && ReferenceEquals(texture, value.texture) && update == value.update && width == value.width && height == value.height && format == value.format && filter == value.filter && wrapU == value.wrapU && wrapV == value.wrapV && wrapW == value.wrapW && aniso == value.aniso && bias.Equals(value.bias);
            internal bool SameMaterial(Value value) => vector.Equals(value.vector) && ReferenceEquals(texture, value.texture);
        }
        sealed class ShaderSchema
        {
            internal readonly (int id, ShaderPropertyType type)[] Descriptors;
            internal readonly LocalKeyword[] KeywordHandles;
            internal ShaderSchema(Shader shader)
            {
                Descriptors = new (int, ShaderPropertyType)[shader.GetPropertyCount()];
                for (int i = 0; i < Descriptors.Length; i++)
                {
                    Descriptors[i] = (shader.GetPropertyNameId(i), shader.GetPropertyType(i));
                }
                KeywordHandles = shader.keywordSpace.keywords ?? Array.Empty<LocalKeyword>();
            }
        }
        static readonly Dictionary<Shader, ShaderSchema> shaderSchemas = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => shaderSchemas.Clear();

        static ShaderSchema SchemaFor(Shader shader)
        {
            if (!shaderSchemas.TryGetValue(shader, out var schema))
            {
                schema = new ShaderSchema(shader);
                shaderSchemas.Add(shader, schema);
            }
            return schema;
        }

        Shader shader;
        (int id, ShaderPropertyType type)[] descriptors = Array.Empty<(int, ShaderPropertyType)>();
        Value[] saved = Array.Empty<Value>();
        Value[] scratch = Array.Empty<Value>();
        LocalKeyword[] keywordHandles = Array.Empty<LocalKeyword>();
        bool[] savedKeywords = Array.Empty<bool>();
        bool[] scratchKeywords = Array.Empty<bool>();
        int queue;
        bool instancing;
        bool valid;
        double time;
        internal ulong Version
        {
            get;
            private set;
        }
        internal LocalKeyword[] KeywordHandles => keywordHandles;
        internal bool[] KeywordStates => savedKeywords;
        internal bool Capture(Material material, bool usesTime, double now)
        {
            var changed = !valid || shader != material.shader;
            var inputsChanged = false;
            if (shader != material.shader)
            {
                shader = material.shader;
                var schema = SchemaFor(shader);
                descriptors = schema.Descriptors;
                saved = new Value[descriptors.Length];
                scratch = new Value[descriptors.Length];
                keywordHandles = schema.KeywordHandles;
                savedKeywords = keywordHandles.Length > 0 ? new bool[keywordHandles.Length] : Array.Empty<bool>();
                scratchKeywords = keywordHandles.Length > 0 ? new bool[keywordHandles.Length] : Array.Empty<bool>();
            }
            var keywordsChanged = false;
            for (var i = 0; i < keywordHandles.Length; i++)
            {
                var enabled = material.IsKeywordEnabled(keywordHandles[i]);
                scratchKeywords[i] = enabled;
                if (enabled != savedKeywords[i])
                {
                    keywordsChanged = true;
                }
            }
            if (keywordsChanged)
            {
                changed = true;
            }
            if (queue != material.renderQueue || instancing != material.enableInstancing)
            {
                changed = true;
            }
            if (usesTime && time != now)
            {
                inputsChanged = true;
            }
            for (int i = 0; i < descriptors.Length; i++)
            {
                var parameter = descriptors[i];
                var value = new Value();
                switch (parameter.type)
                {
                    case ShaderPropertyType.Color:
                        value.vector = material.GetColor(parameter.id);
                        break;
                    case ShaderPropertyType.Vector:
                        value.vector = material.GetVector(parameter.id);
                        break;
                    case ShaderPropertyType.Int:
                        value.vector.x = material.GetInteger(parameter.id);
                        break;
                    case ShaderPropertyType.Texture:
                        value.texture = material.GetTexture(parameter.id);
                        if (value.texture)
                        {
                            var texture = value.texture;
                            value.update = texture.updateCount;
                            value.width = texture.width;
                            value.height = texture.height;
                            value.format = (int)texture.graphicsFormat;
                            value.filter = (int)texture.filterMode;
                            value.wrapU = (int)texture.wrapModeU;
                            value.wrapV = (int)texture.wrapModeV;
                            value.wrapW = (int)texture.wrapModeW;
                            value.bias = texture.mipMapBias;
                            value.aniso = texture.anisoLevel;
                            // 其他GPU表面的内容没有输入帧版本，不能将其误判为静态。
                            if (texture is RenderTexture)
                            {
                                inputsChanged = true;
                            }
                        }
                        break;
                    default:
                        value.vector.x = material.GetFloat(parameter.id);
                        break;
                }
                for (int component = 0; component < 4; component++)
                {
                    if (!float.IsFinite(value.vector[component]))
                    {
                        throw new InvalidOperationException("效果材质包含非有限参数：" + material.name + " / " + parameter.id);
                    }
                }
                scratch[i] = value;
                if (!value.SameMaterial(saved[i]))
                {
                    changed = true;
                }
                if (!value.Equals(saved[i]))
                {
                    inputsChanged = true;
                }
            }
            if (!changed && !inputsChanged)
            {
                return false;
            }
            (saved, scratch) = (scratch, saved);
            if (keywordsChanged)
            {
                (savedKeywords, scratchKeywords) = (scratchKeywords, savedKeywords);
            }
            queue = material.renderQueue;
            instancing = material.enableInstancing;
            time = now;
            valid = true;
            Version++;
            return changed;
        }
    }
    /// <summary>
    /// 材质快照只有GPU fence结束后才能回到空闲队列，避免每帧创建和提前改写。
    /// </summary>
    internal sealed class SurfaceMaterialPool : IDisposable
    {
        readonly Stack<Lease> idle = new();
        bool disposed;
        internal sealed class Lease : IDisposable
        {
            SurfaceMaterialPool owner;
            internal readonly Material Value;
            internal Material Material => owner != null ? Value : null;
            internal Lease(SurfaceMaterialPool owner, Material material)
            {
                this.owner = owner;
                Value = material;
            }
            internal void Open(SurfaceMaterialPool pool)
            {
                owner = pool;
            }
            public void Dispose()
            {
                var previous = owner;
                if (previous == null)
                {
                    return;
                }
                owner = null;
                previous.Return(this);
            }
        }
        internal Lease Rent(Material source, LocalKeyword[] keywordHandles, bool[] keywordStates)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SurfaceMaterialPool));
            }
            var lease = idle.Count > 0 ? idle.Pop() : new Lease(this, new Material(source.shader) { hideFlags = HideFlags.HideAndDontSave });
            var material = lease.Value;
            if (material.shader != source.shader)
            {
                material.shader = source.shader;
            }
            material.CopyPropertiesFromMaterial(source);
            for (var i = 0; i < keywordHandles.Length; i++)
            {
                material.SetKeyword(keywordHandles[i], keywordStates[i]);
            }
            material.renderQueue = source.renderQueue;
            material.enableInstancing = source.enableInstancing;
            lease.Open(this);
            return lease;
        }
        void Return(Lease lease)
        {
            if (disposed || idle.Count >= 4)
            {
                URUI.Resources.GpuRetirement.DestroyOwnedObject(lease.Value);
            }
            else
            {
                idle.Push(lease);
            }
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            while (idle.Count > 0)
            {
                URUI.Resources.GpuRetirement.DestroyOwnedObject(idle.Pop().Value);
            }
        }
    }
}
