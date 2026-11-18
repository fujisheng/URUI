using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Sessions;
using URUI.Editor.Inspection;

namespace URUI.Authoring.Windows
{
    public static class RectCommands
    {
        static string Px(float value) => value.ToString("0.###", CultureInfo.InvariantCulture) + "px";

        internal static bool TryAutomaticOffset(InspectedNode node, string axis, out string offset)
        {
            offset = null;
            bool horizontal = axis == "left";
            if (!StylePixels(node.Computed(horizontal ? "margin-left" : "margin-top"), out float margin))
                return false;
            Vector2 origin = Vector2.zero, scroll = Vector2.zero;
            if (node.Computed("position") != "fixed")
            {
                for (var ancestor = node.Parent; ancestor != null; ancestor = ancestor.Parent)
                {
                    scroll += ancestor.ScrollOffset;
                    if (ancestor.Computed("position") is (null or "static"))
                        continue;
                    if (!ancestor.HasLayout)
                        return false;
                    origin = ancestor.Bounds.position + new Vector2(Pixels(ancestor.Computed("border-left-width")), Pixels(ancestor.Computed("border-top-width")));
                    break;
                }
            }
            Vector2 position = node.Bounds.position + scroll - origin;
            float value = (horizontal ? position.x : position.y) - margin;
            if (!float.IsFinite(value))
                return false;
            offset = Px(value);
            return true;
        }

        static bool StylePixels(string value, out float result)
        {
            result = 0;
            return value is null or "0" or "auto" || (value.EndsWith("px", StringComparison.Ordinal) &&
                float.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out result));
        }
        public static AuthoringEdit[] AnchorEdits(InspectedNode node, string key, int x, int y, bool stretchX, bool stretchY)
        {
            if (node == null || !node.HasLayout || node.Parent == null || !node.Parent.HasLayout)
                throw new InvalidOperationException("Anchor conversion requires completed parent and child layout.");
            if (x < 0 || x > 2 || y < 0 || y > 2)
                throw new InvalidOperationException("Invalid anchor preset.");
            string transform = node.Computed("transform");
            if (!string.IsNullOrEmpty(transform) && transform != "none")
                throw new InvalidOperationException("Preserve complex transforms through advanced CSS; preset conversion would be ambiguous.");
            var rect = node.Layout;
            var parent = ContainingSize(node);
            var values = new Dictionary<string, string>
            {
                ["position"] = "absolute",
                ["margin-left"] = "0px",
                ["margin-top"] = "0px",
                ["margin-right"] = "0px",
                ["margin-bottom"] = "0px",
                ["left"] = stretchX ? Px(rect.x) : (x * 50) + "%",
                ["top"] = stretchY ? Px(rect.y) : (y * 50) + "%",
                ["right"] = stretchX ? Px(parent.x - rect.xMax) : "auto",
                ["bottom"] = stretchY ? Px(parent.y - rect.yMax) : "auto",
                ["width"] = stretchX ? "auto" : Px(rect.width),
                ["height"] = stretchY ? "auto" : Px(rect.height)
            };
            if (!stretchX)
                values["margin-left"] = Px(rect.x - parent.x * x * .5f);
            if (!stretchY)
                values["margin-top"] = Px(rect.y - parent.y * y * .5f);
            var edits = new List<AuthoringEdit>();
            foreach (var pair in values)
                edits.Add(new AuthoringEdit { Node = key, Property = "style." + pair.Key, Value = pair.Value });
            return edits.ToArray();
        }
        public static Vector2 ContainingSize(InspectedNode node)
        {
            if (node.Computed("position") != "fixed")
                for (var ancestor = node.Parent; ancestor != null; ancestor = ancestor.Parent)
                {
                    string transform = ancestor.Computed("transform");
                    if (!string.IsNullOrEmpty(transform) && transform != "none")
                        throw new InvalidOperationException("A transformed containing block requires advanced CSS editing.");
                    if (ancestor.Computed("position") == "static")
                        continue;
                    Vector2 size = ancestor.Layout.size;
                    size.x -= Pixels(ancestor.Computed("border-left-width")) + Pixels(ancestor.Computed("border-right-width"));
                    size.y -= Pixels(ancestor.Computed("border-top-width")) + Pixels(ancestor.Computed("border-bottom-width"));
                    return size;
                }
            return RuntimeInspectionBridge.Viewport(node.Document);
        }
        static float Pixels(string value)
        {
            return value != null && value.EndsWith("px", StringComparison.Ordinal) && float.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : 0;
        }
        public static void DrawPresets(ElementSelectionProxy proxy)
        {
            if (proxy.Node == null || !proxy.Node.HasLayout)
                return;
            if (GUILayout.Button(new GUIContent(L.Tr("定位预设…"), L.Tr("明确转换为 CSS 绝对定位；保留当前矩形。"))))
            {
                var menu = new GenericMenu();
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < 3; x++)
                    {
                        int ax = x, ay = y;
                        menu.AddItem(new GUIContent(new[] { L.Tr("上"), L.Tr("中"), L.Tr("下") }[y] + "/" + new[] { L.Tr("左"), L.Tr("中"), L.Tr("右") }[x]), false, () => ApplyAnchor(proxy, ax, ay, false, false));
                    }
                menu.AddItem(new GUIContent(L.Tr("拉伸/水平")), false, () => ApplyAnchor(proxy, 0, 0, true, false));
                menu.AddItem(new GUIContent(L.Tr("拉伸/垂直")), false, () => ApplyAnchor(proxy, 0, 0, false, true));
                menu.AddItem(new GUIContent(L.Tr("拉伸/双向")), false, () => ApplyAnchor(proxy, 0, 0, true, true));
                menu.ShowAsContext();
            }
        }
        static void ApplyAnchor(ElementSelectionProxy proxy, int x, int y, bool sx, bool sy)
        {
            AuthoringEnvironment.Try(() =>
            {
                if (AuthoringEnvironment.Mode != WorkspaceMode.Layout)
                    throw new InvalidOperationException("请在布局模式修改锚点预设。");
                var author = AuthoringEnvironment.Author;
                var edits = AnchorEdits(proxy.Node, proxy.SourceKey, x, y, sx, sy);
                SessionUndoBridge.Record(author, () => author.Execute(edits, author.Revision));
            });
        }
        public static string Offset(string value, float pixels, float parentExtent, float fallback)
        {
            if (value != null && value.EndsWith("%", StringComparison.Ordinal) && parentExtent > 0 && float.TryParse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float percentage))
                return (percentage + pixels / parentExtent * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";
            if (value != null && value.EndsWith("px", StringComparison.Ordinal) && float.TryParse(value.Substring(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out float length))
                return Px(length + pixels);
            return Px(fallback + pixels);
        }
    }
}
