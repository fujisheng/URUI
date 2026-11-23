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
        bool HasPaintersInSubtree(Element root)
        {
            if (!HasPainters)
            {
                return false;
            }
            if (root == null)
            {
                return true;
            }
            foreach (var registration in painters.Values)
            {
                if (registration.element.IsValid && root.Contains(registration.element))
                {
                    return true;
                }
            }
            return false;
        }
        bool HasPaintCallbacksInSubtree(Element root)
        {
            if (!HasPainters)
            {
                return false;
            }
            foreach (var registration in painters.Values)
            {
                if (registration.paint != null && registration.element.IsValid &&
                    (root == null || root.Contains(registration.element)))
                {
                    return true;
                }
            }
            return false;
        }
        readonly ExternalGeometryContextPool geometryContexts = new();
        static ulong geometryProviderIdentity;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetGeometryProviderIdentity()
        {
            geometryProviderIdentity = 0;
        }
        /// <summary>
        /// 撤销可能引用旧资源的录制，再经宿主 GPU fence 退役。替换期间保留原 painter 注册。
        /// </summary>
        public void RetirePainterResources(IDisposable resource)
        {
            CheckThread();
            RequireEffectDomAccess();
            if (resource == null)
            {
                throw new ArgumentNullException(nameof(resource));
            }
            if (nativeDepth != 0 || eventDepth != 0 || surface.IsDispatching)
            {
                throw new InvalidOperationException("请通过 RunAfterEvent 在绘制/输入回调返回后替换 painter 资源。");
            }
            host.RetirePaintResource(resource);
        }

        sealed class PaintRegistration : IDisposable
        {
            internal Document owner;
            internal readonly Element element;
            internal readonly string shader;
            internal readonly Action<ElementPaintContext> paint;
            internal readonly Action<ExternalGeometryContext> geometry;
            internal readonly Action<int> onGeometryRecorded;
            internal readonly ulong provider;
            internal URUI.Internal.Rendering.NativeProgramLayout parameterLayout;
            Action release;
            bool requested;
            internal PaintRegistration(Document owner, Element element, string shader, Action<ElementPaintContext> paint, Action release)
            {
                this.owner = owner;
                this.element = element;
                this.shader = shader;
                this.paint = paint;
                this.release = release;
            }
            internal PaintRegistration(Document owner, Element element, string shader, Action<ExternalGeometryContext> geometry, Action<int> onRecorded, Action release, ulong provider)
            {
                this.owner = owner;
                this.element = element;
                this.shader = shader;
                this.geometry = geometry;
                onGeometryRecorded = onRecorded;
                this.release = release;
                this.provider = provider;
            }
            public void Dispose()
            {
                if (requested || !owner)
                {
                    return;
                }
                owner.CheckThread();
                requested = true;
                owner.RemovePainter(this);
            }
            internal IDisposable RetiredRelease()
            {
                // 样式访问器只服务 CPU 准备阶段，注销后即可释放；绘制资源仍等待 GPU。
                parameterLayout?.Dispose();
                parameterLayout = null;
                return new PaintRelease(() =>
                {
                    var callback = release;
                    release = null;
                    owner = null;
                    callback?.Invoke();
                });
            }
        }
        sealed class PaintRelease : IDisposable
        {
            Action callback;
            internal PaintRelease(Action callback)
            {
                this.callback = callback;
            }
            public void Dispose()
            {
                var previous = callback;
                callback = null;
                try
                {
                    previous?.Invoke();
                }
                catch (Exception error)
                {
                    UnityEngine.Debug.LogException(error);
                }
            }
        }
        /// <summary>
        /// 绑定普通节点的作者 shader 装饰器；注销或文档卸载后，GPU 完成才调用 release。
        /// </summary>
        public IDisposable AttachPainter(Element element, string shader, Action<ElementPaintContext> paint, Action release = null)
        {
            CheckThread();
            RequireName(shader, nameof(shader));
            if (paint == null)
            {
                throw new ArgumentNullException(nameof(paint));
            }
            if (element == null || !ReferenceEquals(element.Document, this) || !element.IsValid || !IsLoaded)
            {
                throw new ArgumentException("绘制节点必须属于当前活动文档。", nameof(element));
            }
            if (nativeDepth != 0 || eventDepth != 0 || surface.IsDispatching)
            {
                throw new InvalidOperationException("不能在绘制或输入回调内注册绘制器。");
            }
            var key = (element.Token, shader);
            if (painters.ContainsKey(key))
            {
                throw new InvalidOperationException("该作者 shader 已有绘制器：" + shader);
            }
            var registration = new PaintRegistration(this, element, shader, paint, release);
            painters.Add(key, registration);
            return registration;
        }
        /// <summary>
        /// 绑定只读几何准备器；它在图层捕获之前声明输入，不能修改文档。注销后仍沿 GPU fence 退役。
        /// </summary>
        /// <param name="onRecorded">每次实际原生绘制组录制时，通知本准备器参与的组数；不代表相机提交次数。</param>
        public IDisposable AttachGeometryProvider(Element element, string shader, Action<ExternalGeometryContext> prepare, Action<int> onRecorded = null, Action release = null, RenderParameterLayout parameters = null)
        {
            CheckThread();
            RequireEffectDomAccess();
            RequireName(shader, nameof(shader));
            if (prepare == null)
            {
                throw new ArgumentNullException(nameof(prepare));
            }
            if (element == null || !ReferenceEquals(element.Document, this) || !element.IsValid || !IsLoaded)
            {
                throw new ArgumentException("几何节点必须属于当前活动文档。", nameof(element));
            }
            if (nativeDepth != 0 || eventDepth != 0 || surface.IsDispatching)
            {
                throw new InvalidOperationException("不能在绘制或输入回调内注册几何准备器。");
            }
            var key = (element.Token, shader);
            if (painters.ContainsKey(key))
            {
                throw new InvalidOperationException("该作者 shader 已有绘制器：" + shader);
            }
            if (geometryProviderIdentity == ulong.MaxValue)
            {
                throw new InvalidOperationException("外部几何身份已耗尽。");
            }
            var registration = new PaintRegistration(this, element, shader, prepare, onRecorded, release, ++geometryProviderIdentity);
            try
            {
                if (parameters != null)
                {
                    registration.parameterLayout = new URUI.Internal.Rendering.NativeProgramLayout(this, "geometry:" + registration.provider, parameters);
                }
                painters.Add(key, registration);
                return registration;
            }
            catch
            {
                registration.parameterLayout?.Dispose();
                throw;
            }
        }
        internal bool HasGeometryProvider(ulong element, string shader)
            => painters.TryGetValue((element, shader), out var registration) && registration.geometry != null;
        internal ulong PrepareGeometry(ulong element, string shader, IExternalGeometrySink sink, uint command, bool clippedOut, bool mask)
        {
            if (!painters.TryGetValue((element, shader), out var registration) || registration.geometry == null)
            {
                throw new InvalidOperationException("作者 shader 缺少几何准备器：" + shader);
            }
            var context = geometryContexts.Open(sink, registration.provider, command, registration.element.Layout.size, clippedOut, mask, registration.parameterLayout?.RowCount ?? 0);
            try
            {
                context.CaptureParameters(registration.parameterLayout, registration.element);
                effectCallbackDepth++;
                surface.BeginEffect();
                try
                {
                    registration.geometry(context);
                }
                finally
                {
                    effectCallbackDepth--;
                    surface.EndEffect();
                }
            }
            finally
            {
                context.Close();
            }
            return registration.provider;
        }
        internal void GeometryRecorded(ulong element, string shader, int groups)
        {
            if (!painters.TryGetValue((element, shader), out var registration) || registration.onGeometryRecorded == null)
            {
                return;
            }
            effectCallbackDepth++;
            surface.BeginEffect();
            try
            {
                registration.onGeometryRecorded(groups);
            }
            finally
            {
                effectCallbackDepth--;
                surface.EndEffect();
            }
        }
        void RemovePainter(PaintRegistration registration)
        {
            // 卸载已将绘制资源交给 GPU 退役队列；外部组件随后 Dispose 不再访问空内容。
            if (content == null)
            {
                return;
            }
            if (Defer(() => RemovePainter(registration), DeferInvalidation.None))
            {
                return;
            }
            var key = (registration.element.Token, registration.shader);
            if (!painters.TryGetValue(key, out var current) || !ReferenceEquals(current, registration))
            {
                return;
            }
            // 先撤销未来提交，再退役资源；提交状态不确定时保留注册和所有权。
            host.RetirePaintResource(registration.RetiredRelease());
            painters.Remove(key);
        }
        void ReleasePainters(ContentState state)
        {
            if (state.Painters.Count == 0)
            {
                return;
            }
            var releases = new List<IDisposable>(state.Painters.Count);
            foreach (var registration in state.Painters.Values)
            {
                releases.Add(registration.RetiredRelease());
            }
            if (host)
            {
                host.RetirePaintResources(releases);
            }
            else
            {
                foreach (var release in releases)
                {
                    release.Dispose();
                }
            }
            state.Painters.Clear();
        }
        internal void InvokePainter(ulong token, string shader, ElementPaintContext context)
        {
            if (!painters.TryGetValue((token, shader), out var registration))
            {
                throw new InvalidOperationException("作者 shader 缺少绘制器：" + shader);
            }
            if (registration.geometry != null)
            {
                throw new InvalidOperationException("只读几何准备器不能作为任意绘制回调执行。");
            }
            try
            {
                registration.paint(context);
            }
            finally
            {
                context.Close();
            }
        }
    }
}
