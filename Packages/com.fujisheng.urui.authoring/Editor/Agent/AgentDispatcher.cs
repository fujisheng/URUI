using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;
using URUI.Authoring.Windows;
using URUI.Editor.Inspection;
using URUI.Events;

namespace URUI.Authoring.Agent
{
    /// <summary>
    /// 主线程协议适配，所有写入使用与人工相同的会话和命令。
    /// </summary>
    public sealed partial class AgentDispatcher
    {
        readonly string editorSession;
        readonly Dictionary<string, InspectedNode> nodes = new Dictionary<string, InspectedNode>();
        string cachedLiveSession;
        long liveRenderedRevision = -1;
        long liveFrame;
        public AgentDispatcher(string editorSession)
        {
            this.editorSession = editorSession;
        }
        public Dictionary<string, object> Execute(Dictionary<string, object> request)
        {
            if (EditorApplication.isCompiling)
            {
                throw new InvalidOperationException("compile_in_progress: retry after rediscovering endpoint");
            }
            string operation = Text(request, "op");
            object data;
            if (operation == "capabilities")
            {
                var extensions = new List<object>();
                foreach (var extension in AuthoringEnvironment.Extensions)
                {
                    extensions.Add(Obj("id", extension.Id, "properties", extension.Properties));
                }
                return Result(Obj("apiVersion", 1, "authoringSession", AuthoringEnvironment.Author.Id, "liveSession", AuthoringEnvironment.Live?.Id, "properties", PropertySchema.All, "extensions", extensions,
                    "operations", new[] { "documents.list", "document.open", "document.select", "document.read", "document.edit", "document.save", "document.reload", "hierarchy.children", "hierarchy.find", "selection.set", "properties.read", "properties.set", "structure.insert", "structure.remove", "structure.move", "style.rule", "history.undo", "history.redo", "preview.viewport", "preview.render", "preview.capture", "preview.input", "extension", "persistence.apply", "workspace.mode", "animation.read", "animation.open", "animation.create", "animation.preset", "animation.particles", "animation.track", "animation.lock", "animation.remove", "animation.key", "animation.keys.move", "animation.keys.remove", "animation.sample", "animation.play", "animation.pause", "animation.stop", "animation.record" }));
            }
            if (operation == "documents.list")
            {
                var documents = new List<object>();
                foreach (var document in AuthoringEnvironment.Documents())
                {
                    documents.Add(Obj("id", document.GetInstanceID(), "name", document.name, "source", document.SourceUrl));
                }
                return Result(documents);
            }
            string mode = Text(request, "mode");
            if (mode != "authoring" && mode != "live")
            {
                throw new InvalidOperationException("bad_request: mode must be authoring or live");
            }
            bool live = mode == "live";
            if (live && (!EditorApplication.isPlaying || AuthoringEnvironment.Live == null))
            {
                throw new InvalidOperationException("operation_cancelled: no active Play session");
            }
            bool write = operation != "animation.read" && operation != "document.read" && operation != "hierarchy.children" && operation != "hierarchy.find" && operation != "properties.read" && operation != "preview.capture" && operation != "preview.render";
            if (write)
            {
                RequireWrite(request, live);
            }
            var author = AuthoringEnvironment.Author;
            switch (operation)
            {
                case "workspace.mode":
                    RequireAuthor(live);
                    string workspaceMode = Text(request, "workspace");
                    if (workspaceMode != "layout" && workspaceMode != "animation" && workspaceMode != "live")
                    {
                        throw new InvalidOperationException("bad_request: workspace must be layout, animation or live");
                    }
                    AuthoringEnvironment.SetMode(workspaceMode == "layout" ? WorkspaceMode.Layout : workspaceMode == "animation" ? WorkspaceMode.Animation : WorkspaceMode.Live);
                    data = Obj("workspace", workspaceMode);
                    break;
                case "document.open":
                    RequireAuthor(live);
                    AuthoringEnvironment.Open(Text(request, "path"));
                    data = SourceState();
                    break;
                case "document.select":
                    if (!live)
                    {
                        throw new InvalidOperationException("bad_request: document.select requires live mode");
                    }
                    int documentId = checked((int)Integer(request, "document"));
                    URUI.Documents.Document selected = null;
                    foreach (var document in AuthoringEnvironment.Documents())
                    {
                        if (document.GetInstanceID() == documentId)
                        {
                            selected = document;
                        }
                    }
                    if (!selected)
                    {
                        throw new InvalidOperationException("stale_node: document expired");
                    }
                    AuthoringEnvironment.SetDocument(selected);
                    data = LiveNode(RuntimeInspectionBridge.Root(selected));
                    break;
                case "document.read":
                    RequireAuthor(live);
                    var source = author.Workspace.Open(Text(request, "path", false) ?? author.File?.Path ?? throw new InvalidOperationException("Open a document first."));
                    data = Obj("path", source.Path, "text", source.Text, "fileRevision", source.Revision, "dirty", source.Dirty, "spanUnit", "utf-16", "encoding", "utf-8");
                    break;
                case "document.edit":
                    RequireAuthor(live);
                    author.EditFile(Text(request, "path", false) ?? author.File.Path, Text(request, "text"), author.Revision);
                    data = SourceState();
                    break;
                case "document.save":
                    RequireAuthor(live);
                    author.Save();
                    data = SourceState();
                    break;
                case "document.reload":
                    RequireAuthor(live);
                    author.Reload(Boolean(request, "discardLocal"));
                    SessionUndoBridge.Clear();
                    data = SourceState();
                    break;
                case "hierarchy.children":
                    data = Children(request, live);
                    break;
                case "hierarchy.find":
                    data = Find(request, live);
                    break;
                case "selection.set":
                    AuthoringEnvironment.Select(Target(request, live));
                    data = Obj("selected", Text(request, "target"));
                    break;
                case "properties.read":
                    data = ReadProperties(request, Target(request, live));
                    break;
                case "properties.set":
                    data = SetProperties(request, live);
                    break;
                case "structure.insert":
                case "structure.remove":
                case "structure.move":
                    RequireAuthor(live);
                    var edit = new AuthoringEdit
                    {
                        Operation = operation.Substring(10),
                        Node = Text(request, "target", false),
                        Parent = Text(request, "parent", false),
                        Value = Text(request, "html", false),
                        Index = checked((int)Integer(request, "index", -1))
                    };
                    SessionUndoBridge.Record(author, () => author.Execute(new[] { edit }, author.Revision));
                    data = SourceState();
                    break;
                case "style.rule":
                    RequireAuthor(live);
                    author.SetSharedRule(Text(request, "path"), checked((int)Integer(request, "ruleStart")), Text(request, "property"), Boolean(request, "unset") ? null : Text(request, "value"), author.Revision);
                    data = SourceState();
                    break;
                case "history.undo":
                case "history.redo":
                    SessionUndoBridge.Clear();
                    if (live)
                    {
                        if (operation == "history.undo")
                        {
                            AuthoringEnvironment.Live.Undo();
                        }
                        else
                        {
                            AuthoringEnvironment.Live.Redo();
                        }
                        if (AuthoringEnvironment.LiveDocument)
                        {
                            RuntimeInspectionBridge.Refresh(AuthoringEnvironment.LiveDocument);
                        }
                    }
                    else
                    {
                        if (operation == "history.undo")
                        {
                            author.Undo();
                        }
                        else
                        {
                            author.Redo();
                        }
                    }
                    data = Obj("undone", operation == "history.undo");
                    break;
                case "preview.viewport":
                    RequireAuthor(live);
                    AuthoringEnvironment.EnsurePreview();
                    AuthoringEnvironment.Preview.Resize(checked((int)Integer(request, "width")), checked((int)Integer(request, "height")));
                    data = Obj("width", AuthoringEnvironment.Preview.Width, "height", AuthoringEnvironment.Preview.Height);
                    break;
                case "preview.render":
                    Render(live);
                    data = Obj("rendered", true);
                    break;
                case "preview.capture":
                    data = Capture(live);
                    break;
                case "preview.input":
                    var input = (live ? RequireLiveDocument() : AuthoringEnvironment.Preview?.Document ?? throw new InvalidOperationException("Preview is not ready.")).Surface.Input;
                    string kind = Text(request, "kind");
                    if (kind == "pointer")
                    {
                        if (!Enum.TryParse(Text(request, "phase"), false, out PointerPhase phase) || !Enum.IsDefined(typeof(PointerPhase), phase))
                        {
                            throw new InvalidOperationException("bad_request: unknown pointer phase");
                        }
                        input.SendPointer(phase, new Vector2(Number(request, "x"), Number(request, "y")), button: checked((int)Integer(request, "button", 0)), pressedButtons: checked((int)Integer(request, "pressedButtons", 0)));
                    }
                    else if (kind == "key")
                    {
                        if (!Enum.TryParse(Text(request, "key"), false, out KeyCode key) || !Enum.IsDefined(typeof(KeyCode), key))
                        {
                            throw new InvalidOperationException("bad_request: unknown key");
                        }
                        input.SendKey(key, Boolean(request, "pressed"));
                    }
                    else if (kind == "text")
                    {
                        input.SendText(Text(request, "value"));
                    }
                    else if (kind == "reset")
                    {
                        input.Reset();
                    }
                    else
                    {
                        throw new InvalidOperationException("bad_request: unknown input kind");
                    }
                    Render(live);
                    data = Obj("sent", true);
                    break;
                case "extension":
                    IAuthoringExtension found = null;
                    foreach (var extension in AuthoringEnvironment.Extensions)
                    {
                        if (extension.Id == Text(request, "extension"))
                        {
                            found = extension;
                        }
                    }
                    if (found == null)
                    {
                        throw new InvalidOperationException("effect_not_installed: extension unavailable");
                    }
                    var arguments = new Dictionary<string, string>();
                    if (request.TryGetValue("args", out var args) && args is Dictionary<string, object> values)
                    {
                        foreach (var pair in values)
                        {
                            arguments.Add(pair.Key, pair.Value is string s ? s : Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
                        }
                    }
                    data = found.Execute(Target(request, live), Text(request, "operation"), arguments);
                    break;
                case "persistence.apply":
                    data = ApplyLive(request, live);
                    break;
                default:
                    if (!operation.StartsWith("animation.", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("unsupported_operation: " + operation);
                    }
                    data = AnimationCommand(operation, request, live);
                    break;
            }
            return Result(data, mode);
        }
        static object SourceState() => Obj("path", AuthoringEnvironment.Author.File?.Path, "dirty", AuthoringEnvironment.Author.Workspace.Files.Any(file => file.Dirty), "pendingAssets", AuthoringEnvironment.PendingAssets(), "structureRevision", AuthoringEnvironment.Author.StructureRevision);
        void RequireWrite(Dictionary<string, object> request, bool live)
        {
            string expectedSession = live ? AuthoringEnvironment.Live.Id : AuthoringEnvironment.Author.Id;
            if (Text(request, "session") != expectedSession)
            {
                throw new InvalidOperationException("operation_cancelled: target session changed");
            }
            long revision = live ? AuthoringEnvironment.Live.Revision : AuthoringEnvironment.Author.Revision;
            if (Integer(request, "expectedRevision") != revision)
            {
                throw new InvalidOperationException("source_conflict: stale expectedRevision");
            }
        }
        static void RequireAuthor(bool live)
        {
            if (live)
            {
                throw new InvalidOperationException("read_only_asset: structural/source operations require authoring mode");
            }
        }
        ElementSelectionProxy Target(Dictionary<string, object> request, bool live)
        {
            string id = Text(request, "target");
            if (!live)
            {
                AuthoringEnvironment.Author.Model.Find(id);
                return ElementSelectionProxy.ForAuthor(id);
            }
            EnsureLiveCache();
            if (!nodes.TryGetValue(id, out var node) || !node.IsValid)
            {
                throw new InvalidOperationException("stale_node: query hierarchy before editing a live node");
            }
            return ElementSelectionProxy.ForLive(node, AuthoringEnvironment.Live);
        }
        object Children(Dictionary<string, object> request, bool live)
        {
            int offset = checked((int)Integer(request, "offset", 0));
            int limit = checked((int)Integer(request, "limit", 100));
            if (offset < 0 || limit < 1 || limit > 500)
            {
                throw new InvalidOperationException("bad_request: pagination outside supported range");
            }
            string target = Text(request, "target", false);
            var result = new List<object>();
            int count;
            if (live)
            {
                var root = target == null ? RuntimeInspectionBridge.Root(RequireLiveDocument()) : Target(request, true).Node;
                count = root.ChildCount;
                for (int i = offset; i < count && i < offset + limit; i++)
                {
                    result.Add(LiveNode(root.Child(i)));
                }
                if (target == null)
                {
                    return Obj("root", LiveNode(root), "nodes", result, "total", count, "offset", offset);
                }
            }
            else
            {
                var source = AuthoringEnvironment.Author.Model ?? throw new InvalidOperationException("Open a document first.");
                var children = target == null ? source.Roots : source.Find(target).Children;
                count = children.Count;
                for (int i = offset; i < count && i < offset + limit; i++)
                {
                    result.Add(SourceNode(children[i]));
                }
            }
            return Obj("nodes", result, "total", count, "offset", offset);
        }
        object Find(Dictionary<string, object> request, bool live)
        {
            string name = Text(request, "name");
            var result = new List<object>();
            if (live)
            {
                foreach (var element in RuntimeInspectionBridge.GetElement(RuntimeInspectionBridge.Root(RequireLiveDocument())).Query(name))
                {
                    result.Add(LiveNode(RuntimeInspectionBridge.Inspect(element)));
                    if (result.Count == 500)
                    {
                        break;
                    }
                }
            }
            else
            {
                foreach (var node in AuthoringEnvironment.Author.Model.Nodes)
                {
                    if (node.Attribute("id") == name)
                    {
                        result.Add(SourceNode(node));
                        if (result.Count == 500)
                        {
                            break;
                        }
                    }
                }
            }
            return Obj("nodes", result, "truncated", result.Count == 500);
        }
        object LiveNode(InspectedNode node)
        {
            EnsureLiveCache();
            string id = cachedLiveSession + "/" + node.Id;
            nodes[id] = node;
            return Obj("id", id, "tag", node.Tag, "name", node.Name, "childCount", node.ChildCount, "hasLayout", node.HasLayout);
        }
        void EnsureLiveCache()
        {
            string current = AuthoringEnvironment.Live?.Id;
            if (cachedLiveSession != current)
            {
                nodes.Clear();
                cachedLiveSession = current;
                liveRenderedRevision = -1;
                liveFrame = 0;
            }
            else if (nodes.Count > 512)
            {
                List<string> stale = null;
                foreach (var pair in nodes)
                {
                    if (!pair.Value.IsValid)
                    {
                        (stale ??= new List<string>()).Add(pair.Key);
                    }
                }
                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        nodes.Remove(stale[i]);
                    }
                }
            }
        }
        static object SourceNode(SourceDocument.Node node) => Obj("id", node.Key, "tag", node.Tag, "name", node.Attribute("id"), "childCount", node.Children.Count, "sourceStart", node.Start, "sourceEnd", node.End);
        object ReadProperties(Dictionary<string, object> request, ElementSelectionProxy target)
        {
            var properties = new List<string>();
            if (request.TryGetValue("properties", out var list) && list is List<object> values)
            {
                foreach (var value in values)
                {
                    if (!(value is string name))
                    {
                        throw new InvalidOperationException("bad_request: properties must contain strings");
                    }
                    properties.Add(name);
                }
            }
            else
            {
                properties.AddRange(new[] { "attr.id", "attr.class", "text", "style.position", "style.left", "style.top", "style.width", "style.height", "style.opacity" });
            }
            if (properties.Count > 500)
            {
                throw new InvalidOperationException("bad_request: property limit");
            }
            var result = new List<object>();
            foreach (string name in properties)
            {
                PropertySchema.Require(name);
                try
                {
                    result.Add(Obj("property", name, "value", target.Read(name), "computed", name.StartsWith("style.", StringComparison.Ordinal) ? target.Node?.Computed(name.Substring(6)) : null, "origin", name.StartsWith("style.", StringComparison.Ordinal) ? target.Node?.Origin(name.Substring(6)) : null));
                }
                catch (InvalidOperationException error)
                {
                    result.Add(Obj("property", name, "error", error.Message));
                }
            }
            return result;
        }
        object SetProperties(Dictionary<string, object> request, bool live)
        {
            var requests = new List<Dictionary<string, object>>();
            if (request.TryGetValue("edits", out var list) && list is List<object> edits)
            {
                foreach (var item in edits)
                {
                    requests.Add(item as Dictionary<string, object> ?? throw new InvalidOperationException("bad_request: edits must contain objects"));
                }
            }
            else
            {
                requests.Add(request);
            }
            if (requests.Count == 0 || requests.Count > 500)
            {
                throw new InvalidOperationException("bad_request: edit limit");
            }
            var sourceChanges = new List<AuthoringEdit>();
            var liveChanges = new List<LiveEdit>();
            foreach (var item in requests)
            {
                string property = Text(item, "property");
                PropertySchema.Require(property);
                string value = Boolean(item, "unset") ? null : Text(item, "value");
                var target = Target(item, live);
                if (live)
                {
                    liveChanges.Add(new LiveEdit(target.Node, property, value));
                }
                else
                {
                    sourceChanges.Add(new AuthoringEdit
                    {
                        Node = target.SourceKey,
                        Property = property,
                        Value = value
                    });
                }
            }
            if (live)
            {
                SessionUndoBridge.Record(AuthoringEnvironment.Live, () => AuthoringEnvironment.Live.Execute(liveChanges, AuthoringEnvironment.Live.Revision));
                Render(true);
            }
            else if (Animation.AnimationEnvironment.Recording)
            {
                if (sourceChanges.Count == 0)
                {
                    throw new InvalidOperationException("bad_request: no keys");
                }
                var proxy = ElementSelectionProxy.ForAuthor(sourceChanges[0].Node);
                Animation.AnimationEnvironment.Record(proxy, sourceChanges);
            }
            else if (AuthoringEnvironment.Mode == WorkspaceMode.Animation)
            {
                if (sourceChanges.Any(change => change.Node != sourceChanges[0].Node))
                {
                    throw new InvalidOperationException("临时动画预览请一次编辑一个节点。");
                }
                Animation.AnimationEnvironment.PreviewEdits(ElementSelectionProxy.ForAuthor(sourceChanges[0].Node), sourceChanges);
            }
            else
            {
                SessionUndoBridge.Record(AuthoringEnvironment.Author, () => AuthoringEnvironment.Author.Execute(sourceChanges, AuthoringEnvironment.Author.Revision));
            }
            return Obj("changed", requests.Count);
        }
        static URUI.Documents.Document RequireLiveDocument() => AuthoringEnvironment.LiveDocument && AuthoringEnvironment.LiveDocument.IsLoaded ? AuthoringEnvironment.LiveDocument : throw new InvalidOperationException("stale_node: select a loaded document");
        void Render(bool live)
        {
            if (live)
            {
                RuntimeInspectionBridge.Refresh(RequireLiveDocument());
                liveRenderedRevision = AuthoringEnvironment.Live.Revision;
            }
            else
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    throw new InvalidOperationException("preview_unavailable: authoring preview yields to Play Mode");
                }
                AuthoringEnvironment.EnsurePreview();
                AuthoringEnvironment.Preview.Refresh();
            }
        }
        const int MaxCaptureFiles = 64;

        object Capture(bool live)
        {
            string folder = Path.Combine(AuthoringEnvironment.ProjectRoot, "Library/URUI/Authoring/Captures");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
            Render(live);
            if (live)
            {
                using (var pixels = RuntimeInspectionBridge.CaptureLive(RequireLiveDocument()))
                {
                    File.WriteAllBytes(path, pixels.Texture.EncodeToPNG());
                    liveFrame = pixels.FrameId;
                }
            }
            else
            {
                AuthoringEnvironment.Preview.Capture(path);
            }
            PruneOldCaptures(folder, path);
            return Obj("path", path);
        }

        static void PruneOldCaptures(string folder, string newestPath)
        {
            try
            {
                string[] files = Directory.GetFiles(folder, "*.png");
                if (files.Length <= MaxCaptureFiles)
                {
                    return;
                }
                Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
                int toDelete = files.Length - MaxCaptureFiles;
                for (int i = 0; i < files.Length && toDelete > 0; i++)
                {
                    if (string.Equals(files[i], newestPath, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    try
                    {
                        File.Delete(files[i]);
                        toDelete--;
                    }
                    catch { }
                }
            }
            catch { }
        }
        object ApplyLive(Dictionary<string, object> request, bool live)
        {
            if (!live)
            {
                throw new InvalidOperationException("bad_request: persistence.apply requires live mode");
            }
            var target = Target(request, true);
            var selected = new HashSet<string>();
            if (!(request.TryGetValue("properties", out var properties) && properties is List<object> names) || names.Count == 0)
            {
                throw new InvalidOperationException("bad_request: explicitly select properties to apply");
            }
            foreach (var name in names)
            {
                selected.Add(name as string ?? throw new InvalidOperationException("bad_request: property must be a string"));
            }
            string path = target.Node.Document.Source ? AssetDatabase.GetAssetPath(target.Node.Document.Source) : target.Node.Document.SourceUrl.TrimStart('/');
            var source = new AuthoringSession(AuthoringEnvironment.ProjectRoot);
            source.Open(path);
            foreach (var file in AuthoringEnvironment.Author.Workspace.Files)
            {
                if (file.Path == source.File.Path && file.Dirty)
                {
                    throw new InvalidOperationException("source_conflict: unsaved authoring changes");
                }
            }
            var changes = new List<LiveEdit>();
            foreach (var change in AuthoringEnvironment.Live.Changes())
            {
                if (change.Node.Id == target.Node.Id && selected.Contains(change.Property))
                {
                    changes.Add(change);
                }
            }
            if (changes.Count != selected.Count)
            {
                throw new InvalidOperationException("bad_request: selected property is not a current Live change");
            }
            source.Apply(changes, new SourceMap(target.Node.Document, source.Model), source.Revision);
            source.Save();
            return Obj("path", source.File.Path, "applied", changes.Count);
        }
        Dictionary<string, object> Result(object data, string mode = "authoring") => Obj("ok", true, "apiVersion", 1, "editorSession", editorSession, "mode", mode,
            "session", mode == "live" ? AuthoringEnvironment.Live?.Id : AuthoringEnvironment.Author.Id, "sourceRevision", AuthoringEnvironment.Author.Revision, "liveRevision", AuthoringEnvironment.Live?.Revision ?? 0,
            "renderedRevision", mode == "live" ? liveRenderedRevision : AuthoringEnvironment.Preview?.RenderedRevision ?? -1, "frameId", mode == "live" ? liveFrame : AuthoringEnvironment.Preview?.FrameId ?? 0, "diagnostics", AuthoringEnvironment.Status, "workspaceMode", AuthoringEnvironment.Mode.ToString().ToLowerInvariant(), "data", data);
        public static Dictionary<string, object> Obj(params object[] pairs)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i += 2)
            {
                result.Add((string)pairs[i], pairs[i + 1]);
            }
            return result;
        }
        public static string Text(Dictionary<string, object> map, string key, bool required = true)
        {
            if (map.TryGetValue(key, out var value) && value is string text)
            {
                return text;
            }
            if (required || map.ContainsKey(key))
            {
                throw new InvalidOperationException("bad_request: " + key + " must be a string");
            }
            return null;
        }
        public static long Integer(Dictionary<string, object> map, string key, long fallback = long.MinValue)
        {
            if (map.TryGetValue(key, out var value) && value is long number)
            {
                return number;
            }
            if (!map.ContainsKey(key) && fallback != long.MinValue)
            {
                return fallback;
            }
            throw new InvalidOperationException("bad_request: " + key + " must be an integer");
        }
        public static bool Boolean(Dictionary<string, object> map, string key)
        {
            if (!map.TryGetValue(key, out var value))
            {
                return false;
            }
            if (value is bool result)
            {
                return result;
            }
            throw new InvalidOperationException("bad_request: " + key + " must be a boolean");
        }
        static float Number(Dictionary<string, object> map, string key)
        {
            if (!map.TryGetValue(key, out var value) || (value is not long && value is not double))
            {
                throw new InvalidOperationException("bad_request: " + key + " must be a number");
            }
            float result = Convert.ToSingle(value, CultureInfo.InvariantCulture);
            if (!float.IsFinite(result))
            {
                throw new InvalidOperationException("bad_request: finite number required");
            }
            return result;
        }
    }
}
