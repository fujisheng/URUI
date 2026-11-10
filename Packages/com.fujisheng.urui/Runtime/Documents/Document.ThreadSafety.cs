using System;
using System.Threading;
using URUI.Internal.Hosting;

namespace URUI.Documents
{
    // 统一检查主线程、当前世代和名称参数，内部调用同样遵守公开接口的约束。
    public sealed partial class Document
    {
        internal void CheckThread(bool allowImageCallback = false)
        {
            if (imageCallbackDepth != 0 && !allowImageCallback)
                throw new InvalidOperationException("不能在同步图片解析/释放回调中重入文档或元素 API。");
            // 未执行 Awake 的禁用组件也必须遵守主线程约束，使用运行时登记线程兜底。
            var expected = ownerThread == 0 ? unityThread : ownerThread;
            if (expected == 0 || Thread.CurrentThread.ManagedThreadId != expected)
            {
                throw new InvalidOperationException("URUI 文档和元素必须在 Unity 主线程使用。");
            }
        }

        internal bool IsCurrent(uint version) => host && host.Ready &&
            (activeContent != null && !activeContent.Retired && activeContent.Generation == version ||
             candidateContent != null && !candidateContent.Retired && candidateContent.Generation == version);

        internal RenderHost Host => host;

        internal static void RequireName(string value, string parameter)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("名称不能为空或包含 NUL。", parameter);
            }
        }
    }
}
