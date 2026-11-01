using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Resources;

namespace URUI.Documents
{
    // 配置只在文档未加载时修改；校验输入但不转移调用者的资产所有权。
    public sealed partial class Document
    {
        /// <summary>
        /// 在启用组件时加载的 RML TextAsset。资产由调用者持有。
        /// </summary>
        /// <remarks>只能在文档未加载时修改；URUI 不销毁此资产。</remarks>
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
                requestedMarkup = null;
                loadRequested = value != null;
            }
        }

        /// <summary>
        /// 下次加载使用的图片映射；Texture2D 必须开启 Read/Write。
        /// </summary>
        /// <remarks>数组读写使用副本，数组中的纹理仍由调用者持有。修改前先卸载文档。</remarks>
        public ImageAsset[] Images
        {
            get
            {
                CheckThread();
                return (ImageAsset[])images.Clone();
            }

            set
            {
                CheckConfiguration();
                images = value == null ? Array.Empty<ImageAsset>() : (ImageAsset[])value.Clone();
            }
        }

        /// <summary>
        /// 下次加载使用的字体文件绝对路径；为空时使用平台默认字体。
        /// </summary>
        /// <remarks>修改前先卸载文档。调用者负责文件分发、读取权限与所需字形。</remarks>
        public string FontPath
        {
            get
            {
                CheckThread();
                return fontPath;
            }

            set
            {
                CheckConfiguration();
                if (value != null && value.IndexOf('\0') >= 0)
                {
                    throw new ArgumentException("字体路径不能包含 NUL。", nameof(value));
                }

                fontPath = value ?? "";
            }
        }

        void ValidateImages()
        {
            images = images ?? Array.Empty<ImageAsset>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var image in images)
            {
                RequireName(image.Source, nameof(Images));
                if (!image.Texture || !image.Texture.isReadable)
                {
                    throw new ArgumentException("图片必须是开启 Read/Write 的 Texture2D。", nameof(Images));
                }

                if (!names.Add(image.Source))
                {
                    throw new ArgumentException("图片 Source 不能重复。", nameof(Images));
                }
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
