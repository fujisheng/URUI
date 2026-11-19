using System;
using System.Globalization;
using UnityEngine;
using URUI.Documents;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>
    /// 按实际文本布局在作者字号范围内缩字，不创建节点；测量完成前由作者 class 控制可见性。
    /// </summary>
    /// <remarks>
    /// 外部样式声明 --urui-text-fit-min/max 像素值。适用于字体继承自内容节点的文本；显式固定字号子节点仍保留其作者约束。
    /// </remarks>
    public sealed class TextFit : IDisposable
    {
        readonly Document document;
        readonly string previousFont;
        readonly bool previousPending;
        readonly IDisposable detached;
        readonly IDisposable attached;
        string lastFamily;
        string lastWeight;
        ulong lastSignature;
        Vector2 lastSize;
        float minimum;
        float maximum;
        float low;
        float high;
        float current;
        int attempts;
        bool initialized;
        bool searching;
        bool measuredMinimum;
        bool disposed;
        /// <summary>
        /// 保留作者尺寸与对齐的外层节点。
        /// </summary>
        public Element Host
        {
            get;
        }
        /// <summary>
        /// 保留普通布局流和自然换行高度的直接子文本节点。
        /// </summary>
        public Element Content
        {
            get;
        }
        /// <summary>
        /// 最近测量是否在作者最小字号约束内完整容纳文本。
        /// </summary>
        public bool Fits
        {
            get;
            private set;
        }
        /// <summary>
        /// 开始观察实际布局；控制器拥有内容字号覆盖，释放时恢复作者原值。
        /// </summary>
        public TextFit(Element host, Element content)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            if (!ReferenceEquals(host.Document, content.Document) || !ReferenceEquals(content.Parent, host))
            {
                throw new ArgumentException("缩字内容必须是同一文档中宿主的直接子节点。");
            }
            document = host.Document;
            previousFont = content.Style.Get("font-size");
            previousPending = host.ClassListContains("text-fit-pending");
            // 脱离层级不销毁节点身份；池化重新挂载后仍由同一个适配器持有控制器。
            detached = host.RegisterCallback<DetachFromDocumentEvent>(_ => initialized = false);
            attached = host.RegisterCallback<AttachToDocumentEvent>(_ =>
            {
                if (!disposed)
                {
                    Refresh();
                }
            });
            document.LayoutUpdated += Update;
            document.Unloaded += Unloaded;
            Refresh();
        }
        /// <summary>
        /// 绑定或池化更新时立即进入待测量状态，防止先显示旧字号；布局尺寸和文本变化也会自动重测。
        /// </summary>
        public void Refresh()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(TextFit));
            }
            initialized = false;
            Fits = false;
            Host.AddToClassList("text-fit-pending");
        }
        static float Number(Element host, string key)
        {
            var raw = host.ResolvedStyle.Get(key).AsSpan().Trim();
            if (raw.EndsWith("px".AsSpan(), StringComparison.Ordinal))
            {
                raw = raw.Slice(0, raw.Length - 2);
            }
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value) || value <= 0 || value > 4096)
            {
                throw new InvalidOperationException("缩字需要有限、正的像素字号：" + key);
            }
            return value;
        }
        void Write(float value)
        {
            current = value;
            Content.Style.SetPixels("font-size", value);
        }
        static void Mix(ref ulong hash, string value)
        {
            if (value == null)
            {
                return;
            }
            unchecked
            {
                foreach (var character in value)
                {
                    hash = (hash ^ character) * 1099511628211UL;
                }
                hash = (hash ^ 255) * 1099511628211UL;
            }
        }
        static void Visit(Element element, ref ulong hash)
        {
            var tag = element.TagName;
            Mix(ref hash, tag);
            if (tag == "#text")
            {
                Mix(ref hash, element.Text);
                return;
            }
            // 普通文本getter不能读取结构化节点；只读叶文本和作者结构，不序列化或生成HTML。
            Mix(ref hash, element.GetAttribute("class"));
            var hierarchy = element.Hierarchy;
            var count = hierarchy.ChildCount;
            for (var index = 0; index < count; index++)
            {
                var child = hierarchy.ChildAt(index);
                if (child != null && child.IsValid)
                {
                    Visit(child, ref hash);
                }
            }
            unchecked
            {
                hash = (hash ^ 254) * 1099511628211UL;
            }
        }
        static ulong Signature(Element element)
        {
            var hash = 14695981039346656037UL;
            Visit(element, ref hash);
            return hash;
        }
        void Update(Document owner)
        {
            try
            {
                Measure();
            }
            catch (Exception failure)
            {
                try
                {
                    Dispose();
                }
                catch (Exception cleanup)
                {
                    throw new AggregateException(failure, cleanup);
                }
                throw;
            }
        }
        void Measure()
        {
            if (disposed)
            {
                return;
            }
            if (!Host.IsValid || !Content.IsValid)
            {
                Dispose();
                return;
            }
            if (!Host.IsAttached || !Content.IsAttached)
            {
                initialized = false;
                return;
            }
            if (!Host.HasLayout || !Content.HasLayout)
            {
                return;
            }
            var size = Host.ContentRect.size;
            if (size.x <= 0 || size.y <= 0)
            {
                return;
            }
            var min = Number(Host, "--urui-text-fit-min");
            var max = Number(Host, "--urui-text-fit-max");
            if (min > max)
            {
                throw new InvalidOperationException("缩字最小字号不能大于最大字号。");
            }
            var signature = Signature(Content);
            var family = Content.ResolvedStyle.Get("font-family");
            var weight = Content.ResolvedStyle.Get("font-weight");
            if (!initialized || lastSize != size || minimum != min || maximum != max || lastSignature != signature || lastFamily != family || lastWeight != weight)
            {
                initialized = true;
                lastSize = size;
                minimum = min;
                maximum = max;
                lastSignature = signature;
                lastFamily = family;
                lastWeight = weight;
                low = min;
                high = max;
                attempts = 0;
                measuredMinimum = false;
                searching = true;
                Fits = false;
                Host.AddToClassList("text-fit-pending");
                Write(max);
                return;
            }
            if (!searching)
            {
                return;
            }
            var fits = Content.Layout.height <= size.y + .2f && Content.ScrollSize.x <= size.x + .2f;
            attempts++;
            if (current == maximum && fits)
            {
                Finish(maximum, true);
                return;
            }
            if (!measuredMinimum)
            {
                if (current != minimum)
                {
                    Write(minimum);
                    return;
                }
                measuredMinimum = true;
                if (!fits)
                {
                    Finish(minimum, false);
                    return;
                }
            }
            if (fits)
            {
                low = current;
            }
            else
            {
                high = current;
            }
            if (high - low <= .2f || attempts >= 16)
            {
                Finish(low, true);
                return;
            }
            Write((low + high) * .5f);
        }
        void Finish(float value, bool fits)
        {
            Write(value);
            Fits = fits;
            searching = false;
            Host.RemoveFromClassList("text-fit-pending");
        }
        void Unloaded(Document owner) => Dispose();
        /// <summary>
        /// 停止观察并恢复字号覆盖与作者 class，节点仍属于原文档。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            document.LayoutUpdated -= Update;
            document.Unloaded -= Unloaded;
            detached.Dispose();
            attached.Dispose();
            if (Content.IsValid)
            {
                if (previousFont == null)
                {
                    Content.Style.Remove("font-size");
                }
                else
                {
                    Content.Style.Set("font-size", previousFont);
                }
            }
            if (Host.IsValid)
            {
                Host.EnableInClassList("text-fit-pending", previousPending);
            }
        }
    }
}
