using System.Collections.Generic;

namespace URUI.Authoring.Presentation
{
    /// <summary>
    /// Inspector sections describe existing attributes and CSS, not runtime components.
    /// </summary>
    public static class InspectorPresentation
    {
        public sealed class Section
        {
            public string Name
            {
                get;
            }
            public string[] Properties
            {
                get;
            }
            public Section(string name, params string[] properties)
            {
                Name = name;
                Properties = properties;
            }
        }

        public static bool HasText(string tag)
        {
            return tag == "span" || tag == "p" || tag == "label" || tag == "button" || tag == "input" || tag == "textarea" || tag == "select" || tag == "option";
        }

        public static IEnumerable<Section> Sections(string tag)
        {
            return SectionsForControl(tag, null, false);
        }

        public static IEnumerable<Section> SectionsForControl(string tag, string inputType, bool hasTextContent)
        {
            yield return new Section("布局 Layout", "style.position", "style.display", "style.left", "style.top", "style.right", "style.bottom", "style.width", "style.height", "style.overflow");
            yield return new Section("变换 Transform", "style.transform", "style.transform-origin");
            if (tag == "img")
            {
                yield return new Section("图像 Image", "attr.src", "attr.data-urui-sprite", "style.--urui-image-color", "style.object-fit", "style.--urui-image-fill-mode", "style.--urui-image-fill-amount", "style.--urui-image-fill-origin", "style.--urui-image-fill-clockwise", "style.--urui-image-fill-center", "style.--urui-image-slice-top", "style.--urui-image-slice-bottom", "style.--urui-image-slice-left", "style.--urui-image-slice-right");
            }
            bool choice = tag == "input" && (inputType == "checkbox" || inputType == "radio");
            bool range = tag == "input" && inputType == "range";
            if ((HasText(tag) && !choice && !range) || hasTextContent)
            {
                var text = new List<string>();
                if (tag != "input" && tag != "textarea")
                {
                    text.Add("text");
                }
                text.AddRange(new[] { "style.font-family", "style.font-size", "style.font-style", "style.font-weight", "style.color", "style.text-align", "style.white-space", "style.line-height" });
                yield return new Section("文字 Text", text.ToArray());
            }
            if (tag != "img" && tag != "input" && tag != "urui-particles")
            {
                yield return new Section("容器排列 Layout Group", "style.flex-direction", "style.flex-wrap", "style.justify-content", "style.align-items", "style.gap");
            }
            yield return new Section("尺寸约束 Size Constraints", "style.min-width", "style.min-height", "style.max-width", "style.max-height");
            yield return new Section("布局项 Layout Element", "style.flex-grow", "style.flex-shrink", "style.flex-basis", "style.align-self");
            yield return new Section("内外边距 Spacing", "style.padding-left", "style.padding-top", "style.padding-right", "style.padding-bottom", "style.margin-left", "style.margin-top", "style.margin-right", "style.margin-bottom");
            yield return new Section("外观 Appearance", "style.background-color", "style.opacity", "style.visibility", "style.border-width", "style.border-color", "style.border-radius");
            if (tag == "button" || tag == "input" || tag == "textarea" || tag == "select" || tag == "progress" || tag == "label")
            {
                yield return new Section("交互 Interaction", "attr.disabled", "attr.tabindex", "attr.title");
                if (tag == "progress")
                {
                    yield return new Section("控件 Control", "value", "attr.max");
                }
                else if (choice || tag == "select")
                {
                    yield return new Section("控件 Control", "value", "attr.name");
                }
                else if (range || (tag == "input" && inputType == "number"))
                {
                    yield return new Section("控件 Control", "value", "attr.name", "attr.min", "attr.max", "attr.step");
                }
                else if (tag == "input" || tag == "textarea")
                {
                    yield return new Section("控件 Control", "value", "attr.name", "attr.placeholder", "attr.readonly", "attr.maxlength");
                }
                if (tag == "label")
                {
                    yield return new Section("关联控件", "attr.for");
                }
            }
            if (tag == "urui-particles")
            {
                yield return new Section("粒子资源 Particles", "attr.src");
            }
        }

        public static bool IsAnimatable(string property)
        {
            if (!property.StartsWith("style.", System.StringComparison.Ordinal))
            {
                return false;
            }
            string name = property.Substring(6);
            return name == "opacity" || name == "transform" || name == "left" || name == "top" || name == "right" || name == "bottom" || name == "width" || name == "height" || name == "color" || name == "background-color" || name == "border-color" || name == "border-width" || name == "border-radius" || name == "font-size" || name.StartsWith("padding-", System.StringComparison.Ordinal) || name.StartsWith("margin-", System.StringComparison.Ordinal) || name.StartsWith("--urui-fx-", System.StringComparison.Ordinal) || name == "--urui-image-fill-amount" || name == "--urui-image-color";
        }

        public static string[] SectionNames(string tag)
        {
            var result = new List<string>();
            foreach (var section in Sections(tag))
            {
                result.Add(section.Name);
            }
            return result.ToArray();
        }
    }
}
