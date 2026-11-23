using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using URUI.Internal.Interop;

namespace URUI.Tests
{
    public sealed class CompiledDocumentTests
    {
        [Test]
        public void BatchedGeometryFlushesOnReadAndNestedCompletion()
        {
            var camera = new GameObject("Batch camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Batch surface");
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml("<!doctype html><html><head></head><body><div id='box' style='width:10px;height:10px;'></div></body></html>", "/batch.html");
                var box = document.Q("box");
                using (document.BeginUpdate())
                {
                    box.Style.SetWidth(20);
                    using (document.BeginUpdate())
                    {
                        box.Style.SetWidth(30);
                    }
                    Assert.That(box.Layout.width, Is.EqualTo(30).Within(.01), "几何读取必须刷新嵌套批次中的改动。");
                    box.Style.SetWidth(40);
                }
                Assert.That(box.Layout.width, Is.EqualTo(40).Within(.01));
                var scope = document.BeginUpdate();
                document.Unload();
                Assert.DoesNotThrow(() => scope.Dispose(), "卸载中的批次结束不能访问失效文档。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
            }
        }

        [Test]
        public void CompiledQueryKeepsIdLookupWhenClassesChangeAndRejectsStructuralChanges()
        {
            var camera = new GameObject("Compiled Query Camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Compiled Query Surface");
            CompiledDocumentAsset asset = null;
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                asset = Compile("<!doctype html><html><head></head><body><div id='box' class='entry'></div></body></html>");
                document.Load(asset);
                Assert.AreEqual(1, NativeMethods.urui_element_compiled_info(document.Surface.SurfaceHandle, document.NativeHandle, document.Body.Token, 0, null, out var info));
                var index = new CompiledNodeIndex();
                index.Initialize(info.Identity, new[] { 4, 2, 4, 4 }, new[] {
                    new CompiledNodeIndex.Selector {
key = "#box",
nodes = new[] { 3 }
},
                    new CompiledNodeIndex.Selector {
key = ".entry",
nodes = new[] { 3 }
}
                });
                asset.SetBlueprints(index, null);
                var query = document.CreateCompiledQuerySnapshot(document.Body);
                Assert.IsNotNull(query);
                Assert.IsTrue(query.TryQuery(".entry", out var matches));
                Assert.AreEqual(1, matches.Count);
                document.Q("box").AddToClassList("changed");
                Assert.IsFalse(query.TryQuery(".changed", out _));
                Assert.IsTrue(query.TryQuery("#box", out matches));
                Assert.AreEqual(1, matches.Count);
                document.Body.Add(document.CreateElement("div"));
                Assert.IsFalse(query.TryQuery("#box", out _));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
        [DllImport("URUIBridge", CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_compile_document([MarshalAs(UnmanagedType.LPUTF8Str)] string html,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string uri, int fragment, IntPtr files, int count,
            [Out] byte[] output, int capacity, out int required);

        static CompiledDocumentAsset Compile(string html)
        {
            var bytes = new byte[65536];
            var result = urui_compile_document(html, "/compiled.html", 0, IntPtr.Zero, 0, bytes, bytes.Length, out var length);
            if (result == NativeStatus.BufferTooSmall && length > bytes.Length)
            {
                bytes = new byte[length];
                result = urui_compile_document(html, "/compiled.html", 0, IntPtr.Zero, 0, bytes, bytes.Length, out length);
            }
            Assert.AreEqual(1, result);
            Array.Resize(ref bytes, length);
            var asset = ScriptableObject.CreateInstance<CompiledDocumentAsset>();
            asset.Initialize("/compiled.html", bytes, "fixture", Array.Empty<TextResource>(), false, null);
            return asset;
        }

        [Test]
        public void CompiledLoadPreservesDynamicLayoutAndFailedReplacement()
        {
            var camera = new GameObject("Compiled Camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Compiled Surface");
            CompiledDocumentAsset asset = null;
            CompiledDocumentAsset broken = null;
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(400, 200);
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                asset = Compile("<!doctype html><html><head><style>body{--w:25px;}#box{width:var(--w);height:10px;}</style></head><body><div id='box'></div></body></html>");
                document.Load(asset);
                document.SynchronizeGeometry();
                var box = document.Q("box");
                Assert.That(box.Layout.width, Is.EqualTo(25).Within(.01));
                box.Style.Set("--w", "50px");
                document.SynchronizeGeometry();
                Assert.That(box.Layout.width, Is.EqualTo(50).Within(.01));
                broken = Compile("<!doctype html><html><head></head><body></body></html>");
                broken.EncodedData[broken.EncodedData.Length - 1] ^= 1;
                Assert.Throws<InvalidOperationException>(() => document.Load(broken));
                Assert.IsTrue(box.IsValid);
                Assert.AreSame(box, document.Q("box"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(broken);
            }
        }
    }
}
