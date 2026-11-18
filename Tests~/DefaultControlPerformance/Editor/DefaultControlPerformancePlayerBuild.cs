using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace URUI.Validation.DefaultControlPerformance.Editor
{
    public static class DefaultControlPerformancePlayerBuild
    {
        public static void Build()
        {
            try
            {
                string output = Environment.GetEnvironmentVariable("URUI_DEFAULT_PERFORMANCE_BUILD");
                if (string.IsNullOrEmpty(output)) throw new InvalidOperationException("Build output missing");
                PlayerSettings.colorSpace = ColorSpace.Gamma;
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Standalone, Il2CppCompilerConfiguration.Release);
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.ARM64;
                PlayerSettings.defaultScreenWidth = 1280;
                PlayerSettings.defaultScreenHeight = 720;
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                PlayerSettings.runInBackground = true;
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Default controls performance").AddComponent<DefaultControlPerformancePlayerRunner>();
                Directory.CreateDirectory("Assets/DefaultControlPerformance/Player");
                const string path = "Assets/DefaultControlPerformance/Player/Performance.unity";
                EditorSceneManager.SaveScene(scene, path);
                // Include NUnit assertions from the same workload; no Test Runner is placed in the scene.
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { path }, locationPathName = output,
                    target = BuildTarget.StandaloneOSX, options = BuildOptions.IncludeTestAssemblies
                });
                File.WriteAllText(output + ".build.json", "{\"result\":\"" + report.summary.result +
                    "\",\"errors\":" + report.summary.totalErrors + ",\"bytes\":" + report.summary.totalSize + "}");
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 2);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(2); }
        }
    }
}
