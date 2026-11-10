#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using URUI.Documents;

namespace URUI.Tests
{
    /// <summary>在 Editor 程序集中调用平台构建扩展，不使运行时程序集引用平台 Editor DLL。</summary>
    public static class HtmlCssPlayerBuild
    {
        [Serializable] sealed class BuildInputs { public string nativeSha256; }
        static string Hash(byte[] bytes)
        { using var hash = SHA256.Create(); return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        /// <summary>仅在已准备作者输入的隔离工程中构建 macOS ARM64 IL2CPP 验证场景。</summary>
        public static void BuildMacArm64()
        {
            var destination = Environment.GetEnvironmentVariable("URUI_HTML_CSS_PLAYER_OUTPUT");
            if (string.IsNullOrEmpty(destination) || !Path.IsPathRooted(destination)) throw new InvalidOperationException("Set an absolute URUI_HTML_CSS_PLAYER_OUTPUT.");
            var inputs = JsonUtility.FromJson<BuildInputs>(UnityEngine.Resources.Load<TextAsset>("URUIHtmlCssPlayer/manifest").text);
            const string pluginPath = "Packages/com.fujisheng.urui/Runtime/Plugins/macOS/libURUIBridge.dylib";
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(Document).Assembly).resolvedPath;
            if (Hash(File.ReadAllBytes(Path.Combine(package, "Runtime/Plugins/macOS/libURUIBridge.dylib"))) != inputs.nativeSha256)
                throw new InvalidOperationException("Player plugin differs from prepared inputs.");
            var plugin = (UnityEditor.PluginImporter)UnityEditor.AssetImporter.GetAtPath(pluginPath);
            if (!plugin.GetCompatibleWithPlatform(UnityEditor.BuildTarget.StandaloneOSX)) throw new InvalidOperationException("Player native plugin is disabled.");
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            new GameObject("HTML CSS Player acceptance").AddComponent<HtmlCssPlayerValidation>();
            const string scene = "Assets/HtmlCssPlayerValidation.unity";
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scene);
            UnityEditor.PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, UnityEditor.ScriptingImplementation.IL2CPP);
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
            UnityEditor.PlayerSettings.companyName = "URUIValidation"; UnityEditor.PlayerSettings.productName = "HtmlCssPlayerValidation";
            UnityEditor.PlayerSettings.colorSpace = ColorSpace.Linear;
            var result = UnityEditor.BuildPipeline.BuildPlayer(new UnityEditor.BuildPlayerOptions {
                scenes = new[] { scene }, locationPathName = destination, target = UnityEditor.BuildTarget.StandaloneOSX, options = UnityEditor.BuildOptions.Development
            });
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new InvalidOperationException("Player build failed: " + result.summary.result);
            Debug.Log("HTML_CSS_PLAYER_BUILD_PASS"); UnityEditor.EditorApplication.Exit(0);
        }
    }
}
#endif
