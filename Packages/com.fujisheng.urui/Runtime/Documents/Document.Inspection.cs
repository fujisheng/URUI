#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using URUI.Elements;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        sealed class InspectionImageLease : IDisposable
        {
            Action release;
            internal InspectionImageLease(Action release)
            {
                this.release = release;
            }
            public void Dispose()
            {
                var action = release;
                release = null;
                action?.Invoke();
            }
        }
        internal IDisposable OverrideImageForInspection(Element element, UnityEngine.Texture texture)
        {
            CheckThread();
            if (!IsLoaded || !ReferenceEquals(element.Document, this) || !element.IsValid || element.TagName != "img")
            {
                throw new InvalidOperationException("Select a valid img node.");
            }
            var selected = content;
            string uri = "/__urui_inspection/" + Guid.NewGuid().ToString("N") + ".png";
            string original = element.GetAttribute("src");
            selected.InspectionImageOverrides ??= new Dictionary<string, UnityEngine.Texture>(StringComparer.Ordinal);
            if (!ReferenceEquals(selected.ImageResolver, selected.InspectionWrapperImageResolver))
            {
                selected.InspectionBaseImageResolver = selected.ImageResolver;
                selected.InspectionWrapperImageResolver = source =>
                    selected.InspectionImageOverrides.TryGetValue(source, out var overridden)
                        ? new URUI.Resources.ImageLease(overridden)
                        : selected.InspectionBaseImageResolver?.Invoke(source);
                selected.ImageResolver = selected.InspectionWrapperImageResolver;
            }
            selected.InspectionImageOverrides[uri] = texture;
            void RemoveOverride()
            {
                selected.InspectionImageOverrides?.Remove(uri);
                if (selected.InspectionImageOverrides == null || selected.InspectionImageOverrides.Count == 0)
                {
                    if (ReferenceEquals(selected.ImageResolver, selected.InspectionWrapperImageResolver))
                    {
                        selected.ImageResolver = selected.InspectionBaseImageResolver;
                    }
                    selected.InspectionBaseImageResolver = null;
                    selected.InspectionWrapperImageResolver = null;
                }
            }
            try
            {
                element.SetAttribute("src", uri);
            }
            catch
            {
                RemoveOverride();
                throw;
            }
            return new InspectionImageLease(() =>
            {
                try
                {
                    if (this && IsLoaded && element.IsValid && element.GetAttribute("src") == uri)
                    {
                        if (original == null)
                        {
                            element.RemoveAttribute("src");
                        }
                        else
                        {
                            element.SetAttribute("src", original);
                        }
                    }
                }
                finally
                {
                    RemoveOverride();
                }
            });
        }
        internal sealed class AuthorNode
        {
            internal Element Element;
            internal string Tag;
            internal string Name;
            internal readonly List<AuthorNode> Children = new List<AuthorNode>();
        }
        internal static bool CaptureAuthoringSources;
        internal AuthorNode AuthoringRoot
        {
            get;
            private set;
        }
        void CaptureAuthoringProvenance()
        {
            AuthoringRoot = null;
            if (!CaptureAuthoringSources)
            {
                return;
            }
            try
            {
                AuthoringRoot = Capture(DocumentElement, 0);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("URUI source mapping unavailable: " + error.Message);
            }
        }
        static AuthorNode Capture(Element element, int depth)
        {
            if (depth > 256)
            {
                throw new InvalidOperationException("Source mapping nesting limit exceeded.");
            }
            var node = new AuthorNode
            {
                Element = element,
                Tag = element.TagName,
                Name = element.Name
            };
            foreach (var child in element.Hierarchy.Children())
            {
                if (child.TagName != "#text")
                {
                    node.Children.Add(Capture(child, depth + 1));
                }
            }
            return node;
        }
    }
}
#endif
