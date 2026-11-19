using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using URUI.Authoring.Sessions;
using URUI.Editor.Inspection;

namespace URUI.Authoring.Windows
{
    /// <summary>
    /// 画布手势只临时覆盖自有预览，结束后发布一笔作者命令；取消不产生源码或撤销记录。
    /// </summary>
    public sealed class CanvasGesture : IDisposable
    {
        readonly ElementSelectionProxy proxy;
        readonly InspectedNode node;
        readonly long revision;
        readonly string target;
        readonly Dictionary<string, string> originals = new Dictionary<string, string>();
        readonly Dictionary<string, string> applied = new Dictionary<string, string>();
        List<AuthoringEdit> edits;
        bool ended;
        readonly WorkspaceMode mode;
        readonly bool recording;
        readonly long recordingEpoch;
        public CanvasGesture(ElementSelectionProxy proxy)
        {
            if (!proxy || proxy.IsLive || !proxy.IsTargetValid || proxy.Node == null)
            {
                throw new InvalidOperationException("请选择有效源码节点。");
            }
            mode = AuthoringEnvironment.Mode;
            recording = Animation.AnimationEnvironment.Recording;
            recordingEpoch = Animation.AnimationEnvironment.RecordingEpoch;
            Animation.AnimationEnvironment.PrepareGesture();
            this.proxy = proxy;
            node = proxy.Node;
            revision = proxy.Revision;
            target = proxy.TargetId;
        }
        public void Preview(IReadOnlyList<AuthoringEdit> values)
        {
            Check();
            var candidates = new List<AuthoringEdit>();
            foreach (var edit in values)
            {
                if (edit.Node != proxy.SourceKey || !edit.Property.StartsWith("style.", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("手势只能修改当前节点样式。");
                }
                if (!originals.ContainsKey(edit.Property))
                {
                    originals.Add(edit.Property, node.Read(edit.Property));
                }
                candidates.Add(edit);
            }
            foreach (var edit in candidates)
            {
                node.Write(edit.Property, edit.Value);
                applied[edit.Property] = node.Read(edit.Property);
            }
            edits = candidates;
            RuntimeInspectionBridge.Refresh(node.Document);
            AuthoringEnvironment.Preview?.Refresh();
        }
        public void Commit()
        {
            Check();
            Restore();
            ended = true;
            if (edits == null || edits.Count == 0)
            {
                return;
            }
            if (recording)
            {
                Animation.AnimationEnvironment.Record(proxy, edits);
            }
            else if (mode == WorkspaceMode.Animation)
            {
                Animation.AnimationEnvironment.PreviewEdits(proxy, edits);
            }
            else
            {
                var author = AuthoringEnvironment.Author;
                SessionUndoBridge.Record(author, () => author.Execute(edits, revision));
            }
        }
        void Check()
        {
            if (AuthoringEnvironment.Mode != mode || (recording && (!Animation.AnimationEnvironment.Recording || Animation.AnimationEnvironment.RecordingEpoch != recordingEpoch)))
            {
                throw new InvalidOperationException("手势的动画/录制上下文已变化，请重新开始。");
            }
            if (ended || !proxy || !proxy.IsTargetValid || proxy.TargetId != target || proxy.Revision != revision || !node.IsValid)
            {
                throw new InvalidOperationException("手势目标或源码版本已变化，请重新拖动。");
            }
        }
        void Restore()
        {
            if (!node.IsValid)
            {
                return;
            }
            foreach (var pair in originals)
            {
                if (applied.TryGetValue(pair.Key, out var owned) && node.Read(pair.Key) == owned)
                {
                    node.Write(pair.Key, pair.Value);
                }
            }
            RuntimeInspectionBridge.Refresh(node.Document);
            AuthoringEnvironment.Preview?.Refresh();
        }
        public void Dispose()
        {
            if (ended)
            {
                return;
            }
            ended = true;
            Restore();
        }
    }
}
