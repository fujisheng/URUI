using System;
using System.Globalization;
using URUI.Elements;
using URUI.Events;
using UnityEngine;

namespace URUI.Controls
{
    /// <summary>有类型的 RadioButton 表单控件。</summary>
    public class RadioButton : CheckableField
    {
        /// <summary>文档工厂构造入口。</summary>
        protected internal RadioButton(ElementContext context) : base(context) { }
        /// <summary>单选激活只选中，不取消自身。</summary>
        protected override void Activate() => Value = true;
        /// <summary>同一文档中互斥的单选组名称。</summary>
        public string GroupName
        {
            get => GetAttribute("name") ?? "";
            set => SetAttribute("name", value);
        }
        static Element GroupScope(Element element)
        {
            for (var parent = element.Parent; parent != null; parent = parent.Parent)
            {
                if (parent.TagName == "form")
                {
                    return parent;
                }
            }

            return element.Document.Root;
        }
        /// <summary>方向键在同一表单的组内循环，跳过禁用和隐藏的成员。</summary>
        protected override void OnEvent(EventBase evt)
        {
            if (evt is KeyDownEvent key && ReferenceEquals(key.Target, this) && EnabledInHierarchy)
            {
                int direction = key.KeyCode == KeyCode.RightArrow || key.KeyCode == KeyCode.DownArrow ? 1
                    : key.KeyCode == KeyCode.LeftArrow || key.KeyCode == KeyCode.UpArrow ? -1 : 0;
                if (direction != 0)
                {
                    var scope = GroupScope(this);
                    var members = scope.Query<RadioButton>().Where(item => item.GroupName == GroupName
                        && item.EnabledInHierarchy && item.IsAttached && item.Visible && item.HasLayout
                        && ReferenceEquals(GroupScope(item), scope)).ToList();
                    int index = members.IndexOf(this);
                    if (members.Count > 0)
                    {
                        var next = members[(index + direction + members.Count) % members.Count];
                        next.Value = true;
                        if (next.IsValid && !Document.HasPendingInvalidation)
                        {
                            next.Focus();
                        }
                    }
                    key.PreventDefault();
                    return;
                }
            }
            base.OnEvent(evt);
        }
    }
}
