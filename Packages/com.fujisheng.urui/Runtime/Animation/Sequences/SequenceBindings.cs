using System;
using System.Collections.Generic;
using System.Text;
using URUI.Elements;
using URUI.Internal.Interop;

namespace URUI.Animation
{
    internal sealed class SequenceBindings : IDisposable
    {
        internal sealed class Css
        {
            internal Element Node;
            internal ulong Owner;
            internal int Depth;
            internal bool Captured;
            internal readonly List<SequenceTrack> Tracks = new List<SequenceTrack>();
            internal NativeMethods.SequenceSample[] Samples, Previous;
            internal int Count, PreviousCount;
        }
        internal sealed class Extension
        {
            internal Element Node;
            internal string Kind;
            internal readonly List<SequenceTrack> Tracks = new List<SequenceTrack>();
            internal ISequenceTrackDriver Driver;
            internal SequenceTrack Selected, Previous;
            internal double Local, PreviousLocal;
        }
        readonly Element root;
        internal bool IsValid => root.IsValid && Styles.TrueForAll(b => InRoot(b.Node)) && Extensions.TrueForAll(b => InRoot(b.Node));
        bool InRoot(Element node) => node.IsValid && node.IsAttached && (ReferenceEquals(node, root) || root.Contains(node));
        internal readonly List<Css> Styles = new List<Css>();
        internal readonly List<Extension> Extensions = new List<Extension>();
        internal SequenceBindings(Element root, SequenceDefinition definition, ulong owner, ISequenceTrackFactory explicitFactory, IDictionary<string, Element> slots)
        {
            this.root = root;
            try
            {
                int keyCount = 0;
                var nodes = new Dictionary<string, List<Element>>(StringComparer.Ordinal);
                var pending = new Stack<Element>();
                pending.Push(root);
                while (pending.Count != 0)
                {
                    var node = pending.Pop();
                    if (!string.IsNullOrEmpty(node.Name))
                    {
                        if (!nodes.TryGetValue(node.Name, out var values))
                            nodes.Add(node.Name, values = new List<Element>());
                        values.Add(node);
                    }
                    foreach (var child in node.Hierarchy.Children())
                        pending.Push(child);
                }
                var properties = new Dictionary<SequenceTrack, HashSet<string>>();
                foreach (var track in definition.Tracks)
                {
                    if (track.Muted)
                        continue;
                    Element target;
                    if (track.Target == "$root")
                        target = root;
                    else if (slots != null && slots.TryGetValue(track.Target, out var bound))
                        target = bound;
                    else if (nodes.TryGetValue(track.Target, out var found) && found.Count == 1)
                        target = found[0];
                    else
                        throw new InvalidOperationException("动画目标缺失或重名：" + track.Target);
                    if (target == null || !target.IsValid || !ReferenceEquals(target.Document, root.Document) || !(ReferenceEquals(root, target) || root.Contains(target)))
                        throw new InvalidOperationException("动画目标必须位于播放根内。");
                    if (track.Kind == "css")
                    {
                        int count = NativeMethods.urui_keyframes_count(target.NativeHandle, target.CheckedToken(), track.Keyframes);
                        if (count < 1 || (keyCount += count) > 4096)
                            throw new InvalidOperationException("活动轨道合计不能超过4096个关键帧，且引用必须存在。");
                        properties[track] = Properties(target, track.Keyframes);
                        var binding = Styles.Find(value => ReferenceEquals(value.Node, target));
                        if (binding == null)
                        {
                            binding = new Css { Node = target, Owner = owner };
                            for (var parent = target.Parent; parent != null; parent = parent.Parent)
                                binding.Depth++;
                            Styles.Add(binding);
                        }
                        foreach (var previous in binding.Tracks)
                            if (SequenceTiming.Overlaps(previous, track) && properties[previous].Overlaps(properties[track]))
                                throw new InvalidOperationException("动画轨道在同一时段控制相同属性：" + previous.Id + " / " + track.Id);
                        binding.Tracks.Add(track);
                    }
                    else
                    {
                        var binding = Extensions.Find(value => ReferenceEquals(value.Node, target) && value.Kind == track.Kind);
                        if (binding == null)
                        {
                            binding = new Extension { Node = target, Kind = track.Kind };
                            Extensions.Add(binding);
                        }
                        foreach (var previous in binding.Tracks)
                            if (SequenceTiming.Overlaps(previous, track))
                                throw new InvalidOperationException("扩展轨道的活动区间重叠：" + track.Target);
                        binding.Tracks.Add(track);
                    }
                }
                Styles.Sort((a, b) => a.Depth.CompareTo(b.Depth));
                foreach (var binding in Styles)
                {
                    StableSort(binding.Tracks);
                    binding.Samples = new NativeMethods.SequenceSample[binding.Tracks.Count];
                    binding.Previous = new NativeMethods.SequenceSample[binding.Tracks.Count];
                    for (int i = 0; i < binding.Tracks.Count; i++)
                        binding.Samples[i] = Entry(binding.Tracks[i], 0);
                    Native(binding, binding.Samples, binding.Samples.Length, 2);
                    binding.Captured = true;
                }
                foreach (var binding in Extensions)
                {
                    StableSort(binding.Tracks);
                    var factory = explicitFactory?.Kind == binding.Kind ? explicitFactory : SequenceTrackRegistry.Find(root.Document, binding.Kind);
                    if (factory == null)
                        throw new InvalidOperationException("未安装动画轨道扩展：" + binding.Kind);
                    binding.Driver = factory.Create(binding.Node, binding.Tracks.AsReadOnly()) ?? throw new InvalidOperationException("轨道扩展返回null驱动。");
                }
            }
            catch (Exception error) { try { Dispose(); } catch (Exception cleanup) { throw new AggregateException(error, cleanup); } throw; }
        }
        static void StableSort(List<SequenceTrack> tracks)
        {
            for (int i = 1; i < tracks.Count; i++)
            {
                var track = tracks[i];
                int index = i;
                while (index > 0 && tracks[index - 1].Start > track.Start)
                {
                    tracks[index] = tracks[index - 1];
                    index--;
                }
                tracks[index] = track;
            }
        }
        static NativeMethods.SequenceSample Entry(SequenceTrack track, float progress) => new NativeMethods.SequenceSample { Name = track.Keyframes, Easing = track.Easing, Progress = progress };
        static HashSet<string> Properties(Element node, string name)
        {
            int result = NativeMethods.urui_keyframes_properties(node.NativeHandle, node.CheckedToken(), name, null, 0, out int size);
            if (result != -3 || size < 1 || size > 1048576)
                throw new InvalidOperationException("动画关键帧不存在或不可读取：" + name);
            var bytes = new byte[size];
            if (NativeMethods.urui_keyframes_properties(node.NativeHandle, node.CheckedToken(), name, bytes, size, out int actual) != 1 || actual != size)
                throw new InvalidOperationException("动画关键帧版本变化。");
            return new HashSet<string>(Encoding.UTF8.GetString(bytes, 0, size - 1).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }
        static void Native(Css binding, NativeMethods.SequenceSample[] samples, int count, int mode)
        {
            int result = NativeMethods.urui_sequence_sample(binding.Node.NativeHandle, binding.Node.CheckedToken(), binding.Owner, samples, count, mode);
            if (result != 1)
                throw new InvalidOperationException("动画采样失败：" + result + "，请检查关键帧、缓动、插件版本及属性控制权。");
        }
        internal void Sample(double time)
        {
            if (!IsValid)
                throw new InvalidOperationException("动画节点已失效或移出播放根。");
            foreach (var binding in Styles)
            {
                binding.Count = 0;
                foreach (var track in binding.Tracks)
                {
                    var at = SequenceTiming.At(track, time);
                    if (at.Active)
                        binding.Samples[binding.Count++] = Entry(track, at.Progress);
                }
                Native(binding, binding.Samples, binding.Count, 0);
            }
            try
            {
                ApplyStyles(false);
                foreach (var binding in Extensions)
                {
                    binding.Selected = null;
                    binding.Local = 0;
                    foreach (var track in binding.Tracks)
                    {
                        var at = SequenceTiming.At(track, time);
                        if (at.Active)
                        {
                            binding.Selected = track;
                            binding.Local = at.LocalTime;
                        }
                    }
                    binding.Driver.Sample(binding.Selected, binding.Local, binding.Selected != null);
                }
            }
            catch (Exception error)
            {
                var errors = new List<Exception> { error };
                try
                {
                    ApplyStyles(true);
                }
                catch (Exception rollback) { errors.Add(rollback); }
                foreach (var binding in Extensions)
                {
                    try
                    {
                        binding.Driver.Sample(binding.Previous, binding.PreviousLocal, binding.Previous != null);
                    }
                    catch (Exception rollback) { errors.Add(rollback); }
                }
                if (errors.Count > 1)
                    throw new AggregateException("动画采样失败，恢复时也遇到错误。", errors);
                throw;
            }
            foreach (var binding in Styles)
            {
                Array.Copy(binding.Samples, binding.Previous, binding.Count);
                binding.PreviousCount = binding.Count;
            }
            foreach (var binding in Extensions)
            {
                binding.Previous = binding.Selected;
                binding.PreviousLocal = binding.Local;
            }
        }
        void Layout()
        {
            if (NativeMethods.urui_sequence_layout(root.NativeHandle) != 1)
                throw new InvalidOperationException("动画布局上下文已失效。");
        }
        void ApplyStyles(bool previous)
        {
            if (Styles.Count == 0)
                return;
            // Start from the frozen authoring base, so mixed units never inherit a previously sampled layout.
            foreach (var binding in Styles)
                Native(binding, null, 0, 3);
            Layout();
            for (int first = 0; first < Styles.Count;)
            {
                int end = first + 1;
                while (end < Styles.Count && Styles[end].Depth == Styles[first].Depth)
                    end++;
                // First settle this depth's own geometry/font, then resolve transforms/em against it.
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = first; i < end; i++)
                    {
                        var binding = Styles[i];
                        int count = previous ? binding.PreviousCount : binding.Count;
                        Native(binding, previous ? binding.Previous : binding.Samples, count, count == 0 ? 3 : 1);
                    }
                    Layout();
                }
                first = end;
            }
        }
        public void Dispose()
        {
            List<Exception> errors = null;
            foreach (var binding in Styles)
                if (binding.Captured && binding.Node.IsValid)
                    try
                    {
                        Native(binding, null, 0, 1);
                        binding.Captured = false;
                    }
                    catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            foreach (var binding in Extensions)
                try
                {
                    binding.Driver?.Dispose();
                }
                catch (Exception error) { (errors ??= new List<Exception>()).Add(error); }
            Styles.Clear();
            Extensions.Clear();
            if (errors != null)
                throw new AggregateException("部分动画覆盖未能恢复。", errors);
        }
    }
}
