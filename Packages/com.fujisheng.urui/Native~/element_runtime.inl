// Synchronous snapshots of RmlUi-owned state. No borrowed pointer escapes the ABI.
#include <RmlUi/Core/Elements/ElementFormControl.h>
#include <RmlUi/Core/Elements/ElementFormControlSelect.h>
#include <RmlUi/Core/StyleSheetSpecification.h>
#include "ThirdParty/RmlUi/Source/Core/TransformState.h"

namespace urui_interaction
{
    void CancelCompositionFor(Rml::Element* element);
    bool Owner()
    {
        return context && std::this_thread::get_id() == context_thread;
    }

    int Copy(const Rml::String& value, char* out, int capacity, int* required)
    {
        if (!required || capacity < 0 || (!out && capacity > 0) || value.size() >= INT32_MAX)
            return -1;
        *required = static_cast<int>(value.size()) + 1;
        if (!out || capacity < *required)
            return -3;
        std::memcpy(out, value.c_str(), *required);
        return 1;
    }

    Rml::Vector2f ToWorld(Rml::Element* el, Rml::Vector2f point)
    {
        point += el->GetAbsoluteOffset(Rml::BoxArea::Border);
        if (auto* state = el->GetTransformState())
        {
            if (auto* transform = state->GetTransform())
            {
                const auto p = *transform * Rml::Vector4f(point.x, point.y, 0, 1);
                if (std::abs(p.w) > 1e-8f)
                    point = {p.x / p.w, p.y / p.w};
            }
        }
        return point;
    }

    bool Checkable(Rml::Element* el)
    {
        const auto type = el->GetAttribute<Rml::String>("type", "");
        return el->GetTagName() == "input" && (type == "checkbox" || type == "radio");
    }

    void WriteExactRangeValue(Rml::ElementFormControl* control, const char* value)
    {
        auto weak = control->GetObserverPtr();
        const float target = Rml::Variant(Rml::String(value)).Get(0.f);
        const float low = control->GetAttribute<float>("min", 0.f);
        const float distance = std::abs(target - low);
        if (!std::isfinite(distance) || distance == 0)
        {
            control->SetValue(value);
            return;
        }
        const bool had_step = control->HasAttribute("step");
        const auto previous = control->GetAttribute<Rml::String>("step", "");
        auto restore = [&]()
        {
            if (!weak) return;
            if (had_step) weak->SetAttribute("step", previous); else weak->RemoveAttribute("step");
        };
        // A reflected semantic step grid need not share the native widget's min anchor.
        // A power-of-two subdivision represents this one target without a second quantization.
        const float precision = std::ldexp(distance, -20);
        control->SetAttribute("step", precision > 0 ? precision : distance);
        try { control->SetValue(value); }
        catch (...) { restore(); throw; }
        restore();
    }
}

extern "C"
{
    int urui_select_option_count(uint64_t token)
    try
    {
        auto* el = rmlui_dynamic_cast<Rml::ElementFormControlSelect*>(urui_elements::Get(token));
        return el ? el->GetNumOptions() : -1;
    }
    catch (...) { return -8; }

    int urui_select_option_read(uint64_t token, int index, char* out, int capacity, int* required)
    try
    {
        auto* el = rmlui_dynamic_cast<Rml::ElementFormControlSelect*>(urui_elements::Get(token));
        auto* option = el ? el->GetOption(index) : nullptr;
        if (!option) return -1;
        return urui_interaction::Copy(option->GetAttribute<Rml::String>("value", ""), out, capacity, required);
    }
    catch (...) { return -8; }

    int urui_select_option_add(uint64_t token, const char* value)
    try
    {
        auto* el = rmlui_dynamic_cast<Rml::ElementFormControlSelect*>(urui_elements::Get(token));
        if (!el || !value) return -1;
        return el->Add(safe_rml(value), value) >= 0 ? 1 : -2;
    }
    catch (...) { return -8; }

    int urui_select_options_clear(uint64_t token)
    try
    {
        auto* el = rmlui_dynamic_cast<Rml::ElementFormControlSelect*>(urui_elements::Get(token));
        if (!el) return -1;
        el->RemoveAll(); return 1;
    }
    catch (...) { return -8; }

    int urui_select_selection(uint64_t token, int index)
    try
    {
        auto* el = rmlui_dynamic_cast<Rml::ElementFormControlSelect*>(urui_elements::Get(token));
        if (!el) return -2;
        if (index >= 0)
        {
            if (index >= el->GetNumOptions()) return -2;
            el->SetSelection(index);
        }
        return el->GetSelection();
    }
    catch (...) { return -8; }

    int urui_element_api_version(void) { return 1; }

    int urui_element_style_read(uint64_t token, int local, const char* property, char* out, int capacity, int* required)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !property || !*property || (local != 0 && local != 1)) return -1;
        // GetLocalProperty also includes matched stylesheet definitions in RmlUi.
        // The public Style contract requires inline overrides only.
        const Rml::Property* value = nullptr;
        if (local)
        {
            const auto& properties = el->GetLocalStyleProperties();
            auto found = properties.find(Rml::StyleSheetSpecification::GetPropertyId(property));
            if (found != properties.end()) value = &found->second;
        }
        else value = el->GetProperty(property);
        if (!value) { if (required) *required = 0; return 0; }
        return urui_interaction::Copy(value->ToString(), out, capacity, required);
    }
    catch (...) { return -8; }

    int urui_element_box(uint64_t token, float* out, int capacity)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !out || capacity < 16) return -1;
        const auto& box = el->GetBox();
        const auto layout = el->GetRelativeOffset(Rml::BoxArea::Border);
        const auto size = box.GetSize(Rml::BoxArea::Border);
        const auto content = box.GetPosition(Rml::BoxArea::Content) - box.GetPosition(Rml::BoxArea::Border);
        const auto content_size = box.GetSize(Rml::BoxArea::Content);
        auto a = urui_interaction::ToWorld(el, {0, 0});
        auto b = urui_interaction::ToWorld(el, {size.x, 0});
        auto c = urui_interaction::ToWorld(el, {0, size.y});
        auto d = urui_interaction::ToWorld(el, size);
        const float left = std::min({a.x, b.x, c.x, d.x});
        const float top = std::min({a.y, b.y, c.y, d.y});
        const float values[16] = {layout.x, layout.y, size.x, size.y, content.x, content.y, content_size.x, content_size.y,
            left, top, std::max({a.x, b.x, c.x, d.x}) - left, std::max({a.y, b.y, c.y, d.y}) - top,
            el->GetScrollLeft(), el->GetScrollTop(), el->GetScrollWidth(), el->GetScrollHeight()};
        std::copy(values, values + 16, out);
        return 1;
    }
    catch (...) { return -8; }

    int urui_element_viewport(uint64_t token, float* out, int capacity)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !out || capacity < 4) return -1;
        const auto& box = el->GetBox();
        const auto origin = box.GetPosition(Rml::BoxArea::Content) - box.GetPosition(Rml::BoxArea::Border);
        out[0] = origin.x; out[1] = origin.y;
        out[2] = std::max(0.f, el->GetClientWidth() - box.GetEdge(Rml::BoxArea::Padding, Rml::BoxEdge::Left) - box.GetEdge(Rml::BoxArea::Padding, Rml::BoxEdge::Right));
        out[3] = std::max(0.f, el->GetClientHeight() - box.GetEdge(Rml::BoxArea::Padding, Rml::BoxEdge::Top) - box.GetEdge(Rml::BoxArea::Padding, Rml::BoxEdge::Bottom));
        return 1;
    }
    catch (...) { return -8; }

    int urui_element_point(uint64_t token, int to_world, float x, float y, float* out)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !out || !std::isfinite(x) || !std::isfinite(y)) return -1;
        Rml::Vector2f point{x, y};
        if (to_world) point = urui_interaction::ToWorld(el, point);
        else
        {
            if (!el->Project(point)) return -2;
            point -= el->GetAbsoluteOffset(Rml::BoxArea::Border);
        }
        out[0] = point.x; out[1] = point.y;
        return 1;
    }
    catch (...) { return -8; }

    uint64_t urui_element_pick(float x, float y)
    try
    {
        if (!urui_interaction::Owner() || !document || !std::isfinite(x) || !std::isfinite(y)) return 0;
        auto* el = context->GetElementAtPoint({x, y});
        return el ? element_references.Track(el, document) : 0;
    }
    catch (...) { return 0; }

    int urui_element_focus(uint64_t token, int focus)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el) return -1;
        if (focus) return el->Focus(true) ? 1 : 0;
        // RmlUi Blur focuses the immediate parent; URUI must release focus outside
        // a disabled subtree instead of silently moving it to a disabled ancestor.
        if (context->GetFocusElement() == el) document->Focus();
        else el->Blur();
        return 1;
    }
    catch (...) { return -8; }

    uint64_t urui_focused_element(void)
    try
    {
        if (!urui_interaction::Owner() || !document) return 0;
        auto* focused = context->GetFocusElement();
        return focused && focused != document ? element_references.Track(focused, document) : 0;
    }
    catch (...) { return 0; }

    int urui_element_scroll(uint64_t token, float x, float y)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !std::isfinite(x) || !std::isfinite(y)) return -1;
        el->SetScrollLeft(x); el->SetScrollTop(y); return 1;
    }
    catch (...) { return -8; }

    int urui_control_value_read(uint64_t token, char* out, int capacity, int* required)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el) return -1;
        if (urui_interaction::Checkable(el))
            return urui_interaction::Copy(el->HasAttribute("checked") ? "true" : "false", out, capacity, required);
        auto* control = rmlui_dynamic_cast<Rml::ElementFormControl*>(el);
        if (!control) return -2;
        return urui_interaction::Copy(control->GetValue(), out, capacity, required);
    }
    catch (...) { return -8; }

    int urui_control_value_write(uint64_t token, const char* value)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !value) return -1;
        urui_interaction::CancelCompositionFor(el);
        if (urui_interaction::Checkable(el))
        {
            if (std::strcmp(value, "true") == 0) el->SetAttribute("checked", "");
            else if (std::strcmp(value, "false") == 0) el->RemoveAttribute("checked");
            else return -1;
            return 1;
        }
        auto* control = rmlui_dynamic_cast<Rml::ElementFormControl*>(el);
        if (!control) return -2;
        if (el->HasAttribute("data-urui-exact-range") && el->GetTagName() == "input" && el->GetAttribute<Rml::String>("type", "") == "range")
            urui_interaction::WriteExactRangeValue(control, value);
        else control->SetValue(value);
        return 1;
    }
    catch (...) { return -8; }
}
