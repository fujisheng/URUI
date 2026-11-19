using System;
using System.Collections.Generic;
using System.Threading;
using URUI.Documents;
using URUI.Elements;
namespace URUI.Animation
{
    /// <summary>
    /// 用作者状态类启动命名组；一个根/状态由最新句柄拥有，生命周期不进入业务 VM。
    /// </summary>
    public sealed class AnimationController
    {
        readonly Document document;
        readonly SynchronizationContext context;
        readonly int thread;
        readonly Dictionary<(Element, string), AnimationPlayback> playing = new();
        readonly HashSet<(Element, string)> history = new();
        long serial;
        AnimationPlayback[] snapshot;
        AnimationPlayback[] PlayingSnapshot()
        {
            if (snapshot == null)
            {
                snapshot = playing.Count == 0 ? Array.Empty<AnimationPlayback>() : new AnimationPlayback[playing.Count];
                playing.Values.CopyTo(snapshot, 0);
            }
            return snapshot;
        }
        internal AnimationController(Document document)
        {
            this.document = document;
            context = SynchronizationContext.Current ?? throw new InvalidOperationException("动画需要 Unity 主线程上下文。");
            thread = Thread.CurrentThread.ManagedThreadId;
            document.LayoutUpdated += Update;
            document.Unloaded += Unload;
        }
        internal void CheckThread()
        {
            document.CheckThread();
            if (thread != Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException("动画只能在 Unity 主线程操作。");
            }
        }
        public AnimationPlayback Play(Element root, string activeClass, AnimationGroup animationNames, CancellationToken token = default)
        {
            CheckThread();
            token.ThrowIfCancellationRequested();
            if (root == null || !root.IsValid || !ReferenceEquals(root.Document, document) || !root.IsAttached || string.IsNullOrWhiteSpace(activeClass) || animationNames == null)
            {
                throw new ArgumentException("动画需要当前文档的作者根、状态类及命名目标组。");
            }
            var key = (root, activeClass);
            var restart = history.Contains(key) || root.ClassListContains(activeClass);
            if (playing.TryGetValue(key, out var previous))
            {
                previous.Cancel();
            }
            if (root.ClassListContains(activeClass))
            {
                root.RemoveFromClassList(activeClass);
            }
            var playback = new AnimationPlayback(this, ++serial, root, activeClass, animationNames, restart);
            playing.Add(key, playback);
            snapshot = null;
            history.Add(key);
            try
            {
                playback.Start(token);
                return playback;
            }
            catch
            {
                playback.Cancel();
                throw;
            }
        }
        internal void Release(AnimationPlayback playback)
        {
            var key = (playback.Root, playback.ActiveClass);
            if (playing.TryGetValue(key, out var current) && ReferenceEquals(current, playback))
            {
                playing.Remove(key);
                snapshot = null;
                if (playback.Root.IsValid)
                {
                    playback.Root.RemoveFromClassList(playback.ActiveClass);
                }
            }
        }
        internal void RequestCancel(AnimationPlayback playback, CancellationToken token)
        {
            void Stop()
            {
                playback.Finish(null, true, token);
            }
            if (Thread.CurrentThread.ManagedThreadId == thread)
            {
                Stop();
            }
            else
            {
                context.Post(_ => Stop(), null);
            }
        }
        void Update(Document owner)
        {
            foreach (var value in PlayingSnapshot())
            {
                value.Tick();
            }
            history.RemoveWhere(key => !key.Item1.IsValid);
        }
        void Unload(Document owner)
        {
            DiscardInvalid();
        }
        internal void DiscardInvalid()
        {
            foreach (var value in PlayingSnapshot())
            {
                if (!value.Root.IsValid)
                {
                    value.Cancel();
                }
            }
            history.RemoveWhere(key => !key.Item1.IsValid);
        }
    }
}
