using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace URUI.Editor
{
    /// <summary>创建 URUI 文档场景，保留当前场景和已有同名资产。</summary>
    public static class DocumentMenu
    {
        [MenuItem("Tools/URUI/Create Minimal UI Scene")]
        public static void CreateMinimalScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string folder = "Assets/URUIGenerated";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            var documentPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/Hello.txt");
            var scenePath = AssetDatabase.GenerateUniqueAssetPath(folder + "/Hello.unity");
            File.WriteAllText(documentPath, "<rml><head><style>body { font-family:__RMLUNITY_FONT__; color:white; font-size:28px; }</style></head><body><div id='label'>Hello URUI</div></body></rml>");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main;
            if (camera)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }
            var document = new GameObject("URUI Document").AddComponent<Document>();
            var serialized = new SerializedObject(document);
            serialized.FindProperty("source").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(documentPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, scenePath);
            Selection.activeObject = document;
        }
    }
}

namespace RmlUnity.Editor
{
    /// <summary>旧编辑器脚本的兼容入口；新代码使用 URUI.Editor.DocumentMenu。</summary>
    public static class RmlUnityMenu
    {
        [Obsolete("Use URUI.Editor.DocumentMenu.CreateMinimalScene instead.")]
        public static void CreateMinimalScene() { URUI.Editor.DocumentMenu.CreateMinimalScene(); }
    }
}
