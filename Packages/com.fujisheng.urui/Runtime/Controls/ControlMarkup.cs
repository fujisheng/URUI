using System;
using System.Collections.Generic;
using URUI.Elements;

namespace URUI.Controls
{
    internal static class ControlMarkup
    {
        internal static bool IsFormattingNode(Element element)
        {
            if (element.TagName == "#comment") return true;
            if (element.TagName != "#text") return false;
            foreach (char character in element.Text)
                if (character != ' ' && character != '\t' && character != '\n' && character != '\r' && character != '\f') return false;
            return true;
        }
        internal static Element Find(IReadOnlyList<Element> children, string className)
        {
            Element found = null;
            foreach (var child in children)
            {
                if (!child.ClassListContains(className)) continue;
                if (found != null) throw new InvalidOperationException("作者控件不能重复声明 " + className + " 节点。");
                found = child;
            }
            return found;
        }
        internal static void RequireOnly(IReadOnlyList<Element> children, params Element[] slots)
        {
            var allowed = new HashSet<Element>(slots);
            if (allowed.Count != slots.Length || allowed.Contains(null))
                throw new InvalidOperationException("作者控件必须声明完整且互不重叠的结构节点。");
            foreach (var child in children)
                if (!allowed.Contains(child) && !IsFormattingNode(child))
                    throw new InvalidOperationException("作者内容必须放入控件声明的内容区。");
        }
    }
}
