// Synchronous snapshots of URUI-owned state. No borrowed pointer escapes the ABI.
#include <URUI/Core/Elements/ElementFormControl.h>
#include <URUI/Core/Elements/ElementFormControlSelect.h>
#include <URUI/Core/StyleSheetSpecification.h>
#include "Engine/Source/Core/TransformState.h"
#include "Engine/Source/Core/TransformUtilities.h"
#include <URUI/Core/ComputedValues.h>
#include <URUI/Core/TransformPrimitive.h>

namespace urui_interaction
{
    void CancelCompositionFor(URUI::Element* element);
    bool Owner()
    {
        return context && std::this_thread::get_id() == context_thread;
    }

    int Copy(const URUI::String& value, char* out, int capacity, int* required)
    {
        if (!required || capacity < 0 || (!out && capacity > 0) || value.size() >= INT32_MAX)
            return -1;
        *required = static_cast<int>(value.size()) + 1;
        if (!out || capacity < *required)
            return -3;
        std::memcpy(out, value.c_str(), *required);
        return 1;
    }

    URUI::Vector2f ToWorld(URUI::Element* el, URUI::Vector2f point)
    {
        point += el->GetAbsoluteOffset(URUI::BoxArea::Border);
        if (auto* state = el->GetTransformState())
        {
            if (auto* transform = state->GetTransform())
            {
                const auto p = *transform * URUI::Vector4f(point.x, point.y, 0, 1);
                if (std::abs(p.w) > 1e-8f)
                    point = {p.x / p.w, p.y / p.w};
            }
        }
        return point;
    }

    bool Checkable(URUI::Element* el)
    {
        const auto type = el->GetAttribute<URUI::String>("type", "");
        return el->GetTagName() == "input" && (type == "checkbox" || type == "radio");
    }


}

extern "C"
{
    int urui_select_option_count(UruiDocumentHandle document_handle, uint64_t token)
    try
    {
        auto* el = urui_dynamic_cast<URUI::ElementFormControlSelect*>(urui_elements::Get(document_handle, token));
        return el ? el->GetNumOptions() : -1;
    }
    catch (...) { return -8; }

    int urui_select_option_read(UruiDocumentHandle document_handle, uint64_t token, int index, char* out, int capacity, int* required)
    try
    {
        auto* el = urui_dynamic_cast<URUI::ElementFormControlSelect*>(urui_elements::Get(document_handle, token));
        auto* option = el ? el->GetOption(index) : nullptr;
        if (!option) return -1;
        return urui_interaction::Copy(option->GetAttribute<URUI::String>("value", ""), out, capacity, required);
    }
    catch (...) { return -8; }

    int urui_select_option_add(UruiDocumentHandle document_handle, uint64_t token, const char* value)
    try
    {
        auto* el = urui_dynamic_cast<URUI::ElementFormControlSelect*>(urui_elements::Get(document_handle, token));
        if (!el || !value) return -1;
        return el->Add(safe_html(value), value) >= 0 ? 1 : -2;
    }
    catch (...) { return -8; }

    int urui_select_options_clear(UruiDocumentHandle document_handle, uint64_t token)
    try
    {
        auto* el = urui_dynamic_cast<URUI::ElementFormControlSelect*>(urui_elements::Get(document_handle, token));
        if (!el) return -1;
        el->RemoveAll(); return 1;
    }
    catch (...) { return -8; }

    int urui_select_selection(UruiDocumentHandle document_handle, uint64_t token, int index)
    try
    {
        auto* el = urui_dynamic_cast<URUI::ElementFormControlSelect*>(urui_elements::Get(document_handle, token));
        if (!el) return -2;
        if (index >= 0)
        {
            if (index >= el->GetNumOptions()) return -2;
            el->SetSelection(index);
        }
        return el->GetSelection();
    }
    catch (...) { return -8; }

    int urui_element_api_version(void) { return 2; }
    int urui_transform_api_version(void) { return 1; }

    int urui_element_local_transform(UruiDocumentHandle document_handle, uint64_t token, float* out, int capacity)
    try
    {
        auto* el = urui_elements::Get(document_handle, token);
        if (!el || !out || capacity < 32) return -1;
        auto local = URUI::Matrix4f::Identity();
        auto rotation = URUI::Matrix4f::Identity();
        bool separate_rotation = true;
        if (auto transform = el->GetComputedValues().transform())
        {
            for (int i = 0; i < transform->GetNumPrimitives(); ++i)
            {
                const auto& primitive = transform->GetPrimitive(i);
                const auto resolved = URUI::TransformUtilities::ResolveTransform(primitive, *el);
                local *= resolved;
                switch (primitive.type)
                {
                case URUI::TransformPrimitive::ROTATEX:
                case URUI::TransformPrimitive::ROTATEY:
                case URUI::TransformPrimitive::ROTATEZ:
                case URUI::TransformPrimitive::ROTATE2D:
                case URUI::TransformPrimitive::ROTATE3D:
                    rotation *= resolved;
                    break;
                case URUI::TransformPrimitive::MATRIX2D:
                case URUI::TransformPrimitive::MATRIX3D:
                case URUI::TransformPrimitive::DECOMPOSEDMATRIX4:
                    separate_rotation = false;
                    break;
                default: break;
                }
            }
        }
        // 显式逐列复制，避免依赖 URUI 编译时矩阵存储配置。
        for (int column = 0; column < 4; ++column)
        {
            const URUI::Vector4f a = local.GetColumn(column);
            const URUI::Vector4f b = rotation.GetColumn(column);
            for (int row = 0; row < 4; ++row)
            {
                out[column * 4 + row] = a[row];
                out[16 + column * 4 + row] = b[row];
            }
        }
        return separate_rotation ? 1 : 2;
    }
    catch (...) { return -8; }

    int urui_element_style_read(UruiDocumentHandle document_handle, uint64_t token, int local, const char* property, char* out, int capacity, int* required)
    try
    {
        auto* el = urui_elements::Get(document_handle, token);
        if (!el || !property || !*property || (local != 0 && local != 1)) return -1;
        urui_styles::RecordStringRead();
        URUI::String keyword;
        if (!local && urui_styles::ComputedKeyword(el, property, keyword))
            return urui_interaction::Copy(keyword, out, capacity, required);
        // GetLocalProperty also includes matched stylesheet definitions in URUI.
        // The public Style contract requires inline overrides only.
        const URUI::Property* value = nullptr;
        if (local)
        {
            if (std::strncmp(property, "--", 2) == 0 && el->GetPropertySpecification().GetPropertyId(property) == URUI::PropertyId::Invalid)
            {
                const auto& properties = el->GetStyle()->GetLocalStyleProperties().GetCustomProperties();
                auto found = properties.find(property);
                if (found != properties.end()) value = &found->second;
            }
            else
            {
                const auto& properties = el->GetLocalStyleProperties();
                auto found = properties.find(el->GetPropertySpecification().GetPropertyId(property));
                if (found != properties.end()) value = &found->second;
            }
        }
        else value = el->GetProperty(property);
        if (!value) { if (required) *required = 0; return 0; }
        return urui_interaction::Copy(value->ToString(), out, capacity, required);
    }
    catch (...) { return -8; }

    int urui_element_refresh_state(UruiDocumentHandle document_handle, uint64_t token, float* out, int capacity)
    try
    {
        if (!out || capacity < 4) return -1;
        auto* document = urui_elements::Document(document_handle);
        auto* element = urui_elements::Get(document_handle, token);
        if (!element) return 0;
        const auto position = element->GetRelativeOffset(URUI::BoxArea::Border);
        const auto size = element->GetBox().GetSize(URUI::BoxArea::Border);
        out[0] = position.x; out[1] = position.y; out[2] = size.x; out[3] = size.y;
        return element_references.Attached(element, document) ? 3 : 1;
    }
    catch (...) { return -8; }

    int urui_element_box(UruiDocumentHandle document_handle, uint64_t token, float* out, int capacity)
    try
    {
        auto* el = urui_elements::Get(document_handle, token);
        if (!el || !out || capacity < 16) return -1;
        const auto& box = el->GetBox();
        const auto layout = el->GetRelativeOffset(URUI::BoxArea::Border);
        const auto size = box.GetSize(URUI::BoxArea::Border);
        const auto content = box.GetPosition(URUI::BoxArea::Content) - box.GetPosition(URUI::BoxArea::Border);
        const auto content_size = box.GetSize(URUI::BoxArea::Content);
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

    int urui_element_viewport(UruiDocumentHandle document_handle, uint64_t token, float* out, int capacity)
    try
    {
        auto* el = urui_elements::Get(document_handle, token);
        if (!el || !out || capacity < 4) return -1;
        const auto& box = el->GetBox();
        const auto origin = box.GetPosition(URUI::BoxArea::Content) - box.GetPosition(URUI::BoxArea::Border);
        out[0] = origin.x; out[1] = origin.y;
        out[2] = std::max(0.f, el->GetClientWidth() - box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Left) - box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Right));
        out[3] = std::max(0.f, el->GetClientHeight() - box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Top) - box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Bottom));
        return 1;
    }
    catch (...) { return -8; }

    int urui_element_point(UruiDocumentHandle document_handle, uint64_t token, int to_world, float x, float y, float* out)
    try
    {
        auto* el = urui_elements::Get(document_handle, token);
        if (!el || !out || !std::isfinite(x) || !std::isfinite(y)) return -1;
        URUI::Vector2f point{x, y};
        if (to_world) point = urui_interaction::ToWorld(el, point);
        else
        {
            if (!el->Project(point)) return -2;
            point -= el->GetAbsoluteOffset(URUI::BoxArea::Border);
        }
        out[0] = point.x; out[1] = point.y;
        return 1;
    }
    catch (...) { return -8; }

    uint64_t urui_element_pick(UruiDocumentHandle document_handle, float x, float y)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!urui_interaction::Owner() || !document || !std::isfinite(x) || !std::isfinite(y)) return 0;
        auto* el = context->GetElementAtPoint({x, y});
        return el ? element_references.Track(el, document) : 0;
    }
    catch (...) { return 0; }

    int urui_element_focus(UruiDocumentHandle document_handle, uint64_t token, int focus)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        auto* el = urui_elements::Get(document_handle, token);
        if (!el) return -1;
        if (focus) return el->Focus(true) ? 1 : 0;
        // URUI Blur focuses the immediate parent; URUI must release focus outside
        // a disabled subtree instead of silently moving it to a disabled ancestor.
        if (context->GetFocusElement() == el) document->Focus();
        else el->Blur();
        return 1;
    }
    catch (...) { return -8; }

    uint64_t urui_focused_element(UruiDocumentHandle document_handle)
    try
    {
        auto* document = urui_elements::Document(document_handle);
        if (!urui_interaction::Owner() || !document) return 0;
        auto* focused = context->GetFocusElement();
        return focused && focused != document && focused->IsVisible(true) && !context->IsDocumentBlockedByModal(document)
            ? element_references.Track(focused, document) : 0;
    }
    catch (...) { return 0; }

    int urui_element_scroll(UruiDocumentHandle document_handle, uint64_t token, float x, float y)
    try
    {
        auto* el = urui_elements::Get(document_handle, token);
        if (!el || !std::isfinite(x) || !std::isfinite(y)) return -1;
        el->SetScrollLeft(x); el->SetScrollTop(y); return 1;
    }
    catch (...) { return -8; }

    int urui_control_value_read(UruiDocumentHandle document_handle, uint64_t token, char* out, int capacity, int* required)
    try
    {
        auto* el = urui_elements::Get(document_handle, token);
        if (!el) return -1;
        if (urui_interaction::Checkable(el))
            return urui_interaction::Copy(el->HasAttribute("checked") ? "true" : "false", out, capacity, required);
        auto* control = urui_dynamic_cast<URUI::ElementFormControl*>(el);
        if (!control) return -2;
        return urui_interaction::Copy(control->GetValue(), out, capacity, required);
    }
    catch (...) { return -8; }

    int urui_control_range_info(UruiDocumentHandle owner, uint64_t token, UruiRangeInfo* info)
    try {
        if (!info) return -1;
        *info = {};
        auto* element = urui_elements::Get(owner, token);
        if (!element) return -1;
        auto* input = urui_dynamic_cast<URUI::ElementFormControlInput*>(element);
        URUI::RangeControlInfo values{};
        if (!input || !input->GetRangeInfo(values)) return -2;
        *info = {values.minimum, values.maximum, values.step, values.step_base, values.flags};
        return 1;
    } catch (...) { return -8; }

    int urui_control_value_write(UruiDocumentHandle document_handle, uint64_t token, const char* value)
    try
    {
        auto* el = urui_elements::Get(document_handle, token);
        if (!el || !value) return -1;
        urui_interaction::CancelCompositionFor(el);
        if (urui_interaction::Checkable(el))
        {
            if (std::strcmp(value, "true") == 0) el->SetAttribute("checked", "");
            else if (std::strcmp(value, "false") == 0) el->RemoveAttribute("checked");
            else return -1;
            return 1;
        }
        auto* control = urui_dynamic_cast<URUI::ElementFormControl*>(el);
        if (!control) return -2;
        control->SetValue(value);
        return 1;
    }
    catch (...) { return -8; }
}
