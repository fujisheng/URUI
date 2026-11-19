using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Resources;
using URUI.Internal.Resources;
using URUI.Internal.Interop;

namespace URUI.Documents
{
    // 资源配置用于下一次加载；校验输入但不转移调用者的资产所有权。
    public sealed partial class Document
    {
        Func<string, ImageLease> imageResolver;
        /// <summary>
        /// 文档使用的共享 Surface；加载前必须明确配置。
        /// </summary>
        public URUI.Hosting.Surface Surface
        {
            get
            {
                CheckThread();
                return surface;
            }
            set
            {
                CheckConfiguration();
                if (surface && surface != value)
                {
                    surface.Forget(this);
                }
                surface = value;
            }
        }
        /// <summary>
        /// 文档真实 URL，用于诊断和相对资源解析；配置值在重新启用时复用。
        /// </summary>
        public string SourceUrl
        {
            get
            {
                CheckThread();
                return content?.SourceUrl ?? sourceUrl;
            }
            set
            {
                CheckConfiguration();
                RequireName(value, nameof(value));
                sourceUrl = value;
            }
        }
        /// <summary>
        /// 同步预加载图片到文档纹理缓存；成功返回源像素尺寸，不修改节点或布局。
        /// </summary>
        /// <remarks>
        /// 文档必须已加载，限主线程且不能在图片回调中调用。失败也按文档缓存；资源和 GPU 退役沿用正常图片路径。
        /// </remarks>
        public bool TryLoadImage(string source, out Vector2Int dimensions)
        {
            CheckThread();
            RequireEffectDomAccess();
            RequireName(source, nameof(source));
            if (!IsLoaded)
            {
                throw new InvalidOperationException("请先加载文档。");
            }
            var result = NativeMethods.urui_document_preload_image(NativeHandle, source, out var width, out var height);
            dimensions = new Vector2Int(width, height);
            FinishNativeCall();
            if (result < 0)
            {
                throw new InvalidOperationException("URUI 图片预加载被拒绝：" + NativeMethods.LastError());
            }
            return result == 1;
        }
        /// <summary>
        /// 下次加载使用的外置资源；数组使用副本；注册同步借用 TextAsset 原始数据，可包含字体字节。
        /// </summary>
        public TextResource[] TextResources
        {
            get
            {
                CheckThread();
                return (TextResource[])textResources.Clone();
            }
            set
            {
                CheckResourceConfiguration();
                textResources = value == null ? Array.Empty<TextResource>() : (TextResource[])value.Clone();
            }
        }

        void ValidateTextResources(TextResource[] requestedResources = null)
        {
            textResources ??= Array.Empty<TextResource>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var resource in requestedResources ?? textResources)
            {
                RequireName(resource.Source, nameof(TextResources));
                if (!resource.Asset || !names.Add(resource.Source))
                {
                    throw new ArgumentException("文本资源必须有效，Source 不可重复。", nameof(TextResources));
                }
            }
        }
        /// <summary>
        /// 静态 Images 未匹配时，在主线程同步解析规范化的 HTML 路径。
        /// </summary>
        /// <remarks>
        /// 配置在下次成功加载后生效。返回 null 表示路径不存在；每次成功返回独立 ImageLease，之后不能自行 Dispose。回调可排队加载/卸载请求，禁止同步重入元素 API。关闭文档后，已提交图片租约等待 GPU 退役。
        /// </remarks>
        public Func<string, ImageLease> ImageResolver
        {
            get
            {
                CheckThread();
                return imageResolver;
            }
            set
            {
                CheckResourceConfiguration();
                imageResolver = value;
            }
        }

        /// <summary>
        /// 在启用组件时加载的 HTML TextAsset。资产由调用者持有。
        /// </summary>
        /// <remarks>
        /// 只能在文档未加载时修改；URUI 不销毁此资产。
        /// </remarks>
        public TextAsset Source
        {
            get
            {
                CheckThread();
                return source;
            }

            set
            {
                CheckConfiguration();
                source = value;
                compiledSource = null;
                requestedMarkup = null;
                loadRequested = value != null;
            }
        }

        /// <summary>
        /// 启用组件时加载的编译页面；源码变化由 Editor 自动更新资产。
        /// </summary>
        public CompiledDocumentAsset CompiledSource
        {
            get
            {
                CheckThread();
                return compiledSource;
            }
            set
            {
                CheckConfiguration();
                if (value)
                {
                    value.Validate(true);
                }
                compiledSource = value;
                source = null;
                requestedMarkup = null;
                loadRequested = value != null;
                if (value)
                {
                    sourceUrl = value.SourceUrl;
                }
            }
        }

        /// <summary>
        /// 下次加载使用的借用图片映射；Texture2D 无需开启 Read/Write。
        /// </summary>
        /// <remarks>
        /// 数组读写使用副本，数组中的纹理仍由调用者持有。修改在下次成功加载后生效。
        /// </remarks>
        public ImageAsset[] Images
        {
            get
            {
                CheckThread();
                return CopyImages(images);
            }

            set
            {
                CheckResourceConfiguration();
                images = CopyImages(value);
            }
        }

        static ImageAsset[] CopyImages(ImageAsset[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<ImageAsset>();
            }
            var copy = new ImageAsset[source.Length];
            for (var index = 0; index < source.Length; ++index)
            {
                copy[index] = source[index].Snapshot();
            }
            return copy;
        }

        void ValidateImages()
        {
            images = images ?? Array.Empty<ImageAsset>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var spriteNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var image in images)
            {
                RequireName(image.Source, nameof(Images));
                if (!image.Texture)
                {
                    throw new ArgumentException("图片必须是有效的 Texture2D；使用期间不要销毁或卸载。", nameof(Images));
                }

                BorrowedTextureRegistry.ValidateTexture(image.Texture);
                foreach (var sprite in image.Sprites)
                {
                    RequireName(sprite.Name, nameof(Images));
                    var rect = sprite.Region;
                    if (!spriteNames.Add(sprite.Name) || !float.IsFinite(rect.x) || !float.IsFinite(rect.y) ||
                        !float.IsFinite(rect.width) || !float.IsFinite(rect.height) || rect.x < 0 || rect.y < 0 ||
                        rect.width <= 0 || rect.height <= 0 || (double)rect.x + rect.width > image.Texture.width ||
                        (double)rect.y + rect.height > image.Texture.height || !float.IsFinite(sprite.DisplayScale) || sprite.DisplayScale <= 0 ||
                        (double)rect.width * sprite.DisplayScale > float.MaxValue || (double)rect.height * sprite.DisplayScale > float.MaxValue)
                    {
                        throw new ArgumentException("图集区域名称重复、源像素越界或显示比例无效：" + sprite.Name, nameof(Images));
                    }
                }
                if (!names.Add(image.Source))
                {
                    throw new ArgumentException("图片 Source 不能重复。", nameof(Images));
                }
            }
        }

        void CheckResourceConfiguration()
        {
            CheckThread();
            if (changingContent)
            {
                throw new InvalidOperationException("不能在文档事务中修改候选资源配置。");
            }
        }

        void CheckConfiguration()
        {
            CheckThread();
            if (IsLoaded || changingContent)
            {
                throw new InvalidOperationException("请先卸载文档，再修改加载资源配置。");
            }
        }
    }
}
