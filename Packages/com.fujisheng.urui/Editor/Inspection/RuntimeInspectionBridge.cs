using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;
using URUI.Controls;
using URUI.Documents;
using URUI.Elements;

namespace URUI.Editor.Inspection
{
    /// <summary>
    /// 固定 Element 包装身份的受控 Editor 引用；不会按名字重新定位节点。
    /// </summary>
    public sealed class InspectedNode
    {
        internal readonly Element Element;
        IDisposable imageOverride;
        string imageAsset;
        internal InspectedNode(Element element, string id)
        {
            Element = element;
            Id = id;
        }
        public string Id
        {
            get;
        }
        public bool IsValid => Element.Document && Element.IsValid;
        public Document Document => Element.Document;
        public string Tag => Require().TagName;
        public string Name => Require().Name;
        public bool HasLayout => IsValid && Element.HasLayout;
        public Rect Bounds => Require().WorldBound;
        public Rect Layout => Require().Layout;
        /// <summary>
        /// 当前节点内容的滚动偏移，用于在画布上从显示位置恢复源码坐标。
        /// </summary>
        public Vector2 ScrollOffset => Require().ScrollOffset;
        public Vector2 ContentSize => Require().ContentRect.size;
        public InspectedNode Parent => RuntimeInspectionBridge.Inspect(Require().Parent);
        public int ChildCount => Require().Hierarchy.ChildCount;
        public InspectedNode Child(int index) => RuntimeInspectionBridge.Inspect(Require().Hierarchy.ChildAt(index));
        public bool CanEditText
        {
            get
            {
                var element = Require();
                if (element is INotifyValueChanged<string>)
                {
                    return true;
                }
                for (int i = 0; i < ChildCount; i++)
                {
                    if (Child(i).Tag != "#text")
                    {
                        return false;
                    }
                }
                return Tag != "#text" && Tag != "style" && Tag != "script" && Tag != "img";
            }
        }
        public string Read(string property)
        {
            var element = Require();
            if (property == "image")
            {
                if (element.TagName != "img")
                {
                    throw new InvalidOperationException("unsupported_property: image requires img");
                }
                return imageAsset;
            }
            if (property.StartsWith("style.", StringComparison.Ordinal))
            {
                return element.Style.Get(property.Substring(6));
            }
            if (property.StartsWith("attr.", StringComparison.Ordinal))
            {
                return element.GetAttribute(property.Substring(5));
            }
            if (property == "text")
            {
                return CanEditText ? element.Text : null;
            }
            if (property == "value")
            {
                if (element is INotifyValueChanged<string> text)
                {
                    return text.Value;
                }
                if (element is INotifyValueChanged<bool> toggle)
                {
                    return toggle.Value ? "true" : "false";
                }
                if (element is INotifyValueChanged<float> slider)
                {
                    return slider.Value.ToString("R", CultureInfo.InvariantCulture);
                }
                if (element is INotifyValueChanged<int> integer)
                {
                    return integer.Value.ToString(CultureInfo.InvariantCulture);
                }
            }
            throw new InvalidOperationException("unsupported_property: " + property);
        }
        public string Computed(string property) => Require().ReadStyle(property, false);
        /// <summary>
        /// 释放检查器拥有的图片租约；节点失效后仍可调用，不重绑定新节点。
        /// </summary>
        public void ReleaseImageOverride()
        {
            var owned = imageOverride;
            imageOverride = null;
            imageAsset = null;
            owned?.Dispose();
        }
        public string Origin(string property)
        {
            try
            {
                return Document.Styles.GetComputed(Require()).GetOrigin(property).ToString();
            }
            catch (InvalidOperationException)
            {
                return "Unavailable";
            }
        }
        public void Write(string property, string value)
        {
            var element = Require();
            if (property == "image")
            {
                if (element.TagName != "img")
                {
                    throw new InvalidOperationException("unsupported_property: image requires img");
                }
                Texture2D texture = null;
                if (value != null)
                {
                    if (!value.StartsWith("Assets/", StringComparison.Ordinal) && !value.StartsWith("Packages/", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Image must use a project asset path.");
                    }
                    texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(value);
                    if (!texture)
                    {
                        throw new InvalidOperationException("Image Texture2D asset not found.");
                    }
                }
                imageOverride?.Dispose();
                imageOverride = null;
                imageAsset = null;
                if (texture)
                {
                    imageOverride = element.Document.OverrideImageForInspection(element, texture);
                    imageAsset = value;
                }
                return;
            }
            if (property.StartsWith("style.", StringComparison.Ordinal))
            {
                if (value == null)
                {
                    element.Style.Remove(property.Substring(6));
                }
                else
                {
                    element.Style.Set(property.Substring(6), value);
                }
                return;
            }
            if (property.StartsWith("attr.", StringComparison.Ordinal))
            {
                if (value == null)
                {
                    element.RemoveAttribute(property.Substring(5));
                }
                else
                {
                    element.SetAttribute(property.Substring(5), value);
                }
                return;
            }
            if (property == "text")
            {
                if (!CanEditText)
                {
                    throw new InvalidOperationException("Text editing would destroy structured children.");
                }
                if (element is INotifyValueChanged<string> input)
                {
                    input.SetValueWithoutNotify(value ?? "");
                }
                else
                {
                    element.Text = value ?? "";
                }
                return;
            }
            if (property == "value")
            {
                if (element is INotifyValueChanged<string> text)
                {
                    text.SetValueWithoutNotify(value ?? "");
                    return;
                }
                if (element is INotifyValueChanged<bool> toggle)
                {
                    toggle.SetValueWithoutNotify(bool.Parse(value));
                    return;
                }
                if (element is INotifyValueChanged<float> slider)
                {
                    slider.SetValueWithoutNotify(float.Parse(value, CultureInfo.InvariantCulture));
                    return;
                }
                if (element is INotifyValueChanged<int> integer)
                {
                    integer.SetValueWithoutNotify(int.Parse(value, CultureInfo.InvariantCulture));
                    return;
                }
            }
            throw new InvalidOperationException("unsupported_property: " + property);
        }
        Element Require()
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("stale_node: node was destroyed or document reloaded");
            }
            return Element;
        }
    }

    /// <summary>
    /// 集中隔离 friend assembly 访问；Authoring 与扩展不接触原生句柄。
    /// </summary>
    public static class RuntimeInspectionBridge
    {
        public sealed class SourceNodeSnapshot
        {
            public InspectedNode Node;
            public string Tag;
            public string OriginalName;
            public readonly List<SourceNodeSnapshot> Children = new List<SourceNodeSnapshot>();
        }
        static readonly ConditionalWeakTable<Element, InspectedNode> nodes = new ConditionalWeakTable<Element, InspectedNode>();
        static long nextId;
        static long capturedFrame;
        public static void EnableSourceMapping()
        {
            Document.CaptureAuthoringSources = true;
        }
        public static SourceNodeSnapshot SourceTree(Document document) => CopySource(document.AuthoringRoot);
        static SourceNodeSnapshot CopySource(Document.AuthorNode source)
        {
            if (source == null)
            {
                return null;
            }
            var snapshot = new SourceNodeSnapshot
            {
                Node = Inspect(source.Element),
                Tag = source.Tag,
                OriginalName = source.Name
            };
            foreach (var child in source.Children)
            {
                snapshot.Children.Add(CopySource(child));
            }
            return snapshot;
        }
        public static InspectedNode Inspect(Element element) => element == null ? null : nodes.GetValue(element,
            value => new InspectedNode(value, value.Document.GetInstanceID() + ":" + (++nextId).ToString(CultureInfo.InvariantCulture)));
        public static InspectedNode Root(Document document) => document && document.IsLoaded ? Inspect(document.DocumentElement) : null;
        public static string LoadedMarkup(Document document) => document.LoadedMarkup;
        public static Element GetElement(InspectedNode node) => node != null && node.IsValid ? node.Element : throw new InvalidOperationException("stale_node");
        public static string ResolveResourceUrl(string baseUrl, string source) => URUI.Editor.Authoring.HtmlResourceDependencies.ResolveUrl(baseUrl, source);
        public static Vector2 Viewport(Document document) => new Vector2(document.Surface.Width, document.Surface.Height);
        public static void Refresh(Document document)
        {
            if (document && document.IsLoaded)
            {
                document.SynchronizeLayout();
            }
        }
        public static InspectedNode Pick(Document document, Vector2 position) => Inspect(document.Pick(position));
        /// <summary>
        /// The native context is available only after the previous Surface finishes shutting down.
        /// </summary>
        public static bool CanCreatePreview => !URUI.Internal.Hosting.RenderHost.ActiveHost;
        public static EditorPreviewHost CreatePreview(int width, int height) => new EditorPreviewHost(width, height);
        public static CapturedPixels CaptureLive(Document document)
        {
            if (!UnityEditor.EditorApplication.isPlaying || !document || !document.IsLoaded)
            {
                throw new InvalidOperationException("Select a loaded Play Mode document.");
            }
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            {
                throw new InvalidOperationException("Live capture currently requires the Built-in render pipeline.");
            }
            var camera = document.Host.InspectionCamera;
            if (!camera)
            {
                throw new InvalidOperationException("Runtime camera unavailable.");
            }
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            if (width < 1 || height < 1 || width > 8192 || height > 8192)
            {
                throw new InvalidOperationException("Capture size outside supported range.");
            }
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            Texture2D pixels = null;
            try
            {
                target.Create();
                camera.targetTexture = target;
                Refresh(document);
                camera.Render();
                RenderTexture.active = target;
                pixels = new Texture2D(width, height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply();
                return new CapturedPixels(pixels, ++capturedFrame);
            }
            catch
            {
                if (pixels)
                {
                    UnityEngine.Object.DestroyImmediate(pixels);
                }
                throw;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                URUI.Resources.GpuRetirement.Retire(() =>
                {
                    target.Release();
                    URUI.Resources.GpuRetirement.DestroyOwnedObject(target);
                });
            }
        }
    }
}
