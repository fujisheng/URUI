using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Rendering.Effects;
using URUI.Resources;
using URUI.Styles;

namespace URUI.Effect
{
    /// <summary>
    /// 实例的附加图像输入；先取得新租约再替换，旧输入通过所属 Context 的 GPU 退役释放。
    /// </summary>
    public sealed class EffectResources
    {
        sealed class Binding
        {
            internal ComputedImageValue value;
            internal ImageLease lease;
        }
        readonly EffectContext context;
        readonly IEffectResourceResolver resolver;
        readonly Dictionary<string, Binding> images = new(StringComparer.Ordinal);

        public EffectResources(EffectContext context, IEffectResourceResolver resolver)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public Texture GetImage(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("图像输入名称不能为空。", nameof(name));
            }
            return images.TryGetValue(name, out var binding) ? binding.lease?.Texture : null;
        }

        /// <summary>
        /// 缺失的新输入返回 false 并保留旧输入；解析器异常向调用者报告，同样不破坏旧输入。
        /// </summary>
        public bool PrepareImage(string name, ComputedImageValue value)
        {
            context.RequirePreparation();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("图像输入名称不能为空。", nameof(name));
            }
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            images.TryGetValue(name, out var previous);
            if (previous != null && SameValue(previous.value, value) &&
                (value.Kind == ComputedImageKind.None || previous.lease != null && previous.lease.Texture))
            {
                return true;
            }
            ImageLease next = null;
            if (value.Kind != ComputedImageKind.None)
            {
                next = resolver.AcquireImage(value);
                if (next == null)
                {
                    return false;
                }
                if (!next.Texture)
                {
                    next.Dispose();
                    return false;
                }
                try
                {
                    context.Own(next);
                }
                catch
                {
                    next.Dispose();
                    throw;
                }
            }
            if (previous?.lease != null)
            {
                context.Retire(previous.lease);
            }
            images[name] = new Binding
            {
                value = value,
                lease = next
            };
            return true;
        }

        static bool SameValue(ComputedImageValue a, ComputedImageValue b)
        {
            return a.Kind == b.Kind && a.Url == b.Url && a.CssText == b.CssText && a.SourceUrl == b.SourceUrl;
        }
    }
}
