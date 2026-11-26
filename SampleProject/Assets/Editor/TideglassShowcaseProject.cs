using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using URUI.Editor.Menus;

namespace URUI.Showcase
{
    /// <summary>Opens the package's primary sample on a fresh checkout of this showcase project.</summary>
    static class TideglassShowcaseProject
    {
        [InitializeOnLoadMethod]
        static void ScheduleSetup() => EditorApplication.delayCall += Setup;

        static void Setup()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var sample = TideglassSampleMenu.FindDefaultSample();
            string path = TideglassSampleMenu.SceneAssetPath(sample);
            if (!File.Exists(path))
            {
                TideglassSampleMenu.OpenDefaultSample();
                return;
            }

            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0 || scenes.Length == 1 && !File.Exists(scenes[0].path))
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };

            var activeScene = EditorSceneManager.GetActiveScene();
            if (!File.Exists(activeScene.path))
                TideglassSampleMenu.OpenDefaultSample();
        }
    }
}
