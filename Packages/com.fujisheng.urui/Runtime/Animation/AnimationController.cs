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
        static readonly Predicate<(Element, string)> isInvalidHistoryKey = key => !key.Item1.IsValid;
        ulong lastHistoryRefRevision;
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
            => Play(root, activeClass, animationNames, token, null, null);

        /// <summary>
        /// 结束帧在绘制前切换到终态类。终态保留到显式移除或再次播放。
        /// </summary>
        public AnimationPlayback Play(Element root, string activeClass, AnimationGroup animationNames, CancellationToken token, string completionClass)
            => Play(root, activeClass, animationNames, token, completionClass, null);

        /// <summary>
        /// 可选在正式播放前先应用准备类并等待一次布局刷新，再切换到播放类；结束帧在绘制前切换到终态类。
        /// </summary>
        public AnimationPlayback Play(Element root, string activeClass, AnimationGroup animationNames, CancellationToken token, string completionClass, string prepareClass)
        {
            CheckThread();
            token.ThrowIfCancellationRequested();
            if (root == null || !root.IsValid || !ReferenceEquals(root.Document, document) || !root.IsAttached || string.IsNullOrWhiteSpace(activeClass) || animationNames == null)
            {
                throw new ArgumentException("动画需要当前文档的作者根、状态类及命名目标组。");
            }
            if (!string.IsNullOrEmpty(completionClass))
            {
                Element.RequireClassName(completionClass);
                if (completionClass == activeClass)
                {
                    throw new ArgumentException("终态类必须与播放类不同。", nameof(completionClass));
                }
            }
            if (!string.IsNullOrEmpty(prepareClass))
            {
                Element.RequireClassName(prepareClass);
                if (prepareClass == activeClass || prepareClass == completionClass)
                {
                    throw new ArgumentException("准备类必须与播放类和终态类不同。", nameof(prepareClass));
                }
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
            if (!string.IsNullOrEmpty(completionClass) && root.ClassListContains(completionClass))
            {
                root.RemoveFromClassList(completionClass);
            }
            if (!string.IsNullOrEmpty(prepareClass) && root.ClassListContains(prepareClass))
            {
                root.RemoveFromClassList(prepareClass);
            }
            var playback = new AnimationPlayback(this, ++serial, root, activeClass, animationNames, restart, completionClass, prepareClass);
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
                    if (!string.IsNullOrEmpty(playback.PrepareClass))
                    {
                        playback.Root.RemoveFromClassList(playback.PrepareClass);
                    }
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
                if (document && document.Surface != null && document.Surface.IsExecutingCallback)
                {
                    document.RunAfterEvent(Stop);
                }
                else
                {
                    Stop();
                }
            }
            else
            {
                context.Post(_ => Stop(), null);
            }
        }
        void Update(Document owner)
        {
            if (playing.Count > 0)
            {
                var items = PlayingSnapshot();
                for (int i = 0; i < items.Length; i++)
                {
                    var value = items[i];
                    if (!value.HasCompletionClass)
                    {
                        value.Tick();
                    }
                }
            }
            PruneHistory();
        }
        internal bool CommitCompletionClasses()
        {
            if (playing.Count == 0)
            {
                return false;
            }
            var changed = false;
            var items = PlayingSnapshot();
            for (int i = 0; i < items.Length; i++)
            {
                var playback = items[i];
                if (playback.HasCompletionClass && playback.IsPlaying)
                {
                    playback.Tick();
                    changed |= playback.Finished.IsCompletedSuccessfully;
                }
            }
            return changed;
        }
        void Unload(Document owner)
        {
            DiscardInvalid();
        }
        internal void DiscardInvalid()
        {
            if (playing.Count > 0)
            {
                var items = PlayingSnapshot();
                for (int i = 0; i < items.Length; i++)
                {
                    if (!items[i].Root.IsValid)
                    {
                        items[i].Cancel();
                    }
                }
            }
            lastHistoryRefRevision = 0;
            PruneHistory();
        }
        void PruneHistory()
        {
            if (history.Count == 0)
            {
                return;
            }
            var revision = document.RefRevision;
            if (revision != 0 && revision == lastHistoryRefRevision)
            {
                return;
            }
            lastHistoryRefRevision = revision;
            history.RemoveWhere(isInvalidHistoryKey);
        }
    }
}
