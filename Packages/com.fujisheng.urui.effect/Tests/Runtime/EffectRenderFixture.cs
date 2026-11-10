using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using URUI.Documents;
using URUI.Hosting;
using URUI.Resources;
using URUI.Styles;

namespace URUI.Effect.Tests
{
    internal sealed class EffectRenderFixture : IDisposable, IEffectResourceResolver
    {
        readonly GameObject host, cameraObject;
        internal readonly Document Document;
        internal readonly Surface Surface;
        internal readonly Camera Camera;
        internal readonly Texture2D Source, Pattern;
        internal readonly Texture2D GradientInput;
        internal readonly Texture2D Read;
        internal readonly RenderTexture Target;
        internal readonly EffectModule Module;
        internal readonly string Output;
        internal int ImageAcquisitions, ImageReleases;
        internal bool Missing;
        internal readonly System.Collections.Generic.Dictionary<string,GameObject> ParticleSources=new();
        internal EffectRenderFixture()
        {
            var constructor = typeof(EffectModule).GetConstructor(new[] { typeof(Document), typeof(IEffectResourceResolver) });
            Assert.That(constructor, Is.Not.Null, "效果模块必须接收通用纹理/粒子解析器");
            host = new GameObject("Effect reference fixture");
            cameraObject = new GameObject("Effect reference camera", typeof(Camera)); cameraObject.tag = "MainCamera";
            Camera = cameraObject.GetComponent<Camera>(); Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.04f, .06f, .08f, 0);
            Target = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBHalf); Target.Create(); Camera.targetTexture = Target;
            Read = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
            Source = EffectColorTests.InputTexture();
            Pattern = new Texture2D(64, 64, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[4096];
            for (var y = 0; y < 64; y++) for (var x = 0; x < 64; x++)
            { var u=x/63f; var v=y/63f; pixels[y*64+x]=new Color(u,v,1-u,Mathf.Repeat(u+v*.35f,1)); }
            Pattern.SetPixels(pixels); Pattern.Apply();
            GradientInput=new Texture2D(256,1,TextureFormat.RGBA32,false,false) {wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear};
            var gradient=EffectGradientTests.ReferenceGradient();var colors=new Color32[256];
            for(var i=0;i<256;i++)colors[i]=gradient.Evaluate(i/255f);GradientInput.SetPixels32(colors);GradientInput.Apply();
            Surface = host.AddComponent<Surface>(); Surface.Viewport = new Vector2Int(256, 256); Surface.Input.UseUnityInput = false;
            Document = host.AddComponent<Document>(); Document.Surface = Surface;
            Module = (EffectModule)constructor.Invoke(new object[] { Document, this });
            Document.Images = new[] { new ImageAsset("input.png", Source) };
            Document.TextResources = new[] { new TextResource("/effect.css", UnityEngine.Resources.Load<TextAsset>("URUI.Effect/Effect")) };
            Output = Path.Combine(Environment.GetEnvironmentVariable("URUI_EFFECT_CAPTURE_OUTPUT") ??
                Path.GetFullPath(Path.Combine(Application.dataPath, "../../../candidate")), QualitySettings.activeColorSpace.ToString());
            Directory.CreateDirectory(Output);
        }
        public ImageLease AcquireImage(ComputedImageValue image)
        {
            if (Missing) return null;
            Assert.That(image.Kind, Is.EqualTo(ComputedImageKind.Url));
            Assert.That(image.Url, Is.EqualTo("pattern.png").Or.EqualTo("gradient-lut.png"));
            ImageAcquisitions++; return new ImageLease(image.Url=="gradient-lut.png" ? GradientInput : Pattern, () => ImageReleases++);
        }
        public GameObject ResolveParticles(string source) { return ParticleSources.TryGetValue(source,out var value)?value:null; }
        internal void Load(string css, string content = "<img id='subject' src='input.png'>", string selector = "#subject")
        {
            Document.LoadHtml("<!doctype html><html><head><link rel='stylesheet' href='/effect.css'><style>body{margin:0;}" +
                selector + "{position:absolute;left:64px;top:64px;width:128px;height:128px;--urui-effect:ui;" + css +
                "}</style></head><body>" + content + "</body></html>", "/effects.html");
        }
        internal Color[] Render(string id = null)
        {
            Document.SynchronizeLayout(); Camera.Render(); var previous=RenderTexture.active;
            try { RenderTexture.active=Target; Read.ReadPixels(new Rect(0,0,256,256),0,0); Read.Apply(); }
            finally { RenderTexture.active=previous; }
            if (id != null) File.WriteAllBytes(Path.Combine(Output,id+".png"),Read.EncodeToPNG());
            return Read.GetPixels();
        }
        internal void Compare(string id)
        {
            var actual=Render(id);
            var data=UnityEngine.Resources.Load<TextAsset>("URUI.Effect.Tests/Reference/"+QualitySettings.activeColorSpace+"/"+id);
            Assert.That(data,Is.Not.Null,"缺少固定参考："+id);
            var expected=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            try
            {
                Assert.That(expected.LoadImage(data.bytes),Is.True); var original=expected.GetPixels();
                double sum=0; float max=0; bool finite=true;
                for(var i=0;i<actual.Length;i++) for(var c=0;c<4;c++)
                { finite &= float.IsFinite(actual[i][c]); var error=Mathf.Abs(Mathf.Clamp01(actual[i][c])-original[i][c]);max=Mathf.Max(max,error);sum+=error; }
                Assert.That(finite,Is.True,id); Assert.That(max,Is.LessThanOrEqualTo(4f/255),id);
                Assert.That(sum/(actual.Length*4),Is.LessThanOrEqualTo(.5/255),id);
            }
            finally { UnityEngine.Object.DestroyImmediate(expected); }
        }
        internal static string Kebab(string value) => System.Text.RegularExpressions.Regex.Replace(value,"(?<=[a-z0-9])([A-Z])","-$1").ToLowerInvariant();
        public void Dispose()
        {
            Module.Dispose(); Camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(cameraObject);
            Target.Release(); UnityEngine.Object.DestroyImmediate(Target); UnityEngine.Object.DestroyImmediate(Read);
            GpuRetirement.Retire(()=> { UnityEngine.Object.Destroy(Source); UnityEngine.Object.Destroy(Pattern); UnityEngine.Object.Destroy(GradientInput); });
        }
    }
}
