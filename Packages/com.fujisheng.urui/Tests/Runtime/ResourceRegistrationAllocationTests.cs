using System;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.Tests
{
    /// <summary>
    /// 先清理堆再验证大资源不会被复制到托管堆，并用阳性控制校验计量。
    /// </summary>
    public sealed class ResourceRegistrationAllocationTests
    {
        static object positive;
        [Test]
        public void LargeResourceRegistrationDoesNotCopyItsBytesToManagedMemory()
        {
            var camera = new GameObject("Allocation Camera", typeof(Camera));
            camera.tag = "MainCamera";
            var root = new GameObject("Allocation Surface");
            var small = new TextAsset("#box{width:73px;height:19px;}");
            var large = new TextAsset("/*" + new string('x', 1024 * 1024) + "*/#box{width:73px;height:19px;}");
            const string html = "<!doctype html><html><head><link rel='stylesheet' href='theme.css'></head><body><div id='box'></div></body></html>";
            try
            {
                var surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.TextResources = new[] { new TextResource("ui/theme.css", small) };
                document.LoadHtml(html, "ui/a.html");
                document.TextResources = new[] { new TextResource("ui/theme.css", large) };
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var beforePositive = GC.GetTotalMemory(false);
                positive = new byte[1024 * 1024];
                GC.KeepAlive(positive);
                Assert.That(GC.GetTotalMemory(false) - beforePositive, Is.GreaterThanOrEqualTo(1024 * 1024), "堆计量的阳性控制必须有效。");
                var collections = GC.CollectionCount(0);
                var before = GC.GetTotalMemory(false);
                document.LoadHtml(html, "ui/a.html");
                var bytes = GC.GetTotalMemory(false) - before;
                Assert.That(GC.CollectionCount(0), Is.EqualTo(collections), "测量期间发生回收，不能把堆差量当作分配总量。");
                document.SynchronizeLayout();
                Assert.That(document.Q("box").Layout.width, Is.EqualTo(73).Within(.01));
                Assert.That(bytes, Is.LessThan(128 * 1024), "同步注册不得把一MiB资源复制到托管堆。");
            }
            finally
            {
                positive = null;
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);
                UnityEngine.Object.DestroyImmediate(small);
                UnityEngine.Object.DestroyImmediate(large);
            }
        }
    }
}
