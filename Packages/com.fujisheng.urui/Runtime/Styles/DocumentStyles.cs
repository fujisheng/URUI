using System;
using System.Collections.Generic;
using URUI.Documents;
using URUI.Elements;
using URUI.Internal.Interop;

namespace URUI.Styles
{
    /// <summary>
    /// 复用原生稳定版本；只有实际值变化时才分配新快照、通知订阅。
    /// </summary>
    public sealed class DocumentStyles
    {
        readonly Document document;
        readonly Dictionary<ulong, ComputedStyleSnapshot> cache = new Dictionary<ulong, ComputedStyleSnapshot>();
        readonly List<Subscription> subscriptions = new List<Subscription>();
        readonly List<ulong> invalid = new List<ulong>();
        NativeMethods.StyleValue[] entryScratch = Array.Empty<NativeMethods.StyleValue>();
        byte[] textScratch = Array.Empty<byte>();
        ComputedStyleSnapshot sharedValues;
        bool checkedProtocol;
        bool checkedFrozenProtocol;
        ulong lastInvalidationSerial;

        public uint Generation
        {
            get;
            private set;
        } = 1;

        internal DocumentStyles(Document document)
        {
            this.document = document;
            document.LayoutUpdated += OnLayoutUpdated;
            document.Unloaded += OnUnloaded;
        }

        public ComputedStyleSnapshot GetComputed(Element element)
        {
            document.CheckThread();
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            if (!ReferenceEquals(element.Document, document))
            {
                throw new ArgumentException("元素不属于当前文档。", nameof(element));
            }
            if (!element.IsValid || !element.HasLayout)
            {
                throw new InvalidOperationException("元素尚未完成布局或已失效。");
            }
            if (!checkedProtocol)
            {
                try
                {
                    if (NativeMethods.urui_style_api_version() != NativeMethods.ExpectedStyleApiVersion)
                    {
                        throw new NotSupportedException($"需要计算样式协议 v{NativeMethods.ExpectedStyleApiVersion}。");
                    }
                }
                catch (EntryPointNotFoundException exception)
                {
                    throw new NotSupportedException($"已安装插件缺少计算样式协议 v{NativeMethods.ExpectedStyleApiVersion}。", exception);
                }
                checkedProtocol = true;
            }
            var surface = document.SurfaceHandle;
            var token = element.CheckedToken();
            var hasCached = cache.TryGetValue(token, out var current);
            int result;
            ulong revision;
            int count;
            int bytes;
            if (hasCached)
            {
                result = NativeMethods.urui_element_style_snapshot(surface, element.NativeHandle, token, 0, null, 0, null, 0, out revision, out count, out bytes);
                if ((result != NativeStatus.BufferTooSmall && result != NativeStatus.Ok) || count < 0 || bytes < 0)
                {
                    throw new InvalidOperationException("无法取得计算样式快照：" + result);
                }
                if (current.Revision == revision)
                {
                    return current;
                }
            }
            else
            {
                if (entryScratch.Length < 64)
                {
                    entryScratch = new NativeMethods.StyleValue[64];
                }
                if (textScratch.Length < 512)
                {
                    textScratch = new byte[512];
                }
                result = NativeMethods.urui_element_style_snapshot(surface, element.NativeHandle, token, 0, entryScratch, entryScratch.Length, textScratch, textScratch.Length, out revision, out count, out bytes);
                if (result == NativeStatus.Ok && count >= 0 && bytes >= 0 && count <= entryScratch.Length && bytes <= textScratch.Length)
                {
                    var fastSnapshot = new ComputedStyleSnapshot(this, element, Generation, revision, entryScratch, count, textScratch, bytes, ReuseValues(null));
                    sharedValues = fastSnapshot;
                    cache[token] = fastSnapshot;
                    return fastSnapshot;
                }
                if ((result != NativeStatus.BufferTooSmall && result != NativeStatus.Ok) || count < 0 || bytes < 0)
                {
                    throw new InvalidOperationException("无法取得计算样式快照：" + result);
                }
            }
            if (entryScratch.Length < count)
            {
                entryScratch = new NativeMethods.StyleValue[count];
            }
            if (textScratch.Length < bytes)
            {
                textScratch = new byte[bytes];
            }
            var entries = entryScratch;
            var names = textScratch;
            if (count > 0 || bytes > 0)
            {
                result = NativeMethods.urui_element_style_snapshot(surface, element.NativeHandle, token, revision, entries, entries.Length, names, names.Length, out revision, out count, out bytes);
                if (result != NativeStatus.Ok || count > entries.Length || bytes > names.Length)
                {
                    throw new InvalidOperationException("计算样式快照在复制时变化：" + result);
                }
            }
            var snapshot = new ComputedStyleSnapshot(this, element, Generation, revision, entries, count, names, bytes, ReuseValues(current));
            sharedValues = snapshot;
            cache[token] = snapshot;
            return snapshot;
        }

        internal void RequireFrozenProtocol()
        {
            if (checkedFrozenProtocol)
            {
                return;
            }
            if (NativeMethods.urui_style_frozen_api_version() != NativeMethods.ExpectedStyleFrozenApiVersion)
            {
                throw new NotSupportedException($"需要准备样式快照协议 v{NativeMethods.ExpectedStyleFrozenApiVersion}。");
            }
            checkedFrozenProtocol = true;
        }

        internal ComputedStyleSnapshot GetFrozen(Element element, ulong owner, in NativeMethods.FrozenStyleHandle handle, ComputedStyleSnapshot previous)
        {
            document.CheckThread();
            // The native handle already pins a prepared layout's values. Reading it
            // during an effect callback must not query live DOM/layout state.
            if (element == null || !ReferenceEquals(element.Document, document) || !element.IsValid)
            {
                throw new InvalidOperationException("准备样式所属元素已失效。");
            }
            var surface = document.SurfaceHandle;
            var result = NativeMethods.urui_style_snapshot_copy(surface, owner, handle, 0, null, 0, null, 0, out var revision, out var count, out var bytes);
            if ((result != NativeStatus.BufferTooSmall && result != NativeStatus.Ok) || count < 0 || bytes < 0)
            {
                throw new InvalidOperationException("准备样式快照已失效：" + result);
            }
            // 同一元素/版本的不可变公开快照可直接共享，避免观察 Frame 后仅因引用不同重建布局。
            if (cache.TryGetValue(element.Token, out var current) && current.IsValid && current.Revision == revision)
            {
                return current;
            }
            if (previous != null && previous.IsValid && previous.Revision == revision)
            {
                return previous;
            }
            if (entryScratch.Length < count)
            {
                entryScratch = new NativeMethods.StyleValue[count];
            }
            if (textScratch.Length < bytes)
            {
                textScratch = new byte[bytes];
            }
            if (count > 0 || bytes > 0)
            {
                result = NativeMethods.urui_style_snapshot_copy(surface, owner, handle, revision, entryScratch, entryScratch.Length, textScratch, textScratch.Length, out revision, out count, out bytes);
                if (result != NativeStatus.Ok || count > entryScratch.Length || bytes > textScratch.Length)
                {
                    throw new InvalidOperationException("准备样式快照复制失败：" + result);
                }
            }
            // 公开快照始终复制值；只有调用者实际观察它时才创建托管对象。
            return sharedValues = new ComputedStyleSnapshot(this, element, Generation, revision, entryScratch, count, textScratch, bytes, ReuseValues(previous));
        }

        ComputedStyleSnapshot ReuseValues(ComputedStyleSnapshot previous)
        {
            if (previous != null)
            {
                return previous;
            }
            // 不同元素仍有独立身份和版本；构造器只共享完全相等的不可变文本与数值块。
            if (sharedValues != null && !sharedValues.IsValid)
            {
                sharedValues = null;
            }
            return sharedValues;
        }

        /// <summary>
        /// 从下一次稳定帧通知起通知；等值写入不重复通知，取消后不再回调。
        /// </summary>
        public IDisposable Subscribe(Element element, Action<ComputedStyleSnapshot> changed)
        {
            document.CheckThread();
            if (element == null || !ReferenceEquals(element.Document, document))
            {
                throw new ArgumentException("元素必须属于当前文档。", nameof(element));
            }
            if (changed == null)
            {
                throw new ArgumentNullException(nameof(changed));
            }
            var subscription = new Subscription(element, changed);
            subscriptions.Add(subscription);
            return subscription;
        }

        void OnLayoutUpdated(Document source)
        {
            var generation = Generation;
            var count = subscriptions.Count;
            var invalidationSerial = document.InvalidationSerial;
            if (count == 0 && (cache.Count == 0 || invalidationSerial == lastInvalidationSerial))
            {
                return;
            }
            List<Exception> failures = null;
            bool hadDisposed = false;
            for (var i = 0; i < count && Generation == generation; i++)
            {
                var subscription = subscriptions[i];
                if (subscription.Disposed)
                {
                    hadDisposed = true;
                    continue;
                }
                if (!subscription.Element.IsValid)
                {
                    subscription.Dispose();
                    hadDisposed = true;
                    continue;
                }
                if (!subscription.Element.HasLayout)
                {
                    continue;
                }
                try
                {
                    var snapshot = GetComputed(subscription.Element);
                    if (snapshot.Revision == subscription.Revision)
                    {
                        continue;
                    }
                    subscription.Revision = snapshot.Revision;
                    subscription.Changed(snapshot);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }
            if (hadDisposed)
            {
                for (var i = subscriptions.Count - 1; i >= 0; i--)
                {
                    if (subscriptions[i].Disposed)
                    {
                        subscriptions.RemoveAt(i);
                    }
                }
            }
            if (invalidationSerial != lastInvalidationSerial && cache.Count > 0)
            {
                lastInvalidationSerial = invalidationSerial;
                invalid.Clear();
                foreach (var entry in cache)
                {
                    if (!entry.Value.IsValid)
                    {
                        invalid.Add(entry.Key);
                    }
                }
                for (var i = 0; i < invalid.Count; i++)
                {
                    cache.Remove(invalid[i]);
                }
            }
            if (failures != null)
            {
                throw new AggregateException("计算样式订阅失败。", failures);
            }
        }

        void OnUnloaded(Document source)
        {
            Generation++;
            lastInvalidationSerial = 0;
            sharedValues = null;
            cache.Clear();
            DiscardInvalid();
        }

        internal void DiscardInvalid()
        {
            if (sharedValues != null && !sharedValues.IsValid)
            {
                sharedValues = null;
            }
            for (var i = subscriptions.Count - 1; i >= 0; i--)
            {
                var subscription = subscriptions[i];
                if (!subscription.Disposed && !subscription.Element.IsValid)
                {
                    subscription.Dispose();
                }
                if (subscription.Disposed)
                {
                    subscriptions.RemoveAt(i);
                }
            }
            invalid.Clear();
            foreach (var entry in cache)
            {
                if (!entry.Value.IsValid)
                {
                    invalid.Add(entry.Key);
                }
            }
            for (var i = 0; i < invalid.Count; i++)
            {
                cache.Remove(invalid[i]);
            }
        }

        sealed class Subscription : IDisposable
        {
            internal Element Element;
            internal Action<ComputedStyleSnapshot> Changed;
            internal ulong Revision;
            internal bool Disposed;

            internal Subscription(Element element, Action<ComputedStyleSnapshot> changed)
            {
                Element = element;
                Changed = changed;
            }

            public void Dispose()
            {
                Disposed = true;
                Changed = null;
                Element = null;
            }
        }
    }
}
