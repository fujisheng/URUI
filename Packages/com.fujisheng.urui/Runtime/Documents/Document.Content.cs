using System.Collections.Generic;
using URUI.Elements;
using URUI.Rendering.Effects;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        sealed class ContentState
        {
            internal ulong Handle;
            internal uint Generation, LayoutVersion;
            internal string SourceUrl;
            internal bool Retired;
            internal URUI.Resources.TextResource[] TextResources;
            internal URUI.Resources.ImageAsset[] Images;
            internal System.Func<string, URUI.Resources.ImageLease> ImageResolver;
            internal readonly Dictionary<ulong, Element> Elements = new();
            // 回调可能增删元素或重载文档；只在成员变化时替换快照，正在遍历的数组保持独立。
            internal Element[] ElementSnapshot;
            internal Element[] GetElements()
            {
                if (ElementSnapshot == null)
                {
                    ElementSnapshot = new Element[Elements.Count];
                    Elements.Values.CopyTo(ElementSnapshot, 0);
                }
                return ElementSnapshot;
            }
            internal readonly Dictionary<(ulong element, string shader), PaintRegistration> Painters = new();
            internal readonly Dictionary<(ulong token, EffectKind kind, ulong input), EffectEntry> EffectInstances = new();
            internal readonly Dictionary<ulong,EffectLayout> EffectLayouts = new();
            internal readonly Dictionary<string, ILayerFilterProgram> FilterPrograms = new(System.StringComparer.Ordinal);
        }
        ContentState content, activeContent, candidateContent;
        internal System.Func<string, URUI.Resources.ImageLease> ResolverFor(ulong handle)
        {
            if (candidateContent != null && candidateContent.Handle == handle && !candidateContent.Retired) return candidateContent.ImageResolver;
            return activeContent != null && activeContent.Handle == handle && !activeContent.Retired ? activeContent.ImageResolver : null;
        }
        uint nextGeneration;
        uint generation => content?.Generation ?? 0;
        internal ulong NativeHandle => content?.Handle ?? 0;
        internal int Layer { get; private set; }
        internal int Order { get; private set; }
        internal bool Presented { get; private set; } = true;
        bool modal;
        /// <summary>顶层文档的宿主顺序与模态状态；页面内的 CSS z-index 不改变此顺序。</summary>
        public void SetPresentation(int layer, int order, bool visible, bool modal)
        {
            CheckThread();
            if (Defer(() => SetPresentation(layer, order, visible, modal), false)) return;
            Layer = layer; Order = order; Presented = visible; this.modal = modal;
            if (IsLoaded && URUI.Internal.Interop.NativeMethods.urui_document_set_presentation(NativeHandle, layer, order, visible ? 1 : 0, modal ? 1 : 0) != 1)
                throw new System.InvalidOperationException("URUI 文档呈现状态更新失败。");
            FinishNativeCall();
        }
    }
}
