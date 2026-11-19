using System;
using System.Linq;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Hosting;

namespace URUI.Effect.Tests
{
    public sealed class PackageIndependenceTests
    {
        [Test]
        public void PackageHasOnlyAllowedRuntimeDependencies()
        {
            var type = Type.GetType("URUI.Effect.EffectModule, URUI.Effect");
            Assert.That(type, Is.Not.Null, "独立项目应能加载 URUI.Effect 的公共入口");
            var host = new GameObject("Standalone effect contract");
            IDisposable module = null;
            try
            {
                var surface = host.AddComponent<Surface>();
                var document = host.AddComponent<Document>();
                document.Surface = surface;
                module = (IDisposable)Activator.CreateInstance(type, new object[] { document });
                foreach (var reference in type.Assembly.GetReferencedAssemblies())
                {
                    Assert.That(reference.Name.StartsWith("FUI", StringComparison.Ordinal), Is.False);
                    Assert.That(reference.Name.StartsWith("Coffee", StringComparison.Ordinal), Is.False);
                    Assert.That(reference.Name, Is.Not.EqualTo("Unity.ugui"));
                    Assert.That(reference.Name, Is.Not.EqualTo("Unity.TextMeshPro"));
                }
                var shader = UnityEngine.Resources.Load<Shader>("URUI.Effect/Particle");
                Assert.That(shader && shader.isSupported, Is.True, "通用效果资源应可独立加载");
            }
            finally
            {
                module?.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator ExistingEffectGalleryRunsWithoutFui()
        {
            var type = Type.GetType("URUI.Effect.Examples.EffectGallery, URUI.Effect.Gallery");
            Assert.That(type, Is.Not.Null, "通用样例程序集应能在干净项目运行");
            var host = new GameObject("Standalone gallery");
            var cameraObject = new GameObject("Standalone gallery camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            var target = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGBHalf);
            target.Create();
            camera.targetTexture = target;
            var read = new Texture2D(1, 1);
            try
            {
                var gallery = host.AddComponent(type);
                type.GetField("html").SetValue(gallery, UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Gallery/GalleryDocument"));
                type.GetField("style").SetValue(gallery, UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Gallery/GalleryStyle"));
                type.GetField("white").SetValue(gallery, UnityEngine.Resources.Load<Texture2D>("URUI.Effect.Gallery/white"));
                type.GetField("shader").SetValue(gallery, UnityEngine.Resources.Load<Shader>("URUI.Effect.Gallery/GalleryProgram"));
                foreach (var asset in new[] { ("source", "input"), ("pattern", "pattern"), ("gradient", "gradient-lut") })
                {
                    type.GetField(asset.Item1)?.SetValue(gallery, UnityEngine.Resources.Load<Texture2D>("URUI.Effect.Gallery/" + asset.Item2));
                }
                for (var index = 0; index < 4; index++)
                {
                    yield return null;
                }
                var document = host.GetComponent<Document>();
                Assert.That(document && document.IsLoaded, Is.True);
                Assert.That(document.Query(className: "card").Count, Is.EqualTo(83), "完整的13组模式图库");
                Assert.That(document.Q<URUI.Effect.Elements.ParticlesElement>("particle-demo").CurrentSource, Is.EqualTo("sample-particles"));
                Assert.That(type.GetProperty("Playback"), Is.Not.Null, "图库必须包含真实的播放控制");
                var playback = (EffectPlayback)type.GetProperty("Playback").GetValue(gallery);
                document.Q("pause").SendEvent(new URUI.Events.ClickEvent());
                Assert.That(playback.IsPaused, Is.True);
                document.Q("resume").SendEvent(new URUI.Events.ClickEvent());
                Assert.That(playback.IsPaused, Is.False);
                document.Q<URUI.Controls.Slider>("seek").Value = .75f;
                Assert.That(playback.Time, Is.EqualTo(1.5).Within(.001));
                host.GetComponent<Surface>().Viewport = new Vector2Int(1280, 720);
                document.SynchronizeLayout();
                camera.Render();
                var output = System.IO.Path.Combine(Environment.GetEnvironmentVariable("URUI_EFFECT_CAPTURE_OUTPUT") ?? Application.temporaryCachePath, QualitySettings.activeColorSpace.ToString());
                System.IO.Directory.CreateDirectory(output);
                var capture = new Texture2D(1280, 720, TextureFormat.RGBAFloat, false, true);
                var saved = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    capture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    capture.Apply();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(output, "Gallery.png"), capture.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = saved;
                    UnityEngine.Object.DestroyImmediate(capture);
                }
                var bounds = new System.Collections.Generic.List<string>();
                foreach (var id in new[] { "browser", "browser-original", "browser-gray", "browser-color", "browser-blur" })
                {
                    var r = document.Q(id).WorldBound;
                    bounds.Add("\"" + id + "\":[" + string.Join(",", new[] { r.x, r.y, r.width, r.height }.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]");
                }
                System.IO.File.WriteAllText(System.IO.Path.Combine(output, "Gallery-bounds.json"), "{" + string.Join(",", bounds) + "}");
                var point = document.Query(className: "small")[0].WorldBound.center;
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect((int)point.x, target.height - (int)point.y - 1, 1, 1), 0, 0);
                    read.Apply();
                }
                finally
                {
                    RenderTexture.active = previous;
                }
                Assert.That(read.GetPixel(0, 0).a, Is.GreaterThan(.5f), "样例应实际提交可见像素");
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(read);
            }
        }
    }
}
