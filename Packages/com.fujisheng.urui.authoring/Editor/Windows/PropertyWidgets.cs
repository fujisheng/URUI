using L = URUI.Editor.Localization.EditorText;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using URUI.Authoring.Presentation;

namespace URUI.Authoring.Windows
{
    internal static class PropertyWidgets
    {

        static readonly Dictionary<string, string[]> choices = new Dictionary<string, string[]>
        {
            ["position"] = new[] { "static", "relative", "absolute", "fixed" },
            ["display"] = new[] { "block", "flex", "inline-block", "inline", "none" },
            ["visibility"] = new[] { "visible", "hidden" },
            ["overflow"] = new[] { "visible", "hidden", "auto", "scroll" },
            ["flex-direction"] = new[] { "row", "column", "row-reverse", "column-reverse" },
            ["justify-content"] = new[] { "flex-start", "center", "flex-end", "space-between", "space-around", "space-evenly" },
            ["align-items"] = new[] { "stretch", "flex-start", "center", "flex-end", "baseline" },
            ["text-align"] = new[] { "left", "center", "right", "justify" },
            ["flex-wrap"] = new[] { "nowrap", "wrap", "wrap-reverse" },
            ["align-self"] = new[] { "auto", "stretch", "flex-start", "center", "flex-end", "baseline" },
            ["object-fit"] = new[] { "fill", "contain", "cover" },
            ["white-space"] = new[] { "normal", "nowrap", "pre", "pre-wrap", "pre-line" },
            ["font-style"] = new[] { "normal", "italic" },
            ["--urui-image-fill-mode"] = new[] { "none", "horizontal", "vertical", "radial90", "radial180", "radial360" }
        };
        static readonly Dictionary<string, string> labels = new Dictionary<string, string>
        {
            ["id"] = "名称（绑定 id）",
            ["class"] = "样式类",
            ["title"] = "提示文字",
            ["disabled"] = "禁用",
            ["tabindex"] = "焦点顺序",
            ["position"] = "定位方式",
            ["left"] = "左",
            ["top"] = "上",
            ["right"] = "右",
            ["bottom"] = "下",
            ["width"] = "宽度",
            ["height"] = "高度",
            ["min-width"] = "最小宽度",
            ["min-height"] = "最小高度",
            ["display"] = "布局类型",
            ["flex-direction"] = "排列方向",
            ["flex-grow"] = "占用剩余空间",
            ["justify-content"] = "主轴对齐",
            ["align-items"] = "交叉轴对齐",
            ["gap"] = "间距",
            ["overflow"] = "溢出处理",
            ["transform-origin"] = "变换中心",
            ["transform"] = "变换",
            ["background-color"] = "背景颜色",
            ["color"] = "文字颜色",
            ["opacity"] = "透明度",
            ["border-width"] = "边框宽度",
            ["border-color"] = "边框颜色",
            ["border-radius"] = "圆角",
            ["font-size"] = "字号",
            ["text-align"] = "文本对齐",
            ["visibility"] = "可见性",
            ["text"] = "文字",
            ["value"] = "控件值",
            ["src"] = "资源路径",
            ["data-urui-sprite"] = "图集区域名称",
            ["--urui-image-color"] = "图像颜色",
            ["name"] = "表单名称",
            ["placeholder"] = "占位文字",
            ["readonly"] = "只读",
            ["maxlength"] = "最大长度",
            ["min"] = "最小值",
            ["max"] = "最大值",
            ["step"] = "步长",
            ["for"] = "关联控件",
            ["max-width"] = "最大宽度",
            ["max-height"] = "最大高度",
            ["flex-shrink"] = "空间不足时收缩",
            ["flex-basis"] = "基础尺寸",
            ["align-self"] = "自身对齐",
            ["flex-wrap"] = "换行",
            ["font-family"] = "字体",
            ["font-style"] = "字体样式",
            ["font-weight"] = "字重",
            ["white-space"] = "文本换行",
            ["line-height"] = "行高",
            ["object-fit"] = "图片适配",
            ["--urui-image-fill-mode"] = "填充方式",
            ["--urui-image-fill-amount"] = "填充比例",
            ["--urui-image-fill-origin"] = "填充起点",
            ["--urui-image-fill-clockwise"] = "顺时针填充",
            ["--urui-image-fill-center"] = "绘制中心",
            ["--urui-image-slice-top"] = "切片 · 上",
            ["--urui-image-slice-bottom"] = "切片 · 下",
            ["--urui-image-slice-left"] = "切片 · 左",
            ["--urui-image-slice-right"] = "切片 · 右",
            ["margin-left"] = "外边距 · 左",
            ["margin-top"] = "外边距 · 上",
            ["margin-right"] = "外边距 · 右",
            ["margin-bottom"] = "外边距 · 下",
            ["padding-left"] = "内边距 · 左",
            ["padding-top"] = "内边距 · 上",
            ["padding-right"] = "内边距 · 右",
            ["padding-bottom"] = "内边距 · 下"
        };
        internal static string Label(string name) => labels.TryGetValue(name, out var label) ? L.Tr(label) : name;
        internal static string Draw(string property, string shown, GUIContent label, bool typed)
        {
            if (!typed || !property.StartsWith("style.", StringComparison.Ordinal))
            {
                return EditorGUILayout.DelayedTextField(label, shown);
            }
            string name = property.Substring(6);
            if (choices.TryGetValue(name, out var options))
            {
                int index = Array.IndexOf(options, shown);
                if (index >= 0 || string.IsNullOrEmpty(shown))
                {
                    int selected = EditorGUILayout.Popup(label, index, Array.ConvertAll(options, value => new GUIContent(ChoiceLabel(name, value), value)));
                    return selected == index || selected < 0 ? shown : options[selected];
                }
            }
            if (name.EndsWith("color", StringComparison.Ordinal) && TryColor(shown, out var color))
            {
                var next = EditorGUILayout.ColorField(label, color, true, true, false);
                return next == color ? shown : "#" + ColorUtility.ToHtmlStringRGBA(next);
            }
            if (StyleFields.TryNumber(shown, out double value, out string unit))
            {
                if ((name == "--urui-image-fill-clockwise" || name == "--urui-image-fill-center") && unit.Length == 0 && (value == 0 || value == 1))
                {
                    bool enabled = EditorGUILayout.Toggle(label, value == 1);
                    return enabled == (value == 1) ? shown : enabled ? "1" : "0";
                }
                double next = EditorGUILayout.DelayedDoubleField(label, value);
                string nextUnit = unit;
                if (unit.Length > 0)
                {
                    string[] units = unit == "deg" ? new[] { "deg" } : new[] { "px", "%", "em", "rem", "vw", "vh" };
                    int index = Array.IndexOf(units, unit);
                    nextUnit = units[EditorGUILayout.Popup(index, units, GUILayout.Width(48))];
                }
                if (next == value && nextUnit == unit)
                {
                    return shown;
                }
                string updated = StyleFields.WithNumber(shown, next);
                if (nextUnit != unit)
                {
                    int at = updated.LastIndexOf(unit, StringComparison.Ordinal);
                    updated = updated.Substring(0, at) + nextUnit + updated.Substring(at + unit.Length);
                }
                return updated;
            }
            return EditorGUILayout.DelayedTextField(label, shown);
        }
        static string ChoiceLabel(string property, string value)
        {
            switch (value)
            {
                case "normal":
                    return L.Tr("Normal");
                case "italic":
                    return L.Tr("Italic");
                case "static":
                    return L.Tr("正常排布");
                case "relative":
                    return L.Tr("相对定位");
                case "absolute":
                    return L.Tr("绝对定位");
                case "fixed":
                    return L.Tr("固定定位");
                case "flex":
                    return L.Tr("弹性排列");
                case "block":
                    return L.Tr("块级排列");
                case "inline":
                    return L.Tr("行内");
                case "inline-block":
                    return L.Tr("行内块");
                case "none":
                    return property == "display" ? L.Tr("不参与布局") : L.Tr("无");
                case "row":
                    return L.Tr("横向");
                case "column":
                    return L.Tr("纵向");
                case "row-reverse":
                    return L.Tr("横向反序");
                case "column-reverse":
                    return L.Tr("纵向反序");
                case "flex-start":
                    return L.Tr("起始");
                case "flex-end":
                    return L.Tr("末尾");
                case "center":
                    return L.Tr("居中");
                case "stretch":
                    return L.Tr("拉伸");
                case "space-between":
                    return L.Tr("两端分布");
                case "space-around":
                    return L.Tr("环绕分布");
                case "space-evenly":
                    return L.Tr("均匀分布");
                case "visible":
                    return L.Tr("可见");
                case "hidden":
                    return L.Tr("隐藏");
                case "auto":
                    return L.Tr("自动");
                case "scroll":
                    return L.Tr("滚动");
                case "fill":
                    return L.Tr("拉伸填满");
                case "contain":
                    return L.Tr("完整显示");
                case "cover":
                    return L.Tr("覆盖裁剪");
                case "nowrap":
                    return L.Tr("不换行");
                case "wrap":
                    return L.Tr("自动换行");
                case "wrap-reverse":
                    return L.Tr("反向换行");
                case "horizontal":
                    return L.Tr("水平");
                case "vertical":
                    return L.Tr("垂直");
                default:
                    return value;
            }
        }
        static bool TryColor(string text, out Color color)
        {
            if (ColorUtility.TryParseHtmlString(text, out color))
            {
                return true;
            }
            var match = Regex.Match(text ?? "", @"^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([\d.]+))?\s*\)$");
            if (!match.Success)
            {
                return false;
            }
            color = new Color(int.Parse(match.Groups[1].Value) / 255f, int.Parse(match.Groups[2].Value) / 255f, int.Parse(match.Groups[3].Value) / 255f,
                match.Groups[4].Success ? float.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : 1);
            return true;
        }
    }
}
