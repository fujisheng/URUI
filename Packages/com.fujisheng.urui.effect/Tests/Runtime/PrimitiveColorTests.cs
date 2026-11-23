using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Resources;

namespace URUI.Effect.Tests
{
    public sealed class PrimitiveColorTests
    {
        [UnityTest]
        public IEnumerator TransparentPrimitivesImagesAndNestedGroupsUseProjectColorSpace() => Verify(0);
        [UnityTest]
        public IEnumerator BatchedGeometryUsesProjectColorSpace() => Verify(1);
        [UnityTest]
        public IEnumerator TransformedGeometryUsesProjectColorSpace() => Verify(2);

        static IEnumerator Verify(int mode)
        {
            var host = new GameObject("Project color blend document");
            var cameraObject = new GameObject("Project color blend camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.allowHDR = false;
            camera.allowMSAA = false;
            var target = new RenderTexture(128, 80, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            camera.targetTexture = target;
            var image = new Texture2D(1, 1, TextureFormat.RGBA32, false, false);
            image.SetPixels32(new[] { new Color32(192, 96, 32, 128) });
            image.Apply();
            var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                var document = EffectHtmlFixtureAssets.Create(host);
                document.Surface.Viewport = new Vector2Int(128, 80);
                document.Surface.Input.UseUnityInput = false;
                if (mode == 0)
                {
                    document.Images = new[] { new ImageAsset("/half.png", image) };
                }
                document.LoadHtml("<!doctype html><html><head><style>" +
                    "html,body{margin:0;width:128px;height:80px;background-color:#204060;}" +
                    ".box{position:absolute;top:4px;width:16px;height:16px;}" +
                    ".fill{position:absolute;left:0;top:0;width:16px;height:16px;}" +
                    ".half{background-color:rgba(192,96,32,.5);}.group{opacity:.5;}" +
                    (mode == 2 ? ".box{transform:translate(2px,0px);}" : "") + "</style></head><body>" +
                    "<div class='box half' style='left:4px;'></div>" +
                    (mode > 0 ? "" : "<img class='box' style='left:24px;' src='/half.png'>" +
                    "<div class='box group' style='left:44px;background-color:#c06020;'></div>" +
                    "<div class='box group' style='left:64px;'><div class='fill half'></div></div>" +
                    "<div class='box group' style='left:84px;'><div class='fill half'></div>" +
                    "<div class='fill' style='background-color:rgba(32,160,192,.5);'></div></div>" +
                    "<div class='box group' style='left:104px;'><img class='fill' src='/half.png'></div>") +
                    "</body></html>", "/source-over.html");
                const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var renderHost = typeof(global::URUI.Documents.Document).GetField("host", fields).GetValue(document);
                for (var frame = 0; frame < 4; frame++)
                {
                    yield return null;
                }
                camera.Render();
                Assert.IsTrue((bool)renderHost.GetType().GetProperty("ShaderContractActive", fields).GetValue(renderHost));
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    Color ProjectColor(Color32 value)
                    {
                        Color color = value;
                        return QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;
                    }

                    var background = ProjectColor(new Color32(32, 64, 96, 255));
                    var source = ProjectColor(new Color32(192, 96, 32, 255));
                    var overlay = ProjectColor(new Color32(32, 160, 192, 255));
                    var half = Color.Lerp(background, source, .5f);
                    var quarter = Color.Lerp(background, source, .25f);
                    var expected = new[]
                    {
                        half, half, half, quarter,
                        background * .625f + source * .125f + overlay * .25f,
                        quarter
                    };
                    var names = new[] { "rgba", "image", "group", "nested rgba", "overlapping rgba", "nested image" };
                    for (var index = 0; index < (mode == 0 ? expected.Length : 1); index++)
                    {
                        read.ReadPixels(new Rect(10 + index * 20, 69, 1, 1), 0, 0);
                        read.Apply();
                        var actual = (Color32)read.GetPixel(0, 0);
                        var pixel = (Color32)(target.sRGB ? expected[index].gamma : expected[index]);
                        Assert.That((int)actual.r, Is.EqualTo((int)pixel.r).Within(2), names[index] + " red");
                        Assert.That((int)actual.g, Is.EqualTo((int)pixel.g).Within(2), names[index] + " green");
                        Assert.That((int)actual.b, Is.EqualTo((int)pixel.b).Within(2), names[index] + " blue");
                        Assert.AreEqual(255, actual.a, names[index] + " alpha");
                    }
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(read);
            }
        }
    }
}
