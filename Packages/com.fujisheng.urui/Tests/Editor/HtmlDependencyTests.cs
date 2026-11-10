using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using URUI.Editor.Authoring;

namespace URUI.Tests.Editor
{
    public sealed class HtmlDependencyTests
    {
        readonly Dictionary<string, TextAsset> files = new Dictionary<string, TextAsset>(StringComparer.Ordinal);
        TextAsset Add(string uri, string content)
        {
            var asset = new TextAsset(content); files.Add(uri, asset); return asset;
        }
        TextAsset Load(string uri) => files.TryGetValue(uri, out var asset) ? asset : null;
        [TearDown] public void Cleanup()
        {
            foreach (var asset in files.Values) { UnityEngine.Object.DestroyImmediate(asset); }
            files.Clear();
        }
        [Test]
        public void CollectsOwnedFragmentsCssFontsInlineAndEmbeddedImages()
        {
            Add("/ui/page.html", "<!doctype html><html><head><link rel=\"stylesheet\" href=\"css/main.css\"><link rel=\"urui-fragment\" href=\"items/item.html\"><style>.x{background-image:url(embedded.png);}</style></head><body><img src=\"image.png\"><div style=\"background-image:url(inline.png)\"></div></body></html>");
            Add("/ui/css/main.css", "@font-face{font-family:Local;src:url(../font.ttf.bytes);}.x{background-image:url(../icons/a\\ b.png);}");
            Add("/ui/font.ttf.bytes", "opaque-font-data");
            Add("/ui/items/item.html", "<div><img src=\"item.png\"></div>");
            var graph = HtmlResourceDependencies.Collect("/ui/page.html", Load);
            CollectionAssert.AreEquivalent(new[] { "/ui/css/main.css", "/ui/font.ttf.bytes", "/ui/items/item.html" }, graph.TextFiles.Select(file => file.Source));
            // 片段相对 URL 与实际 SetInnerHtml 一样，归属于顶层文档。
            CollectionAssert.AreEquivalent(new[] { "/ui/image.png", "/ui/item.png", "/ui/embedded.png", "/ui/inline.png", "/ui/icons/a%20b.png" }, graph.ImageUrls);
        }
        [Test]
        public void DeduplicatesDependenciesAndRetainsOriginalTextAssets()
        {
            Add("/ui/page.html", "<!doctype html><html><head><link rel=stylesheet href=\"a.css\"><link rel=stylesheet href=\"./a.css\"></head><body></body></html>");
            var css = Add("/ui/a.css", "body{color:red;}");
            var graph = HtmlResourceDependencies.Collect("/ui/page.html", Load);
            Assert.AreEqual(1, graph.TextFiles.Count); Assert.AreSame(css, graph.TextFiles[0].Asset);
        }
        [TestCase("missing.css", "", false)]
        [TestCase("bad.css", "body{unknown-style:1;}", true)]
        [TestCase("old.css.txt", "body{color:red;}", true)]
        [TestCase("https://example.com/a.css", "body{color:red;}", false)]
        [TestCase("../../outside.css", "body{color:red;}", false)]
        public void RejectsMissingInvalidLegacyAndForeignDependencies(string uri, string content, bool exists)
        {
            Add("/ui/page.html", "<!doctype html><html><head><link rel=stylesheet href=\"" + uri + "\"></head><body></body></html>");
            if (exists) { Add("/ui/" + uri, content); }
            Assert.Throws<InvalidOperationException>(() => HtmlResourceDependencies.Collect("/ui/page.html", Load));
        }
        [Test]
        public void RejectsFullDocumentsDeclaredAsOwnedFragments()
        {
            var html = "<!doctype html><html><head></head><body></body></html>";
            Add("/ui/page.html", html.Replace("</head>", "<link rel=urui-fragment href=other.html></head>"));
            Add("/ui/other.html", html);
            Assert.Throws<InvalidOperationException>(() => HtmlResourceDependencies.Collect("/ui/page.html", Load));
        }
    }
}
