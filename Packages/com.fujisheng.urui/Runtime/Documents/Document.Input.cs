using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Elements;
using URUI.Controls;
using URUI.Events;
using URUI.Internal.Interop;
using UnityInput = UnityEngine.Input;

namespace URUI.Events
{
    /// <summary>归一化指针输入阶段。</summary>
    public enum PointerPhase { Down, Move, Up, Cancel, Wheel }
}

namespace URUI.Documents
{
    public sealed partial class Document
    {
        /// <summary>请求此文档的键盘焦点；优先保留当前输入或选择作者 autofocus，隐藏或被模态阻断时返回 false。</summary>
        public bool Focus()
        {
            CheckThread();
            if (!IsLoaded) return false;
            var focused = FocusedElement;
            if (focused != null && focused.EnabledInHierarchy && focused.Focus()) return true;
            foreach (var element in Query())
            {
                if (element.HasAttribute("autofocus") && element.EnabledInHierarchy && element.Focus()) return true;
            }
            // 文档根接收键盘事件，但不加入 HTML 的 Tab 顺序，也不改写作者属性。
            var result = NativeMethods.urui_element_focus(NativeHandle, DocumentElement.Token, 1);
            FinishNativeCall();
            if (result < 0) throw new InvalidOperationException("URUI 文档聚焦被拒绝：" + result);
            return result == 1;
        }

        /// <summary>当前焦点元素，没有焦点时为 null。</summary>
        public Element FocusedElement
        {
            get
            {
                CheckThread();
                return IsLoaded ? WrapElement(NativeMethods.urui_focused_element(NativeHandle)) : null;
            }
        }
        /// <summary>命中文档坐标处的实际节点，不要求节点设置 Name。</summary>
        public Element Pick(Vector2 position)
        {
            CheckThread();
            ElementStyle.RequireFinite(position.x); ElementStyle.RequireFinite(position.y);
            return IsLoaded ? WrapElement(NativeMethods.urui_element_pick(NativeHandle, position.x, position.y)) : null;
        }
    }

}
