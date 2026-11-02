using UnityEngine;
using URUI.Elements;
using URUI.Events;

namespace URUI.Controls
{
    /// <summary>管理布尔选择控件的键盘按下、释放与取消状态。</summary>
    public abstract class CheckableField : BaseField<bool>
    {
        KeyCode pressedKey;
        /// <summary>文档工厂构造入口。</summary>
        protected CheckableField(ElementContext context) : base(context) { }
        /// <summary>读取原生选中状态。</summary>
        protected override bool ParseValue(string value) => value == "true";
        /// <summary>写入原生选中状态。</summary>
        protected override string FormatValue(bool value) => value ? "true" : "false";
        /// <summary>键盘完整激活一次时更新选择。</summary>
        protected abstract void Activate();
        /// <summary>按键重复不重复激活，失焦和取消丢弃尚未完成的按下。</summary>
        protected override void OnEvent(EventBase evt)
        {
            base.OnEvent(evt);
            if (evt is BlurEvent || evt is PointerCancelEvent)
            {
                pressedKey = KeyCode.None;
                return;
            }
            if (!EnabledInHierarchy || !ReferenceEquals(evt.Target, this))
            {
                return;
            }

            if (evt is KeyDownEvent down && IsActivationKey(down.KeyCode))
            {
                if (pressedKey == KeyCode.None)
                {
                    pressedKey = down.KeyCode;
                }

                down.PreventDefault();
            }
            else if (evt is KeyUpEvent up && IsActivationKey(up.KeyCode))
            {
                bool activate = pressedKey == up.KeyCode;
                pressedKey = KeyCode.None;
                up.PreventDefault();
                if (activate)
                {
                    Activate();
                }
            }
        }
        static bool IsActivationKey(KeyCode key) => key == KeyCode.Space || key == KeyCode.Return || key == KeyCode.KeypadEnter;
    }
}
