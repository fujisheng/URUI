using L = URUI.Editor.Localization.EditorText;
using UnityEditor;
using UnityEngine;
namespace URUI.Effect.Editor
{
    public sealed class EffectPreviewWindow : EditorWindow
    {
        EffectPreset preset;
        Texture source;
        RenderTexture target;
        EffectPreview preview;
        float time;
        bool playing;
        double lastUpdate;
        string parameterJson;
        [InitializeOnLoadMethod]
        static void RegisterLocalizedMenus()
        {
            EditorApplication.delayCall += () =>
            {
                URUI.Editor.Localization.EditorMenus.Register("Window/URUI/Effect Preview", 100, ShowWindow);
            };
        }
        [MenuItem("Window/URUI/Effect Preview")]
        static void ShowWindow() => GetWindow<EffectPreviewWindow>("URUI Effect").Show();
        public static void Open(EffectPreset value)
        {
            var window = GetWindow<EffectPreviewWindow>("URUI Effect");
            window.preset = value;
            window.parameterJson = null;
            window.Show();
        }
        void OnEnable()
        {
            EditorApplication.update += UpdatePreview;
            lastUpdate = EditorApplication.timeSinceStartup;
        }
        void OnDisable()
        {
            EditorApplication.update -= UpdatePreview;
            preview?.Dispose();
            preview = null;
            if (target)
            {
                var previous = target;
                URUI.Resources.GpuRetirement.Retire(() =>
                {
                    previous.Release();
                    DestroyImmediate(previous);
                });
                target = null;
            }
        }
        void UpdatePreview()
        {
            var now = EditorApplication.timeSinceStartup;
            if (playing)
            {
                time += (float)(now - lastUpdate);
                Repaint();
            }
            lastUpdate = now;
        }
        void OnGUI()
        {
            L.Title(this, "URUI Effect");
            EditorGUI.BeginChangeCheck();
            preset = (EffectPreset)EditorGUILayout.ObjectField(L.Tr("Preset"), preset, typeof(EffectPreset), false);
            source = (Texture)EditorGUILayout.ObjectField(L.Tr("Texture"), source, typeof(Texture), false);
            time = EditorGUILayout.FloatField(L.Tr("Manual time (s)"), time);
            playing = EditorGUILayout.Toggle(L.Tr("Play clock"), playing);
            if (!float.IsFinite(time))
            {
                time = 0;
            }
            var parameters = preset ? preset.CreateParameters() : new EffectParameters();
            var json = JsonUtility.ToJson(parameters);
            if (preview == null)
            {
                preview = new EffectPreview(parameters);
            }
            if (json != parameterJson)
            {
                preview.SetParameters(parameters);
                parameterJson = json;
            }
            if (!target)
            {
                target = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGBHalf) { hideFlags = HideFlags.HideAndDontSave };
                target.Create();
            }
            if (Event.current.type == EventType.Repaint)
            {
                preview.Render(source ? source : Texture2D.whiteTexture, new Rect(128, 128, 256, 256), time, target, Color.clear);
                var rect = GUILayoutUtility.GetRect(128, 512, 128, 512, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                EditorGUI.DrawTextureTransparent(rect, target, ScaleMode.ScaleToFit);
            }
            else
            {
                GUILayoutUtility.GetRect(128, 512, 128, 512, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            }
            EditorGUILayout.LabelField(L.Tr("生产 Shader / 原始图像网格 / 显式时间"), EditorStyles.miniLabel);
        }
    }
}
