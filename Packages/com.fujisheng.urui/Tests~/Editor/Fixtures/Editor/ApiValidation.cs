#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace URUI.Tests.Editor
{
    /// <summary>
    /// 在独立 Editor 工程中检查包边界，并创建公开 API 的 Play Mode 回归场景。
    /// </summary>
    public static class ApiValidation
    {
        /// <summary>
        /// 批处理入口；创建测试资产并进入 Play Mode，后续由 ApiSmoke 写报告和退出。
        /// </summary>
        public static void Run()
        {
            System.IO.Directory.CreateDirectory("Evidence");
            PublicApiContract.Check();
            var runtime = CompilationPipeline.GetAssemblies().Single(value => value.name == "URUI.Runtime");
            if (runtime.assemblyReferences.Any(value => value.name == "Unity.ugui"))
            {
                throw new Exception("uGUI leaked into URUI runtime");
            }

            PackageImport.Check();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            new GameObject("API Test").AddComponent<ApiSmoke>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/ApiSmoke.unity");
            // 给 Editor 资产初始化留出更新轮次，避免在导入回调内直接切换 Play Mode。
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
    }
}
#endif
