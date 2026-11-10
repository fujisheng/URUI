using System;
using System.Collections.Generic;
using URUI.Elements;

namespace URUI.Controls
{
    // 默认作者皮肤由所在节点拥有，片段实例化、克隆和文档卸载都走正常元素生命周期。
    internal abstract class AuthoredControlSkin : Element
    {
        readonly List<IDisposable> controllers = new List<IDisposable>();
        internal bool HasSkin => controllers.Count != 0;
        protected AuthoredControlSkin(ElementContext context) : base(context) { }
        protected void Own(IDisposable controller) => controllers.Add(controller);
        protected abstract void Bind(bool strict);
        protected virtual void RefreshSkin() { }
        protected override void OnInitialize() => Bind(true);
        protected override void OnUpdate()
        {
            if (!HasSkin) Bind(false);
            if (HasSkin) RefreshSkin();
        }
        internal void RequireManualCreationAllowed()
        {
            if (HasSkin) throw new InvalidOperationException("此默认作者皮肤已经自动装配；手动皮肤请使用独立的普通作者容器。");
        }
        protected override void OnInvalidated()
        {
            foreach (var controller in controllers) controller.Dispose();
            controllers.Clear(); base.OnInvalidated();
        }
    }

    internal sealed class AuthoredSliderSkin : AuthoredControlSkin
    {
        internal AuthoredSliderSkin(ElementContext context) : base(context) { }
        protected override void Bind(bool strict)
        {
            var children = Hierarchy.Children();
            var fill = ControlMarkup.Find(children, "urui-slider-fill");
            var thumb = ControlMarkup.Find(children, "urui-slider-thumb");
            if (fill == null && thumb == null) return;
            Element control = null;
            foreach (var child in children) if (child is Slider || child is IntegerSlider)
            {
                if (control != null) throw new InvalidOperationException("urui-slider 只能有一个直接的 range 输入。");
                control = child;
            }
            if (fill == null || thumb == null || control == null)
            { if (strict) throw new InvalidOperationException("urui-slider 需要 range 输入、urui-slider-fill 和 urui-slider-thumb。"); return; }
            if (control is Slider slider) Own(new SliderVisual(slider, fill, thumb));
            else Own(new IntegerSliderVisual((IntegerSlider)control, fill, thumb));
        }
    }

    internal sealed class AuthoredDropdownSkin : AuthoredControlSkin
    {
        DropdownVisual visual;
        internal AuthoredDropdownSkin(ElementContext context) : base(context) { }
        protected override void Bind(bool strict)
        {
            var children = Hierarchy.Children();
            var trigger = ControlMarkup.Find(children, "urui-select-trigger");
            var popup = ControlMarkup.Find(children, "urui-select-options");
            var prototype = ControlMarkup.Find(children, "urui-select-option");
            if (trigger == null && popup == null && prototype == null) return;
            Dropdown control = null;
            foreach (var child in children) if (child is Dropdown dropdown)
            {
                if (control != null) throw new InvalidOperationException("urui-select 只能有一个直接的 select。");
                control = dropdown;
            }
            var label = trigger?.Q(className: "urui-select-value");
            if (control == null || !(trigger is Button button) || !(prototype is Button option) || popup == null || label == null)
            { if (strict) throw new InvalidOperationException("urui-select 需要 select、触发按钮、值标签、选项原型和列表。"); return; }
            visual = new DropdownVisual(control, button, label, popup, option); Own(visual);
        }
        protected override void RefreshSkin() => visual.Refresh();
    }

    internal sealed class AuthoredScrollSkin : AuthoredControlSkin
    {
        readonly HashSet<Element> tracks = new HashSet<Element>();
        readonly List<ScrollbarVisual> visuals = new List<ScrollbarVisual>();
        internal AuthoredScrollSkin(ElementContext context) : base(context) { }
        internal bool OwnsTrack(Element track) => tracks.Contains(track);
        bool InThisFrame(Element element)
        {
            for (var parent = element.Parent; parent != null; parent = parent.Parent)
            {
                if (ReferenceEquals(parent, this)) return true;
                if (parent is ScrollView || parent is AuthoredScrollSkin) return false;
            }
            return false;
        }
        protected override void Bind(bool strict)
        {
            var bars = Query<Element>(className: "urui-scrollbar").Where(InThisFrame).ToList();
            if (bars.Count == 0) return;
            var viewports = Query<ScrollView>().Where(element => InThisFrame(element)).ToList();
            foreach (var bar in bars)
            {
                var target = bar.GetAttribute("aria-controls");
                ScrollView viewport = null;
                foreach (var candidate in viewports)
                    if (string.IsNullOrEmpty(target) || candidate.Name == target)
                    {
                        if (viewport != null) throw new InvalidOperationException("滚动条的 aria-controls 必须唯一指向所属视口。");
                        viewport = candidate;
                    }
                var thumb = ControlMarkup.Find(bar.Hierarchy.Children(), "urui-scrollbar-thumb");
                if (viewport == null || thumb == null)
                { if (strict) throw new InvalidOperationException("urui-scroll 需要关联视口和作者滑钮。"); return; }
                var visual = new ScrollbarVisual(viewport, bar, thumb);
                visuals.Add(visual); tracks.Add(bar); Own(visual);
            }
        }
        protected override void RefreshSkin() { foreach (var visual in visuals) visual.Refresh(); }
    }
}
