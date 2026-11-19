using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Hosting;
using URUI.Rendering.Effects;
using URUI.Resources;
using URUI.Styles;

namespace URUI.Effect.Tests
{
    public sealed class ElementEffectInputTests
    {
        sealed class Factory : IEffectFactory
        {
            internal EffectFrame Frame;
            internal int Records;
            internal System.Action Preparing;
            internal System.Action Recording;
            public IElementEffect Create(EffectContext context)
            {
                return new Instance(this);
            }
        }
        sealed class Instance : IElementEffect
        {
            readonly Factory owner;
            internal Instance(Factory owner)
            {
                this.owner = owner;
            }
            public void Prepare(EffectFrame frame)
            {
                owner.Frame = frame;
                owner.Preparing?.Invoke();
            }
            public void Record(EffectRenderContext context)
            {
                owner.Records++;
                owner.Recording?.Invoke();
                context.DrawOriginal();
            }
            public void Dispose()
            {
            }
        }
        sealed class ParameterFactory : IEffectFactory
        {
            internal EffectPreset Preset;
            internal readonly System.Collections.Generic.List<EffectParameters> Values = new();
            public IElementEffect Create(EffectContext context)
            {
                var parameters = Preset.CreateParameters();
                Values.Add(parameters);
                return new ParameterInstance(parameters);
            }
        }
        sealed class ParameterInstance : IElementEffect
        {
            readonly EffectParameters parameters;
            internal ParameterInstance(EffectParameters parameters)
            {
                this.parameters = parameters;
            }
            public void Prepare(EffectFrame frame)
            {
                parameters.ToneIntensity = frame.Style.GetNumber("--progress");
            }
            public void Record(EffectRenderContext context)
            {
                context.DrawOriginal();
            }
            public void Dispose()
            {
            }
        }

        [UnityTest]
        public IEnumerator TwoDocumentsDoNotShareMutableParameters()
        {
            var preset = ScriptableObject.CreateInstance<EffectPreset>();
            var factory = new ParameterFactory { Preset = preset };
            var host = new GameObject("Two documents");
            var cameraObject = new GameObject("Documents camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            try
            {
                var surface = host.AddComponent<Surface>();
                var documents = new Document[2];
                for (var index = 0; index < 2; index++)
                {
                    var page = new GameObject("Page " + index);
                    page.transform.SetParent(host.transform);
                    var document = page.AddComponent<Document>();
                    document.Surface = surface;
                    documents[index] = document;
                    document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture) };
                    document.Effects.Register(new EffectDefinition("pass", EffectKind.Element, factory, "pass:v1", "premultiplied output",
                        new[] { new StylePropertyDefinition("--progress", StyleValueType.Number) }));
                    document.LoadHtml("<!doctype html><html><head><style>" +
                        "@property --progress{syntax:'<number>';inherits:false;initial-value:0;}" +
                        "img{width:40px;height:30px;--urui-effect:pass;--progress:" + (index == 0 ? ".2" : ".8") +
                        ";}</style></head><body><img id='image' src='white.png'></body></html>", "/page" + index + ".html");
                }
                for (var index = 0; index < 3; index++)
                {
                    yield return null;
                }
                documents[0].SynchronizeLayout();
                Assert.That(factory.Values.Count, Is.EqualTo(2));
                Assert.That(factory.Values[0].ToneIntensity, Is.EqualTo(.2f));
                Assert.That(factory.Values[1].ToneIntensity, Is.EqualTo(.8f));
                documents[0].Q("image").Style.SetNumber("--progress", .4f);
                documents[0].SynchronizeLayout();
                Assert.That(factory.Values[0].ToneIntensity, Is.EqualTo(.4f));
                Assert.That(factory.Values[1].ToneIntensity, Is.EqualTo(.8f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(preset);
            }
        }

        [UnityTest]
        public IEnumerator LeafImageUsesOriginalGeometry()
        {
            var factory = new Factory();
            var host = new GameObject("Leaf effect");
            var cameraObject = new GameObject("Leaf camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            try
            {
                var surface = host.AddComponent<Surface>();
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture) };
                document.Effects.Register(new EffectDefinition("pass", (EffectKind)1, factory, "pass:v1", "premultiplied output"));
                document.LoadHtml("<!doctype html><html><head><style>img{width:40px;height:30px;--urui-effect:pass;}" +
                    "</style></head><body><img src='white.png'></body></html>", "/leaf.html");
                for (var index = 0; index < 3; index++)
                {
                    yield return null;
                }
                document.SynchronizeLayout();
                cameraObject.GetComponent<Camera>().Render();
                Assert.That(factory.Records, Is.GreaterThan(0));
                Assert.That(factory.Frame.Source, Is.SameAs(Texture2D.whiteTexture), "叶子图片不得先捕获到 RT");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator TextAndSubtreeProvideBoundedInput()
        {
            var factory = new Factory();
            var host = new GameObject("Subtree effect");
            var cameraObject = new GameObject("Subtree camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            try
            {
                var surface = host.AddComponent<Surface>();
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                document.Effects.Register(new EffectDefinition("pass", (EffectKind)1, factory, "pass:v1", "premultiplied output"));
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}" +
                    "#group{width:80px;height:40px;background-color:red;--urui-effect:pass;}" +
                    "#child{width:20px;height:20px;background-color:blue;color:white;font-size:16px;}" +
                    "</style></head><body><div id='group'><div id='child'>X</div></div></body></html>", "/subtree.html");
                for (var index = 0; index < 3; index++)
                {
                    yield return null;
                }
                document.SynchronizeLayout();
                cameraObject.GetComponent<Camera>().Render();
                Assert.That(factory.Records, Is.GreaterThan(0));
                Assert.That(factory.Frame.Source, Is.InstanceOf<RenderTexture>());
                Assert.That(factory.Frame.Source.width, Is.EqualTo(80));
                Assert.That(factory.Frame.Source.height, Is.EqualTo(40));
                var previous = RenderTexture.active;
                var pixels = new Texture2D(80, 40, TextureFormat.RGBAFloat, false, true);
                try
                {
                    RenderTexture.active = (RenderTexture)factory.Frame.Source;
                    pixels.ReadPixels(new Rect(0, 0, 80, 40), 0, 0);
                    pixels.Apply();
                    var white = 0;
                    var red = 0;
                    var blue = 0;
                    foreach (var color in pixels.GetPixels())
                    {
                        if (color.r > .8f && color.g > .8f && color.b > .8f)
                        {
                            white++;
                        }
                        if (color.r > .8f && color.b < .1f)
                        {
                            red++;
                        }
                        if (color.b > .8f && color.r < .1f)
                        {
                            blue++;
                        }
                    }
                    Assert.That(white, Is.GreaterThan(2), "文字应先绘入有界输入，不能直接使用 glyph atlas");
                    Assert.That(red, Is.GreaterThan(100));
                    Assert.That(blue, Is.GreaterThan(10));
                }
                finally
                {
                    RenderTexture.active = previous;
                    UnityEngine.Object.DestroyImmediate(pixels);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator PrepareAndRecordCannotMutateDom()
        {
            var factory = new Factory();
            var host = new GameObject("Effect callback guard");
            var cameraObject = new GameObject("Guard camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            try
            {
                var surface = host.AddComponent<Surface>();
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                document.Images = new[] { new ImageAsset("white.png", Texture2D.whiteTexture) };
                var prepareBlocked = 0;
                var recordBlocked = 0;
                factory.Preparing = () =>
                {
                    try
                    {
                        document.LoadHtml("<!doctype html><html><head></head><body></body></html>", "/forbidden.html");
                    }
                    catch (System.InvalidOperationException)
                    {
                        prepareBlocked++;
                    }
                };
                factory.Recording = () =>
                {
                    try
                    {
                        document.LoadHtml("<!doctype html><html><head></head><body></body></html>", "/forbidden.html");
                    }
                    catch (System.InvalidOperationException)
                    {
                        recordBlocked++;
                    }
                };
                document.Effects.Register(new EffectDefinition("pass", EffectKind.Element, factory, "pass:v1", "premultiplied output"));
                document.LoadHtml("<!doctype html><html><head><style>img{width:30px;height:20px;--urui-effect:pass;}</style></head>" +
                    "<body><img src='white.png'></body></html>", "/guard.html");
                for (var index = 0; index < 3; index++)
                {
                    yield return null;
                }
                document.SynchronizeLayout();
                cameraObject.GetComponent<Camera>().Render();
                Assert.That(prepareBlocked, Is.GreaterThan(0));
                Assert.That(recordBlocked, Is.EqualTo(factory.Records));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
