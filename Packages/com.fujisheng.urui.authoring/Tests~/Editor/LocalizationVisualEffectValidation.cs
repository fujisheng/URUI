using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using URUI.Editor.Localization;
using URUI.Effect;
using URUI.Effect.Editor;

namespace URUI.Authoring.Tests
{
    public sealed class LocalizationVisualEffectValidation : EditorWindow
    {
        static readonly List<string> errors = new List<string>();
        static LocalizationVisualEffectValidation window;
        static EffectPreset preset;
        static UnityEditor.Editor inspector;
        static object originalLanguage, originalPoll;
        static string originalJson;
        static int painted;
        static double readyAt;
        static readonly FieldInfo language = typeof(EditorText).GetField("current", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo poll = typeof(EditorText).GetField("nextPoll", BindingFlags.Static | BindingFlags.NonPublic);
        Vector2 scroll;
        public static void Run()
        {
            Application.logMessageReceived += Log;
            originalLanguage = language.GetValue(null);
            originalPoll = poll.GetValue(null);
            language.SetValue(null, SystemLanguage.ChineseSimplified);
            poll.SetValue(null, EditorApplication.timeSinceStartup + 120);
            Notify();
            preset = CreateInstance<EffectPreset>();
            originalJson = JsonUtility.ToJson(preset);
            inspector = UnityEditor.Editor.CreateEditor(preset, typeof(EffectPresetEditor));
            inspector.serializedObject.FindProperty("parameters").isExpanded = true;
            window = CreateInstance<LocalizationVisualEffectValidation>();
            window.titleContent = new GUIContent("URUI Effect Parameters");
            window.ShowUtility();
            window.position = new Rect(100, 50, 500, 850);
            Directory.CreateDirectory("Evidence");
            Directory.CreateDirectory("Library/URUI/LocalizationEffect");
            EditorApplication.update += Tick;
        }
        static void Notify() => ((Action)typeof(EditorText).GetField("LanguageChanged", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))?.Invoke();
        static void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception)
                errors.Add(message + "\n" + stack);
        }
        void OnGUI()
        {
            using (var area = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = area.scrollPosition;
                inspector.OnInspectorGUI();
            }
            if (Event.current.type == EventType.Repaint)
                painted++;
        }
        static void Tick()
        {
            window.Repaint();
            if (painted < 40 && errors.Count == 0)
                return;
            if (readyAt == 0)
            {
                if (JsonUtility.ToJson(preset) != originalJson)
                    errors.Add("GUI changed serialized preset data");
                File.WriteAllText("Evidence/localization-effect-visual.json", "{\"paintedFrames\":" + painted + ",\"errors\":" + errors.Count + ",\"chinesePresentationSimulated\":true,\"unityPreferencesChanged\":false}");
                File.WriteAllText("Library/URUI/LocalizationEffect/ready", "ready");
                readyAt = EditorApplication.timeSinceStartup;
            }
            if (errors.Count != 0 || File.Exists("Library/URUI/LocalizationEffect/finish") || EditorApplication.timeSinceStartup - readyAt > 40)
            {
                EditorApplication.update -= Tick;
                language.SetValue(null, originalLanguage);
                poll.SetValue(null, originalPoll);
                Notify();
                window.Close();
                DestroyImmediate(inspector);
                DestroyImmediate(preset);
                Application.logMessageReceived -= Log;
                if (errors.Count != 0)
                    File.WriteAllText("Evidence/localization-effect-visual-failure.txt", string.Join("\n", errors));
                EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
            }
        }
    }
}
