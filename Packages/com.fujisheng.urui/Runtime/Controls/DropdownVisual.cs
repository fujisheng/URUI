using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using URUI.Elements;
using URUI.Events;
using URUI.Animation;
using URUI.Documents;
using URUI.Styles;

namespace URUI.Controls
{
    /// <summary>
    /// 把标准 select 的选项和值呈现到作者声明的按钮和列表，皮肤不依赖原生内部节点。
    /// </summary>
    public sealed class DropdownVisual : INotifyValueChanged<string>, IDisposable
    {
        readonly Button trigger;
        readonly Button prototype;
        readonly Element label;
        readonly Element popup;
        readonly Element host;
        readonly List<Button> rows = new List<Button>();
        readonly List<IDisposable> subscriptions = new List<IDisposable>();
        readonly List<IDisposable> rowSubscriptions = new List<IDisposable>();
        readonly List<Option> options = new List<Option>();
        readonly Action deferredRefresh;
        readonly URUI.Hosting.SurfaceInput input;
        bool queued;
        bool disposed;
        bool open;
        bool initialized;
        bool transitionSubscribed;
        AnimationPlayback transition;
        static readonly StylePropertyDefinition FadeProperty = new StylePropertyDefinition("--urui-select-fade", StyleValueType.Number);
        const string OpeningClass = "urui-select-opening";
        const string ClosingClass = "urui-select-closing";
        string renderedLabel;

        readonly struct Option
        {
            internal readonly string Value;
            internal readonly string Label;
            internal readonly bool Disabled;
            internal readonly bool Hidden;
            internal Option(Element element)
            {
                Value = element.GetAttribute("value") ?? "";
                var text = new StringBuilder();
                AppendText(element, text);
                Label = element.GetAttribute("label") ?? NormalizeLabel(text.ToString());
                Disabled = element.HasAttribute("disabled");
                Hidden = element.HasAttribute("hidden");
            }
            internal bool Matches(Option other) => Value == other.Value && Label == other.Label && Disabled == other.Disabled && Hidden == other.Hidden;
        }

        /// <summary>
        /// 负责值及选项的标准 select。
        /// </summary>
        public Dropdown Control
        {
            get;
        }
        /// <summary>
        /// 列表是否展开。
        /// </summary>
        public bool IsOpen => open;
        /// <summary>
        /// 使用同一文档的作者节点；选项原型位于列表外，动态行由本对象负责销毁。
        /// </summary>
        public DropdownVisual(Dropdown control, Button trigger, Element label, Element popup, Button optionPrototype)
        {
            Control = control ?? throw new ArgumentNullException(nameof(control));
            this.trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
            this.label = label ?? throw new ArgumentNullException(nameof(label));
            this.popup = popup ?? throw new ArgumentNullException(nameof(popup));
            prototype = optionPrototype ?? throw new ArgumentNullException(nameof(optionPrototype));
            host = control.Parent;
            if (host == null)
            {
                throw new ArgumentException("标准 select 必须位于作者容器内。", nameof(control));
            }
            if (host is AuthoredDropdownSkin owner)
            {
                owner.RequireManualCreationAllowed();
            }
            foreach (var element in new Element[] { trigger, label, popup, prototype })
            {
                if (!ReferenceEquals(element.Document, control.Document) || !IsWithin(element, host))
                {
                    throw new ArgumentException("皮肤节点必须位于同一个 urui-select 作者容器内。");
                }
            }
            if (IsWithin(prototype, popup))
            {
                throw new ArgumentException("选项原型必须位于列表外。", nameof(optionPrototype));
            }
            deferredRefresh = FlushRefresh;
            input = Control.Document.Surface.Input;
            Control.PresentationChanged += RequestRefresh;
            Control.Document.Unloaded += OnUnloaded;
            input.PointerPressed += OnPointerPressed;
            subscriptions.Add(trigger.RegisterCallback<ClickEvent>(_ => Schedule(() => SetOpen(!open))));
            subscriptions.Add(host.RegisterCallback<KeyDownEvent>(OnKey));
            subscriptions.Add(Control.Document.DocumentElement.RegisterCallback<FocusEvent>(evt =>
            {
                if (open && !IsWithin(evt.Target, host))
                {
                    Schedule(() => SetOpen(false));
                }
            }, TrickleDown.TrickleDown));
            subscriptions.Add(host.RegisterCallback<BlurEvent>(_ => Schedule(() =>
            {
                // Empty Surface space has no document pointer target, but it still clears native focus.
                if (open && !IsWithin(Control.Document.FocusedElement, host))
                {
                    SetOpen(false);
                }
            }), TrickleDown.TrickleDown));
            try
            {
                // A structural clone includes the previous controller's generated rows.
                foreach (var child in popup.Hierarchy.Children())
                {
                    if (child.HasAttribute("data-urui-option-index"))
                    {
                        child.Destroy();
                    }
                }
                popup.RemoveFromClassList(OpeningClass);
                popup.RemoveFromClassList(ClosingClass);
                SetOpen(false);
                RequestRefresh();
            }
            catch
            {
                Dispose();
                throw;
            }
        }
        /// <summary>
        /// 更新标准 select 的值，并发送正常值变化通知。
        /// </summary>
        public string Value
        {
            get => Control.Value;
            set => Control.Value = value;
        }
        /// <summary>
        /// 更新标准值和作者皮肤，不发送用户变化通知。
        /// </summary>
        public void SetValueWithoutNotify(string value) => Control.SetValueWithoutNotify(value);
        /// <summary>
        /// 作者直接修改 option 文本或属性后，重新同步列表。
        /// </summary>
        public void Refresh() => RequestRefresh();

        static bool IsWithin(Element element, Element ancestor)
        {
            for (var current = element; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
            }
            return false;
        }
        static void AppendText(Element element, StringBuilder text)
        {
            if (element.TagName == "#text")
            {
                text.Append(element.Text);
                return;
            }
            if (element.TagName == "#comment")
            {
                return;
            }
            foreach (var child in element.Children())
            {
                AppendText(child, text);
            }
        }
        static string NormalizeLabel(string text)
        {
            var result = new StringBuilder();
            bool space = false;
            foreach (char character in text)
            {
                if (character == ' ' || character == '\t' || character == '\n' || character == '\r' || character == '\f')
                {
                    space = result.Length > 0;
                    continue;
                }
                if (space)
                {
                    result.Append(' ');
                }
                result.Append(character);
                space = false;
            }
            return result.ToString();
        }
        void Schedule(Action action)
        {
            Control.Document.RunAfterEvent(() =>
            {
                if (!disposed && Control.IsValid)
                {
                    action();
                }
            });
        }
        void OnPointerPressed(Vector2 position)
        {
            if (!disposed && open && Control.IsValid && !IsWithin(Control.Document.Pick(position), host))
            {
                Schedule(() => SetOpen(false));
            }
        }
        void OnUnloaded(URUI.Documents.Document document) => Dispose();
        void RequestRefresh()
        {
            if (disposed || !Control.IsValid)
            {
                return;
            }
            if (!Control.Document.Surface.IsExecutingCallback)
            {
                RefreshNow();
                return;
            }
            if (queued)
            {
                return;
            }
            queued = true;
            Control.Document.RunAfterEvent(deferredRefresh);
        }
        void FlushRefresh()
        {
            queued = false;
            if (!disposed)
            {
                RefreshNow();
            }
        }
        void RefreshNow()
        {
            if (!Control.IsValid || !trigger.IsValid || !label.IsValid || !popup.IsValid || !prototype.IsValid)
            {
                return;
            }
            var next = new List<Option>();
            foreach (var child in Control.Children())
            {
                if (child.TagName == "option")
                {
                    next.Add(new Option(child));
                }
            }
            bool changed = next.Count != options.Count;
            for (int i = 0; !changed && i < next.Count; i++)
            {
                changed = !next[i].Matches(options[i]);
            }
            if (changed)
            {
                ClearRows();
                options.Clear();
                options.AddRange(next);
                for (int i = 0; i < options.Count; i++)
                {
                    int index = i;
                    var row = (Button)prototype.Clone();
                    rows.Add(row);
                    row.Name = "";
                    row.RemoveAttribute("hidden");
                    row.SetAttribute("data-urui-option-index", i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (options[i].Hidden)
                    {
                        row.SetAttribute("hidden", "");
                    }
                    row.Text = options[i].Label;
                    row.SetEnabled(!options[i].Disabled);
                    popup.Add(row);
                    rowSubscriptions.Add(row.RegisterCallback<ClickEvent>(_ => Schedule(() => Select(index, true))));
                }
            }
            int selected = Control.SelectedIndex;
            string nextLabel = selected >= 0 && selected < options.Count ? options[selected].Label : "";
            if (renderedLabel != nextLabel)
            {
                label.Text = nextLabel;
                renderedLabel = nextLabel;
            }
            trigger.SetEnabled(Control.EnabledInHierarchy);
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].EnableInClassList("urui-selected", i == selected);
                rows[i].SetAttribute("aria-selected", i == selected ? "true" : "false");
            }
            if (!Control.EnabledInHierarchy)
            {
                SetOpen(false);
            }
        }
        bool UsesDefaultFade()
        {
            var style = Control.Document.Styles.GetComputed(popup);
            return style.Contains(FadeProperty) && style.GetNumber(FadeProperty.Name) > 0;
        }
        void StopTransition()
        {
            var previous = transition;
            transition = null;
            if (transitionSubscribed)
            {
                Control.Document.LayoutUpdated -= OnTransitionLayout;
                transitionSubscribed = false;
            }
            previous?.Dispose();
        }
        void BeginTransition(bool entering)
        {
            var name = entering ? "urui-select-open" : "urui-select-close";
            transition = Control.Document.Animations.Play(popup, entering ? OpeningClass : ClosingClass,
                AnimationGroup.Named(new[] { new AnimationTarget(popup, name) }));
            Control.Document.LayoutUpdated += OnTransitionLayout;
            transitionSubscribed = true;
        }
        void OnTransitionLayout(Document owner)
        {
            if (transition == null || transition.IsPlaying)
            {
                return;
            }
            var finished = transition;
            transition = null;
            owner.LayoutUpdated -= OnTransitionLayout;
            transitionSubscribed = false;
            if (finished.Finished.IsFaulted)
            {
                Debug.LogException(finished.Finished.Exception.GetBaseException());
            }
            // 新一次展开会先撤销旧句柄和回调；旧退场完成不能隐藏新列表。
            if (!open && popup.IsValid)
            {
                popup.SetAttribute("hidden", "");
            }
        }
        void SetOpen(bool value)
        {
            bool next = value && Control.EnabledInHierarchy;
            if (initialized && next == open)
            {
                return;
            }
            bool wasOpen = open;
            initialized = true;
            StopTransition();
            open = next;
            if (open)
            {
                popup.RemoveAttribute("hidden");
            }
            else
            {
                popup.SetAttribute("hidden", "");
            }
            trigger.SetAttribute("aria-expanded", open ? "true" : "false");
            if ((open || wasOpen) && UsesDefaultFade())
            {
                BeginTransition(open);
            }
        }
        void Select(int index, bool close)
        {
            if (!Control.EnabledInHierarchy || index < 0 || index >= options.Count || options[index].Disabled || options[index].Hidden)
            {
                return;
            }
            Control.SelectedIndex = index;
            if (close)
            {
                SetOpen(false);
                trigger.Focus();
            }
        }
        void OnKey(KeyDownEvent evt)
        {
            if (!Control.EnabledInHierarchy)
            {
                return;
            }
            if (evt.KeyCode == KeyCode.Escape && open)
            {
                evt.PreventDefault();
                evt.StopPropagation();
                Schedule(() =>
                {
                    SetOpen(false);
                    trigger.Focus();
                });
                return;
            }
            int direction = evt.KeyCode == KeyCode.DownArrow ? 1 : evt.KeyCode == KeyCode.UpArrow ? -1 : 0;
            bool first = evt.KeyCode == KeyCode.Home;
            bool last = evt.KeyCode == KeyCode.End;
            if (direction == 0 && !first && !last)
            {
                return;
            }
            evt.PreventDefault();
            evt.StopPropagation();
            int index = first ? 0 : last ? options.Count - 1 : Control.SelectedIndex + direction;
            int step = last ? -1 : direction == 0 ? 1 : direction;
            while (index >= 0 && index < options.Count && (options[index].Disabled || options[index].Hidden))
            {
                index += step;
            }
            int selected = index;
            Schedule(() => Select(selected, false));
        }
        void ClearRows()
        {
            foreach (var subscription in rowSubscriptions)
            {
                subscription.Dispose();
            }
            rowSubscriptions.Clear();
            foreach (var row in rows)
            {
                if (row.IsValid)
                {
                    row.Destroy();
                }
            }
            rows.Clear();
        }
        /// <summary>
        /// 解除订阅并销毁生成的选项行，保留标准控件和作者原型。
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            StopTransition();
            Control.PresentationChanged -= RequestRefresh;
            Control.Document.Unloaded -= OnUnloaded;
            input.PointerPressed -= OnPointerPressed;
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
            subscriptions.Clear();
            Control.Document.RunAfterEvent(() =>
            {
                ClearRows();
                open = false;
                if (popup.IsValid)
                {
                    popup.SetAttribute("hidden", "");
                }
                if (trigger.IsValid)
                {
                    trigger.SetAttribute("aria-expanded", "false");
                }
            });
        }
    }
}
