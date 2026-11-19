using System;
using URUI.Elements;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        /// <summary>
        /// 合并初始化中的几何请求；读取几何或最外层作用域结束时同步。
        /// </summary>
        public IDisposable BeginUpdate()
        {
            CheckThread();
            if (!IsLoaded)
            {
                throw new InvalidOperationException("更新批次需要已加载文档。");
            }
            updateDepth++;
            return new UpdateBatch(this);
        }

        static readonly Unity.Profiling.ProfilerMarker flushMarker = new("URUI.Document.FlushPendingGeometry");
        int updateDepth;
        bool updateGeometryPending;
        bool flushingUpdates;
        bool updateRootRequested;
        Element updateRoot;
        sealed class UpdateBatch : IDisposable
        {
            Document owner;
            internal UpdateBatch(Document owner)
            {
                this.owner = owner;
            }
            public void Dispose()
            {
                var target = owner;
                if (ReferenceEquals(target, null))
                {
                    return;
                }
                target.CheckThread();
                owner = null;
                if (--target.updateDepth != 0)
                {
                    return;
                }
                try
                {
                    if (target && target.IsLoaded)
                    {
                        target.FlushUpdateBatch();
                    }
                }
                finally
                {
                    target.updateGeometryPending = false;
                    target.updateRoot = null;
                    target.updateRootRequested = false;
                }
            }
        }

        /// <summary>
        /// 请求初始化几何；更新批次内合并，普通调用在返回前完成同步。
        /// </summary>
        public void RequestGeometry(Element root)
        {
            CheckThread();
            if (root == null || !ReferenceEquals(root.Document, this) || !root.IsValid || !root.IsAttached)
            {
                throw new ArgumentException("几何请求根必须是当前文档中已挂载的有效元素。", nameof(root));
            }
            if (updateDepth == 0)
            {
                SynchronizeGeometry(root);
                return;
            }
            if (!updateRootRequested)
            {
                updateRoot = root;
                updateRootRequested = true;
            }
            else if (updateRoot != null)
            {
                while (updateRoot != null && !updateRoot.Contains(root))
                {
                    updateRoot = updateRoot.Parent;
                }
            }
            updateGeometryPending = true;
        }

        /// <summary>
        /// 立即提交批次内待处理的布局，供持有几何缓存的适配器在读取前使用。
        /// </summary>
        /// <returns>本次是否完成了同步；布局/绘制回调内保留当前回调对应的快照。</returns>
        public bool FlushPendingGeometry()
        {
            CheckThread();
            if (!updateGeometryPending || flushingUpdates || !IsLoaded || eventDepth != 0 || nativeDepth != 0 ||
                changingContent || surface.IsBusy || surface.IsRefreshingDocuments)
            {
                return false;
            }
            flushingUpdates = true;
            updateGeometryPending = false;
            try
            {
                using var sample = flushMarker.Auto();
                host.SynchronizeGeometry();
                return true;
            }
            finally
            {
                flushingUpdates = false;
            }
        }

        void FlushUpdateBatch()
        {
            // 读取几何只更新布局；显式初始化请求在批次末尾才准备所需绘制器。
            if (!updateRootRequested)
            {
                FlushPendingGeometry();
                return;
            }
            var root = updateRoot;
            updateRoot = null;
            updateRootRequested = false;
            SynchronizeGeometryCore(root != null && root.IsValid && root.IsAttached ? root : null);
        }

        internal void MarkGeometryChanged()
        {
            if (updateDepth != 0)
            {
                updateGeometryPending = true;
            }
        }

        internal void ReadGeometryBarrier()
        {
            if (updateDepth != 0)
            {
                FlushPendingGeometry();
            }
        }

        /// <summary>
        /// 同步样式、布局与空间变换，不为普通元素生成完整绘制记录；初始化后可立即读取几何。
        /// </summary>
        /// <remarks>
        /// 带自定义绘制器或外部几何的文档仍完成绘制器准备。后续正常帧或受控提交准备绘制；需要立即借用完整记录时使用 SynchronizeLayout。
        /// </remarks>
        public void SynchronizeGeometry()
            => SynchronizeGeometryCore(null);

        /// <summary>
        /// 同步文档几何，并保证指定子树内的绘制器立即就绪；适用于页内视图初始化。
        /// </summary>
        /// <remarks>
        /// root 必须是当前文档中已挂载的有效元素。子树以外的绘制器由后续正常帧或受控提交准备。
        /// </remarks>
        public void SynchronizeGeometry(Element root)
        {
            CheckThread();
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }
            if (!ReferenceEquals(root.Document, this) || !root.IsValid || !root.IsAttached)
            {
                throw new ArgumentException("几何同步根必须是当前文档中已挂载的有效元素。", nameof(root));
            }
            SynchronizeGeometryCore(root);
        }

        void SynchronizeGeometryCore(Element painterRoot)
        {
            CheckThread();
            if (eventDepth != 0 || nativeDepth != 0 || changingContent || surface.IsBusy || surface.IsRefreshingDocuments)
            {
                throw new InvalidOperationException("同步几何必须在文档回调或元素更新之外执行。");
            }
            if (!IsLoaded)
            {
                throw new InvalidOperationException("同步几何需要已加载文档。");
            }
            updateGeometryPending = false;
            if (HasPaintCallbacksInSubtree(painterRoot))
            {
                host.SynchronizeLayout();
                return;
            }
            host.SynchronizeGeometry();
            // 几何/更新回调可注册绘制器；对返回时的活动内容再确认准备需求。
            if (IsLoaded && host && (painterRoot == null || painterRoot.IsValid && painterRoot.IsAttached) && HasPaintersInSubtree(painterRoot))
            {
                if (HasPaintCallbacksInSubtree(painterRoot))
                {
                    host.SynchronizeLayout();
                }
                else
                {
                    host.PrepareGeometryProviders();
                }
            }
        }

        /// <summary>
        /// 同步完成当前样式、布局和空间变换，以便初始化逻辑读取几何；不采集输入或触发 LayoutUpdated。元素几何变化仍正常通知。
        /// </summary>
        /// <remarks>
        /// 使用正常资源退役路径更新变换缓存；原生共享颜色附件的GPU命令在实际SRP提交时录制。必须在回调外调用；事件内可用RunAfterEvent。
        /// </remarks>
        public void SynchronizeLayout()
        {
            CheckThread();
            if (eventDepth != 0 || nativeDepth != 0 || changingContent || surface.IsBusy)
            {
                throw new InvalidOperationException("同步布局必须在文档回调外执行，请使用 RunAfterEvent。");
            }
            if (!IsLoaded)
            {
                throw new InvalidOperationException("同步布局需要已加载文档。");
            }
            updateGeometryPending = false;
            host.SynchronizeLayout();
        }
    }
}
