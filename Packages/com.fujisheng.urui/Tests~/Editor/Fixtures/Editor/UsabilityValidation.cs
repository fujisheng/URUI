#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using URUI.Editor.Menus;
using URUI.Documents;

namespace URUI.Tests.Editor
{
    /// <summary>
    /// 检查菜单资产保留与序列化文档，再运行 Hello URUI 交互验证。
    /// </summary>
    public static class UsabilityValidation
    {
        static TextAsset AuthorSource(Document document)
        {
            var compiled = new SerializedObject(document).FindProperty("compiledSource").objectReferenceValue;
            if (!compiled) throw new Exception("Menu document must use a compiled asset.");
            return (TextAsset)new SerializedObject(compiled).FindProperty("authorSource").objectReferenceValue;
        }
        /// <summary>
        /// 批处理入口；仅在可丢弃的工程中运行，会创建测试场景和证据文件。
        /// </summary>
        public static void Run()
        {
            System.IO.Directory.CreateDirectory("Evidence");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/MenuTestStart.unity");
            DocumentMenu.CreateMinimalScene();
            var first = UnityEngine.Object.FindFirstObjectByType<Document>();
            var firstSource = AuthorSource(first);
            var firstPath = AssetDatabase.GetAssetPath(firstSource);
            File.AppendAllText(firstPath, "\n<!-- preserve edited document -->\n");
            AssetDatabase.Refresh();
            var firstScene = SceneManager.GetActiveScene().path;
            // 第二次创建必须使用新路径，第一次创建后编辑的文档内容必须原样保留。
            DocumentMenu.CreateMinimalScene();
            var second = UnityEngine.Object.FindFirstObjectByType<Document>();
            var secondSource = AuthorSource(second);
            if (SceneManager.GetActiveScene().path == firstScene
                || AssetDatabase.GetAssetPath(secondSource) == firstPath
                || !File.ReadAllText(firstPath).Contains("preserve edited document"))
            {
                throw new Exception("menu overwrote existing scene/document");
            }

            File.WriteAllText("Evidence/menu-preservation.json", "{\"passed\":true,\"uniqueScenes\":true,\"uniqueDocuments\":true,\"editedContentPreserved\":true}");
            var smoke = new GameObject("Usability Test").AddComponent<UsabilitySmoke>();
            smoke.generatedDocument = second;
            smoke.generatedSource = secondSource;
            smoke.generatedCompiled = (URUI.Resources.CompiledDocumentAsset)new SerializedObject(second).FindProperty("compiledSource").objectReferenceValue;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            // 等待编辑器完成资产初始化后再进入 Play Mode。
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;
        }
    }
}
#endif
