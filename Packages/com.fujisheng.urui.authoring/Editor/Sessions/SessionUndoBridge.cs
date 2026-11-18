using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace URUI.Authoring.Sessions
{
    /// <summary>Unity 撤销只记录命令游标；实际 DOM/源码变更由所属会话重放。</summary>
    [InitializeOnLoad]
    public static class SessionUndoBridge
    {
        sealed class Cursor : ScriptableObject { public int position; }
        sealed class Entry { internal Cursor Cursor; internal Func<int> Position; internal Action Undo, Redo; internal int LastCursor; }
        static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        static SessionUndoBridge() { UnityEditor.Undo.undoRedoPerformed += Replay; AssemblyReloadEvents.beforeAssemblyReload += Clear; }
        public static void Record(AuthoringSession session, Action action) => Record(session.Id, () => session.Workspace.HistoryPosition, session.Undo, session.Redo, action);
        public static void Record(LiveSession session, Action action) => Record(session.Id, () => session.HistoryPosition, session.Undo, session.Redo, action);
        static void Record(string id, Func<int> position, Action undo, Action redo, Action action)
        {
            if (!entries.TryGetValue(id, out var entry))
            {
                var cursor = ScriptableObject.CreateInstance<Cursor>(); cursor.hideFlags = HideFlags.HideAndDontSave;
                entry = new Entry { Cursor = cursor, Position = position, Undo = undo, Redo = redo }; entries[id] = entry;
            }
            entry.Cursor.position = position(); entry.LastCursor = entry.Cursor.position;
            UnityEditor.Undo.IncrementCurrentGroup();
            UnityEditor.Undo.RecordObject(entry.Cursor, "Edit URUI");
            action(); entry.Cursor.position = position(); entry.LastCursor = entry.Cursor.position; UnityEditor.Undo.FlushUndoRecordObjects();
        }
        static void Replay()
        {
            foreach (var entry in entries.Values)
                try
                {
                    // An unrelated asset undo must not rewind commands that bypassed Unity's cursor.
                    if (entry.Cursor.position == entry.LastCursor) continue;
                    while (entry.Position() != entry.Cursor.position)
                    {
                        int before = entry.Position();
                        if (before > entry.Cursor.position) entry.Undo(); else entry.Redo();
                        if (before == entry.Position()) throw new InvalidOperationException("URUI undo history expired after a source/session reload.");
                    }
                    entry.LastCursor = entry.Cursor.position;
                }
                catch (Exception error) { entry.Cursor.position = entry.Position(); entry.LastCursor = entry.Cursor.position; AuthoringEnvironment.Report(error.Message); }
        }
        public static void Clear()
        { foreach (var entry in entries.Values) if (entry.Cursor) { UnityEditor.Undo.ClearUndo(entry.Cursor); UnityEngine.Object.DestroyImmediate(entry.Cursor); } entries.Clear(); }

        internal static void Clear(string id)
        {
            if (entries.TryGetValue(id, out var entry) && entry.Cursor)
            {
                UnityEditor.Undo.ClearUndo(entry.Cursor);
                UnityEngine.Object.DestroyImmediate(entry.Cursor);
            }
            entries.Remove(id);
        }
    }
}
