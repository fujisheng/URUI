#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using URUI.Documents;

namespace URUI.Tests.Editor
{
    /// <summary>在独立工程运行真实 元素文档、输入和控件验收。</summary>
    public static class ElementRuntimeValidation
    {
        /// <summary>独立执行控件完整行为验收。</summary>
        public static void VerifyControls()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            new GameObject("Control Completeness Test").AddComponent<URUI.Tests.ControlCompletenessSmoke>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/ControlCompleteness.unity");
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
        /// <summary>预览综合控件样例，保存真实相机渲染结果。</summary>
        public static void PreviewSample()
        {
            SetPreviewSize(1100, 720);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("URUI Runtime Controls");
            go.AddComponent<URUI.Samples.RuntimeControls>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/RuntimeControls.unity");
            go.AddComponent<URUI.Tests.ControlsPreview>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/RuntimeControlsPreview.unity");
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }

        static void SetPreviewSize(int width, int height)
        {
            // 只在可丢弃验收工程调整 Game View，避免截图被默认 640x480 裁切。
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance").GetValue(null);
            var groupType = assembly.GetType("UnityEditor.GameViewSizeGroupType");
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { Enum.Parse(groupType, "Standalone") });
            var sizeType = assembly.GetType("UnityEditor.GameViewSize");
            var kindType = assembly.GetType("UnityEditor.GameViewSizeType");
            var size = Activator.CreateInstance(sizeType, new[] { Enum.Parse(kindType, "FixedResolution"), (object)width, height, "URUI preview" });
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { size });
            int index = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null) - 1;
            var viewType = assembly.GetType("UnityEditor.GameView");
            var view = EditorWindow.GetWindow(viewType);
            viewType.GetProperty("selectedSizeIndex").SetValue(view, index);
        }

        /// <summary>批处理入口；运行结果保存在 Evidence/element-runtime.json。</summary>
        public static void Run()
        {
            if (typeof(Document).Assembly.GetType("URUI.Controls.Button") == null)
            {
                throw new Exception("Missing element runtime capability: typed Button and Document factory");
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            new GameObject("Element Runtime Test").AddComponent<URUI.Tests.ElementRuntimeSmoke>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/ElementRuntimeSmoke.unity");
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }

        /// <summary>构建同一组行为夹具的 macOS ARM64 IL2CPP Player，完成后退出 Editor。</summary>
        public static void BuildPlayer()
        {
            BuildValidationPlayer(typeof(URUI.Tests.ElementRuntimeSmoke), "ElementRuntimePlayer");
        }

        /// <summary>构建控件完整行为夹具的 ARM64 IL2CPP Player。</summary>
        public static void BuildControlsPlayer()
        {
            BuildValidationPlayer(typeof(URUI.Tests.ControlCompletenessSmoke), "ControlCompletenessPlayer");
        }

        static void BuildValidationPlayer(Type fixture, string productName)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            new GameObject("URUI Validation").AddComponent(fixture);
            string scene = "Assets/" + productName + ".unity";
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), scene);
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
            PlayerSettings.productName = productName;
            const string pluginPath = "Packages/com.fujisheng.urui/Runtime/Plugins/macOS/libURUIBridge.dylib";
            var plugin = (PluginImporter)AssetImporter.GetAtPath(pluginPath);
            if (!plugin.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX))
            {
                throw new Exception("macOS plugin is not enabled for Player");
            }
            Debug.Log("ELEMENT_RUNTIME_PLUGIN_CPU=" + plugin.GetPlatformData(BuildTarget.StandaloneOSX, "CPU"));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = System.IO.Path.GetFullPath("Builds/" + productName + ".app"),
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                throw new Exception("Element runtime Player build failed: " + report.summary.result);
            }

            if (System.IO.Directory.GetFiles("Builds/" + productName + ".app", "*URUIBridge*", System.IO.SearchOption.AllDirectories).Length == 0)
            {
                throw new Exception("Player build omitted the native plugin");
            }

            Debug.Log("ELEMENT_RUNTIME_IL2CPP_BUILD_PASS");
            EditorApplication.Exit(0);
        }
    }
}
#endif
