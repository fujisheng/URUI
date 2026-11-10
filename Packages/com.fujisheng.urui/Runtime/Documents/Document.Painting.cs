using System;
using System.Collections.Generic;
using URUI.Elements;
using URUI.Rendering;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        Dictionary<(ulong element, string shader), PaintRegistration> painters => content.Painters;
        internal bool HasPainters => content != null && painters.Count != 0;
        internal void BeginPainterDraw()=>host.BeginPainterDraw();
        /// <summary>撤销可能引用旧资源的录制，再经宿主 GPU fence 退役。替换期间保留原 painter 注册。</summary>
        public void RetirePainterResources(IDisposable resource)
        {
            CheckThread();RequireEffectDomAccess();
            if(resource==null)throw new ArgumentNullException(nameof(resource));
            if(nativeDepth!=0||eventDepth!=0||surface.IsDispatching)throw new InvalidOperationException("请通过 RunAfterEvent 在绘制/输入回调返回后替换 painter 资源。");
            host.RetirePaintResource(resource);
        }

        sealed class PaintRegistration : IDisposable
        {
            internal Document owner;
            internal readonly Element element;
            internal readonly string shader;
            internal readonly Action<ElementPaintContext> paint;
            Action release;
            bool requested;
            internal PaintRegistration(Document owner, Element element, string shader, Action<ElementPaintContext> paint, Action release)
            { this.owner = owner; this.element = element; this.shader = shader; this.paint = paint; this.release = release; }
            public void Dispose()
            {
                if (requested || !owner) { return; }
                owner.CheckThread(); requested = true;
                owner.RemovePainter(this);
            }
            internal IDisposable RetiredRelease() => new PaintRelease(() =>
            {
                var callback = release; release = null; owner = null;
                callback?.Invoke();
            });
        }
        sealed class PaintRelease : IDisposable
        {
            Action callback;
            internal PaintRelease(Action callback) { this.callback = callback; }
            public void Dispose()
            {
                var previous = callback; callback = null;
                try { previous?.Invoke(); }
                catch (Exception error) { UnityEngine.Debug.LogException(error); }
            }
        }
        /// <summary>绑定普通节点的作者 shader 装饰器；注销或文档卸载后，GPU 完成才调用 release。</summary>
        public IDisposable AttachPainter(Element element, string shader, Action<ElementPaintContext> paint, Action release = null)
        {
            CheckThread(); RequireName(shader, nameof(shader));
            if (paint == null) { throw new ArgumentNullException(nameof(paint)); }
            if (element == null || !ReferenceEquals(element.Document, this) || !element.IsValid || !IsLoaded)
            { throw new ArgumentException("绘制节点必须属于当前活动文档。", nameof(element)); }
            if (nativeDepth != 0 || eventDepth != 0 || surface.IsDispatching) { throw new InvalidOperationException("不能在绘制或输入回调内注册绘制器。"); }
            var key = (element.Token, shader);
            if (painters.ContainsKey(key)) { throw new InvalidOperationException("该作者 shader 已有绘制器：" + shader); }
            var registration = new PaintRegistration(this, element, shader, paint, release);
            painters.Add(key, registration);
            host.NotifyExternalRenderResourcesChanged();
            return registration;
        }
        void RemovePainter(PaintRegistration registration)
        {
            // 卸载已将绘制资源交给 GPU 退役队列；外部组件随后 Dispose 不再访问空内容。
            if (content == null) { return; }
            if (Defer(() => RemovePainter(registration))) { return; }
            var key = (registration.element.Token, registration.shader);
            if (!painters.TryGetValue(key, out var current) || !ReferenceEquals(current, registration)) { return; }
            // 先撤销未来提交，再退役资源；提交状态不确定时保留注册和所有权。
            host.RetirePaintResources(new List<IDisposable> { registration.RetiredRelease() });
            painters.Remove(key);
        }
        void ReleasePainters()
        {
            if (painters.Count == 0) { return; }
            var releases = new List<IDisposable>();
            foreach (var registration in painters.Values) { releases.Add(registration.RetiredRelease()); }
            host.RetirePaintResources(releases);
            painters.Clear();
        }
        internal void InvokePainter(ulong token, string shader, ElementPaintContext context)
        {
            if (!painters.TryGetValue((token, shader), out var registration))
            { throw new InvalidOperationException("作者 shader 缺少绘制器：" + shader); }
            try { registration.paint(context); }
            finally { context.Close(); }
        }
        internal void BeginPaintFrame() { CheckThread(); nativeDepth++; }
        internal void EndPaintFrame() { nativeDepth--; FinishNativeCall(); }
    }
}
