using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using URUI.Animation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Windows;
using URUI.Editor.Inspection;

namespace URUI.Authoring.Animation
{
    /// <summary>
    /// 时间轴预览与录制的单一所有者。只在显式预览时接管姿态，切换源码/模式/生命周期时释放。
    /// </summary>
    [InitializeOnLoad]
    public static class AnimationEnvironment
    {
        public static AnimationWorkspace Workspace
        {
            get;
        }
        public static string SelectedTrack
        {
            get;
            private set;
        }
        public static string SoloTrack
        {
            get;
            private set;
        }
        public static double Time
        {
            get;
            private set;
        }
        public static bool Playing
        {
            get;
            private set;
        }
        public static bool Recording
        {
            get;
            private set;
        }
        public static bool Snap = true;
        public static double FrameRate = 60;
        static SequencePlayer player;
        static URUI.Documents.Document boundDocument;
        sealed class TemporaryPose
        {
            internal InspectedNode Node;
            internal readonly Dictionary<string, string> Original = new Dictionary<string, string>();
            internal readonly Dictionary<string, string> Applied = new Dictionary<string, string>();
        }
        static readonly List<TemporaryPose> temporaryPoses = new List<TemporaryPose>();
        internal static long RecordingEpoch
        {
            get;
            private set;
        }
        static string page;
        static string recordingTarget;
        static long recordingRevision;
        static bool recordingWrite;
        static double lastTick;
        static Preview.PreviewSession attachedPreview;
        static AnimationEnvironment()
        {
            Workspace = new AnimationWorkspace(AuthoringEnvironment.Author);
            AuthoringEnvironment.Author.Changed += OnSourceChanged;
            AuthoringEnvironment.NodeSelectionChanged += () =>
            {
                if (Recording && (AuthoringEnvironment.ActiveNode)?.TargetId != recordingTarget)
                {
                    EndRecording();
                }
            };
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.playModeStateChanged += _ => Stop();
            EditorApplication.update += Tick;
            lastTick = EditorApplication.timeSinceStartup;
        }
        static void OnSourceChanged()
        {
            ReleasePlayer();
            RestoreTemporary();
            Playing = false;
            if (!recordingWrite)
            {
                EndRecording();
            }
        }
        public static void EnsureWorkspace()
        {
            var author = AuthoringEnvironment.Author;
            if (author.File == null)
            {
                return;
            }
            if (page != author.File.Path)
            {
                Stop();
                SelectedTrack = SoloTrack = null;
                Time = 0;
                page = author.File.Path;
                string candidate = System.IO.Path.ChangeExtension(page, ".urui-sequence.json");
                if (System.IO.File.Exists(candidate) || author.Workspace.Files.Any(f => f.Path == candidate && f.Text != null))
                {
                    Workspace.Open(candidate);
                }
                else
                {
                    Workspace.Close();
                }
            }
        }
        public static void SelectTrack(string id)
        {
            EndRecording();
            SelectedTrack = id;
            var definition = Workspace.Definition;
            var track = definition?.Tracks.FirstOrDefault(t => t.Id == id);
            if (track == null || EditorApplication.isPlaying)
            {
                return;
            }
            var node = ResolveSourceTarget(track.Target);
            if (node != null)
            {
                AuthoringEnvironment.Select(ElementSelectionProxy.ForAuthor(node.Key));
            }
        }
        internal static Source.SourceDocument.Node ResolveSourceTarget(string binding)
        {
            var model = AuthoringEnvironment.Author.Model;
            if (model == null)
            {
                return null;
            }
            var matches = model.Nodes.Where(n => binding == "$root" ? n.Tag == "body" : n.Attribute("id") == binding).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        public static void SetSolo(string id)
        {
            Stop();
            SoloTrack = SoloTrack == id ? null : id;
            Sample(Time);
        }
        static void EnsurePlayer()
        {
            EnsureWorkspace();
            var expectedDocument = EditorApplication.isPlaying ? AuthoringEnvironment.LiveDocument : AuthoringEnvironment.Preview?.Document;
            if (player != null && boundDocument != expectedDocument)
            {
                ReleasePlayer();
            }
            if (player != null)
            {
                return;
            }
            var definition = Workspace.Definition ?? throw new InvalidOperationException("请先给选中节点添加动画预设。");
            if (SoloTrack != null)
            {
                foreach (var track in definition.Tracks)
                {
                    if (track.Id != SoloTrack)
                    {
                        track.Muted = true;
                    }
                }
            }
            URUI.Documents.Document document;
            if (EditorApplication.isPlaying)
            {
                document = AuthoringEnvironment.LiveDocument;
            }
            else
            {
                AuthoringEnvironment.EnsurePreview();
                var preview = AuthoringEnvironment.Preview;
                preview.Playing = false;
                preview.Refresh();
                if (attachedPreview != preview)
                {
                    if (attachedPreview != null)
                    {
                        attachedPreview.BeforeReload -= BeforePreviewReload;
                    }
                    attachedPreview = preview;
                    preview.BeforeReload += BeforePreviewReload;
                }
                document = preview.Document;
            }
            if (!document || !document.IsLoaded)
            {
                throw new InvalidOperationException("请选择已加载的 URUI 页面。");
            }
            RuntimeInspectionBridge.Refresh(document);
            player = new SequencePlayer(document.Body, definition);
            boundDocument = document;
        }
        public static void Sample(double time)
        {
            RestoreTemporary();
            EnsurePlayer();
            player.Seek(time);
            Time = player.Time;
            if (EditorApplication.isPlaying)
            {
                RuntimeInspectionBridge.Refresh(AuthoringEnvironment.LiveDocument);
            }
            else
            {
                AuthoringEnvironment.Preview.Refresh();
            }
        }
        public static void Seek(double time)
        {
            Playing = false;
            EndRecording();
            Sample(Math.Max(0, time));
        }
        public static void Play()
        {
            EndRecording();
            EnsurePlayer();
            if (Time >= player.Duration)
            {
                Time = 0;
            }
            Sample(Time);
            Playing = true;
            lastTick = EditorApplication.timeSinceStartup;
        }
        public static void Pause()
        {
            Playing = false;
        }
        public static void Stop()
        {
            Playing = false;
            EndRecording();
            ReleasePlayer();
            RestoreTemporary();
            Time = 0;
            if (EditorApplication.isPlaying)
            {
                if (AuthoringEnvironment.LiveDocument && AuthoringEnvironment.LiveDocument.IsLoaded)
                {
                    RuntimeInspectionBridge.Refresh(AuthoringEnvironment.LiveDocument);
                }
            }
            else if (!EditorApplication.isPlayingOrWillChangePlaymode && AuthoringEnvironment.Preview?.Document && AuthoringEnvironment.Preview.Document.IsLoaded && AuthoringEnvironment.Preview.RenderedRevision == AuthoringEnvironment.Author.Revision)
            {
                AuthoringEnvironment.Preview.Refresh();
            }
        }
        static void BeforePreviewReload()
        {
            ReleasePlayer();
            RestoreTemporary();
            Playing = false;
            if (!recordingWrite)
            {
                EndRecording();
            }
        }
        static void ReleasePlayer()
        {
            var owned = player;
            player = null;
            boundDocument = null;
            owned?.Dispose();
        }
        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            double delta = Math.Max(0, now - lastTick);
            lastTick = now;
            if (!Playing || !AuthoringEnvironment.HasObservers)
            {
                return;
            }
            try
            {
                var definition = Workspace.Definition;
                if (definition == null)
                {
                    Stop();
                    return;
                }
                double next = Time + delta;
                Sample(next);
                if (!definition.Loop && next >= definition.Duration)
                {
                    Playing = false;
                }
            }
            catch (Exception error)
            {
                Stop();
                AuthoringEnvironment.Report(error.Message);
            }
        }
        public static void BeginRecording(ElementSelectionProxy proxy)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("录制需要 Edit Mode。");
            }
            EnsureWorkspace();
            if (!proxy || proxy.IsLive || !proxy.IsTargetValid)
            {
                throw new InvalidOperationException("录制需要编辑模式下的源码节点。");
            }
            var track = Workspace.Definition?.Tracks.FirstOrDefault(t => t.Id == SelectedTrack);
            if (track == null || track.Kind != "css" || track.Locked || track.Muted)
            {
                throw new InvalidOperationException("请选择未锁定、未静音的 CSS 轨道。");
            }
            if (proxy.SourceNode.Attribute("id") != track.Target)
            {
                throw new InvalidOperationException("选中节点与动画轨道不一致。");
            }
            double time = Time;
            AuthoringEnvironment.SetMode(WorkspaceMode.Animation);
            Sample(time);
            RecordingEpoch++;
            Recording = true;
            recordingTarget = proxy.TargetId;
            recordingRevision = Workspace.Revision;
        }
        public static void EndRecording()
        {
            if (Recording)
            {
                RecordingEpoch++;
            }
            Recording = false;
            recordingTarget = null;
        }
        internal static void DocumentChanged(URUI.Documents.Document document)
        {
            if (!ReferenceEquals(boundDocument, null) && boundDocument != document)
            {
                Stop();
            }
        }
        internal static void PrepareGesture()
        {
            if (AuthoringEnvironment.Mode == WorkspaceMode.Animation)
            {
                FreezePose();
            }
        }
        static void FreezePose()
        {
            Playing = false;
            if (player == null)
            {
                return;
            }
            var values = new List<(InspectedNode node, string property, string value)>();
            var definition = Workspace.Definition;
            var sources = new AnimationSourceSet(AuthoringEnvironment.Author);
            if (definition != null)
            {
                foreach (var track in definition.Tracks.Where(t => !t.Muted && t.Kind == "css"))
                {
                    var element = track.Target == "$root" ? boundDocument.Body : boundDocument.Q(track.Target);
                    if (element == null || !element.IsValid)
                    {
                        continue;
                    }
                    var node = RuntimeInspectionBridge.Inspect(element);
                    var names = new HashSet<string>();
                    foreach (var key in Source.CssKeyframes.Read(sources.Find(track.Keyframes).Text, track.Keyframes))
                    {
                        foreach (var declaration in Source.CssSource.Declarations(key.Declarations))
                        {
                            foreach (var name in AnimationWorkspace.Expanded(declaration.Name))
                            {
                                names.Add(name);
                            }
                        }
                    }
                    foreach (var name in names)
                    {
                        if (node.Origin(name) == "Animation")
                        {
                            values.Add((node, "style." + name, node.Computed(name)));
                        }
                    }
                }
            }
            ReleasePlayer();
            foreach (var value in values)
            {
                if (value.value != null)
                {
                    SetTemporary(value.node, value.property, value.value);
                }
            }
        }
        internal static void PreviewEdits(ElementSelectionProxy proxy, IReadOnlyList<AuthoringEdit> edits)
        {
            if (proxy.IsLive || !proxy.IsTargetValid || proxy.Node == null)
            {
                throw new InvalidOperationException("动画临时预览需要当前源码节点。");
            }
            foreach (var edit in edits)
            {
                if (edit.Node != proxy.SourceKey || !CanAnimate(edit.Property))
                {
                    throw new InvalidOperationException("请在布局模式修改此属性：" + edit.Property);
                }
            }
            FreezePose();
            foreach (var edit in edits)
            {
                SetTemporary(proxy.Node, edit.Property, edit.Value);
            }
            AuthoringEnvironment.Preview?.Refresh();
            AuthoringEnvironment.Report("动画临时预览：未写入源文件。开启录制后才保存关键帧。");
        }
        static void SetTemporary(InspectedNode node, string property, string value)
        {
            var pose = temporaryPoses.FirstOrDefault(p => p.Node.Id == node.Id);
            if (pose == null)
            {
                pose = new TemporaryPose { Node = node };
                temporaryPoses.Add(pose);
            }
            if (!pose.Original.ContainsKey(property))
            {
                pose.Original.Add(property, node.Read(property));
            }
            node.Write(property, value);
            pose.Applied[property] = node.Read(property);
        }
        static void RestoreTemporary()
        {
            var owned = temporaryPoses.ToArray();
            temporaryPoses.Clear();
            foreach (var pose in owned)
            {
                if (pose.Node.IsValid)
                {
                    foreach (var pair in pose.Original)
                    {
                        if (pose.Applied.TryGetValue(pair.Key, out var applied) && pose.Node.Read(pair.Key) == applied)
                        {
                            pose.Node.Write(pair.Key, pair.Value);
                        }
                    }
                }
            }
        }
        public static bool TryRecord(ElementSelectionProxy proxy, string property, string value)
        {
            if (!Recording)
            {
                return false;
            }
            Record(proxy, new[] { new AuthoringEdit {
Node = proxy.SourceKey,
Property = property,
Value = value
} });
            return true;
        }
        public static void Record(ElementSelectionProxy proxy, IReadOnlyList<AuthoringEdit> edits)
        {
            if (!Recording || !proxy.IsTargetValid || proxy.TargetId != recordingTarget || Workspace.Revision != recordingRevision)
            {
                throw new InvalidOperationException("录制上下文已变化，请重新开始录制。");
            }
            var track = Workspace.Definition.Tracks.First(t => t.Id == SelectedTrack);
            if (Time < track.Start || Time > track.Start + track.Duration)
            {
                throw new InvalidOperationException("请在轨道的首轮区间内录制关键帧。");
            }
            double position = track.Duration == 0 ? 1 : (Time - track.Start) / track.Duration;
            if (Snap && track.Duration > 0)
            {
                position = Math.Round((Time - track.Start) * FrameRate) / FrameRate / track.Duration;
            }
            position = Math.Max(0, Math.Min(1, position));
            var file = Workspace.KeyframeFile(track);
            string text = file.Text;
            foreach (var edit in edits)
            {
                if (edit.Node != proxy.SourceKey || !CanAnimate(edit.Property) || edit.Value == null)
                {
                    throw new InvalidOperationException("该属性不能录入当前动画：" + edit.Property);
                }
                text = Source.CssKeyframes.SetKey(text, track.Keyframes, position, edit.Property.Substring(6), edit.Value);
            }
            recordingWrite = true;
            try
            {
                string next = text;
                SessionUndoBridge.Record(AuthoringEnvironment.Author, () => Workspace.PublishKeys(new[] { new Source.SourceChange(file.Path, next, file.Revision) }, recordingRevision));
                recordingRevision = Workspace.Revision;
                Sample(Time);
            }
            finally
            {
                recordingWrite = false;
            }
        }
        public static bool CanAnimate(string property)
        {
            if (!property.StartsWith("style.", StringComparison.Ordinal))
            {
                return false;
            }
            string name = property.Substring(6);
            return name.StartsWith("--", StringComparison.Ordinal) || new[] { "opacity", "transform", "left", "top", "right", "bottom", "width", "height", "color", "background-color", "border-color", "border-width", "border-radius", "font-size", "margin-left", "margin-top", "margin-right", "margin-bottom", "padding-left", "padding-top", "padding-right", "padding-bottom" }.Contains(name);
        }
    }
}
