#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using URUI.Elements;

namespace URUI.Tests.Editor
{
    /// <summary>创建独立节点 API 回归场景，不创建 Player 构建。</summary>
    public static class ElementTreeValidation
    {
        public static void Run()
        {
            PublicApiContract.Check();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            new GameObject("Element Tree Checks").AddComponent<ElementTreeSmoke>();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/ElementTreeChecks.unity");
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
    }
}
#endif
