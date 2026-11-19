using System;
namespace URUI.Internal.Resources
{
    // 跟随提交自身存活，转入全局退役也不归还名额；真实 fence 完成后才释放。
    internal sealed class ImageSubmissionBudget
    {
        internal int Count
        {
            get;
            private set;
        }
        internal void Reserve()
        {
            if (Count >= 8)
            {
                throw new InvalidOperationException("URUI GPU 提交积压，等待真实 fence 完成。");
            }
            Count++;
        }
        internal void Release()
        {
            if (Count <= 0)
            {
                throw new InvalidOperationException("URUI GPU 提交名额被重复归还。");
            }
            Count--;
        }
    }
}
