using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using URUI.Documents;
using URUI.Elements;
using URUI.Events;
using URUI.Hosting;
using URUI.Resources;

namespace URUI.Tests
{
    /// <summary>
    /// 在真实 Player 中验证原样作者输入、浏览器几何/颜色基准及多文档生命周期。
    /// </summary>
    public sealed partial class HtmlCssPlayerValidation : MonoBehaviour
    {
        [Serializable]
        sealed class Manifest
        {
            public int schema;
            public string nativeSha256;
            public Case[] cases;
        }
        [Serializable]
        sealed class Case
        {
            public string name;
            public string uri;
            public string html;
            public int width;
            public int height;
            public Resource[] resources;
            public Node[] nodes;
            public Region[] regions;
            public bool preview;
        }
        [Serializable]
        sealed class Resource
        {
            public string uri;
            public string key;
            public string sha256;
        }
        [Serializable]
        sealed class Node
        {
            public string key;
            public string tag;
            public string parent;
            public float[] rect;
            public float fontSize;
            public float opacity;
        }
        [Serializable]
        sealed class Region
        {
            public string name;
            public int[] rect;
            public int[] rgba;
        }
        [Serializable]
        sealed class CaseResult
        {
            public string name;
            public string screenshot;
            public string screenshotSha256;
            public int nodes;
            public int pixels;
            public float maxGeometryDelta;
            public int maxColorDelta;
            public bool previewFallback;
        }
        [Serializable]
        sealed class Report
        {
            public bool passed;
            public string error;
            public string unity;
            public string graphics;
            public string colorSpace;
            public string nativeSha256;
            public string scope = "Browser geometry/styles/flat color probes; public multi-document input/lifecycle. Text rasterization and real devices are separate gates.";
            public int assertions;
            public List<CaseResult> cases = new List<CaseResult>();
            public List<string> errors = new List<string>();
        }
        sealed class Clock : URUI.Animation.IUIClock
        {
            public double Now => 0;
            public float Delta => 0;
        }

        readonly Report report = new Report();
        string output;
        Manifest manifest;
        Surface surface;
        Document a;
        Document b;
        GameObject root;
        GameObject cameraObject;
        Camera renderCamera;
        RenderTexture target;
        bool finished;
        double deadline;

        IEnumerator Start()
        {
            if (!Run(() =>
            {
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, "--urui-evidence");
                if (index < 0 || index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1]))
                {
                    throw new InvalidOperationException("Pass --urui-evidence with an absolute directory.");
                }
                output = args[index + 1];
                Directory.CreateDirectory(output);
                manifest = JsonUtility.FromJson<Manifest>(Load("URUIHtmlCssPlayer/manifest").text);
                Need(manifest.schema == 1 && manifest.cases.Length > 0, "Player input schema");
                report.unity = Application.unityVersion;
                report.graphics = SystemInfo.graphicsDeviceType.ToString();
                report.colorSpace = QualitySettings.activeColorSpace.ToString();
                report.nativeSha256 = manifest.nativeSha256;
                Need(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "Actual graphics device required");
                cameraObject = new GameObject("HTML CSS Player camera", typeof(Camera));
                cameraObject.tag = "MainCamera";
                renderCamera = cameraObject.GetComponent<Camera>();
                renderCamera.allowHDR = false;
                renderCamera.allowMSAA = false;
                renderCamera.clearFlags = CameraClearFlags.SolidColor;
                renderCamera.backgroundColor = Color.white;
                root = new GameObject("HTML CSS Player Surface");
                surface = root.AddComponent<Surface>();
                surface.Input.UseUnityInput = false;
                surface.UIClock = new Clock();
                a = Create("Main");
                b = Create("Overlay");
                Application.logMessageReceived += OnLog;
                deadline = Time.realtimeSinceStartupAsDouble + 120;
            }))
            {
                yield break;
            }

            foreach (var fixture in manifest.cases)
            {
                if (!Run(() =>
                {
                    a.Unload();
                    surface.Viewport = new Vector2Int(fixture.width, fixture.height);
                    SetTarget(fixture.width, fixture.height);
                    LoadCase(a, fixture);
                }))
                {
                    yield break;
                }
                yield return null;
                yield return null;
                if (!Run(() => CheckCase(fixture)))
                {
                    yield break;
                }
            }
            if (!Run(CheckDocuments))
            {
                yield break;
            }
            yield return null;
            yield return null;
            yield return null;
            yield return null;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--urui-performance") >= 0)
            {
                yield return CapturePerformance();
                if (finished)
                {
                    yield break;
                }
            }
            Run(() =>
            {
                Need(report.errors.Count == 0, "No unhandled Unity/native errors");
                report.passed = true;
                Finish(null);
            });
        }

        Document Create(string name)
        {
            var owner = new GameObject(name);
            owner.transform.SetParent(root.transform, false);
            var document = owner.AddComponent<Document>();
            document.Surface = surface;
            return document;
        }

        static TextAsset Load(string key) => UnityEngine.Resources.Load<TextAsset>(key)
            ?? throw new InvalidOperationException("Player input missing: " + key);

        void LoadCase(Document document, Case fixture)
        {
            var files = new List<TextResource>();
            foreach (var resource in fixture.resources)
            {
                var asset = Load(resource.key);
                Need(Hash(asset.bytes) == resource.sha256, "Source hash: " + resource.uri);
                files.Add(new TextResource(resource.uri, asset));
            }
            document.TextResources = files.ToArray();
            document.LoadHtml(Load(fixture.html).text, fixture.uri);
        }

        void SetTarget(int width, int height)
        {
            if (target)
            {
                renderCamera.targetTexture = null;
                target.Release();
                Destroy(target);
            }
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            target.Create();
            renderCamera.targetTexture = target;
        }

        void CheckCase(Case fixture)
        {
            a.SynchronizeLayout();
            renderCamera.Render();
            var result = new CaseResult
            {
                name = fixture.name,
                previewFallback = fixture.preview
            };
            var nodes = new Dictionary<string, Element>();
            foreach (var element in a.Query())
            {
                var key = element.GetAttribute("data-test");
                if (key != null)
                {
                    Need(!nodes.ContainsKey(key), "Unique key: " + key);
                    nodes.Add(key, element);
                }
            }
            Need(nodes.Count == fixture.nodes.Length, "Key node count: " + fixture.name);
            foreach (var expected in fixture.nodes)
            {
                Need(nodes.TryGetValue(expected.key, out var element), "Missing node: " + expected.key);
                Need(element.TagName == expected.tag, "Tag: " + expected.key);
                var parent = element.Parent;
                while (parent != null && !parent.HasAttribute("data-test"))
                {
                    parent = parent.Parent;
                }
                Need((parent?.GetAttribute("data-test") ?? "") == expected.parent, "Parent: " + expected.key);
                var rect = element.WorldBound;
                var values = new[] { rect.x, rect.y, rect.width, rect.height };
                for (var index = 0; index < 4; index++)
                {
                    var delta = Mathf.Abs(values[index] - expected.rect[index]);
                    result.maxGeometryDelta = Mathf.Max(result.maxGeometryDelta, delta);
                    Need(delta <= .5f, "Geometry: " + fixture.name + "/" + expected.key + " delta=" + delta);
                }
                var style = a.Styles.GetComputed(element);
                Need(Mathf.Abs(style.GetPixels("font-size") - expected.fontSize) <= .01f, "Font size: " + expected.key);
                Need(Mathf.Abs(style.GetNumber("opacity") - expected.opacity) <= .001f, "Opacity: " + expected.key);
                result.nodes++;
            }
            var image = ReadTarget();
            try
            {
                foreach (var region in fixture.regions)
                {
                    var index = 0;
                    for (var y = 0; y < region.rect[3]; y++)
                    {
                        for (var x = 0; x < region.rect[2]; x++)
                        {
                            Color32 pixel = image.GetPixel(region.rect[0] + x, image.height - region.rect[1] - y - 1);
                            foreach (var channel in new[] { pixel.r, pixel.g, pixel.b, pixel.a })
                            {
                                var delta = Math.Abs(channel - region.rgba[index++]);
                                result.maxColorDelta = Math.Max(result.maxColorDelta, delta);
                                Need(delta <= 2, "Color: " + fixture.name + "/" + region.name + " delta=" + delta);
                            }
                            result.pixels++;
                        }
                    }
                }
                result.screenshot = fixture.name + ".png";
                var png = image.EncodeToPNG();
                File.WriteAllBytes(Path.Combine(output, result.screenshot), png);
                result.screenshotSha256 = Hash(png);
            }
            finally
            {
                Destroy(image);
            }
            report.cases.Add(result);
            File.WriteAllText(Path.Combine(output, "progress.json"), JsonUtility.ToJson(report, true));
        }

        Texture2D ReadTarget()
        {
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                return image;
            }
            catch
            {
                Destroy(image);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        void CheckDocuments()
        {
            var fixture = Array.Find(manifest.cases, item => item.uri.EndsWith("/forms.html", StringComparison.Ordinal));
            Need(fixture != null, "Shared forms fixture for multi-document Player acceptance");
            a.Unload();
            surface.Viewport = new Vector2Int(fixture.width, fixture.height);
            SetTarget(fixture.width, fixture.height);
            LoadCase(a, fixture);
            LoadCase(b, fixture);
            b.SetPresentation(1, 0, true, false);
            var first = a.Q<URUI.Controls.Button>("button");
            var second = b.Q<URUI.Controls.Button>("button");
            Need(first != null && second != null && !ReferenceEquals(first, second), "Same IDs remain document scoped");
            Need(ReferenceEquals(first.Document, a) && ReferenceEquals(second.Document, b), "Typed element owners");
            var rejected = false;
            try
            {
                a.Body.Add(second);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            Need(rejected && first.IsValid && second.IsValid, "Cross-document insertion rejected without damage");
            rejected = false;
            try
            {
                a.LoadHtml("<!doctype html><html><head></head><body><div/></body></html>", "/invalid.html");
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Need(rejected && ReferenceEquals(first, a.Q("button")) && a.SourceUrl == fixture.uri, "Strict rejection preserves active generation");
            b.DocumentElement.Style.Set("pointer-events", "none");
            b.Body.Style.Set("pointer-events", "none");
            b.Body.Style.Set("background-color", "transparent");
            second.Style.Set("pointer-events", "auto");
            second.Style.Set("left", "300px");
            first.Text = "Main";
            second.Text = "Overlay";
            var clicks = 0;
            using var subscription = second.RegisterCallback<ClickEvent>(_ => clicks++);
            b.SynchronizeLayout();
            var point = second.WorldBound.center;
            surface.Input.SendPointer(PointerPhase.Down, point, pressedButtons: 1);
            surface.Input.SendPointer(PointerPhase.Up, point);
            Need(clicks == 1, "One shared Surface dispatches one click to the overlay owner");
            b.SetPresentation(1, 0, true, true);
            Need(!first.Focus() && second.Focus(), "Modal document blocks background focus");
            a.SynchronizeLayout();
            renderCamera.Render();
            var image = ReadTarget();
            try
            {
                File.WriteAllBytes(Path.Combine(output, "multi-document.png"), image.EncodeToPNG());
            }
            finally
            {
                Destroy(image);
            }
            b.Unload();
            Need(!second.IsValid && first.IsValid && a.IsLoaded && first.Focus(), "Close overlay only; restore background input");
            surface.enabled = false;
            Need(!a.IsLoaded && !first.IsValid, "Surface disable invalidates old generation");
            surface.enabled = true;
            Need(a.IsLoaded && !b.IsLoaded && a.Q("button") != null, "Surface restores only loaded documents");
            a.Unload();
            Need(surface.isActiveAndEnabled, "Last document close keeps Surface available");
            LoadCase(b, fixture);
            Need(b.IsLoaded && !second.IsValid && b.Q("button") != null, "Reopen uses a fresh generation");
            b.Unload();
        }

        void Need(bool value, string message)
        {
            report.assertions++;
            if (!value)
            {
                throw new InvalidOperationException(message);
            }
        }
        bool Run(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception error)
            {
                Finish(error);
                return false;
            }
        }
        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                report.errors.Add(message + "\n" + stack);
            }
        }
        void Update()
        {
            if (!finished && deadline > 0 && Time.realtimeSinceStartupAsDouble > deadline)
            {
                Finish(new TimeoutException("Player validation timed out"));
            }
        }
        static string Hash(byte[] bytes)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        void Finish(Exception error)
        {
            if (finished)
            {
                return;
            }
            finished = true;
            Application.logMessageReceived -= OnLog;
            if (error != null)
            {
                report.passed = false;
                report.error = error.ToString();
            }
            if (root)
            {
                Destroy(root);
            }
            if (cameraObject)
            {
                Destroy(cameraObject);
            }
            if (target)
            {
                target.Release();
                Destroy(target);
            }
            if (output != null)
            {
                File.WriteAllText(Path.Combine(output, "html-css-player.json"), JsonUtility.ToJson(report, true));
            }
            Debug.Log("HTML_CSS_PLAYER: passed=" + report.passed + ", assertions=" + report.assertions + ", error=" + report.error);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(report.passed ? 0 : 1);
#else
            Application.Quit(report.passed ? 0 : 1);
#endif
        }

    }
}
