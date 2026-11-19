using System;
using System.Collections.Generic;
using URUI.Editor.Inspection;

namespace URUI.Authoring.Sessions
{
    public sealed class LiveEdit
    {
        public InspectedNode Node;
        public string Property;
        public string Value;
        public LiveEdit(InspectedNode node, string property, string value)
        {
            Node = node;
            Property = property;
            Value = value;
        }
    }
    /// <summary>
    /// 仅作用于真实节点的临时命令日志，不加载或卸载业务文档。
    /// </summary>
    public sealed class LiveSession : IDisposable
    {
        sealed class Change
        {
            internal InspectedNode Node;
            internal string Property;
            internal string Before;
            internal string After;
        }
        readonly List<Change> baseline = new List<Change>();
        readonly List<List<Change>> history = new List<List<Change>>();
        readonly List<string> restoreDiagnostics = new List<string>();
        int cursor;
        bool disposed;
        public string Id
        {
            get;
        } = Guid.NewGuid().ToString("N");
        public bool IsAlive => !disposed;
        public long Revision
        {
            get;
            private set;
        }
        public int HistoryPosition => cursor;
        public bool CanUndo => cursor > 0;
        public bool CanRedo => cursor < history.Count;
        /// <summary>
        /// 最近一次重置中不能安全恢复的属性；其他目标仍独立恢复。
        /// </summary>
        public IReadOnlyList<string> RestoreDiagnostics => restoreDiagnostics.AsReadOnly();
        public void Set(InspectedNode node, string property, string value, long expectedRevision)
            => Execute(new[] { new LiveEdit(node, property, value) }, expectedRevision);

        public void Execute(IReadOnlyList<LiveEdit> edits, long expectedRevision)
        {
            if (disposed)
            {
                throw new InvalidOperationException("operation_cancelled: session ended");
            }
            if (Revision != expectedRevision)
            {
                throw new InvalidOperationException("source_conflict: stale live revision");
            }
            var changes = new List<Change>();
            var seen = new HashSet<string>();
            foreach (var edit in edits)
            {
                if (edit.Node == null || !edit.Node.IsValid)
                {
                    throw new InvalidOperationException("stale_node");
                }
                if (!seen.Add(edit.Node.Id + "/" + edit.Property))
                {
                    throw new InvalidOperationException("Duplicate property in batch.");
                }
                string before = edit.Node.Read(edit.Property);
                changes.Add(new Change
                {
                    Node = edit.Node,
                    Property = edit.Property,
                    Before = before,
                    After = edit.Value
                });
            }
            int applied = 0;
            try
            {
                foreach (var change in changes)
                {
                    change.Node.Write(change.Property, change.After);
                    applied++;
                }
            }
            catch
            {
                for (int i = applied - 1; i >= 0; i--)
                {
                    if (changes[i].Node.IsValid)
                    {
                        changes[i].Node.Write(changes[i].Property, changes[i].Before);
                    }
                }
                throw;
            }
            foreach (var change in changes)
            {
                bool exists = false;
                foreach (var old in baseline)
                {
                    if (old.Node.Id == change.Node.Id && old.Property == change.Property)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    baseline.Add(change);
                }
            }
            if (cursor < history.Count)
            {
                history.RemoveRange(cursor, history.Count - cursor);
            }
            history.Add(changes);
            cursor++;
            Revision++;
        }
        public void Undo()
        {
            if (CanUndo)
            {
                Replay(history[cursor - 1], false);
                cursor--;
                Revision++;
            }
        }
        public void Redo()
        {
            if (CanRedo)
            {
                Replay(history[cursor], true);
                cursor++;
                Revision++;
            }
        }
        static void Replay(List<Change> changes, bool forward)
        {
            foreach (var change in changes)
            {
                if (!change.Node.IsValid)
                {
                    throw new InvalidOperationException("stale_node: undo target expired");
                }
            }
            foreach (var change in changes)
            {
                change.Node.Write(change.Property, forward ? change.After : change.Before);
            }
        }
        public IReadOnlyList<LiveEdit> Changes()
        {
            var result = new List<LiveEdit>();
            foreach (var change in baseline)
            {
                if (change.Node.IsValid && change.Node.Read(change.Property) != change.Before)
                {
                    result.Add(new LiveEdit(change.Node, change.Property, change.Node.Read(change.Property)));
                }
            }
            return result;
        }
        public void Reset()
        {
            restoreDiagnostics.Clear();
            for (int i = baseline.Count - 1; i >= 0; i--)
            {
                var change = baseline[i];
                try
                {
                    if (change.Node.IsValid)
                    {
                        change.Node.Write(change.Property, change.Before);
                    }
                    else if (change.Property == "image")
                    {
                        change.Node.ReleaseImageOverride();
                    }
                }
                catch (Exception error)
                {
                    restoreDiagnostics.Add(change.Node.Id + "/" + change.Property + ": " + error.Message);
                    if (change.Property == "image")
                    {
                        try
                        {
                            change.Node.ReleaseImageOverride();
                        }
                        catch (Exception releaseError)
                        {
                            restoreDiagnostics.Add("Image lease: " + releaseError.Message);
                        }
                    }
                }
            }
            baseline.Clear();
            history.Clear();
            cursor = 0;
            Revision++;
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            try
            {
                Reset();
            }
            finally
            {
                disposed = true;
            }
        }
    }
}
