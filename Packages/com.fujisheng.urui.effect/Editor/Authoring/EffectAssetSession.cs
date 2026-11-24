using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using URUI.Resources;

namespace URUI.Effect.Editor.Authoring
{
    /// <summary>
    /// 预设、材质和粒子 Prefab 的显式编辑副本；不将临时引用写入原资产。
    /// </summary>
    public sealed class EffectAssetSession : IDisposable
    {
        readonly UnityEngine.Object source;
        readonly string path;
        string originalHash;
        string savedVersion;
        readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        readonly Dictionary<Material, Material> originalToCopy = new Dictionary<Material, Material>();
        bool prefabContents;
        bool disposed;
        public UnityEngine.Object Working
        {
            get;
            private set;
        }
        public UnityEngine.Object Source => source;
        public string SourcePath => path;
        public bool Dirty => Working && ContentVersion != savedVersion;
        /// <summary>
        /// 工作副本的序列化内容版本，含粒子组件与隔离材质；用于同步原生 Undo 和 Agent 版本。
        /// </summary>
        public string ContentVersion
        {
            get
            {
                if (!Working)
                {
                    return null;
                }
                var text = new System.Text.StringBuilder(EditorJsonUtility.ToJson(Working));
                if (Working is GameObject root)
                {
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                    {
                        text.Append(EditorJsonUtility.ToJson(component));
                    }
                }
                foreach (var material in materials.Keys)
                {
                    if (material)
                    {
                        text.Append(EditorJsonUtility.ToJson(material));
                    }
                }
                using (var sha = SHA256.Create())
                {
                    return Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text.ToString())));
                }
            }
        }
        public EffectAssetSession(UnityEngine.Object source)
        {
            this.source = source ? source : throw new ArgumentNullException(nameof(source));
            path = AssetDatabase.GetAssetPath(source);
            originalHash = File.Exists(path) ? Hash(path) : null;
            try
            {
                if (source is EffectPreset preset)
                {
                    var copy = ScriptableObject.CreateInstance<EffectPreset>();
                    copy.SetParameters(preset.CreateParameters());
                    Working = copy;
                }
                else if (source is Material material)
                {
                    Working = new Material(material);
                }
                else if (source is GameObject gameObject)
                {
                    foreach (var component in gameObject.GetComponentsInChildren<Component>(true))
                    {
                        if (!(component is Transform || component is ParticleSystem || component is ParticleSystemRenderer))
                        {
                            throw new InvalidOperationException("Only Transform, ParticleSystem and ParticleSystemRenderer are supported in particle Prefabs.");
                        }
                    }
                    if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        Working = PrefabUtility.LoadPrefabContents(path);
                        prefabContents = true;
                    }
                    else
                    {
                        Working = UnityEngine.Object.Instantiate(gameObject);
                    }
                    foreach (var system in ((GameObject)Working).GetComponentsInChildren<ParticleSystem>(true))
                    {
                        system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    EnsureMaterialIsolation();
                }
                else
                {
                    throw new InvalidOperationException("Supported asset types: EffectPreset, Material, particle Prefab.");
                }
                Working.hideFlags = HideFlags.HideAndDontSave;
                savedVersion = ContentVersion;
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        public void EnsureMaterialIsolation()
        {
            if (!(Working is GameObject root))
            {
                return;
            }
            foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var values = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < values.Length; i++)
                {
                    var material = values[i];
                    if (!material || materials.ContainsKey(material))
                    {
                        continue;
                    }
                    if (!originalToCopy.TryGetValue(material, out var copy) || !copy)
                    {
                        copy = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                        materials[copy] = material;
                        originalToCopy[material] = copy;
                    }
                    values[i] = copy;
                    changed = true;
                }
                if (changed)
                {
                    renderer.sharedMaterials = values;
                }
            }
        }
        public void MarkChanged()
        {
            EnsureMaterialIsolation();
        }
        /// <summary>
        /// 编辑已定义的 Material 属性；纹理使用项目资产路径。
        /// </summary>
        public void SetMaterial(string property, string value)
        {
            if (!(Working is Material material) || !material.HasProperty(property))
            {
                throw new InvalidOperationException("unsupported_property: material." + property);
            }
            int index = material.shader.FindPropertyIndex(property);
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(material, "Edit URUI material preview");
            switch (material.shader.GetPropertyType(index))
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    material.SetColor(property, ParseColor(value));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    var v = Numbers(value, 4);
                    material.SetVector(property, new Vector4(v[0], v[1], v[2], v[3]));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                    material.SetTexture(property, Asset(value, typeof(Texture)) as Texture);
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Int:
                    material.SetInteger(property, int.Parse(value, CultureInfo.InvariantCulture));
                    break;
                default:
                    material.SetFloat(property, Number(value));
                    break;
            }
            MarkChanged();
        }
        /// <summary>
        /// 只编辑 EffectParameters 明确的公开纯数据字段；复杂渐变和曲线使用原生 Inspector。
        /// </summary>
        public void SetPreset(string property, string value)
        {
            if (!(Working is EffectPreset preset))
            {
                throw new InvalidOperationException("Select an EffectPreset working copy.");
            }
            var field = typeof(EffectParameters).GetField(property, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
            {
                throw new InvalidOperationException("unsupported_property: preset." + property);
            }
            Type type = field.FieldType;
            object parsed;
            if (type == typeof(float))
            {
                parsed = Number(value);
            }
            else if (type == typeof(int))
            {
                parsed = int.Parse(value, CultureInfo.InvariantCulture);
            }
            else if (type == typeof(bool))
            {
                parsed = bool.Parse(value);
            }
            else if (type.IsEnum)
            {
                parsed = Enum.Parse(type, value, false);
                if (!Enum.IsDefined(type, parsed))
                {
                    throw new InvalidOperationException("Unsupported enum value.");
                }
            }
            else if (type == typeof(Color))
            {
                parsed = ParseColor(value);
            }
            else if (type == typeof(Vector2))
            {
                var v = Numbers(value, 2);
                parsed = new Vector2(v[0], v[1]);
            }
            else if (type == typeof(Vector3))
            {
                var v = Numbers(value, 3);
                parsed = new Vector3(v[0], v[1], v[2]);
            }
            else if (type == typeof(EffectRange))
            {
                var v = Numbers(value, 2);
                parsed = new EffectRange(v[0], v[1]);
            }
            else if (typeof(Texture).IsAssignableFrom(type))
            {
                parsed = Asset(value, type);
            }
            else
            {
                throw new InvalidOperationException("unsupported_property: complex curves/gradients use the native asset Inspector");
            }
            var range = field.GetCustomAttribute<RangeAttribute>();
            if (range != null && (type == typeof(float) || type == typeof(int)))
            {
                float number = Convert.ToSingle(parsed, CultureInfo.InvariantCulture);
                if (number < range.min || number > range.max)
                {
                    throw new InvalidOperationException("Preset value outside the declared range.");
                }
            }
            var candidate = preset.CreateParameters();
            field.SetValue(candidate, parsed);
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(preset, "Edit URUI preset preview");
            preset.SetParameters(candidate);
            MarkChanged();
        }
        public object Describe()
        {
            if (Working is EffectPreset preset)
            {
                var names = new List<object>();
                foreach (var field in typeof(EffectParameters).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    names.Add(new Dictionary<string, object>
                    {
                        ["name"] = field.Name,
                        ["type"] = field.FieldType.Name
                    });
                }
                return new Dictionary<string, object>
                {
                    ["parametersJson"] = JsonUtility.ToJson(preset.CreateParameters()),
                    ["fields"] = names
                };
            }
            if (Working is Material material)
            {
                var values = new List<object>();
                for (int i = 0; i < material.shader.GetPropertyCount(); i++)
                {
                    string name = material.shader.GetPropertyName(i);
                    var type = material.shader.GetPropertyType(i);
                    object value;
                    if (type == UnityEngine.Rendering.ShaderPropertyType.Color)
                    {
                        var c = material.GetColor(name);
                        value = new[] { c.r, c.g, c.b, c.a };
                    }
                    else if (type == UnityEngine.Rendering.ShaderPropertyType.Vector)
                    {
                        var v = material.GetVector(name);
                        value = new[] { v.x, v.y, v.z, v.w };
                    }
                    else if (type == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    {
                        value = AssetDatabase.GetAssetPath(material.GetTexture(name));
                    }
                    else if (type == UnityEngine.Rendering.ShaderPropertyType.Int)
                    {
                        value = material.GetInteger(name);
                    }
                    else
                    {
                        value = material.GetFloat(name);
                    }
                    values.Add(new Dictionary<string, object>
                    {
                        ["name"] = name,
                        ["type"] = type.ToString(),
                        ["value"] = value
                    });
                }
                return values;
            }
            var systems = new List<object>();
            foreach (var system in ((GameObject)Working).GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                systems.Add(new Dictionary<string, object>
                {
                    ["name"] = system.name,
                    ["duration"] = main.duration,
                    ["loop"] = main.loop,
                    ["startLifetime"] = main.startLifetime.constant,
                    ["startSpeed"] = main.startSpeed.constant,
                    ["startSize"] = main.startSize.constant,
                    ["maxParticles"] = main.maxParticles,
                    ["emissionRate"] = system.emission.rateOverTime.constant
                });
            }
            return systems;
        }
        static float Number(string value)
        {
            float number = float.Parse(value, CultureInfo.InvariantCulture);
            if (!float.IsFinite(number))
            {
                throw new InvalidOperationException("Value must be finite.");
            }
            return number;
        }
        static float[] Numbers(string value, int count)
        {
            var parts = value.Split(',');
            if (parts.Length != count)
            {
                throw new InvalidOperationException("Expected " + count + " comma-separated numbers.");
            }
            var result = new float[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = Number(parts[i]);
            }
            return result;
        }
        static Color ParseColor(string value)
        {
            if (ColorUtility.TryParseHtmlString(value, out var color))
            {
                return color;
            }
            var c = Numbers(value, 4);
            return new Color(c[0], c[1], c[2], c[3]);
        }
        static UnityEngine.Object Asset(string path, Type type)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Asset path must be in the project.");
            }
            return AssetDatabase.LoadAssetAtPath(path, type) ?? throw new InvalidOperationException("Asset not found: " + path);
        }
        public void SetParticle(string property, string value)
        {
            if (!(Working is GameObject root) || !root.TryGetComponent<ParticleSystem>(out var system))
            {
                throw new InvalidOperationException("Select a particle Prefab root.");
            }
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(system, "Edit URUI particle preview");
            var main = system.main;
            var emission = system.emission;
            var shape = system.shape;
            float Number()
            {
                float number = float.Parse(value, CultureInfo.InvariantCulture);
                if (!float.IsFinite(number) || number < 0 || number > 1000000)
                {
                    throw new InvalidOperationException("Particle parameter outside supported range.");
                }
                return number;
            }
            switch (property)
            {
                case "duration":
                    main.duration = Number();
                    break;
                case "loop":
                    main.loop = bool.Parse(value);
                    break;
                case "startLifetime":
                    main.startLifetime = Number();
                    break;
                case "startSpeed":
                    main.startSpeed = Number();
                    break;
                case "startSize":
                    main.startSize = Number();
                    break;
                case "maxParticles":
                    main.maxParticles = (int)Number();
                    break;
                case "emissionRate":
                    emission.rateOverTime = Number();
                    break;
                case "shapeEnabled":
                    shape.enabled = bool.Parse(value);
                    break;
                case "shapeRadius":
                    shape.radius = Number();
                    break;
                case "startColor":
                    if (!ColorUtility.TryParseHtmlString(value, out var color))
                    {
                        throw new InvalidOperationException("Use an HTML color.");
                    }
                    main.startColor = color;
                    break;
                default:
                    throw new InvalidOperationException("unsupported_property: particles." + property);
            }
            MarkChanged();
        }
        public void Apply()
        {
            RequireWritable(path);
            if (originalHash != null && Hash(path) != originalHash)
            {
                throw new InvalidOperationException("source_conflict: asset changed since the working copy was opened");
            }
            Save(path, false);
            originalHash = Hash(path);
            savedVersion = ContentVersion;
        }
        public void SaveAs(string destination)
        {
            RequireWritable(destination);
            if (AssetDatabase.LoadMainAssetAtPath(destination))
            {
                throw new InvalidOperationException("Destination already exists.");
            }
            Save(destination, true);
            savedVersion = ContentVersion;
        }
        void Save(string destination, bool copy)
        {
            if (Working is GameObject root)
            {
                // Material parameters have their own explicit asset session; a Prefab cannot serialize transient materials.
                var restore = new Dictionary<ParticleSystemRenderer, Material[]>();
                try
                {
                    foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    {
                        var values = renderer.sharedMaterials;
                        restore[renderer] = (Material[])values.Clone();
                        for (int i = 0; i < values.Length; i++)
                        {
                            if (values[i] && materials.TryGetValue(values[i], out var original))
                            {
                                values[i] = original;
                            }
                        }
                        renderer.sharedMaterials = values;
                    }
                    var flags = root.hideFlags;
                    root.hideFlags = HideFlags.None;
                    try
                    {
                        if (!PrefabUtility.SaveAsPrefabAsset(root, destination))
                        {
                            throw new InvalidOperationException("Prefab save failed.");
                        }
                    }
                    finally
                    {
                        root.hideFlags = flags;
                    }
                }
                finally
                {
                    foreach (var pair in restore)
                    {
                        if (pair.Key)
                        {
                            pair.Key.sharedMaterials = pair.Value;
                        }
                    }
                }
            }
            else if (copy)
            {
                var asset = UnityEngine.Object.Instantiate(Working);
                asset.hideFlags = HideFlags.None;
                try
                {
                    AssetDatabase.CreateAsset(asset, destination);
                    AssetDatabase.SaveAssetIfDirty(asset);
                }
                catch
                {
                    if (asset && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
                    {
                        UnityEngine.Object.DestroyImmediate(asset);
                    }
                    throw;
                }
            }
            else
            {
                Undo.RecordObject(source, "Apply URUI asset changes");
                var flags = source.hideFlags;
                string originalName = source.name;
                EditorUtility.CopySerialized(Working, source);
                source.hideFlags = flags;
                source.name = originalName;
                EditorUtility.SetDirty(source);
                AssetDatabase.SaveAssetIfDirty(source);
            }
            AssetDatabase.ImportAsset(destination);
        }
        static void RequireWritable(string destination)
        {
            string full = Path.GetFullPath(destination);
            string root = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("read_only_asset: apply/save-as requires a project Assets path");
            }
            if (File.Exists(destination) && (!AssetDatabase.IsOpenForEdit(destination) || (File.GetAttributes(destination) & FileAttributes.ReadOnly) != 0))
            {
                throw new InvalidOperationException("read_only_asset");
            }
            for (string current = full; current != null && current != Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar)); current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("read_only_asset: symbolic link");
                }
            }
        }
        static string Hash(string file)
        {
            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(file)));
            }
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            var working = Working;
            Working = null;
            if (prefabContents && working)
            {
                PrefabUtility.UnloadPrefabContents((GameObject)working);
            }
            else if (working)
            {
                GpuRetirement.Retire(() => GpuRetirement.DestroyOwnedObject(working));
            }
            foreach (var material in materials.Keys)
            {
                var owned = material;
                GpuRetirement.Retire(() => GpuRetirement.DestroyOwnedObject(owned));
            }
            materials.Clear();
            originalToCopy.Clear();
        }
    }
}
