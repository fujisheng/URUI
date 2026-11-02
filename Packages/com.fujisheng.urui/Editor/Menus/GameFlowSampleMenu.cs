using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace URUI.Editor.Menus
{
    /// <summary>The package's recommended sample; imports use Unity's standard destination.</summary>
    [InitializeOnLoad]
    public static class GameFlowSampleMenu
    {
        const string PendingScene = "URUI.Editor.PendingGameFlowScene";
        const string MenuPath = "Tools/URUI/Open Game Flow Sample";

        static GameFlowSampleMenu()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingScene, "")))
                EditorApplication.update += OpenWhenReady;
        }

        /// <summary>Resolve the installed package and its declared primary sample.</summary>
        public static Sample FindDefaultSample()
        {
            var package = PackageInfo.FindForAssetPath("Packages/com.fujisheng.urui");
            if (package == null) throw new InvalidOperationException("URUI package is not registered.");
            foreach (var sample in Sample.FindByPackage(package.name, package.version))
                if (sample.displayName == "Game Flow") return sample;
            throw new InvalidOperationException("URUI's Game Flow sample is not declared in package.json.");
        }

        /// <summary>Use the path computed by Unity, without assuming a project or package version.</summary>
        public static string SceneAssetPath(Sample sample) => FileUtil.GetProjectRelativePath(
            Path.GetFullPath(Path.Combine(sample.importPath, "GameFlow.unity")).Replace('\\', '/'));

        [MenuItem(MenuPath, false, 0)]
        public static void OpenDefaultSample()
        {
            try
            {
                var sample = FindDefaultSample();
                string scene = SceneAssetPath(sample);
                if (!sample.isImported)
                {
                    if (Directory.Exists(sample.importPath) && Directory.GetFileSystemEntries(sample.importPath).Length != 0)
                        throw new InvalidOperationException("The Game Flow import folder already contains files. Use Package Manager to review the existing import.");
                    // Keep the intent across compilation/domain reload caused by import.
                    SessionState.SetString(PendingScene, scene);
                    if (!sample.Import())
                    {
                        SessionState.EraseString(PendingScene);
                        throw new InvalidOperationException("Unity could not import the Game Flow sample.");
                    }
                }
                if (!File.Exists(scene)) throw new FileNotFoundException("Imported Game Flow scene is missing.", scene);
                SessionState.SetString(PendingScene, scene);
                EditorApplication.update -= OpenWhenReady;
                EditorApplication.update += OpenWhenReady;
            }
            catch (Exception error) { Debug.LogError("URUI Game Flow: " + error.Message); }
        }

        [MenuItem(MenuPath, true)]
        static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;

        static void OpenWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= OpenWhenReady;
            string scene = SessionState.GetString(PendingScene, "");
            SessionState.EraseString(PendingScene);
            if (string.IsNullOrEmpty(scene) || !File.Exists(scene)) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        }
    }
}
