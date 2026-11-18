using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace URUI.Authoring.Source
{
    public readonly struct SourceChange
    {
        public readonly string Path, Text;
        public readonly long ExpectedRevision;
        public readonly bool Create, Delete;
        public SourceChange(string path, string text, long expectedRevision, bool create = false, bool delete = false)
        {
            Path = path;
            Text = text;
            ExpectedRevision = expectedRevision;
            Create = create;
            Delete = delete;
        }
    }

    public sealed class SourceFile
    {
        public string Path
        {
            get; internal set;
        }
        public string Text
        {
            get; internal set;
        }
        public long Revision
        {
            get; internal set;
        }
        public bool Dirty => Text != SavedText;
        public bool HasExternalChange => (File.Exists(Path) ? SourceWorkspace.Hash(File.ReadAllBytes(Path)) : null) != DiskHash;
        public string DiskVersion => DiskHash;
        internal string SavedText, DiskHash;
        internal bool Bom;
    }

    /// <summary>无自动磁盘写入的版本化工作副本；保存前检查每个文件，失败时回滚或保留恢复日志。</summary>
    public sealed class SourceWorkspace
    {
        readonly string root;
        readonly Func<string, string, string> validate;
        readonly Action<string, string> replaceFile;
        readonly Dictionary<string, SourceFile> files = new Dictionary<string, SourceFile>(StringComparer.Ordinal);
        readonly Stack<List<SourceChange>> undo = new Stack<List<SourceChange>>();
        readonly Stack<List<SourceChange>> redo = new Stack<List<SourceChange>>();
        long revision;
        public long Revision => revision;
        public string ProjectRoot => root;
        public int HistoryPosition
        {
            get; private set;
        }
        public IEnumerable<SourceFile> Files => files.Values;
        public bool CanUndo => undo.Count != 0;
        public bool CanRedo => redo.Count != 0;
        public SourceWorkspace(string projectRoot, Func<string, string, string> validator = null)
            : this(projectRoot, validator, (stage, target) => File.Replace(stage, target, null)) { }
        // Keep filesystem fault injection internal; normal clients always use the same-volume replace.
        internal SourceWorkspace(string projectRoot, Func<string, string, string> validator, Action<string, string> replace)
        {
            root = System.IO.Path.GetFullPath(projectRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            validate = validator;
            replaceFile = replace;
        }

        public SourceFile Open(string path)
        {
            path = CheckPath(path);
            if (files.TryGetValue(path, out var existing))
                return existing;
            var file = new SourceFile { Path = path };
            Read(file);
            files.Add(path, file);
            return file;
        }
        public void Edit(string path, string text, long expectedRevision)
            => EditBatch(new[] { new SourceChange(path, text, expectedRevision) });

        public void Create(string path, string text, long expectedRevision)
        {
            if (expectedRevision != Revision)
                throw new InvalidOperationException("source_conflict: stale workspace revision");
            EditBatch(new[] { new SourceChange(path, text, 0, true) });
        }

        public void EditBatch(IEnumerable<SourceChange> changes)
        {
            var candidates = new List<SourceChange>();
            var inverse = new List<SourceChange>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var created = new List<SourceFile>();
            foreach (var change in changes)
            {
                string path = CheckPath(change.Path);
                SourceFile file;
                if (change.Create)
                {
                    if (File.Exists(path) || (files.TryGetValue(path, out file) && file.Text != null))
                        throw new InvalidOperationException("source_conflict: file already exists");
                    file = new SourceFile { Path = path, Revision = 0 };
                    created.Add(file);
                }
                else
                    file = Open(path);
                if (file.Revision != change.ExpectedRevision)
                    throw new InvalidOperationException("source_conflict: stale revision");
                if (!seen.Add(file.Path))
                    throw new InvalidOperationException("Batch must supply one final candidate per file.");
                if (change.Text == null && !change.Delete)
                    throw new InvalidOperationException("Null source.");
                string error = change.Text == null ? null : validate?.Invoke(file.Path, change.Text);
                if (error != null)
                    throw new InvalidOperationException(error);
                if (file.Text == change.Text)
                    continue;
                candidates.Add(new SourceChange(file.Path, change.Text, file.Revision));
                inverse.Add(new SourceChange(file.Path, file.Text, file.Revision));
            }
            if (candidates.Count == 0)
                return;
            foreach (var file in created)
                files[file.Path] = file;
            Apply(candidates);
            undo.Push(inverse);
            redo.Clear();
            HistoryPosition++;
        }
        internal void InvalidateContext()
        {
            revision++;
        }
        public void Undo() => Replay(undo, redo);
        public void Redo() => Replay(redo, undo);
        void Replay(Stack<List<SourceChange>> from, Stack<List<SourceChange>> to)
        {
            if (from.Count == 0)
                return;
            var changes = from.Peek();
            var inverse = new List<SourceChange>();
            foreach (var change in changes)
                inverse.Add(new SourceChange(change.Path, files[change.Path].Text, 0));
            Apply(changes);
            from.Pop();
            to.Push(inverse);
            HistoryPosition += ReferenceEquals(from, undo) ? -1 : 1;
        }
        void Apply(List<SourceChange> changes)
        {
            long next = ++revision;
            foreach (var change in changes)
            {
                var file = files[change.Path];
                file.Text = change.Text;
                file.Revision = next;
            }
        }
        public void Reload(string path, bool discardLocal)
        {
            var file = Open(path);
            if (file.Dirty && !discardLocal)
                throw new InvalidOperationException("source_conflict: unsaved local changes");
            Read(file);
            undo.Clear();
            redo.Clear();
            HistoryPosition = 0;
        }
        void Read(SourceFile file)
        {
            byte[] bytes = File.ReadAllBytes(file.Path);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
            int prefix = bom ? 3 : 0;
            string text = new UTF8Encoding(false, true).GetString(bytes, prefix, bytes.Length - prefix);
            string error = validate?.Invoke(file.Path, text);
            if (error != null)
                throw new InvalidOperationException(error);
            file.Bom = bom;
            file.Text = text;
            file.SavedText = file.Text;
            file.DiskHash = Hash(bytes);
            file.Revision = ++revision;
        }

        public void Save(params string[] paths)
        {
            var targets = new List<SourceFile>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                var file = Open(path);
                CheckPath(file.Path);
                if (!seen.Add(file.Path))
                    continue;
                if (file.HasExternalChange)
                    throw new InvalidOperationException("source_conflict: disk changed: " + file.Path);
                string error = file.Text == null ? null : validate?.Invoke(file.Path, file.Text);
                if (error != null)
                    throw new InvalidOperationException(error);
                if (file.Dirty)
                    targets.Add(file);
            }
            if (targets.Count == 0)
                return;
            string id = Guid.NewGuid().ToString("N");
            string journalRoot = System.IO.Path.Combine(root, "Library/URUI/Authoring/Recovery");
            Directory.CreateDirectory(journalRoot);
            string journal = System.IO.Path.Combine(journalRoot, id + ".journal");
            var rows = new List<string>();
            var staged = new List<string>();
            var backups = new List<string>();
            var publishedHashes = new List<string>();
            int committed = 0;
            try
            {
                foreach (var file in targets)
                {
                    string stage = file.Path + ".urui-" + id + ".tmp", backup = file.Path + ".urui-" + id + ".bak";
                    staged.Add(stage);
                    backups.Add(backup);
                    if (file.Text != null)
                        File.WriteAllText(stage, file.Text, new UTF8Encoding(file.Bom));
                    publishedHashes.Add(File.Exists(stage) ? Hash(File.ReadAllBytes(stage)) : null);
                    if (file.DiskHash != null)
                        File.Copy(file.Path, backup, false);
                    rows.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(file.Path)) + "|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(backup)) + "|" + (file.DiskHash == null ? "new" : "existing") + "|" + (publishedHashes[publishedHashes.Count - 1] ?? "missing"));
                }
                File.WriteAllLines(journal, rows);
                // Recheck after staging: a watcher/agent may have edited while the files were written.
                foreach (var file in targets)
                    if (file.HasExternalChange)
                        throw new InvalidOperationException("source_conflict: disk changed during save");
                for (int i = 0; i < targets.Count; i++)
                {
                    var file = targets[i];
                    CheckPath(file.Path);
                    if (file.HasExternalChange)
                        throw new InvalidOperationException("source_conflict: disk changed before publishing: " + file.Path);
                    if (file.Text == null)
                        File.Delete(file.Path);
                    else if (file.DiskHash == null)
                        File.Move(staged[i], file.Path); // Exclusive creation, never replace a newly appeared external file.
                    else
                        replaceFile(staged[i], file.Path);
                    committed++;
                    string actual = File.Exists(file.Path) ? Hash(File.ReadAllBytes(file.Path)) : null;
                    if (actual != publishedHashes[i])
                        throw new InvalidOperationException("source_conflict: disk changed during publishing: " + file.Path);
                }
                for (int i = 0; i < targets.Count; i++)
                    if ((File.Exists(targets[i].Path) ? Hash(File.ReadAllBytes(targets[i].Path)) : null) != publishedHashes[i])
                        throw new InvalidOperationException("source_conflict: disk changed after publishing: " + targets[i].Path);
                for (int i = 0; i < targets.Count; i++)
                {
                    targets[i].SavedText = targets[i].Text;
                    targets[i].DiskHash = publishedHashes[i];
                }
            }
            catch (Exception error)
            {
                bool recovered = true;
                for (int i = committed - 1; i >= 0; i--)
                    try
                    {
                        CheckPath(targets[i].Path);
                        string current = File.Exists(targets[i].Path) ? Hash(File.ReadAllBytes(targets[i].Path)) : null;
                        if (current != publishedHashes[i])
                        {
                            recovered = false;
                            continue;
                        }
                        if (targets[i].DiskHash == null)
                            File.Delete(targets[i].Path);
                        else
                            File.Copy(backups[i], targets[i].Path, true);
                    }
                    catch { recovered = false; }
                if (!recovered)
                    throw new InvalidOperationException("Save failed; recovery data retained at " + journal, error);
                foreach (string backup in backups)
                    if (File.Exists(backup))
                        File.Delete(backup);
                if (File.Exists(journal))
                    File.Delete(journal);
                throw new InvalidOperationException("Save failed; original files restored.", error);
            }
            finally { foreach (string stage in staged) if (File.Exists(stage)) File.Delete(stage); }
            foreach (string backup in backups)
                if (File.Exists(backup))
                    File.Delete(backup);
            File.Delete(journal);
        }

        public string CheckPath(string path)
        {
            string full = System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(root, path));
            bool allowed = false;
            foreach (string folder in new[] { "Assets", "Packages" })
                if (full.StartsWith(System.IO.Path.Combine(root, folder) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    allowed = true;
            if (!allowed)
                throw new InvalidOperationException("read_only_asset: path must be inside project Assets or embedded Packages");
            string ext = System.IO.Path.GetExtension(full).ToLowerInvariant();
            if (ext != ".html" && ext != ".css" && !full.EndsWith(".urui-sequence.json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Only HTML/CSS and .urui-sequence.json source files can be edited.");
            for (string current = full; current != null && current != root; current = System.IO.Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("read_only_asset: symbolic links are not writable through Authoring");
            return full;
        }
        internal static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(bytes));
        }
    }
}
