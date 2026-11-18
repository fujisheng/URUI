using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Editor.Inspection;

namespace URUI.Authoring.Tests
{
    public sealed class PreviewCheckPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline() => new PreviewCheckPipeline();
    }
    public sealed class PreviewCheckPipeline : RenderPipeline
    {
        protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }
    }
    public static class PreviewRenderValidation
    {
        public static void Run()
        {
            var original = GraphicsSettings.defaultRenderPipeline;
            var quality = QualitySettings.renderPipeline;
            var pipeline = ScriptableObject.CreateInstance<PreviewCheckPipelineAsset>();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = null;
            EditorApplication.delayCall += () =>
            {
                string result;
                bool passed = false;
                try
                {
                    if (GraphicsSettings.currentRenderPipeline != pipeline)
                        throw new Exception("SRP test was not active.");
                    using (var preview = RuntimeInspectionBridge.CreatePreview(64, 64))
                    {
                        preview.Document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}#red{width:64px;height:64px;background-color:red;}</style></head><body><div id='red'></div></body></html>", "/preview.html");
                        using (var capture = preview.Capture())
                        {
                            var pixel = capture.Texture.GetPixel(32, 32);
                            if (pixel.r < .7f || pixel.g > .2f || pixel.b > .2f)
                                throw new Exception("SRP preview did not render the page; center=" + pixel);
                            Directory.CreateDirectory("Evidence");
                            File.WriteAllBytes("Evidence/srp-preview.png", capture.Texture.EncodeToPNG());
                        }
                        bool released = false;
                        var image = new Texture2D(1, 1);
                        image.SetPixel(0, 0, Color.blue);
                        image.Apply();
                        preview.Document.ImageResolver = source => new URUI.Resources.ImageLease(image, () => { released = true; UnityEngine.Object.DestroyImmediate(image); });
                        preview.Document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}</style></head><body><img src='/leased.png' style='width:64px;height:64px;'></body></html>", "/preview.html");
                        using (var capture = preview.Capture())
                        {
                            var pixel = capture.Texture.GetPixel(32, 32);
                            if (released || pixel.b < .7f || pixel.r > .2f || pixel.g > .2f)
                                throw new Exception("SRP did not retain and draw the owned image lease; center=" + pixel);
                        }
                    }
                    result = "PASS: preview pixels render with an active SRP";
                    passed = true;
                }
                catch (Exception error) { result = error.ToString(); }
                finally
                {
                    GraphicsSettings.defaultRenderPipeline = original;
                    QualitySettings.renderPipeline = quality;
                    UnityEngine.Object.DestroyImmediate(pipeline);
                }
                Directory.CreateDirectory("Evidence");
                File.WriteAllText("Evidence/preview-render.txt", result);
                Debug.Log(result);
                EditorApplication.Exit(passed ? 0 : 1);
            };
        }
    }
}
