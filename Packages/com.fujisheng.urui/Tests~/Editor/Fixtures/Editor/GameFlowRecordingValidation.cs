#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using URUI.Editor.Menus;
using URUI.Samples.GameFlow;

namespace URUI.Tests.Editor
{
    public static class GameFlowRecordingValidation
    {
        public static void Run()
        {
            const string key = GameFlowSample.SettingsKey;
            SessionState.SetBool("URUI.Recording.HadPreferences", PlayerPrefs.HasKey(key));
            SessionState.SetString("URUI.Recording.Preferences", PlayerPrefs.GetString(key, ""));
            PlayerPrefs.DeleteKey(key);
            GameFlowSmoke.SetSize(1280, 720);
            var sample = GameFlowSampleMenu.FindDefaultSample();
            EditorSceneManager.OpenScene(GameFlowSampleMenu.SceneAssetPath(sample), OpenSceneMode.Single);
            var app = Object.FindFirstObjectByType<GameFlowSample>();
            var serialized = new SerializedObject(app);
            serialized.FindProperty("simulateStartupFailureOnce").boolValue = false;
            serialized.FindProperty("serviceLatencyMilliseconds").intValue = 500;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            new GameObject("Game Flow continuous recording").AddComponent<GameFlowRecording>();
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
    }
}
#endif
