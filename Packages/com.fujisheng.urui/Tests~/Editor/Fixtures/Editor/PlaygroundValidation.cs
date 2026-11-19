#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace URUI.Tests.Editor
{
    /// <summary>
    /// 从实际 UPM 样例创建显式配置资源的独立 Playground 验收场景。
    /// </summary>
    public static class PlaygroundValidation
    {
        public static void Run()
        {
            Directory.CreateDirectory("Evidence");
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.fujisheng.urui");
            var sample = UnityEditor.PackageManager.UI.Sample.FindByPackage(package.name, package.version)
                .Single(value => value.displayName == "Element Playground");
            if (!sample.isImported)
            {
                throw new System.InvalidOperationException("请先使用 Sample.Import 导入 Element Playground。");
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var surface = new GameObject("Playground Surface").AddComponent<URUI.Hosting.Surface>();
            var owner = new GameObject("Element Playground");
            var document = owner.AddComponent<URUI.Documents.Document>();
            var files = new List<URUI.Resources.TextResource>();
            Add("/urui/base.css", "base.css");
            foreach (var family in new[] { "Serif", "Sans", "Mono" })
            {
                foreach (var face in new[] { "Regular", "Italic", "Bold", "BoldItalic" })
                {
                    var name = "Liberation" + family + "-" + face + ".ttf.bytes";
                    Add("/urui/Fonts/" + name, "Fonts/" + name);
                }
            }
            var config = new SerializedObject(document);
            config.FindProperty("surface").objectReferenceValue = surface;
            config.FindProperty("sourceUrl").stringValue = "samples/element-playground/Playground.html";
            config.FindProperty("source").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(
                FileUtil.GetProjectRelativePath(Path.Combine(sample.importPath, "Playground.html")));
            var resources = config.FindProperty("textResources");
            resources.arraySize = files.Count;
            for (var i = 0; i < files.Count; ++i)
            {
                var entry = resources.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("source").stringValue = files[i].Source;
                entry.FindPropertyRelative("asset").objectReferenceValue = files[i].Asset;
            }
            config.ApplyModifiedPropertiesWithoutUndo();
            owner.AddComponent<URUI.Samples.ElementPlayground>();
            new GameObject("Playground Verification").AddComponent<PlaygroundSmoke>();
            Directory.CreateDirectory("Assets/URUIEditorChecks");
            AssetDatabase.Refresh();
            EditorSceneManager.SaveScene(scene, "Assets/URUIEditorChecks/PlaygroundChecks.unity");
            EditorApplication.delayCall += () => EditorApplication.delayCall += () => EditorApplication.isPlaying = true;

            void Add(string uri, string relative)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>("Packages/com.fujisheng.urui/Runtime/Resources/URUI/" + relative);
                if (!asset)
                {
                    throw new System.InvalidOperationException("缺少样例依赖：" + relative);
                }
                files.Add(new URUI.Resources.TextResource(uri, asset));
            }
        }
    }
}
#endif
