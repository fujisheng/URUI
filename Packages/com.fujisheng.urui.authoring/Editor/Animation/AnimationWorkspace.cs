using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using URUI.Animation;
using URUI.Authoring.Sessions;
using URUI.Authoring.Source;

namespace URUI.Authoring.Animation
{
    /// <summary>
    /// 动画作者命令，共享页面工作副本、版本和撤销；所有命令只改内存，Save 才落盘。
    /// </summary>
    public sealed class AnimationWorkspace
    {
        readonly AuthoringSession author;
        public string Path
        {
            get;
            private set;
        }
        public long Revision => author.Revision;
        public SequenceDefinition Definition => Path == null || author.Workspace.Open(Path).Text == null ? null : SequenceDefinition.Parse(author.Workspace.Open(Path).Text);
        public void Close()
        {
            if (author.AnimationPath == Path)
            {
                author.AnimationPath = null;
            }
            Path = null;
        }
        public AnimationWorkspace(AuthoringSession author)
        {
            this.author = author ?? throw new ArgumentNullException(nameof(author));
        }
        public void Open(string path)
        {
            if (author.File == null)
            {
                throw new InvalidOperationException("请先打开 HTML 页面，再打开动画序列。");
            }
            string full = author.Workspace.CheckPath(path);
            if (!full.EndsWith(".urui-sequence.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("请选择 .urui-sequence.json。");
            }
            SequenceDefinition.Parse(author.Workspace.Open(full).Text);
            SetPath(full);
        }
        public void Create(long expectedRevision)
        {
            Check(expectedRevision);
            string path = System.IO.Path.ChangeExtension(author.File.Path, ".urui-sequence.json");
            if (File.Exists(path) || author.Workspace.Files.Any(f => f.Path == path && f.Text != null))
            {
                Open(path);
                return;
            }
            var sequence = new SequenceDefinition { Name = System.IO.Path.GetFileNameWithoutExtension(author.File.Path) };
            author.EditWorkset(new[] { new SourceChange(path, sequence.ToJson(), 0, true) }, expectedRevision);
            SetPath(path);
        }
        public string AddPreset(string sourceKey, string preset, double start, double duration, long expectedRevision)
            => Add(sourceKey, preset, "css", "", start, duration, expectedRevision);
        public string AddParticles(string sourceKey, string source, double start, double duration, long expectedRevision)
            => Add(sourceKey, null, "particles", source, start, duration, expectedRevision);
        string Add(string key, string preset, string kind, string source, double start, double duration, long revision)
        {
            Check(revision);
            var definition = Definition ?? new SequenceDefinition();
            string sequencePath = Path ?? System.IO.Path.ChangeExtension(author.File.Path, ".urui-sequence.json");
            var original = author.Model.Find(key);
            if (kind == "particles" && original.Tag != "urui-particles")
            {
                throw new InvalidOperationException("粒子轨道需要 urui-particles 节点。");
            }
            string target = original.Attribute("id");
            if (string.IsNullOrEmpty(target))
            {
                int serial = 1;
                do
                {
                    target = "urui-node-" + serial++;
                } while (author.Model.Nodes.Any(n => n.Attribute("id") == target));
            }
            if (author.Model.Nodes.Count(n => n.Attribute("id") == target) > 1)
            {
                throw new InvalidOperationException("节点 id 重复，请先设置唯一 id。");
            }
            string id = "track-" + Guid.NewGuid().ToString("N").Substring(0, 10);
            var track = new SequenceTrack
            {
                Id = id,
                Kind = kind,
                Target = target,
                Start = start,
                Duration = duration,
                Source = source ?? "",
                Easing = kind == "css" ? "ease-out" : "linear"
            };
            string relativeLink = null;
            string BuildHtml(SourceDocument model)
            {
                model = SourceDocument.Parse(model.SetAttribute(key, "id", target));
                if (kind == "particles" && string.IsNullOrEmpty(original.Attribute("src")))
                {
                    if (string.IsNullOrEmpty(source))
                    {
                        throw new InvalidOperationException("粒子节点需要有效源资源。");
                    }
                    model = SourceDocument.Parse(model.SetAttribute(key, "src", source));
                }
                if (relativeLink != null && !model.Nodes.Any(n => n.Tag == "link" && n.Attribute("href") == relativeLink && n.Attribute("rel") == "stylesheet"))
                {
                    var head = model.Nodes.FirstOrDefault(n => n.Tag == "head");
                    if (head == null)
                    {
                        throw new InvalidOperationException("页面需要 head 节点来链接动画样式。");
                    }
                    return model.Insert(head.Key, "<link rel=\"stylesheet\" href=\"" + SourceDocument.Escape(relativeLink, '"') + "\">", -1);
                }
                return model.Text;
            }
            var changes = new List<SourceChange>();
            if (kind == "css")
            {
                track.Keyframes = "urui-" + id;
                string cssPath = System.IO.Path.ChangeExtension(author.File.Path, ".animations.css");
                var file = FindFile(cssPath);
                string css = CssKeyframes.CreatePreset(file?.Text ?? "", track.Keyframes, preset);
                changes.Add(new SourceChange(cssPath, css, file?.Revision ?? 0, file == null));
                relativeLink = Uri.UnescapeDataString(new Uri(author.File.Path).MakeRelativeUri(new Uri(cssPath)).ToString());
            }
            definition.Tracks.Add(track);
            definition.Duration = Math.Max(definition.Duration, track.End);
            definition.Validate();
            var sequenceFile = FindFile(sequencePath);
            changes.Add(new SourceChange(sequencePath, definition.ToJson(), sequenceFile?.Revision ?? 0, sequenceFile == null));
            changes.Add(new SourceChange(author.File.Path, BuildHtml(author.Model), author.File.Revision));
            ValidateCandidate(definition, changes);
            author.EditWorkset(changes, author.Revision, BuildHtml);
            SetPath(sequencePath);
            return id;
        }
        public void SetTrack(string id, Action<SequenceTrack> edit, long revision)
        {
            Check(revision);
            var definition = Required();
            var track = Track(definition, id);
            if (track.Locked)
            {
                throw new InvalidOperationException("轨道已锁定，请先解锁。");
            }
            edit(track);
            Write(definition, revision);
        }
        public void SetLocked(string id, bool locked, long revision)
        {
            Check(revision);
            var definition = Required();
            Track(definition, id).Locked = locked;
            Write(definition, revision);
        }
        public void SetSequence(string name, double duration, bool loop, long revision)
        {
            Check(revision);
            var definition = Required();
            definition.Name = name;
            definition.Duration = duration;
            definition.Loop = loop;
            Write(definition, revision);
        }
        public void RemoveTrack(string id, long revision)
        {
            Check(revision);
            var definition = Required();
            var track = Track(definition, id);
            Unlocked(track);
            definition.Tracks.Remove(track);
            Write(definition, revision);
        }
        public readonly struct KeySelection
        {
            public readonly string Track;
            public readonly double Position;
            public KeySelection(string track, double position)
            {
                Track = track;
                Position = position;
            }
        }
        public void TransformKeys(IReadOnlyList<KeySelection> selection, double seconds, bool copy, bool remove, long revision)
        {
            Check(revision);
            var definition = Required();
            var candidates = new Dictionary<SourceFile, string>();
            foreach (var group in selection.GroupBy(k => k.Track))
            {
                var track = Track(definition, group.Key);
                Unlocked(track);
                if (track.Kind != "css")
                {
                    throw new InvalidOperationException("粒子轨道不包含 CSS 关键帧。");
                }
                var file = KeyframeFile(track);
                string text = candidates.TryGetValue(file, out var previous) ? previous : file.Text;
                var source = CssKeyframes.Read(text, track.Keyframes);
                foreach (var key in group.OrderBy(k => seconds > 0 ? -k.Position : k.Position))
                {
                    if (remove)
                    {
                        text = CssKeyframes.RemoveKey(text, track.Keyframes, key.Position);
                    }
                    else
                    {
                        if (track.Duration == 0)
                        {
                            throw new InvalidOperationException("零时长轨道不能移动关键帧。");
                        }
                        double destination = Math.Round(key.Position + seconds / track.Duration, 8);
                        if (!copy)
                        {
                            text = CssKeyframes.MoveKey(text, track.Keyframes, key.Position, destination);
                        }
                        else
                        {
                            foreach (var original in source.Where(k => Math.Abs(k.Position - key.Position) < 1e-8))
                            {
                                foreach (var declaration in CssSource.Declarations(original.Declarations))
                                {
                                    text = CssKeyframes.SetKey(text, track.Keyframes, destination, declaration.Name, original.Declarations.Substring(declaration.ValueStart, declaration.ValueEnd - declaration.ValueStart));
                                }
                            }
                        }
                    }
                }
                candidates[file] = text;
            }
            PublishKeys(candidates.Select(p => new SourceChange(p.Key.Path, p.Value, p.Key.Revision)), revision);
        }
        public IReadOnlyList<CssKeyframes.Key> Keys(string id)
        {
            var track = Track(Required(), id);
            return CssKeyframes.Read(KeyframeFile(track).Text, track.Keyframes);
        }
        public void SetKey(string id, double position, string property, string value, long revision)
            => EditKeys(id, revision, (text, name) => CssKeyframes.SetKey(text, name, position, property, value));
        public void RemoveKeys(string id, IEnumerable<double> positions, long revision)
            => EditKeys(id, revision, (text, name) =>
            {
                foreach (double position in positions.Distinct())
                {
                    text = CssKeyframes.RemoveKey(text, name, position);
                }
                return text;
            });
        public void MoveKeys(string id, IEnumerable<double> positions, double delta, bool copy, long revision)
        {
            var selected = positions.Distinct().OrderBy(p => delta > 0 ? -p : p).ToArray();
            EditKeys(id, revision, (text, name) =>
            {
                var source = CssKeyframes.Read(text, name);
                foreach (double position in selected)
                {
                    double destination = Math.Round(position + delta, 8);
                    if (!copy)
                    {
                        text = CssKeyframes.MoveKey(text, name, position, destination);
                    }
                    else
                    {
                        foreach (var key in source.Where(k => Math.Abs(k.Position - position) < 1e-8))
                        {
                            foreach (var declaration in CssSource.Declarations(key.Declarations))
                            {
                                text = CssKeyframes.SetKey(text, name, destination, declaration.Name, key.Declarations.Substring(declaration.ValueStart, declaration.ValueEnd - declaration.ValueStart));
                            }
                        }
                    }
                }
                return text;
            });
        }
        void EditKeys(string id, long revision, Func<string, string, string> edit)
        {
            Check(revision);
            var track = Track(Required(), id);
            Unlocked(track);
            if (track.Kind != "css")
            {
                throw new InvalidOperationException("粒子轨道使用时间区间与资源参数，不包含 CSS 关键帧。");
            }
            var file = KeyframeFile(track);
            PublishKeys(new[] { new SourceChange(file.Path, edit(file.Text, track.Keyframes), file.Revision) }, revision);
        }
        public SourceFile KeyframeFile(SequenceTrack track)
        {
            var sheet = new AnimationSourceSet(author).Find(track.Keyframes);
            if (sheet.Path == null)
            {
                throw new InvalidOperationException("该动画来自内联或只读样式；请移到当前页面链接的 CSS 后再编辑。");
            }
            return author.Workspace.Open(sheet.Path);
        }
        internal void PublishKeys(IEnumerable<SourceChange> changes, long revision)
        {
            Check(revision);
            var candidates = changes.ToArray();
            ValidateCandidate(Required(), candidates);
            author.EditWorkset(candidates, revision);
        }
        internal void ValidateCandidate(SequenceDefinition definition, IEnumerable<SourceChange> changes = null)
        {
            definition.Validate();
            var sources = new AnimationSourceSet(author, changes);
            var properties = new Dictionary<SequenceTrack, HashSet<string>>();
            int totalKeys = 0;
            foreach (var track in definition.Tracks)
            {
                if (track.Muted)
                {
                    continue;
                }
                var targets = sources.Html.Nodes.Where(n => track.Target == "$root" ? n.Tag == "body" : n.Attribute("id") == track.Target).ToArray();
                if (targets.Length != 1)
                {
                    throw new InvalidOperationException("动画目标缺失或重名：" + track.Target);
                }
                var target = targets[0];
                bool inside = false;
                for (var node = target; node != null; node = node.Parent)
                {
                    if (node.Tag == "body")
                    {
                        inside = true;
                    }
                }
                if (!inside)
                {
                    throw new InvalidOperationException("动画目标必须位于 body 内。");
                }
                if (track.Kind == "particles")
                {
                    if (target.Tag != "urui-particles")
                    {
                        throw new InvalidOperationException("粒子轨道需要 urui-particles 节点。");
                    }
                    ValidateParticles(track, target.Attribute("src"));
                    properties[track] = new HashSet<string> { "$particles" };
                    continue;
                }
                var sheet = sources.Find(track.Keyframes);
                var keys = CssKeyframes.Read(sheet.Text, track.Keyframes);
                totalKeys += keys.Count;
                if (totalKeys > 4096)
                {
                    throw new InvalidOperationException("活动轨道合计不能超过 4096 个关键帧。");
                }
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    foreach (var declaration in CssSource.Declarations(key.Declarations))
                    {
                        foreach (string name in Expanded(declaration.Name))
                        {
                            names.Add(name);
                        }
                    }
                }
                if (names.Count == 0)
                {
                    throw new InvalidOperationException("动画需要至少一个可用属性：" + track.Keyframes);
                }
                properties[track] = names;
            }
            var active = definition.Tracks.Where(t => !t.Muted).ToArray();
            for (int i = 0; i < active.Length; i++)
            {
                for (int j = i + 1; j < active.Length; j++)
                {
                    if (active[i].Target == active[j].Target && active[i].Kind == active[j].Kind && SequenceTiming.Overlaps(active[i], active[j]) && properties[active[i]].Overlaps(properties[active[j]]))
                    {
                        throw new InvalidOperationException("活动轨道在同一时段控制相同属性：" + active[i].Id + " / " + active[j].Id);
                    }
                }
            }
        }
        internal static IEnumerable<string> Expanded(string name)
        {
            if (name == "margin" || name == "padding" || name == "border-width" || name == "border-color" || name == "border-style")
            {
                foreach (string side in new[] { "top", "right", "bottom", "left" })
                {
                    yield return name.StartsWith("border-", StringComparison.Ordinal) ? "border-" + side + name.Substring(6) : name + "-" + side;
                }
            }
            else if (name == "border")
            {
                foreach (string side in new[] { "top", "right", "bottom", "left" })
                {
                    foreach (string component in new[] { "width", "color", "style" })
                    {
                        yield return "border-" + side + "-" + component;
                    }
                }
            }
            else if (new[] { "border-top", "border-right", "border-bottom", "border-left" }.Contains(name))
            {
                foreach (string component in new[] { "width", "color", "style" })
                {
                    yield return name + "-" + component;
                }
            }
            else
            {
                string[] expanded = name == "background" ? new[] { "background-color", "background-image", "background-position", "background-size", "background-origin", "background-clip", "background-repeat", "background-attachment" }
                    : name == "font" ? new[] { "font-family", "font-size", "font-weight", "font-style", "font-variant", "line-height" }
                    : name == "border-radius" ? new[] { "border-top-left-radius", "border-top-right-radius", "border-bottom-left-radius", "border-bottom-right-radius" }
                    : name == "flex" ? new[] { "flex-grow", "flex-shrink", "flex-basis" }
                    : name == "flex-flow" ? new[] { "flex-direction", "flex-wrap" }
                    : name == "overflow" ? new[] { "overflow-x", "overflow-y" }
                    : name == "gap" ? new[] { "row-gap", "column-gap" }
                    : name == "inset" ? new[] { "top", "right", "bottom", "left" }
                    : name == "outline" ? new[] { "outline-width", "outline-style", "outline-color" }
                    : new[] { name };
                foreach (string property in expanded)
                {
                    yield return property;
                }
            }
        }
        void SetPath(string path)
        {
            Path = path;
            author.AnimationPath = path;
            author.AnimationPage = author.File.Path;
        }
        internal static void ValidateForSave(AuthoringSession author)
        {
            if (author.File == null)
            {
                return;
            }
            string path = author.AnimationPage == author.File.Path && author.AnimationPath != null ? author.AnimationPath : System.IO.Path.ChangeExtension(author.File.Path, ".urui-sequence.json");
            var file = author.Workspace.Files.FirstOrDefault(f => f.Path == path);
            string text = file != null ? file.Text : File.Exists(path) ? File.ReadAllText(path) : null;
            if (text == null)
            {
                return;
            }
            var workspace = new AnimationWorkspace(author);
            workspace.ValidateCandidate(SequenceDefinition.Parse(text));
        }
        public bool IsShared(string id)
        {
            var definition = Required();
            var track = Track(definition, id);
            return definition.Tracks.Count(t => t.Keyframes == track.Keyframes) > 1;
        }
        void ValidateParticles(SequenceTrack track, string fallback = null)
        {
            if (!AuthoringEnvironment.Extensions.Any(extension => extension.Id == "effect"))
            {
                throw new InvalidOperationException("effect_not_installed: 请先安装 Effect 包。");
            }
            string source = string.IsNullOrEmpty(track.Source) ? fallback ?? author.Model.Nodes.FirstOrDefault(n => n.Attribute("id") == track.Target)?.Attribute("src") : track.Source;
            if (string.IsNullOrEmpty(source))
            {
                throw new InvalidOperationException("粒子轨道需要有效资源引用。");
            }
            string path = Preview.AuthoringAssetPaths.ResolveAsset(author.Workspace.ProjectRoot, author.File.Path, source);
            if (path == null)
            {
                throw new InvalidOperationException("read_only_asset: 粒子资源必须来自项目资产。");
            }
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
            if (!prefab || prefab.GetComponentsInChildren<UnityEngine.ParticleSystem>(true).Length == 0)
            {
                throw new InvalidOperationException("粒子 Prefab 无法解析：" + source);
            }
        }
        void Write(SequenceDefinition definition, long revision)
        {
            ValidateCandidate(definition);
            var file = author.Workspace.Open(Path);
            author.EditWorkset(new[] { new SourceChange(file.Path, definition.ToJson(), file.Revision) }, revision);
        }
        SequenceDefinition Required() => Definition ?? throw new InvalidOperationException("请先创建或打开动画序列。");
        void Check(long revision)
        {
            if (author.File == null)
            {
                throw new InvalidOperationException("请先打开 HTML 页面。");
            }
            if (revision != Revision)
            {
                throw new InvalidOperationException("source_conflict: stale animation revision");
            }
        }
        SourceFile FindFile(string path)
        {
            var file = author.Workspace.Files.FirstOrDefault(f => f.Path == path && f.Text != null);
            return file ?? (File.Exists(path) ? author.Workspace.Open(path) : null);
        }
        static SequenceTrack Track(SequenceDefinition definition, string id) => definition.Tracks.FirstOrDefault(t => t.Id == id) ?? throw new InvalidOperationException("stale_track: " + id);
        static void Unlocked(SequenceTrack track)
        {
            if (track.Locked)
            {
                throw new InvalidOperationException("轨道已锁定，请先解锁。");
            }
        }
    }
}
