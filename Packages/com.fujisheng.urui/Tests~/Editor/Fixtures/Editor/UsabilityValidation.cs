#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using URUI;
using URUI.Editor;

namespace URUI.Tests.Editor
{
public static class UsabilityValidation
{
    public static void Run()
    {
        System.IO.Directory.CreateDirectory("Evidence");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/MenuTestStart.unity");
        DocumentMenu.CreateMinimalScene();
        var first = UnityEngine.Object.FindFirstObjectByType<Document>();
        var firstSource = (TextAsset)new SerializedObject(first).FindProperty("source").objectReferenceValue;
        var firstPath = AssetDatabase.GetAssetPath(firstSource);
        File.AppendAllText(firstPath, "\n<!-- preserve edited document -->\n"); AssetDatabase.Refresh();
        var firstScene = SceneManager.GetActiveScene().path;
        DocumentMenu.CreateMinimalScene();
        var second = UnityEngine.Object.FindFirstObjectByType<Document>();
        var secondSource = (TextAsset)new SerializedObject(second).FindProperty("source").objectReferenceValue;
        if (SceneManager.GetActiveScene().path == firstScene || AssetDatabase.GetAssetPath(secondSource) == firstPath || !File.ReadAllText(firstPath).Contains("preserve edited document"))
            throw new Exception("menu overwrote existing scene/document");
        File.WriteAllText("Evidence/menu-preservation.json", "{\"passed\":true,\"uniqueScenes\":true,\"uniqueDocuments\":true,\"editedContentPreserved\":true}");
        var smoke = new GameObject("Usability Test").AddComponent<UsabilitySmoke>();
        smoke.generatedDocument = second; smoke.generatedSource = secondSource;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
    }
}

}
#endif
