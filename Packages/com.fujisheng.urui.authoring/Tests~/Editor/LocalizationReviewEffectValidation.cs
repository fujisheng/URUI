using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using URUI.Effect;
using URUI.Effect.Editor;

namespace URUI.Authoring.Tests
{
    public static class LocalizationReviewEffectValidation
    {
        public static void Run() => EditorApplication.delayCall += Exercise;
        static void Exercise()
        {
            EffectPreset preset = null;
            try
            {
                preset = ScriptableObject.CreateInstance<EffectPreset>();
                string before = JsonUtility.ToJson(preset);
                using (var serialized = new SerializedObject(preset))
                {
                    var label = typeof(EffectPresetEditor).GetMethod("ParameterLabel", BindingFlags.NonPublic | BindingFlags.Static);
                    if (label == null)
                    {
                        throw new Exception("Effect parameter labels do not use localization");
                    }
                    var parameters = serialized.FindProperty("parameters");
                    LocalizationReviewValidation.WithLanguage(SystemLanguage.ChineseSimplified, () =>
                    {
                        foreach (var expected in new[] { ("ToneIntensity", "色调强度"), ("TransitionTextureSpeed", "过渡纹理速度"), ("ShadowDistance", "阴影位移") })
                        {
                            var property = parameters.FindPropertyRelative(expected.Item1);
                            string value = (string)label.Invoke(null, new object[] { property });
                            if (value != expected.Item2)
                            {
                                throw new Exception("Unlocalized owned effect field: " + property.propertyPath + " => " + value);
                            }
                        }
                    });
                    LocalizationReviewValidation.WithLanguage(SystemLanguage.ChineseSimplified, () =>
                    {
                        using (var property = parameters.Copy())
                        {
                            using (var end = parameters.GetEndProperty())
                            {
                                while (property.NextVisible(true) && !SerializedProperty.EqualContents(property, end))
                                {
                                    if (property.depth != parameters.depth + 1)
                                    {
                                        continue;
                                    }
                                    string text = (string)label.Invoke(null, new object[] { property });
                                    if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"[\p{IsCJKUnifiedIdeographs}]"))
                                    {
                                        throw new Exception("Unlocalized effect parameter: " + property.displayName);
                                    }
                                }
                            }
                        }
                    });
                    if (JsonUtility.ToJson(preset) != before || serialized.hasModifiedProperties)
                    {
                        throw new Exception("Translating a field changed serialized effect data");
                    }
                }
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/localization-review-effect.json", "{\"passed\":true,\"serializedDataChanged\":false}");
                Debug.Log("URUI Localization review PASS effect");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/localization-review-effect-failure.txt", error.ToString());
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
            finally
            {
                if (preset)
                {
                    UnityEngine.Object.DestroyImmediate(preset);
                }
            }
        }
    }
}
