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
            internal InspectionImageLease(Action release) { this.release = release; }
            public void Dispose() { var action = release; release = null; action?.Invoke(); }
        }
        internal IDisposable OverrideImageForInspection(Element element, UnityEngine.Texture texture)
        {
            CheckThread();
            if (!IsLoaded || !ReferenceEquals(element.Document, this) || !element.IsValid || element.TagName != "img") throw new InvalidOperationException("Select a valid img node.");
            var selected = content; var previous = selected.ImageResolver;
            string uri = "/__urui_inspection/" + Guid.NewGuid().ToString("N") + ".png";
            string original = element.GetAttribute("src"); bool active = true;
            Func<string, URUI.Resources.ImageLease> resolver = source => active && source == uri ? new URUI.Resources.ImageLease(texture) : previous?.Invoke(source);
            selected.ImageResolver = resolver;
            try { element.SetAttribute("src", uri); }
            catch { active = false; selected.ImageResolver = previous; throw; }
            return new InspectionImageLease(() =>
            {
                try
                {
                    if (this && IsLoaded && element.IsValid && element.GetAttribute("src") == uri)
                    { if (original == null) element.RemoveAttribute("src"); else element.SetAttribute("src", original); }
                }
                finally { active = false; if (ReferenceEquals(selected.ImageResolver, resolver)) selected.ImageResolver = previous; }
            });
        }
        internal sealed class AuthorNode
        {
            internal Element Element;
            internal string Tag, Name;
            internal readonly List<AuthorNode> Children = new List<AuthorNode>();
        }
        internal static bool CaptureAuthoringSources;
        internal AuthorNode AuthoringRoot { get; private set; }
        void CaptureAuthoringProvenance()
        {
            AuthoringRoot = null;
            if (!CaptureAuthoringSources) return;
            try { AuthoringRoot = Capture(DocumentElement, 0); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("URUI source mapping unavailable: " + error.Message); }
        }
        static AuthorNode Capture(Element element, int depth)
        {
            if (depth > 256) throw new InvalidOperationException("Source mapping nesting limit exceeded.");
            var node = new AuthorNode { Element = element, Tag = element.TagName, Name = element.Name };
            foreach (var child in element.Hierarchy.Children()) if (child.TagName != "#text") node.Children.Add(Capture(child, depth + 1));
            return node;
        }
    }
}
#endif
