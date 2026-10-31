using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace URUI.Editor
{
    /// <summary>
    /// 创建 URUI 文档场景，保留当前场景和已有同名资产。
    /// </summary>
    public static class DocumentMenu
    {
        /// <summary>
        /// 创建包含文本资产、Document 与相机的最小场景；已有同名资产不会被覆盖。
        /// </summary>
        /// <remarks>先询问保存当前场景；Source 通过序列化赋值，避免在编辑状态加载文档。</remarks>
        [MenuItem("Tools/URUI/Create Minimal UI Scene")]
        public static void CreateMinimalScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            const string folder = "Assets/URUIGenerated";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            // 文档和场景分别分配唯一路径，重复使用菜单时保留调用者已编辑的内容。
            var documentPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/Hello.txt");
            var scenePath = AssetDatabase.GenerateUniqueAssetPath(folder + "/Hello.unity");
            File.WriteAllText(documentPath, "<rml><head><style>body { font-family:__URUI_FONT__; color:white; font-size:28px; }</style></head><body><div id='label'>Hello URUI</div></body></rml>");
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
