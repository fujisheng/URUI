using System;
using System.IO;
using UnityEngine;
using URUI.Internal.Hosting;
using URUI.Internal.Resources;

namespace URUI.Documents
{
    // 同步加载与显式卸载的入口；生命周期通知期间保持重入保护，失败时回收自有资源。
    public sealed partial class Document
    {
        /// <summary>
        /// 同步加载调用者持有的 TextAsset，完成后触发 Loaded。
        /// </summary>
        /// <param name="asset">非空 RML 文本资产，所有权仍属于调用者。</param>
        /// <remarks>必须在启用的组件上调用；重载会使旧元素与订阅失效。</remarks>
        public void Load(TextAsset asset)
        {
            CheckThread();
            if (!asset)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            LoadContent(asset.text, asset);
        }

        /// <summary>
        /// 同步加载 RML 字符串。重载会先卸载旧内容，旧元素不能继续使用。
        /// </summary>
        /// <param name="markup">非空且不包含 NUL 的完整 RML 文档。</param>
        /// <remarks>必须在启用的组件上调用；URUI 管理字符串加载所需的临时资产。</remarks>
        public void LoadMarkup(string markup)
        {
            LoadContent(markup, null);
        }

        void LoadContent(string markup, TextAsset requestedSource)
        {
            CheckThread();
            if (Defer(() => LoadContent(markup, requestedSource))) return;
            if (string.IsNullOrWhiteSpace(markup))
            {
                throw new ArgumentException("RML 内容不能为空。", nameof(markup));
            }

            if (markup.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("RML 不能包含 NUL。", nameof(markup));
            }

            if (!isActiveAndEnabled)
            {
                throw new InvalidOperationException("请先启用 URUI Document。");
            }

            if (changingContent)
            {
                throw new InvalidOperationException("不能在文档生命周期回调中重入加载或卸载。");
            }

            if (RenderHost.ActiveHost && RenderHost.ActiveHost != host)
            {
                throw new InvalidOperationException("URUI 当前仅支持一个活动文档，请先卸载已有文档。");
            }

            ValidateImages();
            if (!string.IsNullOrEmpty(fontPath) && (fontPath.IndexOf('\0') >= 0 || !File.Exists(fontPath)))
            {
                throw new ArgumentException("字体路径必须指向可读取的字体文件。", nameof(FontPath));
            }
            // 生命周期通知也属于本次操作，保持门闩直到通知和失败清理全部结束。
            changingContent = true;
            try
            {
                ReleaseContent();
                // Unloaded 允许业务禁用或销毁组件，也可能让另一个文档占用 context。
                // 释放旧文档后重新检查，避免为失效请求继续创建资源。
                if (!this || !isActiveAndEnabled)
                {
                    throw new InvalidOperationException("URUI 加载已取消：组件在 Unloaded 中被禁用或销毁。");
                }

                if (RenderHost.ActiveHost)
                {
                    throw new InvalidOperationException("URUI 加载已取消：另一个文档已占用 context。");
                }

                loadRequested = true;
                source = requestedSource;
                requestedMarkup = markup;
                // 宿主只接收本组件拥有的副本，释放时不会销毁调用者的 Source。
                ownedMarkup = new TextAsset(markup)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                var renderer = new GameObject("URUI Renderer");
                renderer.transform.SetParent(transform, false);
                renderer.hideFlags = HideFlags.HideAndDontSave;
                host = renderer.AddComponent<RenderHost>();
                host.OwnerDocument = this;
                host.document = ownedMarkup;
                host.overrideFontPath = fontPath;
                host.imageAssets = new ImageBinding[images.Length];
                for (var i = 0; i < images.Length; i++)
                {
                    host.imageAssets[i] = new ImageBinding
                    {
                        source = images[i].Source,
                        texture = images[i].Texture
                    };
                }

                host.InitializeForDocument();
                if (!host.Ready)
                {
                    throw new InvalidOperationException("URUI 文档加载失败，详情见 Unity Console。");
                }

                StartEvents();
                InitializeElements();
                Loaded?.Invoke(this);
            }
            catch
            {
                // 原生初始化或 Loaded 回调失败时，回收已经取得的资源，避免继续占用 context。
                ReleaseContent();
                throw;
            }
            finally
            {
                changingContent = false;
            }
        }

        /// <summary>
        /// 卸载内容并同步释放原生 context 和 URUI 自有资源；调用者图片和 TextAsset 不受影响。
        /// </summary>
        /// <remarks>重复调用安全。显式卸载取消下一次启用时的自动恢复，直到再次加载或配置 Source。</remarks>
        public void Unload()
        {
            CheckThread();
            if (Defer(Unload)) return;
            if (changingContent)
            {
                throw new InvalidOperationException("不能在文档生命周期回调中重入加载或卸载。");
            }

            changingContent = true;
            try
            {
                loadRequested = false;
                requestedMarkup = null;
                ReleaseContent();
            }
            finally
            {
                changingContent = false;
            }
        }
    }
}
