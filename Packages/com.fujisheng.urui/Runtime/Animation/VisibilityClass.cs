using System;
using URUI.Documents;
using URUI.Elements;
namespace URUI.Animation
{
    /// <summary>把实际显示状态映射为作者动画类；不创建进度节点、曲线或样式文本。</summary>
    public sealed class VisibilityClass:IDisposable
    {
        readonly Element target,activation;readonly string className;bool active,disposed;
        public VisibilityClass(Element target,Element activation,string className)
        {
            this.target=target??throw new ArgumentNullException(nameof(target));this.activation=activation??throw new ArgumentNullException(nameof(activation));
            if(!ReferenceEquals(target.Document,activation.Document)||string.IsNullOrWhiteSpace(className))throw new ArgumentException("动画目标与激活源必须属于同一文档并提供状态类。");
            this.className=className;target.Document.LayoutUpdated+=Update;
        }
        void Update(Document document)
        {
            if(disposed||!target.IsValid||!activation.IsValid)return;
            var visible=activation.IsAttached&&activation.HasLayout&&activation.IsVisibleInHierarchy;
            if(active==visible)return;active=visible;target.EnableInClassList(className,active);
        }
        public void Dispose(){if(disposed)return;disposed=true;target.Document.LayoutUpdated-=Update;if(target.IsValid)target.RemoveFromClassList(className);}
    }
}
