using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using URUI.Authoring.Source;

namespace URUI.Authoring.Sessions
{
    /// <summary>只在 Library 保存未提交的原始文本，用于程序集重载恢复；不会保存作者资产。</summary>
    public static class SourceRecovery
    {
        [Serializable]
        sealed class Snapshot
        {
            public string activePath; public Entry[] files;
        }
        [Serializable]
        sealed class Entry
        {
            public string path, text, diskVersion; public bool deleted;
        }
        public static void Save(AuthoringSession session, string path)
        {
            if (session.File == null)
                return;
            CheckPath(session, path);
            var files = new List<Entry>();
            foreach (var file in session.Workspace.Files)
                if (file.Dirty)
                    files.Add(new Entry { path = file.Path, text = file.Text, diskVersion = file.DiskVersion, deleted = file.Text == null });
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(new Snapshot { activePath = session.File.Path, files = files.ToArray() }), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        public static void Restore(AuthoringSession session, string path)
        {
            CheckPath(session, path);
            if (!File.Exists(path))
                return;
            var snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(path));
            if (snapshot == null || string.IsNullOrEmpty(snapshot.activePath))
                throw new InvalidOperationException("Invalid working-copy recovery file.");
            var candidates = new List<SourceChange>();
            foreach (var entry in snapshot.files ?? Array.Empty<Entry>())
            {
                if (string.IsNullOrEmpty(entry.diskVersion))
                {
                    string full = session.Workspace.CheckPath(entry.path);
                    if (File.Exists(full))
                        throw new InvalidOperationException("source_conflict: new recovered file already exists: " + full);
                    if (!entry.deleted)
                        candidates.Add(new SourceChange(full, entry.text, 0, true));
                    continue;
                }
                var file = session.Workspace.Open(entry.path);
                if (file.DiskVersion != entry.diskVersion)
                    throw new InvalidOperationException("source_conflict: working-copy recovery retained at " + path + "; disk changed: " + entry.path);
                candidates.Add(new SourceChange(file.Path, entry.deleted ? null : entry.text, file.Revision, false, entry.deleted));
            }
            session.Open(snapshot.activePath);
            session.Workspace.EditBatch(candidates);
            session.RefreshModel();
        }
        static void CheckPath(AuthoringSession session, string path)
        {
            string root = Path.GetFullPath(Path.Combine(session.Workspace.ProjectRoot, "Library/URUI/Authoring")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.Ordinal))
                throw new InvalidOperationException("Working-copy recovery belongs under Library/URUI/Authoring.");
        }
    }
}
