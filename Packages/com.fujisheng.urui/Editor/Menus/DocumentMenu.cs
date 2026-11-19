using System.IO;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.Editor.Menus
{
    /// <summary>
    /// 创建 URUI 文档场景，保留当前场景和已有同名资产。
    /// </summary>
    public static class DocumentMenu
    {
        /// <summary>
        /// 创建包含文本资产、Document 与相机的最小场景；已有同名资产不会被覆盖。
        /// </summary>
        /// <remarks>
        /// 先询问保存当前场景；Source 通过序列化赋值，避免在编辑状态加载文档。
        /// </remarks>
        [MenuItem("Tools/URUI/Create Minimal UI Scene", false, 100)]
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
            var documentPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/Hello.html");
            var scenePath = AssetDatabase.GenerateUniqueAssetPath(folder + "/Hello.unity");
            File.WriteAllText(documentPath, "<!doctype html><html><head><link rel='stylesheet' href='/urui/base.css'><style>body { color:white; font-size:28px; }</style></head><body><div id='label'>Hello URUI</div></body></html>");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main;
            if (camera)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }

            var surface = new GameObject("URUI Surface").AddComponent<Surface>();
            var document = new GameObject("URUI Document").AddComponent<Document>();
            var resources = new List<TextResource> {
                new TextResource("/urui/base.css", RequiredText("Runtime/Resources/URUI/base.css"))
            };
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
            {
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var file = "Liberation" + family + "-" + face + ".ttf.bytes";
                    resources.Add(new TextResource("/urui/Fonts/" + file, RequiredText("Runtime/Resources/URUI/Fonts/" + file)));
                }
            }
            var serialized = new SerializedObject(document);
            serialized.FindProperty("surface").objectReferenceValue = surface;
            var compiled = URUI.Editor.Compilation.CompiledDocumentCompiler.CompileAndSave(
                AssetDatabase.LoadAssetAtPath<TextAsset>(documentPath), documentPath,
                System.IO.Path.ChangeExtension(documentPath, "compiled.asset"), resources);
            serialized.FindProperty("sourceUrl").stringValue = compiled.SourceUrl;
            serialized.FindProperty("compiledSource").objectReferenceValue = compiled;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, scenePath);
            Selection.activeObject = document;
        }

        static TextAsset RequiredText(string path) => AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.fujisheng.urui/" + path)
            ?? throw new InvalidOperationException("URUI 包缺少作者依赖：" + path);
    }
}
