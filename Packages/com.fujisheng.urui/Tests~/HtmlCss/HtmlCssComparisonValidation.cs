#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using URUI.Documents;
using URUI.Elements;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.HtmlCssValidation
{
    /// <summary>
    /// 在隔离项目中采集同源输入；不把验证旁路模块分发到生产运行时。
    /// </summary>
    public sealed class HtmlCssComparisonValidation
    {
        [Serializable]
        sealed class Manifest
        {
            public string root;
            public string nativeSha256;
            public Case[] cases;
            public Viewport[] viewports;
            public string previewSha256;
        }
        [Serializable]
        sealed class Case
        {
            public string name;
            public string uri;
            public string path;
            public bool preview;
            public Resource[] resources;
            public Region[] regions;
        }
        [Serializable]
        sealed class Resource
        {
            public string uri;
            public string path;
            public string sha256;
            public bool font;
        }
        [Serializable]
        sealed class Region
        {
            public string name;
            public int[] rect;
        }
        [Serializable]
        sealed class Viewport
        {
            public int width;
            public int height;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct TextLine
        {
            public float x;
            public float y;
            public float width;
            public float height;
            public float baseline;
        }
        sealed class Clock : URUI.Animation.IUIClock
        {
            public double Now => 0;
            public float Delta => 0;
        }
        [DllImport("URUIValidation", CallingConvention = CallingConvention.Cdecl)]
        static extern int urui_validation_text_lines([MarshalAs(UnmanagedType.LPUTF8Str)] string source,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string id, [Out] TextLine[] lines, int capacity);

        [UnityTest]
        public IEnumerator CaptureAll()
        {
            Assert.AreNotEqual(UnityEngine.Rendering.GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType);
            var input = Environment.GetEnvironmentVariable("URUI_HTML_CSS_INPUTS");
            var output = Environment.GetEnvironmentVariable("URUI_HTML_CSS_OUTPUT");
            Assert.IsFalse(string.IsNullOrEmpty(input));
            Assert.IsFalse(string.IsNullOrEmpty(output));
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(input));
            Assert.AreEqual(RuntimePlatform.OSXEditor, Application.platform, "当前采集入口只验证 macOS Editor；其他平台另行验收。");
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(Document).Assembly).resolvedPath;
            var nativeHash = Hash(File.ReadAllBytes(Path.Combine(package, "Runtime/Plugins/macOS/libURUIBridge.dylib")));
            Assert.AreEqual(manifest.nativeSha256, nativeHash, "隔离项目插件与输入清单不一致。");
            var cameraObject = new GameObject("HTML CSS comparison camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.white;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            var root = new GameObject("HTML CSS comparison Surface");
            var surface = root.AddComponent<Surface>();
            surface.Input.UseUnityInput = false;
            surface.UIClock = new Clock();
            var document = root.AddComponent<Document>();
            document.Surface = surface;
            RenderTexture target = null;
            var temporary = new List<TextAsset>();
            try
            {
                foreach (var viewport in manifest.viewports)
                {
                    surface.Viewport = new Vector2Int(viewport.width, viewport.height);
                    target = new RenderTexture(viewport.width, viewport.height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    target.Create();
                    camera.targetTexture = target;
                    foreach (var fixture in manifest.cases)
                    {
                        var resources = new List<TextResource>();
                        var inputs = new Dictionary<string, object>();
                        var fonts = new Dictionary<string, object>();
                        foreach (var resource in fixture.resources)
                        {
                            var bytes = File.ReadAllBytes(Path.Combine(manifest.root, resource.path));
                            Assert.AreEqual(resource.sha256, Hash(bytes), resource.uri + " source changed");
                            TextAsset asset;
                            if (resource.font)
                            {
                                var name = Path.GetFileName(resource.path);
                                if (name.EndsWith(".bytes", StringComparison.Ordinal))
                                {
                                    name = name.Substring(0, name.Length - 6);
                                }
                                asset = UnityEngine.Resources.Load<TextAsset>("URUI/Fonts/" + name);
                                Assert.IsNotNull(asset, resource.uri);
                                Assert.AreEqual(resource.sha256, Hash(asset.bytes));
                                fonts.Add(resource.uri, resource.sha256);
                            }
                            else
                            {
                                asset = new TextAsset(Encoding.UTF8.GetString(bytes));
                                temporary.Add(asset);
                            }
                            resources.Add(new TextResource(resource.uri, asset));
                            inputs.Add(resource.uri, resource.sha256);
                        }
                        document.TextResources = resources.ToArray();
                        document.LoadHtml(File.ReadAllText(Path.Combine(manifest.root, fixture.path)), fixture.uri);
                        yield return null;
                        yield return null;
                        document.SynchronizeLayout();
                        camera.Render();
                        var directory = Path.Combine(output, fixture.name + "-" + viewport.width + "x" + viewport.height);
                        Directory.CreateDirectory(directory);
                        var screenshot = Capture(target);
                        File.WriteAllBytes(Path.Combine(directory, "unity.png"), screenshot);
                        var nodes = new Dictionary<string, object>();
                        foreach (var element in document.Query())
                        {
                            var name = element.GetAttribute("data-test");
                            if (name == null)
                            {
                                continue;
                            }
                            Assert.IsFalse(nodes.ContainsKey(name));
                            Assert.IsFalse(string.IsNullOrEmpty(element.Name));
                            var computed = document.Styles.GetComputed(element);
                            var lines = ReadLines(fixture.uri, element.Name);
                            var parent = element.Parent;
                            while (parent != null && !parent.HasAttribute("data-test"))
                            {
                                parent = parent.Parent;
                            }
                            nodes.Add(name, new Dictionary<string, object>
                            {
                                ["tag"] = element.TagName,
                                ["parent"] = parent?.GetAttribute("data-test"),
                                ["rect"] = RectValues(element.WorldBound),
                                ["text"] = Text(element),
                                ["lines"] = lines,
                                ["styles"] = new Dictionary<string, object>
                                {
                                    ["font-size"] = computed.GetPixels("font-size"),
                                    ["opacity"] = computed.GetNumber("opacity"),
                                    ["display"] = element.ResolvedStyle.Get("display"),
                                    ["visibility"] = element.ResolvedStyle.Get("visibility")
                                }
                            });
                        }
                        var regions = new Dictionary<string, object>();
                        foreach (var region in fixture.regions)
                        {
                            regions.Add(region.name, region.rect);
                        }
                        var snapshot = new Dictionary<string, object>
                        {
                            ["schemaVersion"] = 1,
                            ["runtime"] = "unity",
                            ["time"] = 0,
                            ["viewport"] = new Dictionary<string, object>
                            {
                                ["width"] = viewport.width,
                                ["height"] = viewport.height,
                                ["dpr"] = 1
                            },
                            ["inputs"] = inputs,
                            ["fonts"] = fonts,
                            ["fontsLoaded"] = true,
                            ["nodes"] = nodes,
                            ["flatRegions"] = regions,
                            ["screenshot"] = "unity.png",
                            ["screenshotSha256"] = Hash(screenshot),
                            ["engine"] = Application.unityVersion,
                            ["graphics"] = SystemInfo.graphicsDeviceType.ToString(),
                            ["colorSpace"] = QualitySettings.activeColorSpace.ToString(),
                            ["nativePluginSha256"] = nativeHash,
                            ["textBounds"] = "CSS line-height boxes; renderer baseline and actual advances",
                            ["extensions"] = fixture.preview ? "authored-control-preview-fallback" : "none"
                        };
                        File.WriteAllText(Path.Combine(directory, "unity.json"), Json(snapshot));
                        document.Unload();
                        foreach (var asset in temporary)
                        {
                            UnityEngine.Object.DestroyImmediate(asset);
                        }
                        temporary.Clear();
                    }
                    camera.targetTexture = null;
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                    target = null;
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                if (target)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
                foreach (var asset in temporary)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                }
            }
        }

        static List<object> ReadLines(string uri, string id)
        {
            var count = urui_validation_text_lines(uri, id, null, 0);
            Assert.That(count, Is.InRange(0, 65536), id);
            var native = new TextLine[count];
            Assert.AreEqual(count, urui_validation_text_lines(uri, id, native, count));
            var lines = new List<object>();
            foreach (var line in native)
            {
                lines.Add(new Dictionary<string, object>
                {
                    ["rect"] = new[] { line.x, line.y, line.width, line.height },
                    ["baseline"] = line.baseline
                });
            }
            return lines;
        }
        static float[] RectValues(Rect rect) => new[] { rect.x, rect.y, rect.width, rect.height };
        static string Text(Element element)
        {
            if (element.TagName == "#text")
            {
                return element.Text;
            }
            var result = new StringBuilder();
            foreach (var child in element.Children())
            {
                result.Append(Text(child));
            }
            return result.ToString();
        }
        static byte[] Capture(RenderTexture target)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
            try
            {
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                texture.Apply();
                return texture.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
        static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static string Json(object value)
        {
            if (value == null)
            {
                return "null";
            }
            if (value is string text)
            {
                var quoted = new StringBuilder("\"");
                foreach (var c in text)
                {
                    if (c == '"' || c == '\\')
                    {
                        quoted.Append('\\').Append(c);
                    }
                    else if (c < 32)
                    {
                        quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        quoted.Append(c);
                    }
                }
                return quoted.Append('"').ToString();
            }
            if (value is bool flag)
            {
                return flag ? "true" : "false";
            }
            if (value is IDictionary map)
            {
                var entries = new List<string>();
                foreach (DictionaryEntry entry in map)
                {
                    entries.Add(Json((string)entry.Key) + ":" + Json(entry.Value));
                }
                return "{" + string.Join(",", entries) + "}";
            }
            if (value is IEnumerable sequence)
            {
                var entries = new List<string>();
                foreach (var item in sequence)
                {
                    entries.Add(Json(item));
                }
                return "[" + string.Join(",", entries) + "]";
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
#endif
