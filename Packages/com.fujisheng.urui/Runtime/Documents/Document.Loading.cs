using System;
using UnityEngine;
using URUI.Internal.Interop;

namespace URUI.Documents
{
    public sealed partial class Document
    {
        static readonly Unity.Profiling.ProfilerMarker loadContentMarker = new("URUI.Document.Load");
        static readonly Unity.Profiling.ProfilerMarker loadNativeMarker = new("URUI.Document.LoadNative");
        /// <summary>
        /// 加载 Editor 编译资产，跳过 HTML/CSS 文本解析；失败保持原页面。
        /// </summary>
        public void Load(URUI.Resources.CompiledDocumentAsset asset)
        {
            CheckThread(true);
            if (!asset)
            {
                throw new ArgumentNullException(nameof(asset));
            }
            LoadContent(null, null, asset.SourceUrl, asset);
        }

        /// <summary>
        /// 加载调用者持有的完整 HTML 资产；失败保留原页面。
        /// </summary>
        public void Load(TextAsset asset, string sourceUrl)
        {
            CheckThread(true);
            if (!asset)
            {
                throw new ArgumentNullException(nameof(asset));
            }
            LoadContent(asset.text, asset, sourceUrl);
        }

        /// <summary>
        /// 严格解析并加载完整 HTML；成功后才使原页面的元素与订阅失效。
        /// </summary>
        public void LoadHtml(string html, string sourceUrl) => LoadContent(html, null, sourceUrl);

        void LoadContent(string html, TextAsset requestedSource, string requestedUrl, URUI.Resources.CompiledDocumentAsset compiled = null)
        {
            using var sample = loadContentMarker.Auto();
            CheckThread(true);
            if (DeferLifecycle(() => LoadContent(html, requestedSource, requestedUrl, compiled)))
            {
                return;
            }
            var isCompiled = !ReferenceEquals(compiled, null);
            if (isCompiled)
            {
                if (!compiled)
                {
                    throw new InvalidOperationException("编译资产在加载前已被释放。");
                }
                compiled.Validate(true);
            }
            else if (string.IsNullOrWhiteSpace(html) || html.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("HTML 内容不能为空或包含 NUL。", nameof(html));
            }
            RequireName(requestedUrl, nameof(requestedUrl));
            if (!isActiveAndEnabled || !surface || !surface.isActiveAndEnabled)
            {
                throw new InvalidOperationException("请先启用 Document 并配置已启用的 URUI Surface。");
            }
            var files = (isCompiled ? CompiledResources(compiled) : textResources) ?? Array.Empty<URUI.Resources.TextResource>();
            ValidateImages();
            ValidateTextResources(files);
            host = surface.EnsureStarted(this);
            var previous = activeContent;
            ContentState staged = null;
            changingContent = true;
            surface.BeginTransaction();
            try
            {
                if (NativeMethods.urui_document_create(out var handle) != 1)
                {
                    throw new InvalidOperationException("URUI 文档身份创建失败：" + NativeMethods.LastError());
                }
                staged = new ContentState
                {
                    Handle = handle,
                    Generation = ++nextGeneration,
                    SourceUrl = requestedUrl,
                    SourceMarkup = html,
                    CompiledAsset = compiled,
                    CompiledFingerprint = compiled ? compiled.Fingerprint : null,
                    TextResources = (URUI.Resources.TextResource[])files.Clone(),
                    Images = CopyImages(images),
                    ImageResolver = imageResolver
                };
                if (previous != null && NativeMethods.urui_document_set_replacement(handle, previous.Handle) != 1)
                {
                    throw new InvalidOperationException("URUI 候选文档资源身份配置失败：" + NativeMethods.LastError());
                }
                candidateContent = content = staged;
                surface.Bind(handle, this);
                effects?.ConfigureDocument(handle);
                host.RegisterDocumentResources(handle, requestedUrl, staged.TextResources, staged.Images);
                StartEvents();
                using (loadNativeMarker.Auto())
                {
                    var loaded = isCompiled ? compiled.Load(handle) : NativeMethods.urui_document_load_html(handle, html, requestedUrl);
                    if (loaded != 1)
                    {
                        throw new InvalidOperationException("URUI HTML 加载失败：" + NativeMethods.LastError());
                    }
                }
                if (!this || !isActiveAndEnabled || !surface || !surface.isActiveAndEnabled)
                {
                    throw new InvalidOperationException("URUI 候选页面加载期间宿主已关闭。");
                }
                InitializeElements();
                content = previous;
                if (previous != null)
                {
                    RetireContent(previous, true);
                }
                content = activeContent = staged;
                candidateContent = null;
                loadRequested = true;
                source = requestedSource;
                compiledSource = compiled;
                sourceUrl = requestedUrl;
                requestedMarkup = html;
                if (NativeMethods.urui_document_set_presentation(handle, Layer, Order, Presented ? 1 : 0, modal ? 1 : 0) != 1)
                {
                    throw new InvalidOperationException("URUI 页面发布失败：" + NativeMethods.LastError());
                }
#if UNITY_EDITOR
                CaptureAuthoringProvenance();
#endif
                NotifyLifecycle(Loaded);
            }
            catch
            {
                if (staged != null && !ReferenceEquals(activeContent, staged))
                {
                    content = staged;
                    RetireContent(staged, false);
                    content = activeContent = previous;
                    candidateContent = null;
                    styles?.DiscardInvalid();
                    animations?.DiscardInvalid();
                }
                throw;
            }
            finally
            {
                changingContent = false;
                surface.EndTransaction();
                FinishNativeCall();
            }
        }

        URUI.Resources.TextResource[] CompiledResources(URUI.Resources.CompiledDocumentAsset asset)
        {
            var compiledFiles = asset.StaticResources;
            if (textResources == null || textResources.Length == 0)
            {
                return compiledFiles;
            }
            if (compiledFiles.Length == 0)
            {
                return textResources;
            }
            var files = new System.Collections.Generic.List<URUI.Resources.TextResource>(compiledFiles);
            var known = new System.Collections.Generic.Dictionary<string, TextAsset>(StringComparer.Ordinal);
            foreach (var file in compiledFiles)
            {
                known.Add(file.Source, file.Asset);
            }
            foreach (var file in textResources)
            {
                if (known.TryGetValue(file.Source, out var existing))
                {
                    if (existing != file.Asset)
                    {
                        throw new InvalidOperationException("编译资产的文件依赖发生冲突：" + file.Source);
                    }
                }
                else
                {
                    files.Add(file);
                    known.Add(file.Source, file.Asset);
                }
            }
            return files.ToArray();
        }

        /// <summary>
        /// 关闭当前文档；同 Surface 的其他页面和共享上下文继续工作。
        /// </summary>
        /// <remarks>
        /// 显式卸载取消自动恢复，直到再次加载或配置 Source。回调内按 Surface 队列顺序执行。
        /// </remarks>
        public void Unload()
        {
            CheckThread(true);
            if (DeferLifecycle(Unload))
            {
                return;
            }
            loadRequested = false;
            requestedMarkup = null;
            ReleaseContent();
        }
    }
}
