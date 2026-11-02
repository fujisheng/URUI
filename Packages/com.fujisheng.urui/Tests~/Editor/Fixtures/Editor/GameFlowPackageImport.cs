#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using URUI.Editor.Menus;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace URUI.Tests.Editor
{
    /// <summary>Independent bootstrap: compiles before any sample has been imported.</summary>
    public static class GameFlowPackageImport
    {
        [Serializable] sealed class Report
        {
            public bool passed, nativeUpmImport, importedNow;
            public string packageName, packageVersion, displayName, resolvedPath, importPath, sceneAssetPath;
        }

        public static void Run()
        {
            try
            {
                var package = PackageInfo.FindForAssetPath("Packages/com.fujisheng.urui");
                var sample = GameFlowSampleMenu.FindDefaultSample();
                bool importedNow = !sample.isImported;
                if (importedNow && !sample.Import())
                    throw new Exception("Unity Sample.Import returned false.");
                string scene = GameFlowSampleMenu.SceneAssetPath(sample);
                if (!File.Exists(scene)) throw new Exception("Imported scene is absent: " + scene);
                var report = new Report { passed = true, nativeUpmImport = true, importedNow = importedNow,
                    packageName = package.name, packageVersion = package.version, displayName = sample.displayName,
                    resolvedPath = sample.resolvedPath, importPath = sample.importPath, sceneAssetPath = scene };
                Directory.CreateDirectory("Artifacts/GameFlow");
                File.WriteAllText("Artifacts/GameFlow/package-import.json", JsonUtility.ToJson(report, true));
                Debug.Log("GAME_FLOW_PACKAGE_IMPORT_PASS " + scene);
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
#endif
