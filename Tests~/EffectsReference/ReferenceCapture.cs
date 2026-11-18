using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Coffee.UIEffects;
using Coffee.UIEffectInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace URUI.Effect.Reference
{
    /// <summary>只在隔离参考项目中运行原插件；生成真实 GPU 图像和参数，不参与生产包。</summary>
    public static class ReferenceCapture
    {
        [Serializable]
        sealed class Capture
        {
            public string id, property, mode;
            public float progress;
            public int value;
        }
        [Serializable]
        sealed class Report
        {
            public string version = "5.8.4";
            public string colorSpace, graphicsDevice;
            public int width = 256, height = 256;
            public List<Capture> captures = new List<Capture>();
        }
        static readonly string[] Properties = {
            "toneFilter", "colorFilter", "samplingFilter", "transitionFilter", "targetMode",
            "blendType", "shadowMode", "gradationMode", "gradationColorFilter", "edgeMode",
            "detailFilter", "patternArea", "flip"
        };

        public static void CaptureAll(string output)
        {
            Directory.CreateDirectory(output);
            var report = new Report { colorSpace = QualitySettings.activeColorSpace.ToString(),
                graphicsDevice = SystemInfo.graphicsDeviceType.ToString() };
            var source = CreateTexture(false);
            var pattern = CreateTexture(true);
            var cameraObject = new GameObject("Original UIEffect reference camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 128;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.04f, .06f, .08f, 0);
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.cullingMask = 1 << 30;
            var target = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBHalf);
            target.Create(); camera.targetTexture = target;
            var canvasObject = new GameObject("Reference canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.layer = 30;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10;
            var read = new Texture2D(256, 256, TextureFormat.RGBAFloat, false, true);
            try
            {
                File.WriteAllBytes(Path.Combine(output, "Input.png"), source.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(output, "Pattern.png"), pattern.EncodeToPNG());
                CaptureCase("Neutral", null, 0, .5f);
                foreach (var name in Properties)
                {
                    var property = typeof(UIEffect).GetProperty(name);
                    var values = name == "flip" ? FlipValues() : Enum.GetValues(property.PropertyType);
                    foreach (var value in values)
                    {
                        var number = Convert.ToInt32(value);
                        var mode = name == "flip" ? number.ToString() : value.ToString();
                        var family = property.PropertyType.Name;
                        var phases = name == "transitionFilter" ? new[] { 0f, .25f, .5f, .75f, 1f } :
                            name == "samplingFilter" || name == "detailFilter" || name == "edgeMode" ? new[] { 0f, .5f, 1f } : new[] { .5f };
                        foreach (var phase in phases)
                        {
                            var id = family + "." + mode + "." + Mathf.RoundToInt(phase * 100).ToString("000");
                            CaptureCase(id, name, number, phase);
                            report.captures.Add(new Capture { id = id, property = name, mode = mode,
                                value = number, progress = phase });
                        }
                    }
                }
                Extra("Combination.TransitionDetailEdge", e => { e.transitionFilter=TransitionFilter.Shiny; e.detailFilter=DetailFilter.Additive; e.edgeMode=EdgeMode.Plain; e.toneFilter=ToneFilter.Negative; });
                Extra("Combination.RgbTarget", e => { e.samplingFilter=SamplingFilter.RgbShift; e.transitionFilter=TransitionFilter.Shiny; e.detailFilter=DetailFilter.Multiply; e.edgeMode=EdgeMode.Plain; e.targetMode=TargetMode.Hue; e.targetRange=.35f; e.colorFilter=ColorFilter.Replace; });
                Extra("Combination.MeltSampling", e => { e.samplingFilter=SamplingFilter.BlurFast; e.transitionFilter=TransitionFilter.Melt; });
                Extra("Parameters.Transition", e => { e.transitionFilter=TransitionFilter.Pattern; e.transitionTextureScale=new Vector2(2,.8f); e.transitionTextureOffset=new Vector2(.2f,-.1f); e.transitionRotation=37; e.transitionKeepAspectRatio=false; e.transitionReverse=true; e.transitionPatternReverse=true; e.transitionRange=new MinMax01(.15f,.8f); e.edgeMode=EdgeMode.Plain; e.patternArea=PatternArea.Edge; });
                Extra("Parameters.Detail", e => { e.detailFilter=DetailFilter.Masking; e.detailTextureScale=new Vector2(2,.8f); e.detailTextureOffset=new Vector2(.2f,-.1f); e.detailThreshold=new MinMax01(.3f,.7f); });
                Extra("Parameters.Edge", e => { e.edgeMode=EdgeMode.Shiny; e.edgeShinyRate=.35f; e.edgeShinyWidth=.7f; e.edgeWidth=.2f; e.edgeColorFilter=ColorFilter.HsvModifier; e.edgeColor=new Color(-.1f,.2f,-.1f,.7f); e.edgeColorGlow=true; });
                Extra("Time.Transition", e => { e.transitionFilter=TransitionFilter.Shiny; e.transitionAutoPlaySpeed=.35f; e.transitionTextureSpeed=new Vector2(.2f,-.1f); },1.25f);
                Extra("Time.Detail", e => { e.detailFilter=DetailFilter.Multiply; e.detailTextureSpeed=new Vector2(.2f,.1f); },1.25f);
                Extra("Time.Edge", e => { e.edgeMode=EdgeMode.Shiny; e.edgeShinyAutoPlaySpeed=-.7f; },1.25f);
                Extra("Combination.MeltFlipV",e=>{e.transitionFilter=TransitionFilter.Melt;e.flip=Flip.Vertical;},0,"e06");
                Extra("Combination.MeltFlipVEffect",e=>{e.transitionFilter=TransitionFilter.Melt;e.flip=Flip.Vertical|Flip.Effect;},0,"e06");
                Extra("Combination.GradientShadowFlip",e=>{e.gradationMode=GradationMode.Diagonal;e.shadowMode=ShadowMode.Outline8;e.flip=Flip.Horizontal|Flip.Effect|Flip.Shadow;},0,"e06");
                CaptureCase("Time.Repeat",null,0,.5f,e => { e.transitionFilter=TransitionFilter.Shiny; e.transitionAutoPlaySpeed=.35f; e.transitionTextureSpeed=new Vector2(.2f,-.1f); },1.25f);
                File.WriteAllText(Path.Combine(output, "captures.json"), JsonUtility.ToJson(report, true));
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(read);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(pattern);
            }

            void Extra(string id, Action<UIEffect> configure, float time=0, string family="e05")
            {
                CaptureCase(id,null,0,.5f,configure,time);
                report.captures.Add(new Capture {id=id,property=family,mode=id,value=0,progress=.5f});
            }

            void CaptureCase(string id, string propertyName, int value, float phase, Action<UIEffect> configure=null, float time=0)
            {
                var item = new GameObject(id, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                item.layer = 30; item.transform.SetParent(canvasObject.transform, false);
                var rect = item.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(128, 128);
                var image = item.GetComponent<RawImage>(); image.texture = source;
                Material clockMaterial = null;
                UIEffect effect;
                if (id.StartsWith("Time."))
                {
                    var shader=Shader.Find("Hidden/URUI/Reference Clock (UIEffect)");
                    if (!shader) throw new InvalidOperationException("缺少只替换时钟输入的锁定参考 Shader。");
                    clockMaterial=new Material(shader); image.material=clockMaterial;
                    var timed=item.AddComponent<TimedReferenceEffect>();timed.ReferenceTime=time;effect=timed;
                }
                else { effect=item.AddComponent<UIEffect>(); }
                try
                {
                    effect.toneIntensity = phase; effect.colorIntensity = phase;
                    effect.samplingIntensity = phase; effect.samplingWidth = 1;
                    effect.transitionRate = phase; effect.transitionTexture = pattern;
                    effect.transitionWidth = .25f; effect.transitionSoftness = .4f;
                    effect.transitionAutoPlaySpeed = 0;
                    effect.detailTexture = pattern; effect.detailIntensity = phase;
                    effect.color = new Color(.7f, .2f, .4f, .8f);
                    effect.shadowDistance = new Vector2(5, -4); effect.shadowIteration = 2;
                    effect.gradationIntensity = phase;
                    effect.gradationColor1 = new Color(.9f, .1f, .2f, 1);
                    effect.gradationColor2 = new Color(.1f, .8f, .3f, .7f);
                    effect.gradationColor3 = new Color(.2f, .3f, .9f, 1);
                    effect.gradationColor4 = new Color(.9f, .7f, .2f, .4f);
                    effect.edgeWidth = .5f; effect.edgeShinyRate = phase;
                    effect.edgeShinyAutoPlaySpeed = 0;
                    effect.SetGradientKeys(new[] { new GradientColorKey(Color.red, 0),
                        new GradientColorKey(Color.green, .4f), new GradientColorKey(Color.blue, 1) },
                        new[] { new GradientAlphaKey(.3f, 0), new GradientAlphaKey(1, 1) });
                    if (propertyName == "targetMode")
                    { effect.colorFilter = ColorFilter.Replace; effect.targetRange = .5f; }
                    if (propertyName == "gradationColorFilter") { effect.gradationMode = GradationMode.HorizontalGradient; }
                    if (propertyName == "patternArea")
                    { effect.transitionFilter = TransitionFilter.Pattern; effect.edgeMode = EdgeMode.Plain; }
                    if (propertyName == "flip")
                    { effect.transitionFilter = TransitionFilter.Shiny; effect.gradationMode = GradationMode.Diagonal; }
                    if (propertyName != null)
                    {
                        var property = typeof(UIEffect).GetProperty(propertyName);
                        if(propertyName=="blendType")
                        {
                            // 锁定版本的 public setter 没有赋值 m_BlendType；使用真实资产/Inspector 的字段初始化路径。
                            typeof(UIEffect).GetField("m_BlendType",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(effect,(BlendType)value);
                            effect.enabled=false;effect.enabled=true;
                        }
                        else { property.SetValue(effect, Enum.ToObject(property.PropertyType, value)); }
                    }
                    configure?.Invoke(effect);
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); read.Apply();
                        File.WriteAllBytes(Path.Combine(output, id + ".png"), read.EncodeToPNG());
                        File.WriteAllBytes(Path.Combine(output, id + ".exr"), read.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat));
                    }
                    finally { RenderTexture.active = previous; }
                }
                finally { UnityEngine.Object.DestroyImmediate(item); if (clockMaterial) UnityEngine.Object.DestroyImmediate(clockMaterial); }
            }
        }

        static Array FlipValues()
        {
            var values = new Flip[16];
            for (var index = 0; index < values.Length; index++) { values[index] = (Flip)index; }
            return values;
        }

        static Texture2D CreateTexture(bool pattern)
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            texture.wrapMode = pattern ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            var pixels = new Color[4096];
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var u = x / 63f; var v = y / 63f;
                    var alpha = Mathf.Clamp01((.47f - Vector2.Distance(new Vector2(u, v), new Vector2(.5f, .5f))) * 40);
                    pixels[y * 64 + x] = pattern ? new Color(u, v, 1 - u, Mathf.Repeat(u + v * .35f, 1))
                        : new Color(.15f + .7f * u, .1f + .4f * v, .8f - .5f * u, alpha * .8f);
                }
            }
            texture.SetPixels(pixels); texture.Apply(); return texture;
        }
    }
}
