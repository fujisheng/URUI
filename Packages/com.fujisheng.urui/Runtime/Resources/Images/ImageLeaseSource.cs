using System;
using System.Threading;
using UnityEngine;

namespace URUI.Resources
{
    /// <summary>生产者和渲染租约共同持有动态图片，最后一个持有者退出后归还资源。</summary>
    public sealed class ImageLeaseSource : IDisposable
    {
        readonly Texture texture;
        readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        Action release;
        int references = 1;
        bool disposed;
        public ImageLeaseSource(Texture texture, Action release)
        {
            if (!texture) throw new ArgumentException("图片源必须有效。", nameof(texture));
            this.texture = texture;
            this.release = release ?? throw new ArgumentNullException(nameof(release));
        }
        public ImageLease Acquire()
        {
            CheckThread();
            if (disposed) throw new ObjectDisposedException(nameof(ImageLeaseSource));
            if (!texture) throw new InvalidOperationException("生产者在租约结束前销毁了图片。");
            references++;
            return new ImageLease(texture, Release);
        }
        public void Dispose()
        {
            CheckThread();
            if (disposed) return;
            disposed = true; Release();
        }
        void Release()
        {
            CheckThread();
            if (--references != 0) return;
            var callback = release; release = null;
            callback();
        }
        void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("动态图像所有权只能在创建线程操作。");
        }
    }
}
