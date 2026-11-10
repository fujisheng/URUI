using System;
using UnityEngine;
using URUI.Documents;
using URUI.Events;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    /// <summary>命中策略；Ignore 元素本身不作为命中目标，子元素仍可命中。</summary>
    public enum PickingMode { Position, Ignore }

    public partial class Element
    {
        uint createdLayoutVersion;
        bool observedAttached, initialized;
        Rect? observedLayout;

        /// <summary>供派生控件使用的构造入口；上下文只能由 Document 工厂提供。</summary>
        protected internal Element(ElementContext context) : this(
            (context ?? throw new ArgumentNullException(nameof(context))).Document, context.Generation, context.Token, context.NativeHandle)
        { }

        internal ulong Token => token;
        /// <summary>拥有本节点的文档。</summary>
        public Document Document => document;
        /// <summary>业务子节点的容器；组合控件可覆写。</summary>
        public virtual Element ContentContainer => this;
        /// <summary>访问真实 DOM 子节点，绕过内容容器重定向。</summary>
        public ElementHierarchy Hierarchy => new ElementHierarchy(this);
        /// <summary>调用者关联数据；不向原生层序列化。</summary>
        public object UserData { get; set; }
        /// <summary>本节点自己的启用状态。</summary>
        public bool EnabledSelf => !HasAttribute("disabled");
        /// <summary>本节点及所有祖先均启用。</summary>
        public bool EnabledInHierarchy
        {
            get
            {
                for (var value = this; value != null; value = value.Parent)
                {
                    if (!value.EnabledSelf)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        /// <summary>设置本节点启用状态；禁用后不接受交互默认行为。</summary>
        public void SetEnabled(bool value)
        {
            if (value)
            {
                RemoveAttribute("disabled");
            }
            else
            {
                SetAttribute("disabled", "");
            }

            if (!value)
            {
                document.Surface.Input.CancelPointers(this);
            }

            if (!IsValid)
            {
                return;
            }

            var focused = document.FocusedElement;
            if (!value && focused != null && Contains(focused))
            {
                focused.Blur();
            }
        }
        /// <summary>是否绘制；隐藏时保留布局。</summary>
        public bool IsVisibleInHierarchy=>NativeMethods.urui_element_visible_in_hierarchy(NativeHandle, CheckedToken())==1;
        public bool Visible
        {
            get => ReadStyle("visibility", false) != "hidden";
            set => Style.Set("visibility", value ? "visible" : "hidden");
        }
        /// <summary>是否可获得键盘焦点。</summary>
        public bool Focusable
        {
            get => HasAttribute("tabindex") || ReadStyle("--urui-tab-index", false) != "none";
            set
            {
                if (!value) RemoveAttribute("tabindex");
                Style.Set("--urui-tab-index", value ? "auto" : "none");
            }
        }
        /// <summary>HTML tabindex；负数保留程序焦点并退出顺序导航，正数优先于零。</summary>
        public int TabIndex
        {
            get => int.TryParse(GetAttribute("tabindex"), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : Focusable ? 0 : -1;
            set => SetAttribute("tabindex", value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        /// <summary>是否为当前文档焦点节点。</summary>
        public bool Focused => NativeMethods.urui_focused_element(NativeHandle) == CheckedToken();
        /// <summary>请求焦点，禁用或未挂载时返回 false。</summary>
        public bool Focus()
        {
            RequireValid();
            bool focused = Focusable && EnabledInHierarchy && IsAttached && NativeMethods.urui_element_focus(NativeHandle, token, 1) == 1;
            document.FinishNativeCall();
            return focused;
        }
        /// <summary>释放本节点焦点。</summary>
        public void Blur()
        {
            RequireValid();
            RequireSuccess(NativeMethods.urui_element_focus(NativeHandle, token, 0), "释放焦点");
            document.FinishNativeCall();
        }
        /// <summary>节点自身的指针命中策略。</summary>
        public PickingMode PickingMode
        {
            get => ReadStyle("pointer-events", false) == "none" ? PickingMode.Ignore : PickingMode.Position;
            set => Style.Set("pointer-events", value == PickingMode.Ignore ? "none" : "auto");
        }
        /// <summary>包装完成后的初始化钩子，此时可创建子节点和注册回调。</summary>
        protected virtual void OnInitialize() { }
        /// <summary>每次完成原生布局后的控件更新钩子。</summary>
        protected virtual void OnUpdate() { }
        /// <summary>节点失效后的资源清理钩子；此时不得再访问原生节点。</summary>
        protected virtual void OnInvalidated() { }
        internal void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            createdLayoutVersion = document.LayoutVersion;
            observedAttached = IsAttached;
            OnInitialize();
        }
        internal void Refresh(bool layout)
        {
            if(invalid)return;
            if(!document.IsCurrent(generation)){Invalidate();return;}
            var state=NativeMethods.urui_element_refresh_state(NativeHandle,token,out var current,4);
            if(state<0)throw new InvalidOperationException("元素刷新状态读取失败："+NativeMethods.LastError());
            if((state&1)==0){Invalidate();return;}
            bool attached = (state&2)!=0;
            var delivered=false;
            if (attached != observedAttached)
            {
                observedAttached = attached;
                SendEvent(attached ? (EventBase)new AttachToDocumentEvent() : new DetachFromDocumentEvent());
                delivered=true;
            }
            if (layout && attached)
            {
                // 挂载回调可同步修改/重载节点；保持回调之后读取布局的原有时序。
                if(delivered||document.LayoutVersion<=createdLayoutVersion)current=Layout;
                if (!observedLayout.HasValue || observedLayout.Value != current)
                {
                    var previous = observedLayout ?? default;
                    observedLayout = current;
                    SendEvent(new GeometryChangedEvent { OldRect = previous, NewRect = current });
                    delivered=true;
                }
                if (!delivered || IsValid)
                {
                    OnUpdate();
                }
            }
        }
        internal ulong CheckedToken()
        {
            RequireValid();
            return token;
        }
        // 只由原生绘制记录确认：新包装句柄对应的节点已经参与本次布局，不发布额外布局事件。
        internal void AcceptRenderedLayout()
        {
            if(document.LayoutVersion==0)throw new InvalidOperationException("原生绘制发生在布局完成之前。");
            if(createdLayoutVersion==document.LayoutVersion)createdLayoutVersion=document.LayoutVersion-1;
        }
        /// <summary>节点已经参与至少一次完成的文档布局。</summary>
        public bool HasLayout => IsValid && IsAttached && document.LayoutVersion > createdLayoutVersion;
        /// <summary>查询当前节点及后代中的首个有类型元素。</summary>
        public T Q<T>(string name = null, string className = null) where T : Element => Query<T>(name, className).First();
        /// <summary>构造当前节点及后代的有类型查询。</summary>
        public ElementQuery<T> Query<T>(string name = null, string className = null) where T : Element
        {
            RequireValid();
            if (name != null)
            {
                RequireText(name, nameof(name));
            }

            if (className != null)
            {
                RequireClassName(className);
            }

            return new ElementQuery<T>(this, name, className);
        }
        /// <summary>检查是否为自身或后代节点。</summary>
        public bool Contains(Element element)
        {
            RequireValid();
            for (var current = element; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, this))
                {
                    return true;
                }
            }

            return false;
        }
        /// <summary>返回最近的指定类型祖先，不包含自身。</summary>
        public T GetFirstAncestorOfType<T>() where T : Element
        {
            for (var current = Parent; current != null; current = current.Parent)
            {
                if (current is T typed)
                {
                    return typed;
                }
            }

            return null;
        }
        /// <summary>移动到兄弟节点末尾。</summary>
        public void BringToFront() => Parent?.Add(this);
        /// <summary>移动到兄弟节点开头。</summary>
        public void SendToBack() => Parent?.Insert(0, this);
    }
}
