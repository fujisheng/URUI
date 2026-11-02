#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using URUI.Editor.Menus;
using URUI.Samples.GameFlow;

namespace URUI.Tests.Editor
{
    public static class GameFlowValidation
    {
        public static void Run()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--reset-game-flow-demo-preferences") >= 0)
                UnityEngine.PlayerPrefs.DeleteKey(GameFlowSample.SettingsKey);
            GameFlowSmoke.SetSize(1280, 720);
            var packageSample = GameFlowSampleMenu.FindDefaultSample();
            if (!packageSample.isImported) throw new Exception("Import Game Flow from Package Manager first.");
            EditorSceneManager.OpenScene(GameFlowSampleMenu.SceneAssetPath(packageSample), OpenSceneMode.Single);
            var sample = UnityEngine.Object.FindFirstObjectByType<GameFlowSample>();
            if (!sample) throw new Exception("Relocated sample scene did not resolve its script reference.");
            var config = new SerializedObject(sample); config.FindProperty("simulateStartupFailureOnce").boolValue = true; config.ApplyModifiedPropertiesWithoutUndo();
            new UnityEngine.GameObject("Game Flow acceptance").AddComponent<GameFlowSmoke>();
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
    }
}
#endif
