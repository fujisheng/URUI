using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace URUI.Validation.Editor
{
    public static class RenderPlanPlayerBuild
    {
        public static void Build()
        {
            try
            {
                var output = Environment.GetEnvironmentVariable("URUI_PLAN_PLAYER_OUTPUT") ?? throw new InvalidOperationException("Missing build output");
                var web = Environment.GetEnvironmentVariable("URUI_PLAN_PLAYER_WEB") == "1";
                PlayerSettings.colorSpace = ColorSpace.Linear;
                PlayerSettings.defaultScreenWidth = 256; PlayerSettings.defaultScreenHeight = 256; PlayerSettings.runInBackground = true;
                if (web) { PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; }
                else
                {
                    PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
                    UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
                }
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("Render parameter contract").AddComponent<RenderPlanPlayerValidation>();
                Directory.CreateDirectory("Assets/Player"); const string scene = "Assets/Player/RenderPlanContract.unity";
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scene);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene }, locationPathName = output, target = web ? BuildTarget.WebGL : BuildTarget.StandaloneOSX, options = BuildOptions.Development });
                File.WriteAllText(output + ".build.json", "{\"result\":\"" + report.summary.result + "\",\"errors\":" + report.summary.totalErrors + "}");
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 2);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(2); }
        }
    }
}
