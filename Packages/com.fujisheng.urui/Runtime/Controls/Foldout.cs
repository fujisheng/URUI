using URUI.Elements;
using URUI.Events;
using System;
using UnityEngine;

namespace URUI.Controls
{
    /// <summary>由按钮标题和可折叠内容区组成的容器。</summary>
    public class Foldout : Element, INotifyValueChanged<bool>
    {
        Button header;
        Element content;
        Element label;
        bool expanded = true;
        bool projectionQueued;
        string expandedDisplay;
        Action project;
        /// <summary>文档工厂构造入口。</summary>
        protected internal Foldout(ElementContext context) : base(context) { }
        /// <summary>业务节点位于可折叠内容区。</summary>
        public override Element ContentContainer => content ?? this;
        /// <summary>标题文本，只更新标题节点。</summary>
        public override string Text { get => GetAttribute("data-urui-label") ?? label.Text; set => SetAttribute("data-urui-label", value); }
        /// <summary>是否展开内容，折叠不销毁子节点。</summary>
        public bool Value
        {
            get { RequireValid(); return expanded; }
            set
            {
                RequireValid();
                bool previous = expanded;
                SetValueWithoutNotify(value);
                if (previous != value)
                {
                    SendEvent(new URUI.Events.ChangeEvent<bool> { PreviousValue = previous, NewValue = value });
                }
            }
        }
        /// <summary>静默设置展开状态。</summary>
        public void SetValueWithoutNotify(bool value)
        {
            RequireValid(); SetAttribute("data-urui-expanded", value ? "true" : "false");
        }
        /// <summary>订阅展开状态变化。</summary>
        public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<bool>> callback) => RegisterCallback(callback);
        /// <summary>解绑展开状态变化。</summary>
        public void UnregisterValueChangedCallback(Action<ChangeEvent<bool>> callback) => UnregisterCallback(callback);
        /// <summary>复用作者标题和内容区；空的运行时控件生成同名结构。</summary>
        protected override void OnInitialize()
        {
            var children = Hierarchy.Children();
            var headerElement = ControlMarkup.Find(children, "urui-foldout-header");
            content = ControlMarkup.Find(children, "urui-foldout-content");
            if (headerElement != null || content != null)
            {
                ControlMarkup.RequireOnly(children, headerElement, content);
                header = headerElement as Button ?? throw new InvalidOperationException("urui-foldout-header 必须是 button。");
                label = header.Q(className: "urui-foldout-label") ?? header;
            }
            else
            {
                ControlMarkup.RequireOnly(children);
                Style.Display = DisplayStyle.Block;
                header = Document.Create<Button>(); header.AddToClassList("urui-foldout-header");
                label = Document.CreateElement("span"); label.AddToClassList("urui-foldout-label"); header.Add(label);
                content = Document.Create<Element>(); content.AddToClassList("urui-foldout-content");
                content.Style.Display = DisplayStyle.Block;
                Hierarchy.Add(header); Hierarchy.Add(content);
            }
            expandedDisplay = content.Style.Get("display");
            project = Project;
            header.Clicked += () => Value = !Value;
            expanded = GetAttribute("data-urui-expanded") != "false";
            RequestProjection();
        }
        /// <summary>属性写入与有类型写入共享同一状态；属性写入不产生用户变化事件。</summary>
        protected override void OnAttributeChanged(string name)
        {
            base.OnAttributeChanged(name);
            if (project == null) return;
            if (name == "data-urui-expanded") expanded = GetAttribute(name) != "false";
            if (name == "data-urui-expanded" || name == "data-urui-label") RequestProjection();
        }
        void RequestProjection()
        {
            if (projectionQueued) return;
            projectionQueued = true; Document.RunAfterEvent(project);
        }
        void Project()
        {
            projectionQueued = false;
            if (!IsValid || !header.IsValid || !content.IsValid || !label.IsValid) return;
            if (!expanded)
            {
                Document.Surface.Input.CancelPointers(content);
                if (!IsValid || !content.IsValid) return;
                var focused = Document.FocusedElement;
                if (focused != null && content.Contains(focused)) header.Focus();
            }
            if (!IsValid || !content.IsValid || !header.IsValid) return;
            if (!expanded) content.Style.Display = DisplayStyle.None;
            else if (expandedDisplay == null) content.Style.Remove("display");
            else content.Style.Set("display", expandedDisplay);
            var caption = GetAttribute("data-urui-label");
            if (caption != null) label.Text = caption;
            EnableInClassList("urui-expanded", expanded);
            header.SetAttribute("aria-expanded", expanded ? "true" : "false");
            if (!string.IsNullOrEmpty(content.Name)) header.SetAttribute("aria-controls", content.Name);
        }
        /// <summary>标题获得焦点时，左右方向键折叠或展开。</summary>
        protected override void OnEvent(EventBase evt)
        {
            if (evt is KeyDownEvent key && ReferenceEquals(evt.Target, header) && EnabledInHierarchy)
            {
                if (key.KeyCode == KeyCode.LeftArrow) { Value = false; key.PreventDefault(); }
                else if (key.KeyCode == KeyCode.RightArrow) { Value = true; key.PreventDefault(); }
            }
        }
    }
}
