using System;

namespace URUI.Authoring.Presentation
{
    /// <summary>
    /// Editor labels and creation templates; never changes runtime identity or layout rules.
    /// </summary>
    public static class NodePresentation
    {
        public static string Name(string tag, string id)
        {
            return string.IsNullOrEmpty(id) ? tag + "（未命名）" : id;
        }

        public static bool IsMetadata(string tag)
        {
            return tag == "head" || tag == "style" || tag == "link" || tag == "meta" || tag == "title" || tag == "script" || tag == "base";
        }

        public static bool Visible(string tag, string id, bool full)
        {
            return full || !string.IsNullOrEmpty(id) || (tag != "html" && tag != "#text" && !IsMetadata(tag));
        }

        public static string TypeLabel(string tag, string inputType = null)
        {
            switch (tag)
            {
                case "body":
                    return "页面";
                case "div":
                    return "容器";
                case "span":
                case "p":
                case "label":
                    return "文字";
                case "img":
                    return "图片";
                case "button":
                    return "按钮";
                case "input":
                    if (inputType == "checkbox" || inputType == "radio")
                    {
                        return "选择控件";
                    }
                    if (inputType == "range")
                    {
                        return "滑动条";
                    }
                    return "输入框";
                case "textarea":
                    return "多行输入框";
                case "select":
                    return "下拉选择";
                case "progress":
                    return "进度条";
                case "urui-particles":
                    return "粒子";
                default:
                    return IsMetadata(tag) ? "文档信息" : "节点";
            }
        }

        public static string Template(string kind)
        {
            switch (kind)
            {
                case "panel":
                    return "<div style=\"width:200px;height:120px;background-color:#444444;\"></div>";
                case "image":
                    return "<img style=\"width:100px;height:100px;\" />";
                case "text":
                    return "<span style=\"font-size:24px;color:#ffffff;\">Text</span>";
                case "button":
                    return "<button style=\"width:160px;height:40px;color:#ffffff;background-color:#2d3947;\">Button</button>";
                case "input":
                    return "<input type=\"text\" placeholder=\"Enter text\" style=\"width:200px;height:32px;\" />";
                case "toggle":
                    return "<input type=\"checkbox\" />";
                case "radio":
                    return "<input type=\"radio\" />";
                case "slider":
                    return "<input type=\"range\" min=\"0\" max=\"1\" step=\"0.01\" value=\"0.5\" style=\"width:200px;\" />";
                case "progress":
                    return "<progress value=\"0.5\" max=\"1\" style=\"width:200px;height:20px;\"></progress>";
                case "particles":
                    return "<urui-particles style=\"width:200px;height:200px;\"></urui-particles>";
                case "textarea":
                    return "<textarea placeholder=\"Text\" style=\"width:200px;height:70px;\"></textarea>";
                case "select":
                    return "<select style=\"width:160px;\"><option>Option</option></select>";
                // Keep the existing tag-based creation entry point for callers and Agent workflows.
                case "img":
                    return "<img />";
                case "div":
                case "span":
                case "urui-particles":
                    return "<" + kind + ">" + (kind == "span" ? "Text" : "") + "</" + kind + ">";
                default:
                    throw new InvalidOperationException("Unsupported control template: " + kind);
            }
        }
    }
}
