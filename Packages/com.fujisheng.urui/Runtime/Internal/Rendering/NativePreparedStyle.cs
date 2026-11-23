using System;
using URUI.Elements;
using URUI.Internal.Interop;
using URUI.Styles;

namespace URUI.Internal.Rendering
{
    // 每个准备实例持有一份原生样式版本；公开值快照按需物化后独立存活。
    internal unsafe struct NativePreparedStyle : IDisposable
    {
        Element element;
        ulong surface;
        ulong owner;
        NativeMethods.FrozenStyleHandle handle;
        ComputedStyleSnapshot materialized;

        internal void Replace(Element value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            value.Document.CheckThread();
            if (!value.IsValid || !value.HasLayout)
            {
                throw new InvalidOperationException("准备样式需要已完成布局的节点。");
            }
            value.Document.Styles.RequireFrozenProtocol();
            if (element != null && (!ReferenceEquals(element.Document, value.Document) || element.Token != value.Token))
            {
                Dispose();
            }
            var previous = handle;
            handle = default;
            surface = value.Document.SurfaceHandle;
            owner = value.NativeHandle;
            element = value;
            var pointer = previous.Slot == 0 ? IntPtr.Zero : (IntPtr)(&previous);
            var result = NativeMethods.urui_style_snapshot_freeze(surface, owner, value.CheckedToken(), pointer, out handle);
            if (result != NativeStatus.Ok)
            {
                throw new InvalidOperationException("准备样式冻结失败：" + result);
            }
        }

        internal ComputedStyleSnapshot Materialize(Element value)
        {
            if (handle.Slot == 0 || element == null || value == null || !ReferenceEquals(element.Document, value.Document) || element.Token != value.Token)
            {
                throw new InvalidOperationException("准备样式不属于该节点或已失效。");
            }
            return materialized = value.Document.Styles.GetFrozen(value, owner, handle, materialized);
        }

        public void Dispose()
        {
            element?.Document.CheckThread();
            var previous = handle;
            var surfaceHandle = surface;
            var document = owner;
            handle = default;
            surface = 0;
            owner = 0;
            element = null;
            materialized = null;
            if (previous.Slot == 0)
            {
                return;
            }
            var result = NativeMethods.urui_style_snapshot_release(surfaceHandle, document, previous);
            if (result != NativeStatus.Ok && result != NativeStatus.NotReady)
            {
                throw new InvalidOperationException("准备样式释放失败：" + result);
            }
        }
    }
}
