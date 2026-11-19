using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using URUI.Elements;

namespace URUI.Effect
{
    /// <summary>
    /// 把作者单行纯文本的字素沿圆弧排列；字素使用作者span原型，参数来自CSS。
    /// </summary>
    /// <remarks>
    /// 不处理跨字素连写文字或多行富文本；原始文本节点保留给本地化和阅读顺序。
    /// </remarks>
    public sealed class ArcTextLayout : IDisposable
    {
        readonly Element host;
        readonly Element source;
        readonly Element prototype;
        readonly List<Element> glyphs = new();
        string text;
        bool disposed;
        bool arranging;
        public ArcTextLayout(Element host, Element source, Element prototype)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.prototype = prototype ?? throw new ArgumentNullException(nameof(prototype));
            if (!ReferenceEquals(host.Document, source.Document) || !ReferenceEquals(host.Document, prototype.Document))
            {
                throw new ArgumentException("圆弧文本节点必须属于同一文档。");
            }
            host.Document.LayoutUpdated += Layout;
        }
        float Number(string name)
        {
            if (!float.TryParse(host.ResolvedStyle.Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
            {
                throw new InvalidOperationException("圆弧文本缺少有效CSS参数：" + name);
            }
            return value;
        }
        void Layout(global::URUI.Documents.Document owner)
        {
            if (disposed || arranging || !host.HasLayout)
            {
                return;
            }
            arranging = true;
            try
            {
                var current = source.Text ?? string.Empty;
                if (current != text)
                {
                    if (current.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
                    {
                        throw new NotSupportedException("圆弧文本需要单行纯文本。");
                    }
                    foreach (var glyph in glyphs)
                    {
                        glyph.Destroy();
                    }
                    glyphs.Clear();
                    text = current;
                    var offsets = StringInfo.ParseCombiningCharacters(text);
                    for (var i = 0; i < offsets.Length; i++)
                    {
                        var glyph = prototype.Clone();
                        glyph.RemoveAttribute("hidden");
                        glyph.RemoveFromClassList("arc-glyph-template");
                        glyph.AddToClassList("arc-glyph");
                        glyph.Text = text.Substring(offsets[i], (i + 1 < offsets.Length ? offsets[i + 1] : text.Length) - offsets[i]);
                        host.Add(glyph);
                        glyphs.Add(glyph);
                    }
                    host.AddToClassList("arc-text-ready");
                    return;
                }
                var radius = Number("--urui-arc-radius");
                if (radius <= 0)
                {
                    throw new InvalidOperationException("圆弧半径必须为正。");
                }
                var direction = Number("--urui-arc-direction");
                var rotate = Number("--urui-arc-rotate") != 0;
                var width = 0f;
                foreach (var glyph in glyphs)
                {
                    if (!glyph.HasLayout)
                    {
                        return;
                    }
                    width += glyph.Layout.width;
                }
                var x = -width * .5f;
                foreach (var glyph in glyphs)
                {
                    var box = glyph.Layout;
                    var center = x + box.width * .5f;
                    var dx = Mathf.Clamp(center, -radius + .0001f, radius - .0001f);
                    var root = Mathf.Sqrt(Mathf.Max(.000001f, radius * radius - dx * dx));
                    var y = direction * (radius - root);
                    var angle = rotate ? Mathf.Atan(direction * dx / root) * Mathf.Rad2Deg : 0;
                    glyph.SetTransform(new Vector3(center - box.width * .5f, y - box.height * .5f, 0), Quaternion.Euler(0, 0, angle), Vector3.one);
                    x += box.width;
                }
            }
            finally
            {
                arranging = false;
            }
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            host.Document.LayoutUpdated -= Layout;
            foreach (var glyph in glyphs)
            {
                if (glyph.IsValid)
                {
                    glyph.Destroy();
                }
            }
            glyphs.Clear();
            if (host.IsValid)
            {
                host.RemoveFromClassList("arc-text-ready");
            }
        }
    }
}
