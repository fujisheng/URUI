using System;
using System.Threading;

namespace URUI.Internal {
    /// <summary>
    /// 文档元素的弱对象身份。移除、替换和卸载后失效；必须在创建线程释放。
    /// 不在终结器调用原生代码，文档卸载也会统一释放引用。
    /// </summary>
    internal sealed class ElementReference : IDisposable
    {
        readonly RenderHost owner;
        readonly int thread;
        ulong token;

        internal ElementReference(RenderHost host, ulong value)
        {
            owner = host;
            token = value;
            thread = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>原对象仍存活且引用未释放。</summary>
        public bool IsValid
        {
            get { CheckThread(); return token != 0 && owner.ReferenceIsValid(token); }
        }

        /// <summary>写入二维位移与缩放；引用失效时返回 false。</summary>
        public bool SetTranslateScale2D(float x, float y, float sx, float sy)
        {
            CheckThread();
            return token != 0 && owner.SetReferenceTransform(token, x, y, sx, sy);
        }

        /// <summary>释放引用，不删除节点；重复调用安全。</summary>
        public void Dispose()
        {
            CheckThread();
            if (token == 0) return;
            owner.ReleaseReference(token);
            token = 0;
        }

        void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread)
                throw new InvalidOperationException("URUI element references require their context thread");
        }
    }
}
