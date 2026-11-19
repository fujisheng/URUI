using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using URUI.Elements;
using URUI.Events;
namespace URUI.Animation
{
    /// <summary>
    /// 带独立身份的播放句柄。取消、隐藏、销毁和旧句柄都不会发出成功完成。
    /// </summary>
    public sealed class AnimationPlayback : IDisposable
    {
        internal readonly AnimationController Owner;
        internal readonly Element Root;
        internal readonly string ActiveClass;
        readonly AnimationGroup group;
        readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly Dictionary<(Element, string), Tracks> observed = new();
        readonly List<IDisposable> subscriptions = new();
        CancellationTokenRegistration cancellation;
        IReadOnlyList<(Element, string)> expected;
        ulong epoch;
        bool started;
        bool sealedGroup;
        int stableFrames;
        bool restart;
        sealed class Tracks
        {
            internal readonly HashSet<ulong> started = new();
            internal readonly HashSet<ulong> ended = new();
        }
        public long Identity
        {
            get;
        }
        public Task Finished => completion.Task;
        public bool IsPlaying => !completion.Task.IsCompleted;
        internal AnimationPlayback(AnimationController owner, long identity, Element root, string activeClass, AnimationGroup group, bool restart)
        {
            Owner = owner;
            Identity = identity;
            Root = root;
            ActiveClass = activeClass;
            this.group = group;
            this.restart = restart;
            subscriptions.Add(root.RegisterCallback<AnimationCreatedEvent>(Created));
            subscriptions.Add(root.RegisterCallback<AnimationEndEvent>(Ended));
        }
        internal void Start(CancellationToken token)
        {
            cancellation = token.Register(() => Owner.RequestCancel(this, token));
            if (IsPlaying && !restart)
            {
                Begin();
            }
        }
        void Begin()
        {
            epoch = URUI.Internal.Interop.NativeMethods.urui_animation_next_identity();
            started = true;
            Root.AddToClassList(ActiveClass);
        }
        void Created(AnimationCreatedEvent value)
        {
            if (!IsPlaying || !started || value.Identity < epoch || value.Identity == 0 || value.Iterations < 0 || string.IsNullOrEmpty(value.AnimationName))
            {
                return;
            }
            var key = (value.Target, value.AnimationName);
            if (!observed.TryGetValue(key, out var tracks))
            {
                observed.Add(key, tracks = new Tracks());
            }
            tracks.started.Add(value.Identity);
        }
        void Ended(AnimationEndEvent value)
        {
            if (!IsPlaying || !started || value.Identity < epoch || value.Identity == 0)
            {
                return;
            }
            if (observed.TryGetValue((value.Target, value.AnimationName), out var tracks))
            {
                tracks.ended.Add(value.Identity);
            }
        }
        internal void Tick()
        {
            if (!IsPlaying)
            {
                return;
            }
            if (!Root.IsValid || !Root.IsAttached || !Root.IsVisibleInHierarchy)
            {
                Cancel();
                return;
            }
            if (restart)
            {
                restart = false;
                Begin();
                return;
            }
            if (!started)
            {
                return;
            }
            stableFrames++;
            try
            {
                if (!sealedGroup)
                {
                    var entries = new List<(Element, string)>();
                    if (group.Targets != null)
                    {
                        foreach (var target in group.Targets)
                        {
                            Validate(target.Element);
                            entries.Add((target.Element, target.Name));
                        }
                        foreach (var key in entries)
                        {
                            if (!observed.ContainsKey(key))
                            {
                                if (stableFrames < 2)
                                {
                                    return;
                                }
                                throw new InvalidOperationException($"命名动画没有在作者目标上启动：#{key.Item1.Name} / {key.Item2}");
                            }
                        }
                    }
                    else
                    {
                        var targets = group.Resolve() ?? throw new InvalidOperationException("作者动画目标解析器返回 null；空序列请返回空列表。");
                        foreach (var target in targets)
                        {
                            // 逐项入场可在动画启动前保持隐藏；观察到动画后才要求持续可见。
                            Validate(target, false);
                            var found = false;
                            foreach (var key in observed.Keys)
                            {
                                if (ReferenceEquals(key.Item1, target))
                                {
                                    entries.Add(key);
                                    found = true;
                                }
                            }
                            if (!found)
                            {
                                return;
                            }
                            Validate(target);
                        }
                    }
                    expected = entries;
                    sealedGroup = true;
                }
                foreach (var key in expected)
                {
                    Validate(key.Item1);
                    var tracks = observed[key];
                    if (tracks.started.Count == 0 || !tracks.ended.IsSupersetOf(tracks.started))
                    {
                        return;
                    }
                }
                Finish(null, false, default);
            }
            catch (Exception error)
            {
                Finish(error, false, default);
            }
        }
        void Validate(Element target, bool requireVisible = true)
        {
            if (target == null || !ReferenceEquals(target.Document, Root.Document) || !target.IsValid || !target.IsAttached || requireVisible && !target.IsVisibleInHierarchy)
            {
                throw new OperationCanceledException("动画目标已隐藏或销毁。");
            }
            if (!ReferenceEquals(target, Root) && !Root.Contains(target))
            {
                throw new ArgumentException("动画目标必须位于播放根的作者子树中。");
            }
        }
        internal void Finish(Exception error, bool cancelled, CancellationToken token)
        {
            if (!IsPlaying)
            {
                return;
            }
            Owner.Release(this);
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
            subscriptions.Clear();
            cancellation.Dispose();
            observed.Clear();
            expected = null;
            if (cancelled || error is OperationCanceledException)
            {
                completion.TrySetCanceled(token);
            }
            else if (error != null)
            {
                completion.TrySetException(error);
            }
            else
            {
                completion.TrySetResult(true);
            }
        }
        public void Cancel()
        {
            Owner.CheckThread();
            Finish(null, true, default);
        }
        public void Dispose() => Cancel();
    }
}
