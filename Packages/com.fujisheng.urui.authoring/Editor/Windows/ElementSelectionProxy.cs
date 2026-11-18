using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using URUI.Editor.Inspection;

namespace URUI.Authoring.Windows
{
    /// <summary>为 URUI Inspector 提供固定的节点代理；对象不写入场景。</summary>
    public sealed class ElementSelectionProxy : ScriptableObject, IAuthoringContext
    {
        static readonly Dictionary<string, ElementSelectionProxy> proxies = new Dictionary<string, ElementSelectionProxy>();
        [NonSerialized] InspectedNode liveNode;
        [NonSerialized] LiveSession live;
        [NonSerialized] string sourceKey, sourcePath, sourceIdentity;
        [NonSerialized] long structureRevision;
        public bool IsLive => live != null;
        public bool IsObserved => AuthoringEnvironment.HasObservers;
        public string SessionId => IsLive ? live.Id : AuthoringEnvironment.Author.Id;
        public string TargetId => IsLive ? liveNode.Id : sourcePath + "/" + sourceIdentity;
        public string SourceKey => IsLive ? null : AuthoringEnvironment.Author.ResolveIdentity(sourceIdentity);
        public long Revision => IsLive ? live.Revision : AuthoringEnvironment.Author.Revision;
        public InspectedNode Node => IsLive ? liveNode : IsTargetValid ? AuthoringEnvironment.Preview?.Node(SourceKey) : null;
        public bool IsTargetValid => IsLive ? live.IsAlive && liveNode != null && liveNode.IsValid :
            AuthoringEnvironment.Author.File?.Path == sourcePath && AuthoringEnvironment.Author.ResolveIdentity(sourceIdentity) != null;
        public SourceDocument.Node SourceNode => IsTargetValid && !IsLive ? AuthoringEnvironment.Author.Model.Find(SourceKey) : null;
        public static ElementSelectionProxy ForLive(InspectedNode node, LiveSession session)
        {
            string id = session.Id + "/" + node.Id;
            if (proxies.TryGetValue(id, out var existing) && existing)
                return existing;
            var proxy = CreateInstance<ElementSelectionProxy>();
            proxy.hideFlags = HideFlags.HideAndDontSave;
            proxy.liveNode = node;
            proxy.live = session;
            proxy.name = node.Tag + " #" + node.Name;
            proxies[id] = proxy;
            return proxy;
        }
        public static ElementSelectionProxy ForAuthor(string key)
        {
            var author = AuthoringEnvironment.Author;
            string id = author.Id + "/" + author.File.Path + "/" + author.NodeIdentity(key);
            if (proxies.TryGetValue(id, out var existing) && existing)
                return existing;
            var proxy = CreateInstance<ElementSelectionProxy>();
            proxy.hideFlags = HideFlags.HideAndDontSave;
            proxy.sourceKey = key;
            proxy.sourceIdentity = author.NodeIdentity(key);
            proxy.sourcePath = author.File.Path;
            proxy.structureRevision = author.StructureRevision;
            proxy.name = author.Model.Find(key).Tag;
            proxies[id] = proxy;
            return proxy;
        }
        public string Read(string property)
        {
            if (!IsTargetValid)
                throw new InvalidOperationException("stale_node");
            if (IsLive)
                return liveNode.Read(property);
            var node = SourceNode;
            if (property.StartsWith("style.", StringComparison.Ordinal))
                return CssSource.GetDeclaration(node.Attribute("style") ?? "", property.Substring(6));
            if (property.StartsWith("attr.", StringComparison.Ordinal))
                return node.Attribute(property.Substring(5));
            if (property == "value")
            {
                if (node.Attribute("type") == "checkbox" || node.Attribute("type") == "radio")
                    return node.Attribute("checked") != null ? "true" : "false";
                if (node.Tag != "textarea")
                    return node.Attribute("value") ?? "";
                return System.Net.WebUtility.HtmlDecode(AuthoringEnvironment.Author.File.Text.Substring(node.OpenEnd, node.CloseStart - node.OpenEnd));
            }
            if (property == "text")
                return node.Children.Count == 0 && !node.IsVoid ? System.Net.WebUtility.HtmlDecode(AuthoringEnvironment.Author.File.Text.Substring(node.OpenEnd, node.CloseStart - node.OpenEnd)) : null;
            throw new InvalidOperationException("unsupported_property: " + property);
        }
        public void Set(string property, string value)
        {
            if (!IsTargetValid)
                throw new InvalidOperationException("stale_node");
            if (!IsLive && Animation.AnimationEnvironment.TryRecord(this, property, value))
            {
                Refresh();
                return;
            }
            if (!IsLive && AuthoringEnvironment.Mode == WorkspaceMode.Animation)
            {
                Animation.AnimationEnvironment.PreviewEdits(this, new[] { new AuthoringEdit { Node = SourceKey, Property = property, Value = value } });
                return;
            }
            if (IsLive)
            {
                SessionUndoBridge.Record(live, () => live.Set(liveNode, property, value, live.Revision));
                RuntimeInspectionBridge.Refresh(liveNode.Document);
            }
            else
                SessionUndoBridge.Record(AuthoringEnvironment.Author, () => AuthoringEnvironment.Author.Set(SourceKey, property, value, Revision));
            Refresh();
        }
        public void SetWithStyleSheet(string property, string value, string source)
        {
            if (!IsTargetValid)
                throw new InvalidOperationException("stale_node");
            if (IsLive)
            {
                if (Node.Computed("--urui-fx-tone-filter") == null)
                    throw new InvalidOperationException("effect_styles_missing: runtime page must already load " + source);
                Set(property, value);
                return;
            }
            if (Animation.AnimationEnvironment.TryRecord(this, property, value))
            {
                Refresh();
                return;
            }
            if (AuthoringEnvironment.Mode == WorkspaceMode.Animation)
            {
                Animation.AnimationEnvironment.PreviewEdits(this, new[] { new AuthoringEdit { Node = SourceKey, Property = property, Value = value } });
                return;
            }
            var author = AuthoringEnvironment.Author;
            string key = null;
            SessionUndoBridge.Record(author, () => key = author.SetWithStyleSheet(SourceKey, property, value, source, author.Revision));
            AuthoringEnvironment.Select(ForAuthor(key));
        }
        public void Refresh()
        {
            if (IsLive)
                RuntimeInspectionBridge.Refresh(liveNode.Document);
            else if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                AuthoringEnvironment.Preview?.Refresh();
            AuthoringEnvironment.Report(IsLive ? "Live · temporary changes" : "Authoring · unsaved changes");
        }
        public void Report(string message) => AuthoringEnvironment.Report(message);
        public static void SetMany(IReadOnlyList<ElementSelectionProxy> targets, string property, string value)
        {
            SetProperties(targets, new Dictionary<string, string> { [property] = value });
        }
        /// <summary>Applies linked Inspector fields as one Undo transaction through the existing mode routing.</summary>
        public static void SetProperties(IReadOnlyList<ElementSelectionProxy> targets, IReadOnlyDictionary<string, string> values)
        {
            var changes = new List<IReadOnlyDictionary<string, string>>();
            foreach (var target in targets)
                changes.Add(values);
            SetPerTarget(targets, changes);
        }
        internal static void SetPerTarget(IReadOnlyList<ElementSelectionProxy> targets, IReadOnlyList<IReadOnlyDictionary<string, string>> values)
        {
            if (targets.Count != values.Count)
                throw new InvalidOperationException("Target/value count mismatch.");
            if (targets.Count == 0)
                return;
            var first = targets[0];
            foreach (var proxy in targets)
                if (!proxy.IsTargetValid || proxy.SessionId != first.SessionId)
                    throw new InvalidOperationException("Selection targets different or expired sessions.");
            var sourceEdits = new List<AuthoringEdit>();
            for (int i = 0; i < targets.Count; i++)
                foreach (var pair in values[i])
                    sourceEdits.Add(new AuthoringEdit { Node = targets[i].SourceKey, Property = pair.Key, Value = pair.Value });
            if (sourceEdits.Count == 0)
                return;
            if (!first.IsLive && Animation.AnimationEnvironment.Recording)
            {
                if (targets.Count != 1)
                    throw new InvalidOperationException("录制时请编辑当前轨道绑定的一个节点。");
                Animation.AnimationEnvironment.Record(first, sourceEdits);
                first.Refresh();
                return;
            }
            if (!first.IsLive && AuthoringEnvironment.Mode == WorkspaceMode.Animation)
            {
                if (targets.Count != 1)
                    throw new InvalidOperationException("临时动画预览请一次编辑一个节点。");
                Animation.AnimationEnvironment.PreviewEdits(first, sourceEdits);
                return;
            }
            if (first.IsLive)
            {
                var edits = new List<LiveEdit>();
                for (int i = 0; i < targets.Count; i++)
                    foreach (var pair in values[i])
                        edits.Add(new LiveEdit(targets[i].Node, pair.Key, pair.Value));
                SessionUndoBridge.Record(first.live, () => first.live.Execute(edits, first.Revision));
                foreach (var proxy in targets)
                    RuntimeInspectionBridge.Refresh(proxy.Node.Document);
            }
            else
                SessionUndoBridge.Record(AuthoringEnvironment.Author, () => AuthoringEnvironment.Author.Execute(sourceEdits, first.Revision));
            first.Refresh();
        }
        public static void ClearLive()
        {
            var remove = new List<string>();
            foreach (var pair in proxies)
                if (!pair.Value || pair.Value.IsLive)
                    remove.Add(pair.Key);
            foreach (var key in remove)
            {
                if (proxies[key])
                    DestroyImmediate(proxies[key]);
                proxies.Remove(key);
            }
        }
        public static void Clear()
        {
            foreach (var proxy in proxies.Values)
                if (proxy)
                    DestroyImmediate(proxy);
            proxies.Clear();
        }
    }
}
