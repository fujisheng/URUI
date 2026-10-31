#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace URUI.Tests.Editor
{
public static class ApiValidation
{
    public static void Run()
    {
        System.IO.Directory.CreateDirectory("Evidence");
        PublicApiContract.Check();
        var runtime = CompilationPipeline.GetAssemblies().Single(value => value.name == "URUI.Runtime");
        if (runtime.assemblyReferences.Any(value => value.name == "Unity.ugui")) throw new Exception("uGUI leaked into URUI runtime");
        PackageImport.Check();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera").AddComponent<Camera>();
        camera.tag = "MainCamera"; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
        new GameObject("API Test").AddComponent<ApiSmoke>();
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/ApiSmoke.unity");
        EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
    }
}

}
#endif
