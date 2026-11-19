using System;
using System.Collections.Generic;

namespace URUI.Editor.Inspection
{
    [Serializable]
    public sealed class AuthoringProperty
    {
        public string Name;
        public string Group;
        public string Type = "string";
        public string Unit;
        public string Description;
        public bool ReadOnly;
        public string[] Choices = Array.Empty<string>();
    }
    public interface IAuthoringContext
    {
        string SessionId
        {
            get;
        }
        string TargetId
        {
            get;
        }
        bool IsLive
        {
            get;
        }
        bool IsObserved
        {
            get;
        }
        InspectedNode Node
        {
            get;
        }
        long Revision
        {
            get;
        }
        void Set(string property, string value);
        void SetWithStyleSheet(string property, string value, string source);
        void Refresh();
        void Report(string message);
    }
    /// <summary>
    /// Core Editor 的可选扩展契约，不引用 Authoring 包。
    /// </summary>
    public interface IAuthoringExtension : IDisposable
    {
        string Id
        {
            get;
        }
        IReadOnlyList<AuthoringProperty> Properties
        {
            get;
        }
        UnityEngine.TextAsset ResolveTextResource(string uri);
        IDisposable AttachPreview(URUI.Documents.Document document, Func<string, Type, UnityEngine.Object> load);
        void AdvancePreview(URUI.Documents.Document document, float delta);
        void EndSession(string sessionId);
        void DrawInspector(IAuthoringContext context);
        void DrawWorkbench(IAuthoringContext context);
        object Execute(IAuthoringContext context, string operation, IDictionary<string, string> arguments);
    }
}
