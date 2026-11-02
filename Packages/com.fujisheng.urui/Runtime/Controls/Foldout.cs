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
        bool expanded = true;
        /// <summary>文档工厂构造入口。</summary>
        protected internal Foldout(ElementContext context) : base(context) { }
        /// <summary>业务节点位于可折叠内容区。</summary>
        public override Element ContentContainer => content ?? this;
        /// <summary>标题文本，只更新标题节点。</summary>
        public override string Text { get => header.Text; set => header.Text = value; }
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
            RequireValid(); expanded = value;
            content.Style.Display = value ? DisplayStyle.Block : DisplayStyle.None;
            EnableInClassList("urui-expanded", value);
        }
        /// <summary>订阅展开状态变化。</summary>
        public IDisposable RegisterValueChangedCallback(Action<ChangeEvent<bool>> callback) => RegisterCallback(callback);
        /// <summary>解绑展开状态变化。</summary>
        public void UnregisterValueChangedCallback(Action<ChangeEvent<bool>> callback) => UnregisterCallback(callback);
        /// <summary>构建标题和内容区，保留 RML 中已有的业务节点。</summary>
        protected override void OnInitialize()
        {
            var children = Hierarchy.Children();
            Style.Display = DisplayStyle.Block;
            header = Document.Create<Button>();
            header.AddToClassList("urui-foldout-header");
            header.Text = GetAttribute("text") ?? "";
            header.Clicked += () => Value = !Value;
            content = Document.Create<Element>();
            content.Style.Display = DisplayStyle.Block;
            Hierarchy.Add(header); Hierarchy.Add(content);
            foreach (var child in children)
            {
                content.Add(child);
            }

            SetValueWithoutNotify(GetAttribute("expanded") != "false");
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
