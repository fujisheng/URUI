#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace URUI.Tests.Editor
{
    /// <summary>在 URUI-Dev 工程中为真实 Playground 场景创建独立回归副本。</summary>
    public static class PlaygroundValidation
    {
        public static void Run()
        {
            Directory.CreateDirectory("Evidence");
            var scene = EditorSceneManager.OpenScene("Assets/URUI/Scenes/Playground.unity", OpenSceneMode.Single);
            new GameObject("Playground Verification").AddComponent<PlaygroundSmoke>();
            EditorSceneManager.SaveScene(scene, "Assets/URUI/Verification/PlaygroundChecks.unity");
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
    }
}
#endif
