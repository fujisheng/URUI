using NUnit.Framework;
using UnityEngine;
using URUI.Editor.Compilation;
using URUI.Resources;
using UnityEditor;
using System.IO;
using System;
using Object = UnityEngine.Object;
using System.Collections;
using UnityEngine.TestTools;
using UnityEditor.SceneManagement;

namespace URUI.Editor.Tests
{
    public sealed class CompiledDocumentCompilerTests
    {
        [Test]
        public void CompilerBakesPageAndParentScopedTemplateIndices()
        {
            var html = new TextAsset("<!doctype html><html><head><style>@property --width{syntax:'<length>';inherits:true;initial-value:12px;}</style></head><body><div id='list' class='items'></div></body></html>");
            var fragment = new TextAsset("<div class='entry'><span id='label' class='caption caption'></span></div>");
            CompiledDocumentAsset asset = null;
            try
            {
                asset = CompiledDocumentCompiler.Compile(html, "/ui/page.html", new[] { new TextResource("/ui/item.html", fragment) });
                var template = asset.GetTemplate("/ui/item.html");
                Assert.AreEqual(asset.Fingerprint, template.ParentFingerprint);
                Assert.IsNotNull(asset.FindBlueprint(template.NodeIndex.Identity));
                var entries = template.NodeIndex.Query(".caption", 0, template.NodeIndex.NodeCount, out var key);
                Assert.AreEqual("caption", key);
                Assert.AreEqual(1, entries.Count, "重复 class token 不能重复绑定同一节点。");
                var page = asset.FindBlueprint(asset.FindBlueprint(template.NodeIndex.Identity).Identity);
                Assert.AreSame(template.NodeIndex, page);
            }
            finally
            {
                Object.DestroyImmediate(asset);
                Object.DestroyImmediate(fragment);
                Object.DestroyImmediate(html);
            }
        }

        [Test]
        public void SceneGuardValidatesSerializedDocumentsBeforeRuntimeAwake()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var source = new TextAsset("<!doctype html><html><head></head><body></body></html>");
            CompiledDocumentAsset compiled = null;
            try
            {
                var root = new GameObject("Build document");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                var document = root.AddComponent<URUI.Documents.Document>();
                compiled = CompiledDocumentCompiler.Compile(source, "/page.html");
                var serialized = new SerializedObject(document);
                serialized.FindProperty("compiledSource").objectReferenceValue = compiled;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.DoesNotThrow(() => CompiledAssetBuildGuard.ValidateScene(scene));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(compiled);
                Object.DestroyImmediate(source);
            }
        }
        [UnityTest]
        public IEnumerator ImportedStyleAutomaticallyUpdatesExistingCompiledAsset()
        {
            const string folder = "Assets/CompiledDocumentAutoImportTests";
            AssetDatabase.CreateFolder("Assets", "CompiledDocumentAutoImportTests");
            try
            {
                File.WriteAllText(folder + "/page.html", "<!doctype html><html><head><link rel='stylesheet' href='a.css'></head><body></body></html>");
                File.WriteAllText(folder + "/a.css", "body{color:red;}");
                AssetDatabase.Refresh();
                var source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/page.html");
                var asset = CompiledDocumentCompiler.CompileAndSave(source, "/ui/page.html", folder + "/page.asset");
                var fingerprint = asset.Fingerprint;
                File.WriteAllText(folder + "/a.css", "body{color:blue;}");
                AssetDatabase.ImportAsset(folder + "/a.css", ImportAssetOptions.ForceSynchronousImport);
                var deadline = EditorApplication.timeSinceStartup + 5;
                while (asset.Fingerprint == fingerprint && EditorApplication.timeSinceStartup < deadline)
                {
                    yield return null;
                }
                Assert.AreNotEqual(fingerprint, asset.Fingerprint);
                CompiledDocumentCompiler.ValidateCurrent(asset);
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }
        [Test]
        public void RecompileDiscoversNewStylesAndBuildValidationRejectsStaleAsset()
        {
            const string folder = "Assets/CompiledDocumentCompilerTests";
            AssetDatabase.CreateFolder("Assets", "CompiledDocumentCompilerTests");
            try
            {
                File.WriteAllText(folder + "/page.html", "<!doctype html><html><head><link rel='stylesheet' href='a.css'></head><body></body></html>");
                File.WriteAllText(folder + "/a.css", "body{color:red;}");
                File.WriteAllText(folder + "/b.css", "body{color:blue;}");
                AssetDatabase.Refresh();
                var source = AssetDatabase.LoadAssetAtPath<TextAsset>(folder + "/page.html");
                var asset = CompiledDocumentCompiler.CompileAndSave(source, "/ui/page.html", folder + "/page.asset");
                var firstFingerprint = asset.Fingerprint;
                File.WriteAllText(folder + "/page.html", "<!doctype html><html><head><link rel='stylesheet' href='a.css'><link rel='stylesheet' href='b.css'></head><body></body></html>");
                AssetDatabase.ImportAsset(folder + "/page.html", ImportAssetOptions.ForceSynchronousImport);
                Assert.Throws<InvalidOperationException>(() => CompiledDocumentCompiler.ValidateCurrent(asset));
                CompiledDocumentCompiler.Recompile(asset);
                CompiledDocumentCompiler.ValidateCurrent(asset);
                Assert.AreEqual(2, asset.Dependencies.Count);
                Assert.AreNotEqual(firstFingerprint, asset.Fingerprint);
                Assert.AreSame(asset, AssetDatabase.LoadAssetAtPath<CompiledDocumentAsset>(folder + "/page.asset"));
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }
        [Test]
        public void CompilerResolvesRelativeStylesAndInvalidatesChangedDependency()
        {
            var html = new TextAsset("<!doctype html><html><head><link rel='stylesheet' href='../shared.css'></head><body><div id='item'></div></body></html>");
            var firstStyle = new TextAsset("#item{width:25px;}");
            var changedStyle = new TextAsset("#item{width:50px;}");
            CompiledDocumentAsset first = null;
            CompiledDocumentAsset changed = null;
            try
            {
                first = CompiledDocumentCompiler.Compile(html, "ui/pages/page.html", new[] { new TextResource("ui/shared.css", firstStyle) });
                changed = CompiledDocumentCompiler.Compile(html, "ui/pages/page.html", new[] { new TextResource("ui/shared.css", changedStyle) });
                Assert.IsNotNull(first);
                Assert.AreEqual("/ui/pages/page.html", first.SourceUrl);
                Assert.IsFalse(first.IsFragment);
                Assert.AreNotEqual(first.Fingerprint, changed.Fingerprint);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(changed);
                Object.DestroyImmediate(html);
                Object.DestroyImmediate(firstStyle);
                Object.DestroyImmediate(changedStyle);
            }
        }
    }
}
