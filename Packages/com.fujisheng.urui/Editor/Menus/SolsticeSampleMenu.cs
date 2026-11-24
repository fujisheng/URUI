using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace URUI.Editor.Menus
{
    /// <summary>
    /// The package's recommended primary sample; imports use Unity's standard Package Manager destination.
    /// </summary>
    [InitializeOnLoad]
    public static class SolsticeSampleMenu
    {
        const string PendingScene = "URUI.Editor.PendingSolsticeScene";
        const string MenuPath = "Tools/URUI/Open Solstice Sample";

        static SolsticeSampleMenu()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingScene, "")))
            {
                EditorApplication.update += OpenWhenReady;
            }
        }

        /// <summary>
        /// Resolve the installed URUI package and its declared primary Solstice sample.
        /// </summary>
        public static Sample FindDefaultSample()
        {
            var package = PackageInfo.FindForAssetPath("Packages/com.fujisheng.urui");
            if (package == null)
            {
                throw new InvalidOperationException("URUI package is not registered.");
            }
            foreach (var sample in Sample.FindByPackage(package.name, package.version))
            {
                if (sample.displayName == "Solstice")
                {
                    return sample;
                }
            }
            throw new InvalidOperationException("URUI's Solstice sample is not declared in package.json.");
        }

        /// <summary>
        /// Compute the Unity-relative path to the imported Solstice scene.
        /// </summary>
        public static string SceneAssetPath(Sample sample) => FileUtil.GetProjectRelativePath(
            Path.GetFullPath(Path.Combine(sample.importPath, "Solstice.unity")).Replace('\\', '/'));

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
                    {
                        throw new InvalidOperationException("The Solstice import folder already contains files. Use Package Manager to review the existing import.");
                    }
                    SessionState.SetString(PendingScene, scene);
                    if (!sample.Import())
                    {
                        SessionState.EraseString(PendingScene);
                        throw new InvalidOperationException("Unity could not import the Solstice sample.");
                    }
                }
                if (!File.Exists(scene))
                {
                    throw new FileNotFoundException("Imported Solstice scene is missing.", scene);
                }
                SessionState.SetString(PendingScene, scene);
                EditorApplication.update -= OpenWhenReady;
                EditorApplication.update += OpenWhenReady;
            }
            catch (Exception error)
            {
                Debug.LogError("URUI Solstice: " + error.Message);
            }
        }

        [MenuItem(MenuPath, true)]
        static bool CanOpen() => !EditorApplication.isPlayingOrWillChangePlaymode;

        static void OpenWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }
            EditorApplication.update -= OpenWhenReady;
            string scene = SessionState.GetString(PendingScene, "");
            SessionState.EraseString(PendingScene);
            if (string.IsNullOrEmpty(scene) || !File.Exists(scene))
            {
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        }
    }
}
