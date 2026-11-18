using System;
using UnityEngine;
using UnityEngine.UIElements;
using L = URUI.Editor.Localization.EditorText;

namespace URUI.Authoring.Windows
{
    /// <summary>Static samples for the library. Picking stays with the card so a click or drag adds the control.</summary>
    internal static class WorkbenchControlPreview
    {
        internal static VisualElement Create(string kind)
        {
            var sample = kind switch
            {
                "panel" => Part("urui-sample-panel", Part("urui-sample-panel-top"), Part("urui-sample-panel-body")),
                "text" => Caption(L.Tr("文字 Text"), "urui-sample-text"),
                "image" => ImageSample(),
                "button" => Caption(L.Tr("按钮 Button"), "urui-sample-button"),
                "input" => Caption(L.Tr("输入文字…"), "urui-sample-field"),
                "textarea" => Part("urui-sample-area", Part("urui-sample-line"), Part("urui-sample-short-line")),
                "toggle" => Part("urui-sample-toggle", Caption("✓", "urui-sample-check")),
                "radio" => Part("urui-sample-radio", Part("urui-sample-radio-dot")),
                "slider" => Part("urui-sample-slider", Part("urui-sample-fill"), Part("urui-sample-thumb")),
                "select" => Part("urui-sample-select",
                    Caption(L.Tr("选项"), "urui-sample-option"), Caption("▾", "urui-sample-arrow")),
                "progress" => Part("urui-sample-progress", Part("urui-sample-fill")),
                "particles" => ParticleSample(),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown control sample.")
            };
            var preview = new VisualElement { name = "urui-control-preview", pickingMode = PickingMode.Ignore };
            preview.AddToClassList("urui-control-preview");
            preview.Add(sample);
            return preview;
        }

        static VisualElement Part(string className, params VisualElement[] children)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList(className);
            foreach (var child in children)
                element.Add(child);
            return element;
        }

        static Label Caption(string value, string className)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }

        static VisualElement ImageSample()
        {
            var picture = Part("urui-sample-image", Part("urui-sample-sun"));
            picture.generateVisualContent += context =>
            {
                Rect rect = context.visualElement.contentRect;
                if (rect.width <= 0 || rect.height <= 0)
                    return;
                Vector2 Point(float x, float y) => new Vector2(rect.x + x * rect.width, rect.y + y * rect.height);
                var painter = context.painter2D;
                painter.fillColor = new Color(.40f, .68f, .77f);
                painter.BeginPath();
                painter.MoveTo(Point(0, .92f));
                painter.LineTo(Point(.30f, .38f));
                painter.LineTo(Point(.52f, .69f));
                painter.LineTo(Point(.72f, .50f));
                painter.LineTo(Point(1, .86f));
                painter.LineTo(Point(1, 1));
                painter.LineTo(Point(0, 1));
                painter.ClosePath();
                painter.Fill();
            };
            return picture;
        }

        static VisualElement ParticleSample()
        {
            var burst = Part("urui-sample-particles");
            foreach (var (x, y, size, opacity) in new (int, int, int, float)[]
            {
                (7, 18, 4, .55f), (17, 7, 5, .72f), (22, 20, 6, .86f),
                (33, 11, 10, 1f), (47, 4, 5, .9f), (50, 22, 4, .64f), (60, 13, 3, .62f)
            })
            {
                var dot = Part("urui-sample-particle");
                dot.style.left = x;
                dot.style.top = y;
                dot.style.width = size;
                dot.style.height = size;
                dot.style.opacity = opacity;
                burst.Add(dot);
            }
            return burst;
        }
    }
}
