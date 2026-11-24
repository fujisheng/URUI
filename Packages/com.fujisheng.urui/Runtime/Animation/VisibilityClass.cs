using System;
using URUI.Documents;
using URUI.Elements;
namespace URUI.Animation
{
    /// <summary>
    /// 把实际显示状态映射为作者动画类；不创建进度节点、曲线或样式文本。
    /// </summary>
    public sealed class VisibilityClass : IDisposable
    {
        readonly Element target;
        readonly Element activation;
        readonly string className;
        bool active;
        bool disposed;
        public VisibilityClass(Element target, Element activation, string className)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
            this.activation = activation ?? throw new ArgumentNullException(nameof(activation));
            if (!ReferenceEquals(target.Document, activation.Document) || string.IsNullOrWhiteSpace(className))
            {
                throw new ArgumentException("动画目标与激活源必须属于同一文档并提供状态类。");
            }
            this.className = className;
            target.Document.LayoutUpdated += Update;
            target.Document.Unloaded += HandleUnloaded;
        }
        void HandleUnloaded(Document document) => Dispose();
        void Update(Document document)
        {
            if (disposed)
            {
                return;
            }
            if (!target.IsValid || !activation.IsValid)
            {
                Dispose();
                return;
            }
            const ElementState RequiredVisible = ElementState.Valid | ElementState.Attached | ElementState.VisibleInHierarchy;
            var flags = activation.StateFlags;
            if ((flags & ElementState.Valid) == 0)
            {
                Dispose();
                return;
            }
            var visible = (flags & RequiredVisible) == RequiredVisible && activation.HasLayout;
            if (active == visible)
            {
                return;
            }
            active = visible;
            target.EnableInClassList(className, active);
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (target.Document)
            {
                target.Document.LayoutUpdated -= Update;
                target.Document.Unloaded -= HandleUnloaded;
            }
            if (target.IsValid)
            {
                target.RemoveFromClassList(className);
            }
        }
    }
}
