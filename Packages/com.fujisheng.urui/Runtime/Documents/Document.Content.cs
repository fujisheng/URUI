using System;
using System.Collections.Generic;
using URUI.Elements;
using URUI.Internal.Interop;
using URUI.Rendering.Effects;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        sealed class ContentState
        {
            internal ulong SurfaceHandle;
            internal ulong Handle;
            internal uint Generation;
            internal uint LayoutVersion;
            internal ulong NativeLayoutVersion;
            internal ulong RefRevision;
            internal ulong InvalidationSerial;
            internal NativeMethods.ElementRefreshStamp ObservedRefreshStamp;
            internal string SourceUrl;
            internal string SourceMarkup;
            internal URUI.Resources.CompiledDocumentAsset CompiledAsset;
            internal string CompiledFingerprint;
            internal bool Retired;
            // -1 表示没有完整证明，-2 表示正在更新；非负值索引快照尾部的有序钩子。
            internal int CachedUpdateHookCount = -1;
            internal URUI.Resources.TextResource[] TextResources;
            internal URUI.Resources.ImageAsset[] Images;
            internal Func<string, URUI.Resources.ImageLease> ImageResolver;
#if UNITY_EDITOR
            internal Dictionary<string, UnityEngine.Texture> InspectionImageOverrides;
            internal Func<string, URUI.Resources.ImageLease> InspectionBaseImageResolver;
            internal Func<string, URUI.Resources.ImageLease> InspectionWrapperImageResolver;
#endif
            internal readonly Dictionary<ulong, Element> Elements = new();
            // 仅在文档完成首次布局后动态插入子树（SetInnerHtml/Template/Clone）时记录尚未包装节点的创建布局版本；
            // 首次文档加载（LayoutVersion == 0）或下一轮布局推进后为空，零常驻开销。
            internal Dictionary<ulong, uint> PendingSubtreeLayoutVersions;
            // 追加只写入已发布区间以外；每次遍历固定长度，回调新建节点不会进入旧快照。
            static readonly Unity.Profiling.ProfilerMarker snapshotCopyMarker = new("URUI.Elements.Snapshot.Copy");
            internal Element[] ElementSnapshot;
            int elementSnapshotCount;

            internal void AppendElementSnapshot(Element element)
            {
                // 初始化可以重入刷新；新包装器尚未被完整观察，先撤销整份文档的证明。
                ObservedRefreshStamp = default;
                CachedUpdateHookCount = -1;
                if (ElementSnapshot == null)
                {
                    return;
                }
                if (elementSnapshotCount == ElementSnapshot.Length)
                {
                    ElementSnapshot = null;
                    return;
                }
                ElementSnapshot[elementSnapshotCount++] = element;
            }

            internal ArraySegment<Element> GetElements()
            {
                if (ElementSnapshot == null)
                {
                    using var sample = snapshotCopyMarker.Auto();
                    elementSnapshotCount = Elements.Count;
                    ElementSnapshot = new Element[Math.Max(16, elementSnapshotCount + elementSnapshotCount / 2)];
                    Elements.Values.CopyTo(ElementSnapshot, 0);
                }
                return new ArraySegment<Element>(ElementSnapshot, 0, elementSnapshotCount);
            }

            internal readonly Dictionary<(ulong element, string shader), PaintRegistration> Painters = new();
            internal readonly Dictionary<(ulong token, EffectKind kind, ulong input), EffectEntry> EffectInstances = new();
            internal readonly Dictionary<ulong, EffectLayout> EffectLayouts = new();
            internal readonly Dictionary<string, URUI.Internal.Rendering.NativeProgramLayout> ImageLayouts = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, ILayerFilterProgram> FilterPrograms = new(StringComparer.Ordinal);
        }

        ContentState content;
        ContentState activeContent;
        ContentState candidateContent;

        internal ulong InvalidationSerial => content?.InvalidationSerial ?? 0;
        internal ulong RefRevision
        {
            get
            {
                var surfaceHandle = SurfaceHandle;
                return surfaceHandle != 0 ? NativeMethods.urui_element_ref_revision(surfaceHandle) : 0;
            }
        }

        internal ulong NativeLayoutVersion
        {
            get
            {
                if (!IsLoaded)
                {
                    return 0;
                }
                return NativeMethods.urui_document_layout_version(SurfaceHandle, content.Handle);
            }
        }

        internal string LoadedMarkup
        {
            get
            {
#if UNITY_EDITOR
                if (content?.CompiledAsset && content.CompiledAsset.AuthorSource)
                {
                    return content.CompiledAsset.AuthorSource.text;
                }
#endif
                return content?.SourceMarkup;
            }
        }

        internal Func<string, URUI.Resources.ImageLease> ResolverFor(ulong handle)
        {
            if (candidateContent != null && candidateContent.Handle == handle && !candidateContent.Retired)
            {
                return candidateContent.ImageResolver;
            }
            return activeContent != null && activeContent.Handle == handle && !activeContent.Retired ? activeContent.ImageResolver : null;
        }

        uint nextGeneration;
        uint generation => content?.Generation ?? 0;
        internal ulong NativeHandle => content?.Handle ?? 0;

        internal int Layer
        {
            get;
            private set;
        }

        internal int Order
        {
            get;
            private set;
        }

        internal bool Presented
        {
            get;
            private set;
        } = true;

        bool modal;
        internal bool IsModal => modal;

        /// <summary>
        /// 顶层文档的宿主顺序与模态状态；页面内的 CSS z-index 不改变此顺序。
        /// </summary>
        public void SetPresentation(int layer, int order, bool visible, bool modal)
        {
            CheckThread();
            if (Defer(() => SetPresentation(layer, order, visible, modal), DeferInvalidation.None))
            {
                return;
            }
            Layer = layer;
            Order = order;
            Presented = visible;
            this.modal = modal;
            if (IsLoaded && NativeMethods.urui_document_set_presentation(SurfaceHandle, NativeHandle, layer, order, visible ? 1 : 0, modal ? 1 : 0) != NativeStatus.Ok)
            {
                throw new InvalidOperationException("URUI 文档呈现状态更新失败。");
            }
            FinishNativeCall();
        }
    }
}
