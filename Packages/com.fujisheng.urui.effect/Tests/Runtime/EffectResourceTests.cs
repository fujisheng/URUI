using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using URUI.Styles;
using URUI.Rendering.Effects;

namespace URUI.Effect.Tests
{
    public sealed class EffectResourceTests
    {
        sealed class Resolver : IEffectResourceResolver
        {
            internal Texture2D texture;
            internal bool missing;
            internal int released;
            public ImageLease AcquireImage(ComputedImageValue image) { return missing ? null : new ImageLease(texture, () => released++); }
            public GameObject ResolveParticles(string source) { return null; }
        }
        sealed class EmptyFactory : IEffectFactory
        {
            public IElementEffect Create(EffectContext context) { throw new NotSupportedException(); }
        }

        [Test]
        public void InvalidImageKeepsPreviousLease()
        {
            var type = typeof(EffectModule).Assembly.GetType("URUI.Effect.EffectResources");
            Assert.That(type, Is.Not.Null, "附加纹理需要原子换源与 GPU 退役的资源所有者");
            var texture = new Texture2D(2, 2);
            var resolver = new Resolver { texture = texture };
            var retired = new List<IDisposable>();
            var definition = new EffectDefinition("resource-probe", EffectKind.Image, new EmptyFactory(), "resource:v1", "premultiplied");
            var context = (EffectContext)Activator.CreateInstance(typeof(EffectContext), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { definition, null, (ulong)1, (Action<IDisposable>)(value => retired.Add(value)) }, null);
            try
            {
                var resources = Activator.CreateInstance(type, new object[] { context, resolver });
                var prepare = type.GetMethod("PrepareImage"); var get = type.GetMethod("GetImage");
                ComputedImageValue Value(string url) { return (ComputedImageValue)Activator.CreateInstance(typeof(ComputedImageValue),
                    BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { 1, url, "/styles/effect.css" }, null); }
                Assert.That(prepare.Invoke(resources, new object[] { "pattern", Value("a.png") }), Is.True);
                resolver.missing = true;
                Assert.That(prepare.Invoke(resources, new object[] { "pattern", Value("missing.png") }), Is.False);
                Assert.That(get.Invoke(resources, new object[] { "pattern" }), Is.SameAs(texture));
                Assert.That(resolver.released, Is.Zero);
                resolver.missing = false;
                Assert.That(prepare.Invoke(resources, new object[] { "pattern", Value("b.png") }), Is.True);
                Assert.That(retired.Count, Is.EqualTo(1)); Assert.That(resolver.released, Is.Zero);
                retired[0].Dispose(); Assert.That(resolver.released, Is.EqualTo(1));
            }
            finally { context.Dispose(); UnityEngine.Object.DestroyImmediate(texture); }
            Assert.That(resolver.released, Is.EqualTo(2));
        }
        [Test]
        public void StylesheetRelativeTextureUrl()
        {
            Assert.That(Enum.IsDefined(typeof(StyleValueType), "Image"), Is.True,
                "附加纹理需要原生解析的类型化图像参数");
            var host = new GameObject("Image parameter source");
            var camera = new GameObject("Image parameter camera", typeof(Camera)); camera.tag = "MainCamera";
            var css = new TextAsset("@property --probe-image {syntax:'<image>';inherits:false;initial-value:none;}" +
                "#node{width:32px;height:32px;--probe-image:url('../images/pattern.png');}");
            try
            {
                var surface = host.AddComponent<Surface>();
                var document = host.AddComponent<Document>(); document.Surface = surface;
                document.TextResources = new[] { new TextResource("/styles/effect.css", css) };
                document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/styles/effect.css'></head>" +
                    "<body><div id='node'></div></body></html>", "/views/example.html");
                document.SynchronizeLayout();
                var snapshot = document.Styles.GetComputed(document.Query("node")[0]);
                var method = typeof(ComputedStyleSnapshot).GetMethod("GetImage");
                Assert.That(method, Is.Not.Null);
                var value = method.Invoke(snapshot, new object[] { "--probe-image" });
                Assert.That(value.GetType().GetProperty("Url").GetValue(value), Is.EqualTo("../images/pattern.png"));
                Assert.That(value.GetType().GetProperty("SourceUrl").GetValue(value), Is.EqualTo("/styles/effect.css"));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(camera); UnityEngine.Object.DestroyImmediate(css); }
        }
    }
}
