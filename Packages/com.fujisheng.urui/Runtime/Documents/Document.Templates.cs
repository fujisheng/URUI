using System;
using UnityEngine;
using URUI.Elements;
using URUI.Internal.Interop;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        /// <summary>
        /// 释放已加载的图片缓存；仍使用该路径的节点会再次解析图片，不卸载其他页面。
        /// </summary>
        /// <remarks>
        /// 可释放租约等待 GPU 退役。调用方先删除或更换相关节点，再释放自己的媒体生产者。
        /// </remarks>
        public void ReleaseImage(string source)
        {
            CheckThread();
            RequireName(source, nameof(source));
            if (Defer(() => ReleaseImage(source), DeferInvalidation.None))
            {
                return;
            }
            if (!IsLoaded)
            {
                return;
            }
            BeginControlUpdate();
            try
            {
                host.ReleaseCachedImage(NativeHandle, source);
            }
            finally
            {
                EndControlUpdate();
            }
        }

        /// <summary>
        /// 载入已有 CSS 资产；默认追加，默认皮肤可明确放在作者规则之前。
        /// </summary>
        public void AddStyleSheet(TextAsset asset, string sourceUrl, bool beforeAuthor = false)
        {
            CheckThread();
            RequireName(sourceUrl, nameof(sourceUrl));
            if (!asset)
            {
                throw new ArgumentNullException(nameof(asset));
            }
            if (Defer(() => AddStyleSheet(asset, sourceUrl, beforeAuthor), DeferInvalidation.None))
            {
                return;
            }
            if (!IsLoaded)
            {
                throw new InvalidOperationException("先加载文档，再追加样式资源。");
            }
            if (string.IsNullOrWhiteSpace(asset.text) || asset.text.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("样式资源不能为空或包含 NUL。", nameof(asset));
            }
            if (NativeMethods.urui_document_add_stylesheet(SurfaceHandle, NativeHandle, asset.text, sourceUrl, beforeAuthor ? 1 : 0) != NativeStatus.Ok)
            {
                throw new InvalidOperationException("样式资源加载失败：" + asset.name);
            }
            MarkGeometryChanged();
        }

        internal Element CloneElement(Element source)
        {
            CheckThread();
            source.RequireValid();
            var surfaceHandle = SurfaceHandle;
            var token = NativeMethods.urui_element_clone(surfaceHandle, NativeHandle, source.Token);
            if (token == 0)
            {
                throw new InvalidOperationException("作者模板无法克隆。");
            }
            try
            {
                var cloned = WrapNewElement(token);
                if (cloned != null)
                {
                    RecordPendingSubtreeLayout(cloned);
                }
                return cloned;
            }
            catch
            {
                NativeMethods.urui_element_destroy(surfaceHandle, NativeHandle, token);
                SweepInvalidElements();
                throw;
            }
        }
    }
}
