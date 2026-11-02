using System;
using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>可通过指针、Enter 和空格激活的文本按钮。</summary>
    public class Button : Element
    {
        Action clicked;
        bool spacePressed;
        /// <summary>文档工厂构造入口。</summary>
        protected internal Button(ElementContext context) : base(context) { }
        /// <summary>点击与键盘激活通知；禁用时不触发。</summary>
        public event Action Clicked
        {
            add { RequireValid(); clicked += value; }
            remove { Document.CheckThread(); clicked -= value; }
        }
        /// <summary>按钮默认参与焦点导航。</summary>
        protected override void OnInitialize() { Focusable = true; }
        /// <summary>处理原生点击及键盘激活。</summary>
        protected override void OnEvent(EventBase evt)
        {
            if (evt is BlurEvent || evt is PointerCancelEvent)
            {
                spacePressed = false;
            }

            if (!EnabledInHierarchy)
            {
                return;
            }

            if (evt is ClickEvent)
            {
                InvokeClicked();
            }

            if (evt is KeyDownEvent key && ReferenceEquals(evt.Target, this))
            {
                if (key.KeyCode == KeyCode.Return || key.KeyCode == KeyCode.KeypadEnter)
                {
                    SendEvent(new ClickEvent { PointerId = -1 }); key.PreventDefault();
                }
                else if (key.KeyCode == KeyCode.Space) { spacePressed = true; key.PreventDefault(); }
            }
            if (evt is KeyUpEvent up && up.KeyCode == KeyCode.Space && spacePressed)
            {
                spacePressed = false; SendEvent(new ClickEvent { PointerId = -1 }); up.PreventDefault();
            }
            if (evt is BlurEvent || evt is PointerCancelEvent)
            {
                spacePressed = false;
            }
        }
        void InvokeClicked()
        {
            if (clicked == null)
            {
                return;
            }

            foreach (Action callback in clicked.GetInvocationList())
            {
                if (!IsValid || Document.HasPendingInvalidation || !EnabledInHierarchy)
                {
                    break;
                }

                try { callback(); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }
        /// <summary>失效时释放业务回调引用。</summary>
        protected override void OnInvalidated() { clicked = null; }
    }
}
