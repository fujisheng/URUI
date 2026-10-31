using System;
using System.Threading;

namespace RmlUnity
{
    /// <summary>
    /// 兼容层弱对象引用，不是会重新匹配的 ID 选择器。删除、替换和卸载会使它失效。
    /// 请在 context 线程 Dispose；文档卸载也会释放 token，不在终结器调用原生代码。
    /// 每个 context 最多 65536 个 token。新业务代码优先使用 URUI.Document.Q。
    /// </summary>
    public sealed class RmlElementReference : IDisposable
    {
        readonly RmlUnityHost owner;
        readonly int thread;
        ulong token;

        internal RmlElementReference(RmlUnityHost host, ulong value)
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
                throw new InvalidOperationException("Rml element references require their context thread");
        }
    }
}
