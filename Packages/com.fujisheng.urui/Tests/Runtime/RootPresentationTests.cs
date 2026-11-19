using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Documents;
using URUI.Hosting;
using URUI.Internal.Hosting;
using URUI.Internal.Rendering;

namespace URUI.Tests
{
    public sealed class RootPresentationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static FieldInfo Field(string name) => typeof(RenderHost).GetField(name, Private);
        static void Clear(RenderTexture texture, Color color)
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                GL.Clear(false, true, color);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }
        static Color Read(RenderTexture texture)
        {
            var previous = RenderTexture.active;
            var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = texture;
                read.ReadPixels(new Rect(8, 8, 1, 1), 0, 0);
                read.Apply();
                return read.GetPixel(0, 0);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(read);
            }
        }
        [Test]
        public void PendingEncodedRootResolvesToOutputWithoutOverwritingWorkingCopy()
        {
            var cameraObject = new GameObject("Root presentation camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var root = new GameObject("Root presentation Surface");
            var output = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                output.Create();
                cameraObject.GetComponent<Camera>().targetTexture = output;
                var surface = root.AddComponent<Surface>();
                surface.Viewport = new Vector2Int(16, 16);
                surface.Input.UseUnityInput = false;
                var document = root.AddComponent<Document>();
                document.Surface = surface;
                document.LoadHtml("<!doctype html><html><head><style>body{margin:0;}div{width:16px;height:16px;background:red;}</style></head><body><div></div></body></html>", "/root-presentation.html");
                document.SynchronizeLayout();
                var host = (RenderHost)typeof(Document).GetField("host", Private).GetValue(document);
                typeof(RenderHost).GetMethod("EnsureLayerProgram", Private).Invoke(host, null);
                var pool = (EffectTargetPool)Field("effectTargets").GetValue(host);
                var targets = (Dictionary<(ulong, int), EffectTargetPool.Lease>)Field("preparedLayerTargets").GetValue(host);
                if (!targets.ContainsKey((0, -1)))
                {
                    targets.Add((0, -1), pool.Rent(16, 16, RenderTextureFormat.ARGB32, true));
                }
                targets.Add((0, -2), pool.Rent(16, 16, RenderTextureFormat.ARGB32, true));
                Clear(targets[(0, -1)].Texture, Color.red);
                Clear(targets[(0, -2)].Texture, Color.green);
                Clear(output, Color.blue);
                var commands = (CommandBuffer)Field("buffer").GetValue(host);
                commands.Clear();
                Field("submissionTarget").SetValue(host, (RenderTargetIdentifier?)new RenderTargetIdentifier(output));
                Field("submissionViewport").SetValue(host, new Rect(0, 0, 16, 16));
                Field("rootTextureRequired").SetValue(host, true);
                Field("encodedRasterActive").SetValue(host, true);
                Field("activeLayer").SetValue(host, 0UL);
                typeof(RenderHost).GetMethod("PresentScreenRoot", Private).Invoke(host, null);
                Graphics.ExecuteCommandBuffer(commands);
                Assert.That(Read(output).r, Is.GreaterThan(.99f));
                Assert.That(Read(targets[(0, -2)].Texture).g, Is.GreaterThan(.99f), "最终解析应直接写入输出，不应先重写工作副本再全屏拷贝。");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraObject);
                output.Release();
                Object.DestroyImmediate(output);
            }
        }
    }
}
