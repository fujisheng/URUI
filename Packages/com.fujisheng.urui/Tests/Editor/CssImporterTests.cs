using System.IO;
using System;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using URUI.Editor.Authoring;

namespace URUI.Tests.Editor
{
    public sealed class CssImporterTests
    {
        const string Path = "Assets/URUI-css-import-test.css";
        const string OldPath = "Assets/URUI-css-migration-test.css.txt";
        const string CssPath = "Assets/URUI-css-migration-test.css";
        const string ReferencePath = "Assets/URUI-css-migration-reference.asset";
        const string ShaderPath = "Assets/URUI-css-migration-test.shader";
        [TearDown] public void TearDown()
        {
            AssetDatabase.DeleteAsset(ReferencePath); AssetDatabase.DeleteAsset(OldPath);
            AssetDatabase.DeleteAsset(CssPath); AssetDatabase.DeleteAsset(Path);
            AssetDatabase.DeleteAsset(ShaderPath);
        }

        [Test]
        public void FailedTextAssetMoveRestoresSourceAndSerializedReference()
        {
            const string content = "Shader \"Hidden/URUI/TextMoveRollback\" { SubShader { Pass {} } }";
            File.WriteAllText(OldPath, content, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(OldPath, ImportAssetOptions.ForceSynchronousImport);
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(OldPath);
            var reference = ScriptableObject.CreateInstance<TextReferenceFixture>(); reference.Source = source;
            AssetDatabase.CreateAsset(reference, ReferencePath); AssetDatabase.SaveAssets();
            global::UnityEngine.Resources.UnloadAsset(reference);
            Assert.Throws<InvalidOperationException>(() => TextAssetRelocation.Move(OldPath, ShaderPath));
            Assert.IsFalse(File.Exists(ShaderPath));
            var restored = AssetDatabase.LoadAssetAtPath<TextAsset>(OldPath);
            Assert.IsNotNull(restored); Assert.AreEqual(content, restored.text);
            var restoredReference = AssetDatabase.LoadAssetAtPath<TextReferenceFixture>(ReferencePath);
            Assert.IsTrue(restoredReference.Source); Assert.AreSame(restored, restoredReference.Source);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CssExtensionMigrationRebindsSerializedTextAssetReference(bool multiline)
        {
            File.WriteAllText(OldPath, ".example{color:red;}", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(OldPath, ImportAssetOptions.ForceSynchronousImport);
            var original = AssetDatabase.LoadAssetAtPath<TextAsset>(OldPath);
            Assert.IsNotNull(original);
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(original, out string beforeGuid, out long beforeId));
            var reference = ScriptableObject.CreateInstance<TextReferenceFixture>(); reference.Source = original;
            var literal = "Source: {fileID: " + beforeId + ", guid: " + beforeGuid + ", type: 3}";
            if (multiline) literal = "First line\n" + literal + "\nLast line";
            reference.Literal = literal;
            AssetDatabase.CreateAsset(reference, ReferencePath); AssetDatabase.SaveAssets();
            global::UnityEngine.Resources.UnloadAsset(reference);
            TextAssetRelocation.Move(OldPath, CssPath);
            AssetDatabase.ImportAsset(CssPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var migrated = AssetDatabase.LoadAssetAtPath<TextAsset>(CssPath);
            Assert.IsNotNull(migrated);
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(migrated, out string afterGuid, out long afterId));
            var restored = AssetDatabase.LoadAssetAtPath<TextReferenceFixture>(ReferencePath);
            Assert.IsTrue(restored.Source, "改扩展名后的 TextAsset 引用失效；localID：" + beforeId + " -> " + afterId);
            Assert.AreSame(migrated, restored.Source); Assert.AreEqual(beforeGuid, afterGuid);
            Assert.AreEqual(literal, restored.Literal, "字符串内容中的引用外观不能作为真实资源引用改写。");
        }
        [TestCase(false)]
        [TestCase(true)]
        public void RealCssExtensionImportsAsTextAssetWithoutChangingUtf8Bytes(bool bom)
        {
            var content = "/* 样式 */\r\n.example { color: red; }\r\n";
            var bytes = Encoding.UTF8.GetBytes((bom ? "\uFEFF" : "") + content);
            File.WriteAllBytes(Path, bytes);
            AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(Path);
            Assert.IsNotNull(asset, "真实 .css 必须可直接作为 TextAsset 使用。");
            CollectionAssert.AreEqual(bytes, asset.bytes);
        }
    }
}
