using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using URUI.Documents;
using URUI.Elements;
using URUI.Internal.Interop;

namespace URUI.Animation
{
    /// <summary>绑定真实Element的统一序列。主线程使用；拥有采样/扩展租约，不拥有Document和原生源资产。</summary>
    public sealed class SequencePlayer : IDisposable
    {
        static long nextOwner;
        readonly Element root;
        readonly Document document;
        readonly SequenceDefinition definition;
        readonly ISequenceTrackFactory factory;
        readonly Dictionary<string, Element> slots;
        readonly ulong owner;
        SequenceBindings bindings;
        TaskCompletionSource<bool> completion = Completion();
        double lastClock;
        bool disposed, completed;
        public double Time
        {
            get; private set;
        }
        public bool IsPlaying
        {
            get; private set;
        }
        public Task Finished => completion.Task;
        public string Name => definition.Name;
        public double Duration => definition.Duration;
        static TaskCompletionSource<bool> Completion() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>复制定义并冻结基础值。目标须已挂载并完成布局；slots在播放根内，工厂可省略以使用文档注册扩展。</summary>
        public SequencePlayer(Element root, SequenceDefinition definition, ISequenceTrackFactory factory = null, IDictionary<string, Element> slots = null)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            document = root.Document;
            if (!document)
                throw new InvalidOperationException("动画文档已失效。");
            document.CheckThread();
            RequireRoot();
            this.definition = SequenceDefinition.Parse((definition ?? throw new ArgumentNullException(nameof(definition))).ToJson());
            this.factory = factory;
            this.slots = slots == null ? null : new Dictionary<string, Element>(slots, StringComparer.Ordinal);
            owner = unchecked((ulong)Interlocked.Increment(ref nextOwner));
            try
            {
                if (NativeMethods.urui_sequence_api_version() != 1)
                    throw new NotSupportedException("需要序列采样协议v1插件。");
            }
            catch (EntryPointNotFoundException error) { throw new NotSupportedException("已安装插件缺少序列采样协议v1。", error); }
            bindings = new SequenceBindings(root, this.definition, owner, factory, this.slots);
            document.RegisterSequence(this);
            document.Unloaded += OnUnloaded;
        }
        void RequireRoot()
        {
            if (!root.IsValid || !root.IsAttached || !root.HasLayout)
                throw new InvalidOperationException("动画根必须有效、挂载并完成布局。");
        }
        void Check()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(SequencePlayer));
            if (!document)
                throw new InvalidOperationException("动画文档已失效。");
            document.CheckThread();
            if (!root.IsValid || !root.IsAttached || !root.HasLayout || (bindings != null && !bindings.IsValid))
            {
                Stop();
                throw new InvalidOperationException("动画绑定失效，播放已取消。");
            }
        }
        void EnsureBindings()
        {
            if (bindings == null)
                bindings = new SequenceBindings(root, definition, owner, factory, slots);
        }
        /// <summary>从当前位置开始或继续。自然完成后重新播放会从零开始；无限循环不会自然完成。</summary>
        public void Play()
        {
            Check();
            if (IsPlaying)
                return;
            if (completion.Task.IsCompleted)
                completion = Completion();
            if (completed)
                Time = 0;
            completed = false;
            EnsureBindings();
            Sample(Time);
            lastClock = document.Surface.UiClock.Now;
            IsPlaying = true;
        }
        /// <summary>暂停自动推进，保留当前姿态和租约。</summary>
        public void Pause()
        {
            Check();
            IsPlaying = false;
        }
        /// <summary>定位并暂停；不会发送自然完成或业务事件。</summary>
        public void Seek(double time)
        {
            Check();
            Sample(time);
            IsPlaying = false;
            lastClock = document.Surface.UiClock.Now;
        }
        /// <summary>按绝对序列时间采样；允许任意前后顺序。有限序列保持终态，循环序列按时长取模。</summary>
        public void Sample(double time)
        {
            Check();
            if (double.IsNaN(time) || double.IsInfinity(time) || time < 0)
                throw new InvalidOperationException("动画采样时间必须有限且非负。");
            EnsureBindings();
            double target = definition.Loop ? time % definition.Duration : Math.Min(time, definition.Duration);
            bindings.Sample(target);
            Time = target;
        }
        internal void Tick(double now)
        {
            if (disposed)
                return;
            try
            {
                if (!root.IsValid || !root.IsAttached || !root.IsVisibleInHierarchy || (bindings != null && !bindings.IsValid))
                {
                    Stop();
                    return;
                }
                if (!IsPlaying)
                    return;
                double delta = now - lastClock;
                if (double.IsNaN(delta) || double.IsInfinity(delta) || delta < 0)
                    throw new InvalidOperationException("动画时钟必须单调递增。");
                lastClock = now;
                double target = Time + delta;
                Sample(target);
                if (!definition.Loop && target >= definition.Duration)
                {
                    IsPlaying = false;
                    completed = true;
                    completion.TrySetResult(true);
                }
            }
            catch (Exception error)
            {
                IsPlaying = false;
                try
                {
                    Release();
                }
                catch (Exception releaseError) { error = new AggregateException(error, releaseError); }
                completion.TrySetException(error);
            }
        }
        /// <summary>恢复所有自有覆盖并回到零；正在等待的本次播放取消。之后可重新Play。</summary>
        public void Stop()
        {
            if (disposed)
                return;
            if (document)
                document.CheckThread();
            IsPlaying = false;
            Time = 0;
            completed = false;
            try
            {
                Release();
            }
            finally { completion.TrySetCanceled(); }
        }
        void Release()
        {
            var owned = bindings;
            bindings = null;
            owned?.Dispose();
        }
        void OnUnloaded(Document value) => Dispose();
        public void Dispose()
        {
            if (disposed)
                return;
            try
            {
                Stop();
            }
            finally
            {
                disposed = true;
                if (document)
                {
                    document.Unloaded -= OnUnloaded;
                    document.UnregisterSequence(this);
                }
            }
        }
    }
}
