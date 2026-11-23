using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
            internal IntPtr[] TrackNames;
            internal IntPtr[] TrackEasings;
            internal NativeMethods.SequenceSampleNative[] Samples;
            internal NativeMethods.SequenceSampleNative[] Previous;
            internal int Count;
            internal int PreviousCount;
        }
        internal sealed class Extension
        {
            internal Element Node;
            internal string Kind;
            internal readonly List<SequenceTrack> Tracks = new List<SequenceTrack>();
            internal ISequenceTrackDriver Driver;
            internal SequenceTrack Selected;
            internal SequenceTrack Previous;
            internal double Local;
            internal double PreviousLocal;
        }
        readonly Element root;
        readonly ulong surfaceHandle;
        readonly ulong documentHandle;
        byte[] utf8Pool;
        GCHandle utf8PoolHandle;
        NativeMethods.SequenceBatchItemNative[] batchItems;
        NativeMethods.SequenceSampleNative[] batchSamples;

        internal bool IsValid
        {
            get
            {
                if (!root.IsValid)
                {
                    return false;
                }
                for (int i = 0; i < Styles.Count; i++)
                {
                    if (!InRoot(Styles[i].Node))
                    {
                        return false;
                    }
                }
                for (int i = 0; i < Extensions.Count; i++)
                {
                    if (!InRoot(Extensions[i].Node))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        bool InRoot(Element node) => node.IsValid && node.IsAttached && (ReferenceEquals(node, root) || root.Contains(node));
        internal readonly List<Css> Styles = new List<Css>();
        internal readonly List<Extension> Extensions = new List<Extension>();

        internal SequenceBindings(Element root, SequenceDefinition definition, ulong owner, ISequenceTrackFactory explicitFactory, IDictionary<string, Element> slots)
        {
            this.root = root;
            surfaceHandle = root.SurfaceHandle;
            documentHandle = root.NativeHandle;
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
                        {
                            nodes.Add(node.Name, values = new List<Element>());
                        }
                        values.Add(node);
                    }
                    var hierarchy = node.Hierarchy;
                    int childCount = hierarchy.ChildCount;
                    for (int i = 0; i < childCount; i++)
                    {
                        var child = hierarchy.ChildAt(i);
                        if (child != null)
                        {
                            pending.Push(child);
                        }
                    }
                }
                var properties = new Dictionary<SequenceTrack, HashSet<string>>();
                for (int t = 0; t < definition.Tracks.Count; t++)
                {
                    var track = definition.Tracks[t];
                    if (track.Muted)
                    {
                        continue;
                    }
                    Element target;
                    if (track.Target == "$root")
                    {
                        target = root;
                    }
                    else if (slots != null && slots.TryGetValue(track.Target, out var bound))
                    {
                        target = bound;
                    }
                    else if (nodes.TryGetValue(track.Target, out var found) && found.Count == 1)
                    {
                        target = found[0];
                    }
                    else
                    {
                        throw new InvalidOperationException("动画目标缺失或重名：" + track.Target);
                    }
                    if (target == null || !target.IsValid || !ReferenceEquals(target.Document, root.Document) || !(ReferenceEquals(root, target) || root.Contains(target)))
                    {
                        throw new InvalidOperationException("动画目标必须位于播放根内。");
                    }
                    if (track.Kind == "css")
                    {
                        int count = NativeMethods.urui_keyframes_count(surfaceHandle, documentHandle, target.CheckedToken(), track.Keyframes);
                        if (count < 1 || (keyCount += count) > 4096)
                        {
                            throw new InvalidOperationException("活动轨道合计不能超过4096个关键帧，且引用必须存在。");
                        }
                        properties[track] = Properties(target, track.Keyframes);
                        Css binding = null;
                        for (int i = 0; i < Styles.Count; i++)
                        {
                            if (ReferenceEquals(Styles[i].Node, target))
                            {
                                binding = Styles[i];
                                break;
                            }
                        }
                        if (binding == null)
                        {
                            binding = new Css
                            {
                                Node = target,
                                Owner = owner
                            };
                            for (var parent = target.Parent; parent != null; parent = parent.Parent)
                            {
                                binding.Depth++;
                            }
                            Styles.Add(binding);
                        }
                        for (int i = 0; i < binding.Tracks.Count; i++)
                        {
                            var previous = binding.Tracks[i];
                            if (SequenceTiming.Overlaps(previous, track) && properties[previous].Overlaps(properties[track]))
                            {
                                throw new InvalidOperationException("动画轨道在同一时段控制相同属性：" + previous.Id + " / " + track.Id);
                            }
                        }
                        binding.Tracks.Add(track);
                    }
                    else
                    {
                        Extension binding = null;
                        for (int i = 0; i < Extensions.Count; i++)
                        {
                            if (ReferenceEquals(Extensions[i].Node, target) && Extensions[i].Kind == track.Kind)
                            {
                                binding = Extensions[i];
                                break;
                            }
                        }
                        if (binding == null)
                        {
                            binding = new Extension
                            {
                                Node = target,
                                Kind = track.Kind
                            };
                            Extensions.Add(binding);
                        }
                        for (int i = 0; i < binding.Tracks.Count; i++)
                        {
                            var previous = binding.Tracks[i];
                            if (SequenceTiming.Overlaps(previous, track))
                            {
                                throw new InvalidOperationException("扩展轨道的活动区间重叠：" + track.Target);
                            }
                        }
                        binding.Tracks.Add(track);
                    }
                }
                Styles.Sort((a, b) => a.Depth.CompareTo(b.Depth));
                int totalTracks = 0;
                for (int i = 0; i < Styles.Count; i++)
                {
                    var binding = Styles[i];
                    StableSort(binding.Tracks);
                    totalTracks += binding.Tracks.Count;
                }
                BuildUtf8Pool();
                if (Styles.Count > 0)
                {
                    batchItems = new NativeMethods.SequenceBatchItemNative[Styles.Count];
                    batchSamples = new NativeMethods.SequenceSampleNative[Math.Max(1, totalTracks)];
                    for (int i = 0; i < Styles.Count; i++)
                    {
                        var binding = Styles[i];
                        int trackCount = binding.Tracks.Count;
                        binding.Samples = new NativeMethods.SequenceSampleNative[trackCount];
                        binding.Previous = new NativeMethods.SequenceSampleNative[trackCount];
                        for (int j = 0; j < trackCount; j++)
                        {
                            binding.Samples[j] = Entry(binding, j, 0);
                        }
                        Native(binding, binding.Samples, trackCount, 2);
                        binding.Captured = true;
                    }
                }
                for (int i = 0; i < Extensions.Count; i++)
                {
                    var binding = Extensions[i];
                    StableSort(binding.Tracks);
                    var factory = explicitFactory?.Kind == binding.Kind ? explicitFactory : SequenceTrackRegistry.Find(root.Document, binding.Kind);
                    if (factory == null)
                    {
                        throw new InvalidOperationException("未安装动画轨道扩展：" + binding.Kind);
                    }
                    binding.Driver = factory.Create(binding.Node, binding.Tracks.AsReadOnly()) ?? throw new InvalidOperationException("轨道扩展返回null驱动。");
                }
            }
            catch (Exception error)
            {
                try
                {
                    Dispose();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(error, cleanup);
                }
                throw;
            }
        }

        ~SequenceBindings()
        {
            if (utf8PoolHandle.IsAllocated)
            {
                utf8PoolHandle.Free();
            }
        }

        void BuildUtf8Pool()
        {
            if (Styles.Count == 0)
            {
                return;
            }
            var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
            var unique = new List<string>();
            int totalBytes = 0;
            void Collect(string value)
            {
                var key = value ?? "";
                if (!offsets.ContainsKey(key))
                {
                    offsets.Add(key, totalBytes);
                    unique.Add(key);
                    totalBytes += Encoding.UTF8.GetByteCount(key) + 1;
                }
            }
            for (int i = 0; i < Styles.Count; i++)
            {
                var tracks = Styles[i].Tracks;
                for (int j = 0; j < tracks.Count; j++)
                {
                    Collect(tracks[j].Keyframes);
                    Collect(tracks[j].Easing);
                }
            }
            utf8Pool = new byte[Math.Max(1, totalBytes)];
            int cursor = 0;
            for (int i = 0; i < unique.Count; i++)
            {
                var text = unique[i];
                if (text.Length > 0)
                {
                    cursor += Encoding.UTF8.GetBytes(text, 0, text.Length, utf8Pool, cursor);
                }
                utf8Pool[cursor++] = 0;
            }
            utf8PoolHandle = GCHandle.Alloc(utf8Pool, GCHandleType.Pinned);
            var basePtr = utf8PoolHandle.AddrOfPinnedObject();
            for (int i = 0; i < Styles.Count; i++)
            {
                var binding = Styles[i];
                int trackCount = binding.Tracks.Count;
                binding.TrackNames = new IntPtr[trackCount];
                binding.TrackEasings = new IntPtr[trackCount];
                for (int j = 0; j < trackCount; j++)
                {
                    binding.TrackNames[j] = IntPtr.Add(basePtr, offsets[binding.Tracks[j].Keyframes ?? ""]);
                    binding.TrackEasings[j] = IntPtr.Add(basePtr, offsets[binding.Tracks[j].Easing ?? ""]);
                }
            }
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

        static NativeMethods.SequenceSampleNative Entry(Css binding, int trackIndex, float progress) => new NativeMethods.SequenceSampleNative
        {
            Name = binding.TrackNames[trackIndex],
            Easing = binding.TrackEasings[trackIndex],
            Progress = progress
        };

        HashSet<string> Properties(Element node, string name)
        {
            var token = node.CheckedToken();
            int result = NativeMethods.urui_keyframes_properties(surfaceHandle, documentHandle, token, name, null, 0, out int size);
            if ((result != NativeStatus.BufferTooSmall && result != NativeStatus.Ok) || size < 1 || size > 1048576)
            {
                throw new InvalidOperationException("动画关键帧不存在或不可读取：" + name);
            }
            var bytes = new byte[size];
            if (NativeMethods.urui_keyframes_properties(surfaceHandle, documentHandle, token, name, bytes, size, out int actual) != NativeStatus.Ok || actual != size)
            {
                throw new InvalidOperationException("动画关键帧版本变化。");
            }
            return new HashSet<string>(Encoding.UTF8.GetString(bytes, 0, size - 1).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }

        unsafe void Native(Css binding, NativeMethods.SequenceSampleNative[] samples, int count, int mode)
        {
            int result;
            if (count > 0 && samples != null)
            {
                fixed (NativeMethods.SequenceSampleNative* ptr = samples)
                {
                    result = NativeMethods.urui_sequence_sample(surfaceHandle, documentHandle, binding.Node.CheckedToken(), binding.Owner, ptr, count, mode);
                }
            }
            else
            {
                result = NativeMethods.urui_sequence_sample(surfaceHandle, documentHandle, binding.Node.CheckedToken(), binding.Owner, null, 0, mode);
            }
            if (result != NativeStatus.Ok)
            {
                throw new InvalidOperationException("动画采样失败：" + result + "，请检查关键帧、缓动、插件版本及属性控制权。");
            }
        }

        unsafe void ApplyBatch(int itemCount, int sampleCount)
        {
            if (itemCount == 0)
            {
                return;
            }
            fixed (NativeMethods.SequenceBatchItemNative* itemsPtr = batchItems)
            fixed (NativeMethods.SequenceSampleNative* samplesPtr = batchSamples)
            {
                int result = NativeMethods.urui_sequence_apply_batch(
                    surfaceHandle, documentHandle, itemsPtr, itemCount,
                    sampleCount > 0 ? samplesPtr : null, sampleCount);
                if (result != NativeStatus.Ok)
                {
                    throw new InvalidOperationException("动画采样失败：" + result + "，请检查关键帧、缓动、插件版本及属性控制权。");
                }
            }
        }

        internal void Sample(double time)
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("动画节点已失效或移出播放根。");
            }
            if (Styles.Count > 0)
            {
                int sampleOffset = 0;
                for (int i = 0; i < Styles.Count; i++)
                {
                    var binding = Styles[i];
                    binding.Count = 0;
                    uint startOffset = (uint)sampleOffset;
                    var tracks = binding.Tracks;
                    for (int j = 0; j < tracks.Count; j++)
                    {
                        var at = SequenceTiming.At(tracks[j], time);
                        if (at.Active)
                        {
                            var sample = Entry(binding, j, at.Progress);
                            binding.Samples[binding.Count++] = sample;
                            batchSamples[sampleOffset++] = sample;
                        }
                    }
                    batchItems[i] = new NativeMethods.SequenceBatchItemNative
                    {
                        Token = binding.Node.CheckedToken(),
                        Owner = binding.Owner,
                        SampleOffset = startOffset,
                        SampleCount = (uint)binding.Count,
                        Mode = 0,
                        Reserved = 0
                    };
                }
                ApplyBatch(Styles.Count, sampleOffset);
            }
            try
            {
                ApplyStyles(false);
                for (int i = 0; i < Extensions.Count; i++)
                {
                    var binding = Extensions[i];
                    binding.Selected = null;
                    binding.Local = 0;
                    var tracks = binding.Tracks;
                    for (int j = 0; j < tracks.Count; j++)
                    {
                        var track = tracks[j];
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
                catch (Exception rollback)
                {
                    errors.Add(rollback);
                }
                for (int i = 0; i < Extensions.Count; i++)
                {
                    var binding = Extensions[i];
                    try
                    {
                        binding.Driver.Sample(binding.Previous, binding.PreviousLocal, binding.Previous != null);
                    }
                    catch (Exception rollback)
                    {
                        errors.Add(rollback);
                    }
                }
                if (errors.Count > 1)
                {
                    throw new AggregateException("动画采样失败，恢复时也遇到错误。", errors);
                }
                throw;
            }
            for (int i = 0; i < Styles.Count; i++)
            {
                var binding = Styles[i];
                if (binding.Count > 0)
                {
                    Array.Copy(binding.Samples, binding.Previous, binding.Count);
                }
                binding.PreviousCount = binding.Count;
            }
            for (int i = 0; i < Extensions.Count; i++)
            {
                var binding = Extensions[i];
                binding.Previous = binding.Selected;
                binding.PreviousLocal = binding.Local;
            }
        }

        void Layout()
        {
            if (NativeMethods.urui_sequence_layout(surfaceHandle, documentHandle) != NativeStatus.Ok)
            {
                throw new InvalidOperationException("动画布局上下文已失效。");
            }
        }

        void ApplyStyles(bool previous)
        {
            if (Styles.Count == 0)
            {
                return;
            }
            // Start from the frozen authoring base, so mixed units never inherit a previously sampled layout.
            for (int i = 0; i < Styles.Count; i++)
            {
                var binding = Styles[i];
                batchItems[i] = new NativeMethods.SequenceBatchItemNative
                {
                    Token = binding.Node.CheckedToken(),
                    Owner = binding.Owner,
                    SampleOffset = 0,
                    SampleCount = 0,
                    Mode = 3,
                    Reserved = 0
                };
            }
            ApplyBatch(Styles.Count, 0);
            Layout();
            for (int first = 0; first < Styles.Count;)
            {
                int end = first + 1;
                while (end < Styles.Count && Styles[end].Depth == Styles[first].Depth)
                {
                    end++;
                }
                // First settle this depth's own geometry/font, then resolve transforms/em against it.
                int itemCount = end - first;
                int sampleOffset = 0;
                for (int i = first; i < end; i++)
                {
                    var binding = Styles[i];
                    int count = previous ? binding.PreviousCount : binding.Count;
                    var source = previous ? binding.Previous : binding.Samples;
                    uint startOffset = (uint)sampleOffset;
                    for (int j = 0; j < count; j++)
                    {
                        batchSamples[sampleOffset++] = source[j];
                    }
                    batchItems[i - first] = new NativeMethods.SequenceBatchItemNative
                    {
                        Token = binding.Node.CheckedToken(),
                        Owner = binding.Owner,
                        SampleOffset = startOffset,
                        SampleCount = (uint)count,
                        Mode = count == 0 ? 3 : 1,
                        Reserved = 0
                    };
                }
                for (int pass = 0; pass < 2; pass++)
                {
                    ApplyBatch(itemCount, sampleOffset);
                    Layout();
                }
                first = end;
            }
        }

        public void Dispose()
        {
            List<Exception> errors = null;
            try
            {
                for (int i = 0; i < Styles.Count; i++)
                {
                    var binding = Styles[i];
                    if (binding.Captured && binding.Node.IsValid)
                    {
                        try
                        {
                            Native(binding, null, 0, 1);
                            binding.Captured = false;
                        }
                        catch (Exception error)
                        {
                            (errors ??= new List<Exception>()).Add(error);
                        }
                    }
                }
                for (int i = 0; i < Extensions.Count; i++)
                {
                    try
                    {
                        Extensions[i].Driver?.Dispose();
                    }
                    catch (Exception error)
                    {
                        (errors ??= new List<Exception>()).Add(error);
                    }
                }
                Styles.Clear();
                Extensions.Clear();
            }
            finally
            {
                if (utf8PoolHandle.IsAllocated)
                {
                    utf8PoolHandle.Free();
                }
                utf8Pool = null;
                batchItems = null;
                batchSamples = null;
                GC.SuppressFinalize(this);
            }
            if (errors != null)
            {
                throw new AggregateException("部分动画覆盖未能恢复。", errors);
            }
        }
    }
}
