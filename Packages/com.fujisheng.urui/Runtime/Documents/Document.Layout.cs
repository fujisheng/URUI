using System;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        /// <summary>同步完成当前样式、布局和空间变换，以便初始化逻辑读取几何；不采集输入或触发 LayoutUpdated。元素几何变化仍正常通知。</summary>
        /// <remarks>使用正常绘制记录及资源退役路径更新URUI的变换缓存。必须在回调外调用；事件内可用RunAfterEvent。</remarks>
        public void SynchronizeLayout()
        {
            CheckThread();
            if(eventDepth!=0||nativeDepth!=0||changingContent||surface.IsBusy)
            {throw new InvalidOperationException("同步布局必须在文档回调外执行，请使用 RunAfterEvent。");}
            if(!IsLoaded){throw new InvalidOperationException("同步布局需要已加载文档。");}
            host.SynchronizeLayout();
        }
    }
}
