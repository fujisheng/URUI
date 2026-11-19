using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace URUI.Effect.Editor.Import
{
    public sealed class EffectImportContext
    {
        public Func<UnityEngine.Object, string> ResolveRoot;
        public Func<Texture, string> TextureUrl;
        public Func<Gradient, string> GradientUrl;
    }
    public sealed class ImportedEffectPreset
    {
        readonly EffectParameters parameters;
        public string RootElementId
        {
            get;
        }
        internal ImportedEffectPreset(EffectParameters parameters, string root)
        {
            this.parameters = parameters.Clone();
            RootElementId = root;
        }
        public EffectParameters CreateParameters() => parameters.Clone();
    }
    /// <summary>
    /// 严格读取 UIEffect 5.8.4 的71个预设字段或74个组件字段。编辑器工具不引用 Coffee 程序集。
    /// </summary>
    public static class UIEffectPresetImporter
    {
        static readonly HashSet<string> unityFields = new(StringComparer.Ordinal) { "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject", "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier" };
        static readonly string[] required ={
            "m_ToneFilter",
            "m_ToneIntensity",
            "m_ColorFilter",
            "m_ColorIntensity",
            "m_Color",
            "m_ColorGlow",
            "m_SamplingFilter",
            "m_SamplingIntensity",
            "m_SamplingWidth",
            "m_TransitionFilter",
            "m_TransitionRate",
            "m_TransitionReverse",
            "m_TransitionTex",
            "m_TransitionTexScale",
            "m_TransitionTexOffset",
            "m_TransitionTexSpeed",
            "m_TransitionRotation",
            "m_TransitionKeepAspectRatio",
            "m_TransitionWidth",
            "m_TransitionSoftness",
            "m_TransitionRange",
            "m_TransitionColorFilter",
            "m_TransitionColor",
            "m_TransitionColorGlow",
            "m_TransitionPatternReverse",
            "m_TransitionAutoPlaySpeed",
            "m_TargetMode",
            "m_TargetColor",
            "m_TargetRange",
            "m_TargetSoftness",
            "m_BlendType",
            "m_SrcBlendMode",
            "m_DstBlendMode",
            "m_ShadowMode",
            "m_ShadowDistance",
            "m_ShadowIteration",
            "m_ShadowFade",
            "m_ShadowMirrorScale",
            "m_ShadowBlurIntensity",
            "m_ShadowColorFilter",
            "m_ShadowColor",
            "m_ShadowColorGlow",
            "m_GradationMode",
            "m_GradationIntensity",
            "m_GradationColorFilter",
            "m_GradationColor1",
            "m_GradationColor2",
            "m_GradationColor3",
            "m_GradationColor4",
            "m_GradationGradient",
            "m_GradationOffset",
            "m_GradationScale",
            "m_GradationRotation",
            "m_EdgeMode",
            "m_EdgeWidth",
            "m_EdgeColorFilter",
            "m_EdgeColor",
            "m_EdgeColorGlow",
            "m_EdgeShinyRate",
            "m_EdgeShinyWidth",
            "m_EdgeShinyAutoPlaySpeed",
            "m_PatternArea",
            "m_DetailFilter",
            "m_DetailIntensity",
            "m_DetailColor",
            "m_DetailThreshold",
            "m_DetailTex",
            "m_DetailTexScale",
            "m_DetailTexOffset",
            "m_DetailTexSpeed",
            "m_Flip",
        };
        static readonly string[] componentFields = { "m_SamplingScale", "m_AllowToModifyMeshShape", "m_CustomRoot" };
        public static ImportedEffectPreset Read(UnityEngine.Object value, EffectImportContext references = null)
        {
            if (!value)
            {
                throw new ArgumentNullException(nameof(value));
            }
            if (value.GetType().FullName.StartsWith("Coffee.UIEffects.", StringComparison.Ordinal))
            {
                var script = value is MonoBehaviour behaviour ? MonoScript.FromMonoBehaviour(behaviour) : value is ScriptableObject asset ? MonoScript.FromScriptableObject(asset) : null;
                var info = script ? UnityEditor.PackageManager.PackageInfo.FindForAssetPath(AssetDatabase.GetAssetPath(script)) : null;
                if (info != null && info.version != "5.8.4")
                {
                    throw new NotSupportedException("只支持锁定的 UIEffect 5.8.4，当前为 " + info.version);
                }
            }
            var source = new SerializedObject(value);
            source.Update();
            var known = new HashSet<string>(required, StringComparer.Ordinal);
            known.UnionWith(componentFields);
            var iterator = source.GetIterator();
            var children = true;
            while (iterator.Next(children))
            {
                children = false;
                if (!unityFields.Contains(iterator.name) && !known.Contains(iterator.name))
                {
                    throw new InvalidOperationException("未知的源字段：" + iterator.propertyPath + "；仅支持锁定的 UIEffect 5.8.4 schema。");
                }
            }
            foreach (var name in required)
            {
                if (source.FindProperty(name) == null)
                {
                    throw new InvalidOperationException("源预设缺少必需字段：" + name);
                }
            }
            var anyComponent = false;
            foreach (var name in componentFields)
            {
                anyComponent |= source.FindProperty(name) != null;
            }
            if (anyComponent)
            {
                foreach (var name in componentFields)
                {
                    if (source.FindProperty(name) == null)
                    {
                        throw new InvalidOperationException("源组件 schema 不完整：" + name);
                    }
                }
            }
            var parameters = new EffectParameters();
            parameters.ToneFilter = EnumValue<ToneFilter>(source, "m_ToneFilter");
            parameters.ToneIntensity = Number(source, "m_ToneIntensity");
            parameters.ColorFilter = EnumValue<ColorFilter>(source, "m_ColorFilter");
            parameters.ColorIntensity = Number(source, "m_ColorIntensity");
            parameters.Color = ColorValue(source, "m_Color");
            parameters.ColorGlow = Property(source, "m_ColorGlow", SerializedPropertyType.Boolean).boolValue;
            parameters.SamplingFilter = EnumValue<SamplingFilter>(source, "m_SamplingFilter");
            parameters.SamplingIntensity = Number(source, "m_SamplingIntensity");
            parameters.SamplingWidth = Number(source, "m_SamplingWidth");
            parameters.TransitionFilter = EnumValue<TransitionFilter>(source, "m_TransitionFilter");
            parameters.TransitionProgress = Number(source, "m_TransitionRate");
            parameters.TransitionReverse = Property(source, "m_TransitionReverse", SerializedPropertyType.Boolean).boolValue;
            parameters.TransitionTexture = TextureValue(source, "m_TransitionTex");
            parameters.TransitionTextureScale = Vector(source, "m_TransitionTexScale");
            parameters.TransitionTextureOffset = Vector(source, "m_TransitionTexOffset");
            parameters.TransitionTextureSpeed = Vector(source, "m_TransitionTexSpeed");
            parameters.TransitionRotation = Number(source, "m_TransitionRotation");
            parameters.TransitionKeepAspectRatio = Property(source, "m_TransitionKeepAspectRatio", SerializedPropertyType.Boolean).boolValue;
            parameters.TransitionWidth = Number(source, "m_TransitionWidth");
            parameters.TransitionSoftness = Number(source, "m_TransitionSoftness");
            parameters.TransitionRange = Range(source, "m_TransitionRange");
            parameters.TransitionColorFilter = EnumValue<ColorFilter>(source, "m_TransitionColorFilter");
            parameters.TransitionColor = ColorValue(source, "m_TransitionColor");
            parameters.TransitionColorGlow = Property(source, "m_TransitionColorGlow", SerializedPropertyType.Boolean).boolValue;
            parameters.TransitionPatternReverse = Property(source, "m_TransitionPatternReverse", SerializedPropertyType.Boolean).boolValue;
            parameters.TransitionAutoPlaySpeed = Number(source, "m_TransitionAutoPlaySpeed");
            parameters.TargetMode = EnumValue<TargetMode>(source, "m_TargetMode");
            parameters.TargetColor = ColorValue(source, "m_TargetColor");
            parameters.TargetRange = Number(source, "m_TargetRange");
            parameters.TargetSoftness = Number(source, "m_TargetSoftness");
            parameters.BlendType = EnumValue<BlendType>(source, "m_BlendType");
            parameters.SrcBlendMode = EnumValue<BlendMode>(source, "m_SrcBlendMode");
            parameters.DstBlendMode = EnumValue<BlendMode>(source, "m_DstBlendMode");
            parameters.ShadowMode = EnumValue<ShadowMode>(source, "m_ShadowMode");
            parameters.ShadowDistance = Vector(source, "m_ShadowDistance");
            parameters.ShadowIteration = Integer(source, "m_ShadowIteration");
            parameters.ShadowFade = Number(source, "m_ShadowFade");
            parameters.ShadowMirrorScale = Number(source, "m_ShadowMirrorScale");
            parameters.ShadowBlurIntensity = Number(source, "m_ShadowBlurIntensity");
            parameters.ShadowColorFilter = EnumValue<ColorFilter>(source, "m_ShadowColorFilter");
            parameters.ShadowColor = ColorValue(source, "m_ShadowColor");
            parameters.ShadowColorGlow = Property(source, "m_ShadowColorGlow", SerializedPropertyType.Boolean).boolValue;
            parameters.GradationMode = EnumValue<GradationMode>(source, "m_GradationMode");
            parameters.GradationIntensity = Number(source, "m_GradationIntensity");
            parameters.GradationColorFilter = EnumValue<GradationColorFilter>(source, "m_GradationColorFilter");
            parameters.GradationColor1 = ColorValue(source, "m_GradationColor1");
            parameters.GradationColor2 = ColorValue(source, "m_GradationColor2");
            parameters.GradationColor3 = ColorValue(source, "m_GradationColor3");
            parameters.GradationColor4 = ColorValue(source, "m_GradationColor4");
            parameters.GradationGradient = Property(source, "m_GradationGradient", SerializedPropertyType.Gradient).gradientValue;
            parameters.GradationOffset = Number(source, "m_GradationOffset");
            parameters.GradationScale = Number(source, "m_GradationScale");
            parameters.GradationRotation = Number(source, "m_GradationRotation");
            parameters.EdgeMode = EnumValue<EdgeMode>(source, "m_EdgeMode");
            parameters.EdgeWidth = Number(source, "m_EdgeWidth");
            parameters.EdgeColorFilter = EnumValue<ColorFilter>(source, "m_EdgeColorFilter");
            parameters.EdgeColor = ColorValue(source, "m_EdgeColor");
            parameters.EdgeColorGlow = Property(source, "m_EdgeColorGlow", SerializedPropertyType.Boolean).boolValue;
            parameters.EdgeShinyProgress = Number(source, "m_EdgeShinyRate");
            parameters.EdgeShinyWidth = Number(source, "m_EdgeShinyWidth");
            parameters.EdgeShinyAutoPlaySpeed = Number(source, "m_EdgeShinyAutoPlaySpeed");
            parameters.PatternArea = EnumValue<PatternArea>(source, "m_PatternArea");
            parameters.DetailFilter = EnumValue<DetailFilter>(source, "m_DetailFilter");
            parameters.DetailIntensity = Number(source, "m_DetailIntensity");
            parameters.DetailColor = ColorValue(source, "m_DetailColor");
            parameters.DetailThreshold = Range(source, "m_DetailThreshold");
            parameters.DetailTexture = TextureValue(source, "m_DetailTex");
            parameters.DetailTextureScale = Vector(source, "m_DetailTexScale");
            parameters.DetailTextureOffset = Vector(source, "m_DetailTexOffset");
            parameters.DetailTextureSpeed = Vector(source, "m_DetailTexSpeed");
            parameters.Flip = (Flip)Flags(source, "m_Flip");
            string root = null;
            if (anyComponent)
            {
                parameters.SamplingScale = Number(source, "m_SamplingScale");
                parameters.AllowToModifyMeshShape = Property(source, "m_AllowToModifyMeshShape", SerializedPropertyType.Boolean).boolValue;
                var reference = Reference(source, "m_CustomRoot");
                if (reference)
                {
                    root = references?.ResolveRoot?.Invoke(reference);
                    if (string.IsNullOrWhiteSpace(root) || root.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '\0' }) >= 0)
                    {
                        throw new InvalidOperationException("自定义根没有有效的 HTML id 映射：" + reference.name);
                    }
                }
            }
            return new ImportedEffectPreset(parameters, root);
        }
        static SerializedProperty Property(SerializedObject source, string name, SerializedPropertyType type)
        {
            var property = source.FindProperty(name) ?? throw new InvalidOperationException("源字段不存在：" + name);
            if (property.propertyType != type)
            {
                throw new InvalidOperationException("源字段类型不匹配：" + name + " / " + property.propertyType + "，要求 " + type);
            }
            return property;
        }
        static float Number(SerializedObject source, string name)
        {
            var value = Property(source, name, SerializedPropertyType.Float).floatValue;
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException("源数值必须有限：" + name);
            }
            return value;
        }
        static int Integer(SerializedObject source, string name) => Property(source, name, SerializedPropertyType.Integer).intValue;
        static T EnumValue<T>(SerializedObject source, string name) where T : Enum
        {
            var property = Property(source, name, SerializedPropertyType.Enum);
            var value = property.intValue;
            if (!Enum.IsDefined(typeof(T), value))
            {
                throw new InvalidOperationException("未知的源枚举：" + name + "=" + value);
            }
            if (property.enumValueIndex < 0 || property.enumNames[property.enumValueIndex] != Enum.GetName(typeof(T), value))
            {
                throw new InvalidOperationException("源枚举名称/值已变化：" + name);
            }
            return (T)Enum.ToObject(typeof(T), value);
        }
        static int Flags(SerializedObject source, string name)
        {
            var value = Property(source, name, SerializedPropertyType.Enum).intValue;
            if ((value & ~15) != 0)
            {
                throw new InvalidOperationException("未知 Flip 标志：" + value);
            }
            return value;
        }
        static Color ColorValue(SerializedObject source, string name)
        {
            var value = Property(source, name, SerializedPropertyType.Color).colorValue;
            for (var i = 0; i < 4; i++)
            {
                if (!float.IsFinite(value[i]))
                {
                    throw new InvalidOperationException("颜色分量必须有限：" + name);
                }
            }
            return value;
        }
        static Vector2 Vector(SerializedObject source, string name)
        {
            var value = Property(source, name, SerializedPropertyType.Vector2).vector2Value;
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y))
            {
                throw new InvalidOperationException("向量必须有限：" + name);
            }
            return value;
        }
        static UnityEngine.Object Reference(SerializedObject source, string name)
        {
            var property = Property(source, name, SerializedPropertyType.ObjectReference);
            var value = property.objectReferenceValue;
            if (!value && property.objectReferenceInstanceIDValue != 0)
            {
                throw new InvalidOperationException("源引用已丢失：" + name);
            }
            return value;
        }
        static Texture TextureValue(SerializedObject source, string name)
        {
            var value = Reference(source, name);
            if (value && !(value is Texture))
            {
                throw new InvalidOperationException("源引用不是纹理：" + name);
            }
            return value as Texture;
        }
        static EffectRange Range(SerializedObject source, string name)
        {
            var field = Property(source, name, SerializedPropertyType.Generic);
            var min = field.FindPropertyRelative("m_Min");
            var max = field.FindPropertyRelative("m_Max");
            if (min == null || max == null || min.propertyType != SerializedPropertyType.Float || max.propertyType != SerializedPropertyType.Float || !float.IsFinite(min.floatValue) || !float.IsFinite(max.floatValue))
            {
                throw new InvalidOperationException("源区间字段无效：" + name);
            }
            return new EffectRange(min.floatValue, max.floatValue);
        }
        public static EffectPreset CreatePreset(ImportedEffectPreset source, EffectPreset appendTo = null)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            var result = ScriptableObject.CreateInstance<EffectPreset>();
            if (appendTo)
            {
                result.SetParameters(appendTo.CreateParameters());
                result.Append(source.CreateParameters());
            }
            else
            {
                result.SetParameters(source.CreateParameters());
            }
            return result;
        }
        public static EffectPreset Save(ImportedEffectPreset source, string path, EffectPreset appendTo = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || path.Contains("..") || path.Contains('\\'))
            {
                throw new ArgumentException("输出必须是 Assets 内的新 .asset 路径。", nameof(path));
            }
            var folder = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(folder))
            {
                throw new ArgumentException("输出文件夹尚不存在：" + folder, nameof(path));
            }
            var result = CreatePreset(source, appendTo);
            var created = new List<string>();
            var saved = false;
            try
            {
                var parameters = result.CreateParameters();
                parameters.TransitionTexture = PortableTexture(parameters.TransitionTexture, folder, created);
                parameters.DetailTexture = PortableTexture(parameters.DetailTexture, folder, created);
                result.SetParameters(parameters);
                var unique = AssetDatabase.GenerateUniqueAssetPath(path);
                for (var suffix = 1; !string.IsNullOrEmpty(source.RootElementId) && File.Exists(Path.ChangeExtension(unique, "bindings.json")); suffix++)
                {
                    unique = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileNameWithoutExtension(path) + " " + suffix + ".asset");
                }
                result.name = Path.GetFileNameWithoutExtension(unique);
                AssetDatabase.CreateAsset(result, unique);
                created.Add(unique);
                saved = true;
                if (!string.IsNullOrEmpty(source.RootElementId))
                {
                    var binding = Path.ChangeExtension(unique, "bindings.json");
                    created.Add(binding);
                    File.WriteAllText(binding, JsonUtility.ToJson(new RootBinding
                    {
                        attribute = "data-urui-effect-root",
                        value = source.RootElementId
                    }, true));
                    AssetDatabase.ImportAsset(binding);
                }
                AssetDatabase.SaveAssetIfDirty(result);
                return result;
            }
            catch
            {
                for (var i = created.Count - 1; i >= 0; i--)
                {
                    AssetDatabase.DeleteAsset(created[i]);
                }
                if (!saved)
                {
                    UnityEngine.Object.DestroyImmediate(result);
                }
                throw;
            }
        }
        [Serializable]
        sealed class RootBinding
        {
            public string attribute;
            public string value;
        }
        static Texture PortableTexture(Texture texture, string folder, List<string> created)
        {
            if (!texture)
            {
                return null;
            }
            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || (!path.StartsWith("Assets/", StringComparison.Ordinal) && !path.StartsWith("Packages/", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("纹理尚未保存为可移植资产：" + texture.name);
            }
            if (!path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                return texture;
            }
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            if (package == null)
            {
                throw new InvalidOperationException("无法确定包纹理的来源：" + path);
            }
            var notices = new System.Text.StringBuilder("Source: " + path + "\nPackage: " + package.name + "@" + package.version + "\n");
            var foundLicense = false;
            foreach (var name in new[] { "LICENSE", "LICENSE.md", "LICENSE.txt", "License.md", "Third Party Notices.md", "ThirdPartyNotices.md" })
            {
                var license = Path.Combine(package.resolvedPath, name);
                if (!File.Exists(license))
                {
                    continue;
                }
                foundLicense = true;
                notices.Append("\n--- ").Append(name).Append(" ---\n").Append(File.ReadAllText(license));
            }
            // 没有可追溯的许可时不能静默复制第三方纹理；调用者可先在项目内明确准备资源。
            if (!foundLicense)
            {
                throw new InvalidOperationException("包纹理缺少可随资产保存的许可证：" + path);
            }
            var destination = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(path));
            if (!AssetDatabase.CopyAsset(path, destination))
            {
                throw new InvalidOperationException("无法复制包纹理：" + path);
            }
            created.Add(destination);
            var provenance = destination + ".source.txt";
            created.Add(provenance);
            File.WriteAllText(provenance, notices.ToString());
            AssetDatabase.ImportAsset(provenance);
            var copied = AssetDatabase.LoadAssetAtPath<Texture>(destination);
            if (!copied)
            {
                throw new InvalidOperationException("复制的纹理不可加载：" + destination);
            }
            return copied;
        }
    }
}
