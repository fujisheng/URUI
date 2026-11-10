using System;
using System.Linq;
using NUnit.Framework;
using URUI.Documents;
using URUI.Editor.Authoring;
using URUI.Editor.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace URUI.Tests.Editor
{
    public sealed class DocumentMenuTests
    {
        [Test]
        public void GeneratedSceneUsesStrictHtmlExplicitResourcesAndPreservesExistingAssets()
        {
            var existing = AssetDatabase.GetAllAssetPaths().ToHashSet(StringComparer.Ordinal);
            var previousScene = SceneManager.GetActiveScene().path;
            try
            {
                var blank = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(blank, AssetDatabase.GenerateUniqueAssetPath("Assets/UruiMenuTest.unity"));
                DocumentMenu.CreateMinimalScene();
                var document = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Document>()).Single();
                var serialized = new SerializedObject(document);
                var source = (UnityEngine.TextAsset)serialized.FindProperty("source").objectReferenceValue;
                var path = AssetDatabase.GetAssetPath(source);
                Assert.That(path, Does.EndWith(".html"));
                Assert.AreEqual(path, serialized.FindProperty("sourceUrl").stringValue);
                Assert.IsNotNull(serialized.FindProperty("surface").objectReferenceValue);
                Assert.IsTrue(HtmlCssValidator.Validate(source.text, path, false).IsValid);
                var resources = serialized.FindProperty("textResources");
                Assert.AreEqual(13, resources.arraySize);
                for (var i = 0; i < resources.arraySize; i++)
                    Assert.IsNotNull(resources.GetArrayElementAtIndex(i).FindPropertyRelative("asset").objectReferenceValue);
                var original = source.text;
                DocumentMenu.CreateMinimalScene();
                Assert.AreEqual(original, AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(path).text);
                var second = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Document>()).Single();
                Assert.AreNotEqual(path, AssetDatabase.GetAssetPath(new SerializedObject(second).FindProperty("source").objectReferenceValue));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (var path in AssetDatabase.GetAllAssetPaths().Where(path => !existing.Contains(path) && path.StartsWith("Assets/", StringComparison.Ordinal)).OrderByDescending(path => path.Length))
                    AssetDatabase.DeleteAsset(path);
                if (!string.IsNullOrEmpty(previousScene)) EditorSceneManager.OpenScene(previousScene);
            }
        }
    }
}
