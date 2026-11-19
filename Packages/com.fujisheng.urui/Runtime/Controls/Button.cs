using System;
using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>
    /// 可通过指针、Enter 和空格激活的文本按钮。
    /// </summary>
    public class Button : Element
    {
        Action clicked;
        bool spacePressed;
        bool keyboardPressed;
        double submitUntil;
        const string PressedClass = "urui-control-pressed";
        void SetKeyboardPressed(bool value)
        {
            if (keyboardPressed == value)
            {
                return;
            }
            keyboardPressed = value;
            EnableInClassList(PressedClass, value);
        }
        void CancelKeyboardPress()
        {
            spacePressed = false;
            SetKeyboardPressed(false);
        }
        /// <summary>
        /// 取消已失去焦点、禁用或到期的键盘反馈；使用Surface的界面时钟。
        /// </summary>
        protected override void OnUpdate()
        {
            if (keyboardPressed && (!EnabledInHierarchy || !Focused || !IsVisibleInHierarchy ||
                (!spacePressed && Document.Surface.UIClock.Now >= submitUntil)))
            {
                CancelKeyboardPress();
            }
        }
        /// <summary>
        /// 禁用立即取消键盘按压，重新启用时不恢复旧输入。
        /// </summary>
        protected override void OnAttributeChanged(string name)
        {
            if (name == "disabled" && !EnabledSelf)
            {
                CancelKeyboardPress();
            }
        }
        /// <summary>
        /// 文档工厂构造入口。
        /// </summary>
        protected internal Button(ElementContext context) : base(context) { }
        /// <summary>
        /// 点击与键盘激活通知；禁用时不触发。
        /// </summary>
        public event Action Clicked
        {
            add
            {
                RequireValid();
                clicked += value;
            }
            remove
            {
                Document.CheckThread();
                clicked -= value;
            }
        }
        /// <summary>
        /// 按钮默认参与焦点导航。
        /// </summary>
        protected override void OnInitialize()
        {
            Focusable = true;
            // 克隆保留作者结构，但不保留原实例尚未释放的键盘按压。
            RemoveFromClassList(PressedClass);
        }
        /// <summary>
        /// 处理原生点击及键盘激活。
        /// </summary>
        protected override void OnEvent(EventBase evt)
        {
            if (evt is BlurEvent || evt is PointerCancelEvent)
            {
                CancelKeyboardPress();
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
                    spacePressed = false;
                    submitUntil = Document.Surface.UIClock.Now + .1;
                    SetKeyboardPressed(true);
                    SendEvent(new ClickEvent { PointerId = -1 });
                    key.PreventDefault();
                }
                else if (key.KeyCode == KeyCode.Space)
                {
                    spacePressed = true;
                    SetKeyboardPressed(true);
                    key.PreventDefault();
                }
            }
            if (evt is KeyUpEvent up && up.KeyCode == KeyCode.Space && spacePressed)
            {
                CancelKeyboardPress();
                SendEvent(new ClickEvent { PointerId = -1 });
                up.PreventDefault();
            }
            if (evt is BlurEvent || evt is PointerCancelEvent)
            {
                CancelKeyboardPress();
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

                try
                {
                    callback();
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
            }
        }
        /// <summary>
        /// 失效时释放业务回调引用。
        /// </summary>
        protected override void OnInvalidated()
        {
            clicked = null;
        }
    }
}
