#include "bridge_runtime.h"
#include "Engine/Source/Core/ElementStyle.h"
#include "Engine/Source/Core/HtmlBuilder.h"
#include "Engine/Source/Core/HtmlParser.h"
#include "Engine/Source/Core/TransformState.h"
#include "Engine/Source/Core/TransformUtilities.h"
#include <URUI/Core/ComputedValues.h>
#include <URUI/Core/Elements/ElementFormControl.h>
#include <URUI/Core/Elements/ElementFormControlInput.h>
#include <URUI/Core/Elements/ElementFormControlSelect.h>
#include <URUI/Core/PropertyDefinition.h>
#include <URUI/Core/StyleSheetSpecification.h>
#include <URUI/Core/Transform.h>
#include <URUI/Core/TransformPrimitive.h>
#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstring>
#include <vector>

#if defined(URUI_DEBUG)
#include "Debug/diagnostics.h"
using namespace URUI::Debug;
#endif

namespace URUI::Bridge
{
    namespace
    {
        bool AcceptNumber(const URUI::PropertySpecification& specification, URUI::PropertyId id, int unit)
        {
            const auto* definition = specification.GetProperty(id);
            if (!definition)
            {
                return false;
            }
            const auto initial = definition->GetDefaultValue()->unit;
            if (unit == 4)
            {
                return initial == URUI::Unit::KEYWORD;
            }
            if (unit == 0)
            {
                return initial == URUI::Unit::NUMBER || id == URUI::PropertyId::ZIndex;
            }
            if (initial == URUI::Unit::PX || initial == URUI::Unit::DP || initial == URUI::Unit::EM ||
                initial == URUI::Unit::REM || initial == URUI::Unit::PERCENT)
            {
                return true;
            }
            switch (id)
            {
            case URUI::PropertyId::BorderTopWidth:
            case URUI::PropertyId::BorderRightWidth:
            case URUI::PropertyId::BorderBottomWidth:
            case URUI::PropertyId::BorderLeftWidth:
                return unit == 1;
            case URUI::PropertyId::Width:
            case URUI::PropertyId::Height:
            case URUI::PropertyId::MinWidth:
            case URUI::PropertyId::MinHeight:
            case URUI::PropertyId::MaxWidth:
            case URUI::PropertyId::MaxHeight:
            case URUI::PropertyId::Left:
            case URUI::PropertyId::Right:
            case URUI::PropertyId::Top:
            case URUI::PropertyId::Bottom:
            case URUI::PropertyId::FontSize:
            case URUI::PropertyId::LineHeight:
                return true;
            default:
                return false;
            }
        }

        bool ClassToken(const char* value) noexcept
        {
            if (!value || !*value)
            {
                return false;
            }
            for (const auto* p = value; *p; ++p)
            {
                if (std::isspace(static_cast<unsigned char>(*p)))
                {
                    return false;
                }
            }
            return true;
        }

        std::vector<URUI::Element*> QuerySubtree(URUI::Element* root, const char* name, const char* class_name,
                                                 bool first = false)
        {
            std::vector<URUI::Element*> result;
            std::vector<URUI::Element*> pending{root};
            while (!pending.empty())
            {
                auto* current = pending.back();
                pending.pop_back();
                if ((!name || current->GetId() == name) && (!class_name || current->IsClassSet(class_name)))
                {
                    result.push_back(current);
                    if (first)
                    {
                        break;
                    }
                }
                for (int i = current->GetNumChildren() - 1; i >= 0; --i)
                {
                    pending.push_back(current->GetChild(i));
                }
            }
            return result;
        }

        URUI::Vector2f ToWorldPoint(URUI::Element* el, URUI::Vector2f point)
        {
            point += el->GetAbsoluteOffset(URUI::BoxArea::Border);
            if (auto* state = el->GetTransformState())
            {
                if (auto* transform = state->GetTransform())
                {
                    const auto p = *transform * URUI::Vector4f(point.x, point.y, 0, 1);
                    if (std::abs(p.w) > 1e-8f)
                    {
                        point = {p.x / p.w, p.y / p.w};
                    }
                }
            }
            return point;
        }

        bool IsCheckableControl(URUI::Element* el)
        {
            if (el->GetTagName() != "input")
            {
                return false;
            }
            const auto type = el->GetAttribute<URUI::String>("type", "");
            return type == "checkbox" || type == "radio";
        }

        int ComputeStateFlags(SurfaceContext& surface, URUI::ElementDocument* document, uint64_t token,
                              URUI::Element*& outElement)
        {
            outElement = nullptr;
            const int state = surface.elementReferences.ResolveState(token, document, outElement);
            if (!state || !outElement)
            {
                return 0;
            }
            int flags = URUI_STATE_VALID;
            bool visible = false;
            if (state & 2)
            {
                flags |= URUI_STATE_ATTACHED;
                visible = outElement->IsVisible(true);
            }
            for (auto* cur = outElement; cur; cur = cur->GetParentNode())
            {
                if (visible)
                {
                    if (const auto* disp = cur->GetLocalProperty(URUI::PropertyId::Display);
                        disp && disp->unit == URUI::Unit::KEYWORD && disp->Get<int>() == static_cast<int>(URUI::Style::Display::None))
                    {
                        visible = false;
                    }
                    else if (cur == outElement)
                    {
                        if (const auto* vis = cur->GetLocalProperty(URUI::PropertyId::Visibility);
                            vis && vis->unit == URUI::Unit::KEYWORD && vis->Get<int>() == static_cast<int>(URUI::Style::Visibility::Hidden))
                        {
                            visible = false;
                        }
                    }
                }
                if (cur->HasAttribute("disabled"))
                {
                    flags |= URUI_STATE_DISABLED_IN_HIERARCHY;
                }
                if (cur == document)
                {
                    break;
                }
            }
            if (visible)
            {
                flags |= URUI_STATE_VISIBLE_IN_HIERARCHY;
            }
            return flags;
        }

        void ComputeLocalTransformMatrices(URUI::Element* el, float* localOut, float* rotOut,
                                           bool& separateRotation)
        {
            auto local = URUI::Matrix4f::Identity();
            auto rotation = URUI::Matrix4f::Identity();
            separateRotation = true;
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
                        separateRotation = false;
                        break;
                    default:
                        break;
                    }
                }
            }
            for (int column = 0; column < 4; ++column)
            {
                const URUI::Vector4f a = local.GetColumn(column);
                const URUI::Vector4f b = rotation.GetColumn(column);
                for (int row = 0; row < 4; ++row)
                {
                    localOut[column * 4 + row] = a[row];
                    rotOut[column * 4 + row] = b[row];
                }
            }
        }
    }

    std::string SafeHtmlEscape(const char* src)
    {
        std::string out;
        if (!src)
        {
            return out;
        }
        for (const char* p = src; *p; ++p)
        {
            switch (*p)
            {
            case '&':
                out += "&amp;";
                break;
            case '<':
                out += "&lt;";
                break;
            case '>':
                out += "&gt;";
                break;
            default:
                out.push_back(*p);
                break;
            }
        }
        return out;
    }

    int ReplaceElementHtml(SurfaceContext& surface, URUI::Element& element, const char* source)
    {
        const auto* owner = element.GetOwnerDocument();
        const URUI::String url = owner ? owner->GetSourceURL() : "urui:fragment";
        auto parsed = URUI::ParseHtmlFragment(source, element.GetTagName(), url.empty() ? "urui:fragment" : url);
        if (!parsed)
        {
            surface.SetError(parsed.diagnostics.empty() ? "Invalid HTML fragment" : parsed.diagnostics.front().message);
            return URUI_ERR_VALIDATION;
        }
        URUI::Vector<URUI::Diagnostic> diagnostics;
        if (!URUI::ReplaceInnerHtml(element, *parsed.document, diagnostics))
        {
            surface.SetError(diagnostics.empty() ? "HTML fragment construction failed" : diagnostics.front().message);
            return URUI_ERR_NOT_READY;
        }
        surface.ClearError();
        return URUI_OK;
    }

    int ApplyTypedTransform(SurfaceContext& surface, URUI::Element* el, float x, float y, float sx, float sy)
    {
#if defined(URUI_DEBUG)
        if (typed_probe_mode == 1)
        {
            URUI::Property property;
            {
                ProfileScope scope(typed_probe.construct_ns);
                ++typed_probe.clock_pairs;
                URUI::Transform::PrimitiveList primitives;
                primitives.reserve(2);
                primitives.emplace_back(URUI::Transforms::Translate2D(x, y, URUI::Unit::PX));
                primitives.emplace_back(URUI::Transforms::Scale2D(sx, sy));
                property = URUI::Transform::MakeProperty(std::move(primitives));
            }
            bool ok = false;
            {
                ProfileScope scope(typed_probe.apply_ns);
                ++typed_probe.clock_pairs;
                ok = el->SetProperty(URUI::PropertyId::Transform, property);
            }
            if (!ok)
            {
                surface.SetError("URUI rejected typed transform");
                return URUI_ERR_NOT_READY;
            }
            surface.ClearError();
            return URUI_OK;
        }
#endif
        URUI::Transform::PrimitiveList primitives;
        primitives.reserve(2);
        primitives.emplace_back(URUI::Transforms::Translate2D(x, y, URUI::Unit::PX));
        primitives.emplace_back(URUI::Transforms::Scale2D(sx, sy));
        if (!el->SetProperty(URUI::PropertyId::Transform, URUI::Transform::MakeProperty(std::move(primitives))))
        {
            surface.SetError("URUI rejected typed transform");
            return URUI_ERR_NOT_READY;
        }
        surface.ClearError();
        return URUI_OK;
    }
}

using namespace URUI::Bridge;

extern "C"
{
    int urui_element_api_version(void) { return 4; }
    int urui_transform_api_version(void) { return 2; }

    uint64_t urui_element_ref_revision(URUISurfaceHandle surfaceHandle)
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, false, false) != URUI_OK)
        {
            return 0;
        }
        return surface->elementReferences.Revision();
    }

    int urui_element_refresh_stamp(URUISurfaceHandle surfaceHandle, URUIElementRefreshStamp* out)
    {
        if (!out)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *out = {};
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        *out = {surface->context->GetNativeRenderRevision(), surface->elementReferences.Revision()};
        return URUI_OK;
    }

    int urui_element_state_flags(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document)
        {
            return 0;
        }
        URUI::Element* element = nullptr;
        return ComputeStateFlags(*surface, document, token, element);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_spatial_snapshot(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                      URUIElementSpatialSnapshot* out)
    try
    {
        if (!out)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *out = {};
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document)
        {
            return URUI_ERR_NOT_READY;
        }
        URUI::Element* el = nullptr;
        const int flags = ComputeStateFlags(*surface, document, token, el);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        out->state_flags = flags;
        const auto& box = el->GetBox();
        const auto layout = el->GetRelativeOffset(URUI::BoxArea::Border);
        const auto size = box.GetSize(URUI::BoxArea::Border);
        const auto content = box.GetPosition(URUI::BoxArea::Content) - box.GetPosition(URUI::BoxArea::Border);
        const auto content_size = box.GetSize(URUI::BoxArea::Content);
        out->layout[0] = layout.x;
        out->layout[1] = layout.y;
        out->layout[2] = size.x;
        out->layout[3] = size.y;
        out->content[0] = content.x;
        out->content[1] = content.y;
        out->content[2] = content_size.x;
        out->content[3] = content_size.y;
        const auto a = ToWorldPoint(el, {0, 0});
        const auto b = ToWorldPoint(el, {size.x, 0});
        const auto c = ToWorldPoint(el, {0, size.y});
        const auto d = ToWorldPoint(el, size);
        const float left = std::min({a.x, b.x, c.x, d.x});
        const float top = std::min({a.y, b.y, c.y, d.y});
        out->world_bounds[0] = left;
        out->world_bounds[1] = top;
        out->world_bounds[2] = std::max({a.x, b.x, c.x, d.x}) - left;
        out->world_bounds[3] = std::max({a.y, b.y, c.y, d.y}) - top;
        out->scroll[0] = el->GetScrollLeft();
        out->scroll[1] = el->GetScrollTop();
        out->scroll[2] = el->GetScrollWidth();
        out->scroll[3] = el->GetScrollHeight();
        out->viewport[0] = content.x;
        out->viewport[1] = content.y;
        out->viewport[2] = std::max(0.f, el->GetClientWidth() - box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Left) -
                                             box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Right));
        out->viewport[3] = std::max(0.f, el->GetClientHeight() - box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Top) -
                                             box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Bottom));
        bool separateRotation = true;
        ComputeLocalTransformMatrices(el, out->local_matrix, out->rotation_matrix, separateRotation);
        out->separate_rotation = separateRotation ? 1 : 0;
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    uint64_t urui_element_create(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const char* tag)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document || !tag || !*tag)
        {
            return 0;
        }
        for (const auto* p = tag; *p; ++p)
        {
            if (!(std::isalnum(static_cast<unsigned char>(*p)) || *p == '-' || *p == '_' || *p == ':'))
            {
                return 0;
            }
        }
        ActiveSurfaceScope scope(surface);
        return surface->elementReferences.Create(document, tag);
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Native element operation failed");
        return 0;
    }

    uint64_t urui_element_clone(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, true) != URUI_OK)
        {
            return 0;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* source = ResolveElement(*surface, owner, token);
        if (!source)
        {
            return 0;
        }
        ActiveSurfaceScope scope(surface);
        return surface->elementReferences.Clone(source, document);
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Native template clone failed");
        return 0;
    }

    int urui_element_clone_template_content(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t source,
                                            uint64_t destination)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        const auto* prototype = urui_dynamic_cast<URUI::ElementTemplate*>(ResolveElement(*surface, owner, source));
        auto* container = ResolveElement(*surface, owner, destination);
        if (!prototype || !container || container->GetTagName() == "template" || container->GetNumChildren(true) != 0)
        {
            surface->SetError("Template cloning requires an empty container in the same document");
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        URUI::Vector<URUI::Diagnostic> diagnostics;
        if (!URUI::ReplaceInnerHtml(*container, prototype->GetContent(), diagnostics))
        {
            surface->SetError(diagnostics.empty() ? "Template construction failed" : diagnostics.front().message);
            return URUI_ERR_NOT_READY;
        }
        surface->ClearError();
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Native template construction failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_element_number(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                            const char* property, float value, int unit)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!property || !*property || !std::isfinite(value) || unit < 0 || unit > 4)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        if (std::strncmp(property, "--", 2) == 0 &&
            el->GetPropertySpecification().GetPropertyId(property) == URUI::PropertyId::Invalid)
        {
            if (unit == 4)
            {
                return URUI_ERR_INVALID_ARG;
            }
            const auto units = unit == 0   ? URUI::Unit::NUMBER
                               : unit == 1 ? URUI::Unit::PX
                               : unit == 2 ? URUI::Unit::PERCENT
                                           : URUI::Unit::DP;
            el->GetStyle()->SetCustomProperty(property, URUI::Property(value, units));
            return URUI_OK;
        }
        const auto& spec = el->GetPropertySpecification();
        const auto* definition = spec.GetProperty(property);
        URUI::PropertyIdSet properties;
        if (definition)
        {
            properties.Insert(definition->GetId());
        }
        else
        {
            const auto shorthand = spec.GetShorthandId(property);
            if (shorthand == URUI::ShorthandId::Invalid)
            {
                return URUI_ERR_INVALID_ARG;
            }
            properties = spec.GetShorthandUnderlyingProperties(shorthand);
        }
        if (properties.Empty())
        {
            return URUI_ERR_INVALID_ARG;
        }
        for (const auto id : properties)
        {
            if (!AcceptNumber(spec, id, unit))
            {
                return URUI_ERR_INVALID_ARG;
            }
            if (unit == 4)
            {
                if (value < 0 || value > 1024 || std::floor(value) != value)
                {
                    return URUI_ERR_INVALID_ARG;
                }
                const auto* def = spec.GetProperty(id);
                if (!def || !def->IsValidKeyword(int(value)))
                {
                    return URUI_ERR_INVALID_ARG;
                }
            }
        }
        const auto units = unit == 0   ? URUI::Unit::NUMBER
                           : unit == 1 ? URUI::Unit::PX
                           : unit == 2 ? URUI::Unit::PERCENT
                           : unit == 3 ? URUI::Unit::DP
                                       : URUI::Unit::KEYWORD;
        for (const auto id : properties)
        {
            if (!el->SetProperty(id, unit == 4 ? URUI::Property(int(value), units) : URUI::Property(value, units)))
            {
                return URUI_ERR_NOT_READY;
            }
        }
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Native numeric value failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_element_color(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                           const char* property, int red, int green, int blue, int alpha)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!property || red < 0 || red > 255 || green < 0 || green > 255 || blue < 0 || blue > 255 || alpha < 0 ||
            alpha > 255)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* element = ResolveElement(*surface, owner, token);
        if (!element)
        {
            return URUI_ERR_NOT_READY;
        }
        const auto* definition = element->GetPropertySpecification().GetProperty(property);
        if (!definition || definition->GetDefaultValue()->unit != URUI::Unit::COLOUR)
        {
            return URUI_ERR_INVALID_ARG;
        }
        ActiveSurfaceScope scope(surface);
        return element->SetProperty(definition->GetId(),
                                    URUI::Property(URUI::Colourb(red, green, blue, alpha), URUI::Unit::COLOUR))
                   ? URUI_OK
                   : URUI_ERR_NOT_READY;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Native color value failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_element_trs_ex(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                            const float* values, int count, int preserve_origin)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, true);
        if (status != URUI_OK)
        {
            return status;
        }
        const int requiredCount = preserve_origin ? 10 : 12;
        if (!values || count < requiredCount || count > 12 || (preserve_origin != 0 && preserve_origin != 1))
        {
            return URUI_ERR_INVALID_ARG;
        }
        for (int i = 0; i < requiredCount; ++i)
        {
            if (!std::isfinite(values[i]))
            {
                return URUI_ERR_INVALID_ARG;
            }
        }
        const float axisLength = values[3] * values[3] + values[4] * values[4] + values[5] * values[5];
        if (!std::isfinite(axisLength) || axisLength <= 0)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* element = ResolveElement(*surface, owner, token);
        if (!element)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        URUI::Transform::PrimitiveList primitives;
        primitives.reserve(3);
        primitives.emplace_back(URUI::Transforms::Translate3D(values[0], values[1], values[2], URUI::Unit::PX));
        primitives.emplace_back(URUI::Transforms::Rotate3D(values[3], values[4], values[5], values[6]));
        primitives.emplace_back(URUI::Transforms::Scale3D(values[7], values[8], values[9]));
        if (!preserve_origin)
        {
            if (!element->SetProperty(URUI::PropertyId::TransformOriginX,
                                      URUI::Property(values[10], URUI::Unit::PERCENT)) ||
                !element->SetProperty(URUI::PropertyId::TransformOriginY,
                                      URUI::Property(values[11], URUI::Unit::PERCENT)))
            {
                return URUI_ERR_NOT_READY;
            }
        }
        return element->SetProperty(URUI::PropertyId::Transform, URUI::Transform::MakeProperty(std::move(primitives)))
                   ? URUI_OK
                   : URUI_ERR_NOT_READY;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Native transform value failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_element_trs(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, const float* values,
                         int count)
    {
        if (count != 12)
        {
            return URUI_ERR_INVALID_ARG;
        }
        return urui_element_trs_ex(surfaceHandle, owner, token, values, count, 0);
    }

    uint64_t urui_element_parent(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return UINT64_MAX;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return UINT64_MAX;
        }
        if (el == document || !el->GetParentNode())
        {
            return 0;
        }
        const auto parent = surface->elementReferences.Track(el->GetParentNode(), document);
        return parent ? parent : UINT64_MAX;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Native element operation failed");
        return UINT64_MAX;
    }

    int urui_element_child_count(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* el = ResolveElement(*surface, owner, token);
        return el ? el->GetNumChildren() : URUI_ERR_NOT_READY;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    uint64_t urui_element_child_at(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int index)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* el = ResolveElement(*surface, owner, token);
        return el && index >= 0 && index < el->GetNumChildren()
                   ? surface->elementReferences.Track(el->GetChild(index), document)
                   : 0;
    }
    catch (...)
    {
        return 0;
    }

    uint64_t urui_element_find(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                               const char* name, const char* class_name)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return UINT64_MAX;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* root = ResolveElement(*surface, owner, token);
        if (!root || (class_name && !ClassToken(class_name)))
        {
            return UINT64_MAX;
        }
        auto result = QuerySubtree(root, name, class_name, true);
        if (result.empty())
        {
            return 0;
        }
        const auto found = surface->elementReferences.Track(result.front(), document);
        return found ? found : UINT64_MAX;
    }
    catch (...)
    {
        return UINT64_MAX;
    }

    int urui_element_query(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, const char* name,
                           const char* class_name, uint64_t* out, int capacity)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* root = ResolveElement(*surface, owner, token);
        if (!root)
        {
            return URUI_ERR_NOT_READY;
        }
        if (capacity < 0 || (!out && capacity > 0) || (class_name && !ClassToken(class_name)))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto matches = QuerySubtree(root, name, class_name);
        if (matches.size() > ElementReferences::MaxOutstanding)
        {
            return URUI_ERR_BUDGET;
        }
        const int count = static_cast<int>(matches.size());
        if (!out || capacity < count)
        {
            return count;
        }
        std::vector<uint64_t> tokens;
        tokens.reserve(matches.size());
        for (auto* el : matches)
        {
            const auto identity = surface->elementReferences.Track(el, document);
            if (!identity)
            {
                return URUI_ERR_BUDGET;
            }
            tokens.push_back(identity);
        }
        std::copy(tokens.begin(), tokens.end(), out);
        return count;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_read(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int field,
                          const char* name, char* out, int capacity, int* required)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!required || capacity < 0 || (!out && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        URUI::String value;
        switch (field)
        {
        case 0:
            value = el->GetId();
            break;
        case 1:
            value = el->GetTagName();
            break;
        case 2:
            if (el->GetTagName() == "#text")
            {
                value = static_cast<URUI::ElementText*>(el)->GetText();
            }
            else
            {
                for (int i = 0; i < el->GetNumChildren(); ++i)
                {
                    auto* child = el->GetChild(i);
                    if (child->GetTagName() != "#text")
                    {
                        return URUI_ERR_STRUCTURED;
                    }
                    value += static_cast<URUI::ElementText*>(child)->GetText();
                }
            }
            break;
        case 3:
            if (!name || !*name)
            {
                return URUI_ERR_INVALID_ARG;
            }
            if (std::strcmp(name, "class") == 0)
            {
                value = el->GetClassNames();
                if (value.empty() && !el->HasAttribute(name))
                {
                    return URUI_ABSENT;
                }
            }
            else
            {
                if (!el->HasAttribute(name))
                {
                    return URUI_ABSENT;
                }
                value = el->GetAttribute<URUI::String>(name, "");
            }
            break;
        case 4:
            value = el->GetInnerHTML();
            break;
        default:
            return URUI_ERR_INVALID_ARG;
        }
        return EngineRuntime::CopyString(value, out, capacity, required);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_write(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int field,
                           const char* name, const char* value)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!value)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* el = ResolveElement(*surface, owner, token);
        if (!el || !document)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        switch (field)
        {
        case 0:
            el->SetAttribute("id", URUI::String(value));
            break;
        case 1:
        {
            URUI_DEBUG_CODE(ProfileScope timer(profile.text_ns); ++profile.text_calls;)
            if (el->GetTagName() == "#text")
            {
                URUI_DEBUG_CODE(++profile.text_fast_calls;)
                static_cast<URUI::ElementText*>(el)->SetText(value);
            }
            else if (el->GetNumChildren() == 1 && el->GetChild(0)->GetTagName() == "#text")
            {
                URUI_DEBUG_CODE(++profile.text_fast_calls;)
                if (*value)
                {
                    static_cast<URUI::ElementText*>(el->GetChild(0))->SetText(value);
                }
                else
                {
                    el->RemoveChild(el->GetChild(0));
                }
            }
            else
            {
                URUI_DEBUG_CODE(++profile.text_fallback_calls;)
                auto text = *value ? document->CreateTextNode(value) : URUI::ElementPtr{};
                if (*value && !text)
                {
                    return URUI_ERR_NOT_READY;
                }
                for (int i = el->GetNumChildren() - 1; i >= 0; --i)
                {
                    el->RemoveChild(el->GetChild(i));
                }
                if (text)
                {
                    el->AppendChild(std::move(text));
                }
            }
            break;
        }
        case 2:
        {
            URUI_DEBUG_CODE(ProfileScope timer(api_profile.attribute_ns, api_profile_enabled);)
            URUI_DEBUG_CODE(if (api_profile_enabled)++ api_profile.attribute_calls;)
            if (!name || !*name)
            {
                return URUI_ERR_INVALID_ARG;
            }
            el->SetAttribute(name, URUI::String(value));
            break;
        }
        case 3:
            if (el->GetTagName() == "#text" || el->GetTagName() == "#comment")
            {
                surface->SetError("Attach the element before parsing inner HTML");
                return URUI_ERR_INVALID_ARG;
            }
            if (const int replaced = ReplaceElementHtml(*surface, *el, value); replaced != URUI_OK)
            {
                return replaced;
            }
            break;
        case 4:
        {
#if defined(URUI_DEBUG)
            URUI::LookupProbe::StyleScope lookup_style;
            ProfileScope timer(api_profile.style_ns, api_profile_enabled);
            if (api_profile_enabled)
            {
                ++api_profile.style_calls;
            }
            auto* sample = URUI::StyleProbe::mode ? &URUI::StyleProbe::total : nullptr;
            if (sample)
            {
                ++sample->calls;
            }
            struct ActiveGuard
            {
                URUIStyleProbe* previous;
                explicit ActiveGuard(URUIStyleProbe* p) : previous(URUI::StyleProbe::active)
                {
                    URUI::StyleProbe::active = p;
                }
                ~ActiveGuard()
                {
                    URUI::StyleProbe::active = previous;
                }
            } guard(sample);
#endif
            if (!name || !*name)
            {
                return URUI_ERR_INVALID_ARG;
            }
            if (!el->SetProperty(name, value))
            {
                surface->SetError(std::string("URUI rejected property: ") + name);
                return URUI_ERR_NOT_READY;
            }
            break;
        }
        default:
            return URUI_ERR_INVALID_ARG;
        }
        surface->ClearError();
        return URUI_OK;
    }
    catch (...)
    {
        EngineRuntime::SetThreadError("Native element operation failed");
        return URUI_ERR_INTERNAL;
    }

    int urui_element_erase(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int field,
                           const char* name)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!name || !*name || (field != 0 && field != 1))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        if (field == 0)
        {
            if (std::strcmp(name, "class") == 0)
            {
                el->SetClassNames("");
            }
            el->RemoveAttribute(name);
        }
        else
        {
            el->RemoveProperty(name);
        }
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_class(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, const char* name,
                           int action)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!ClassToken(name) || action < -1 || action > 1)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        if (action == -1)
        {
            return el->IsClassSet(name) ? URUI_OK : URUI_ABSENT;
        }
        ActiveSurfaceScope scope(surface);
        el->SetClassSyncAttribute(name, action == 1);
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_insert(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t parent, uint64_t child,
                            int index)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        ActiveSurfaceScope scope(surface);
        return surface->elementReferences.Insert(ResolveElement(*surface, owner, parent),
                                                 ResolveElement(*surface, owner, child), index, document);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_insert_before(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t parent,
                                   uint64_t child, uint64_t ref_child)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* refEl = ref_child ? ResolveElement(*surface, owner, ref_child) : nullptr;
        if (ref_child && !refEl)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        return surface->elementReferences.InsertBefore(ResolveElement(*surface, owner, parent),
                                                       ResolveElement(*surface, owner, child), refEl, document);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_detach(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        ActiveSurfaceScope scope(surface);
        return surface->elementReferences.Detach(ResolveElement(*surface, owner, token), document);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_clear(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        ActiveSurfaceScope scope(surface);
        return surface->elementReferences.ClearChildren(ResolveElement(*surface, owner, token), document);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_destroy(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        ActiveSurfaceScope scope(surface);
        return surface->elementReferences.Destroy(ResolveElement(*surface, owner, token), document);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_attached(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* el = ResolveElement(*surface, owner, token);
        return el ? (surface->elementReferences.Attached(el, document) ? URUI_OK : URUI_ABSENT) : URUI_ERR_NOT_READY;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_visible_in_hierarchy(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* e = ResolveElement(*surface, owner, token);
        return e && e->GetOwnerDocument() == document && e->IsVisible(true) ? URUI_OK : URUI_ABSENT;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_pop_click(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t* target)
    try
    {
        if (!target)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *target = 0;
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* state = surface->documentRegistry->Find(owner);
        auto* document = state ? state->Element() : nullptr;
        if (!document)
        {
            return URUI_ERR_NOT_READY;
        }
        auto& clicks = state->clicks;
        while (!clicks.empty())
        {
            auto current = clicks.front();
            clicks.pop_front();
            if (current && surface->elementReferences.Attached(current.get(), document))
            {
                *target = surface->elementReferences.Track(current.get(), document);
                return *target ? URUI_OK : URUI_ERR_BUDGET;
            }
        }
        return URUI_ABSENT;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    uint64_t urui_element_ref_acquire(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const char* id)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        auto* document = ResolveDocument(*surface, owner);
        return surface->elementReferences.Acquire(document, id);
    }
    catch (...)
    {
        return 0;
    }

    int urui_element_ref_valid(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        return surface->elementReferences.Resolve(token, document) ? URUI_OK : URUI_ABSENT;
    }

    int urui_element_ref_release(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document)
        {
            return URUI_ERR_NOT_READY;
        }
        if (surface->elementReferences.OwnerDocument(token) != document)
        {
            return URUI_ERR_INVALID_ARG;
        }
        return surface->elementReferences.Release(token);
    }

    int urui_element_ref_set_translate_scale_2d(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner,
                                                uint64_t token, float x, float y, float sx, float sy)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        URUI_DEBUG_CODE(ProfileScope api(api_profile.style_ns, api_profile_enabled);)
        URUI_DEBUG_CODE(if (api_profile_enabled)++ api_profile.style_calls;)
        URUI_DEBUG_CODE(const bool measure = typed_probe_mode == 1;)
        URUI_DEBUG_CODE(ProfileScope total(typed_probe.total_ns, measure);)
        URUI_DEBUG_CODE(if (typed_probe_mode) {
            ++typed_probe.calls;
            if (measure)
            {
                ++typed_probe.clock_pairs;
            }
        })
        if (!std::isfinite(x) || !std::isfinite(y) || !std::isfinite(sx) || !std::isfinite(sy))
        {
            URUI_DEBUG_CODE(if (typed_probe_mode)++ typed_probe.rejects;)
            surface->SetError("Invalid typed translate-scale mutation");
            return URUI_ERR_INVALID_ARG;
        }
        auto* document = ResolveDocument(*surface, owner);
        URUI::Element* el = nullptr;
        {
            URUI_DEBUG_CODE(ProfileScope scope(typed_probe.lookup_ns, measure);)
            URUI_DEBUG_CODE(if (measure)++ typed_probe.clock_pairs;)
            el = surface->elementReferences.Resolve(token, document);
        }
        if (!el)
        {
            URUI_DEBUG_CODE(if (typed_probe_mode)++ typed_probe.misses;)
            return URUI_ABSENT;
        }
        ActiveSurfaceScope scope(surface);
        return ApplyTypedTransform(*surface, el, x, y, sx, sy);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_local_transform(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                     float* out, int capacity)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!out || capacity < 32)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        bool separateRotation = true;
        ComputeLocalTransformMatrices(el, out, out + 16, separateRotation);
        return separateRotation ? 1 : 2;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_style_read(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int local,
                                const char* property, char* out, int capacity, int* required)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!property || !*property || (local != 0 && local != 1) || !required || capacity < 0 ||
            (!out && capacity > 0))
        {
            return URUI_ERR_INVALID_ARG;
        }
        *required = 0;
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        RecordStyleStringRead(surface->styles.get());
        URUI::String keyword;
        if (!local && ComputedStyleKeyword(surface->styles.get(), el, property, keyword))
        {
            return EngineRuntime::CopyString(keyword, out, capacity, required);
        }
        const URUI::Property* value = nullptr;
        if (local)
        {
            if (std::strncmp(property, "--", 2) == 0 &&
                el->GetPropertySpecification().GetPropertyId(property) == URUI::PropertyId::Invalid)
            {
                const auto& properties = el->GetStyle()->GetLocalStyleProperties().GetCustomProperties();
                const auto found = properties.find(property);
                if (found != properties.end())
                {
                    value = &found->second;
                }
            }
            else
            {
                const auto& properties = el->GetLocalStyleProperties();
                const auto found = properties.find(el->GetPropertySpecification().GetPropertyId(property));
                if (found != properties.end())
                {
                    value = &found->second;
                }
            }
        }
        else
        {
            value = el->GetProperty(property);
        }
        if (!value)
        {
            return URUI_ABSENT;
        }
        return EngineRuntime::CopyString(value->ToString(), out, capacity, required);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_refresh_state(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                   float* out, int capacity)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!out || capacity < 4)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* document = ResolveDocument(*surface, owner);
        URUI::Element* element = nullptr;
        const auto state = surface->elementReferences.ResolveState(token, document, element);
        if (!state)
        {
            return 0;
        }
        const auto position = element->GetRelativeOffset(URUI::BoxArea::Border);
        const auto size = element->GetBox().GetSize(URUI::BoxArea::Border);
        out[0] = position.x;
        out[1] = position.y;
        out[2] = size.x;
        out[3] = size.y;
        return state;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_refresh_states(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, const uint64_t* tokens,
                                    URUIElementRefreshState* out, uint32_t count)
    {
        if (count > 65536 || (count && (!tokens || !out)))
        {
            return URUI_ERR_INVALID_ARG;
        }
        if (count)
        {
            std::fill_n(out, count, URUIElementRefreshState{});
        }
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        try
        {
            auto* document = ResolveDocument(*surface, owner);
            if (!document)
            {
                return URUI_ERR_NOT_READY;
            }
            for (uint32_t n = 0; n < count; n++)
            {
                URUI::Element* element = nullptr;
                const auto state = surface->elementReferences.ResolveState(tokens[n], document, element);
                if (!state)
                {
                    continue;
                }
                const auto position = element->GetRelativeOffset(URUI::BoxArea::Border);
                const auto size = element->GetBox().GetSize(URUI::BoxArea::Border);
                out[n] = {state, {position.x, position.y, size.x, size.y}};
            }
            return URUI_OK;
        }
        catch (...)
        {
            if (count)
            {
                std::fill_n(out, count, URUIElementRefreshState{});
            }
            return URUI_ERR_INTERNAL;
        }
    }

    int urui_element_box(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, float* out,
                         int capacity)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!out || capacity < 16)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        const auto& box = el->GetBox();
        const auto layout = el->GetRelativeOffset(URUI::BoxArea::Border);
        const auto size = box.GetSize(URUI::BoxArea::Border);
        const auto content = box.GetPosition(URUI::BoxArea::Content) - box.GetPosition(URUI::BoxArea::Border);
        const auto content_size = box.GetSize(URUI::BoxArea::Content);
        const auto a = ToWorldPoint(el, {0, 0});
        const auto b = ToWorldPoint(el, {size.x, 0});
        const auto c = ToWorldPoint(el, {0, size.y});
        const auto d = ToWorldPoint(el, size);
        const float left = std::min({a.x, b.x, c.x, d.x});
        const float top = std::min({a.y, b.y, c.y, d.y});
        const float values[16] = {layout.x,
                                  layout.y,
                                  size.x,
                                  size.y,
                                  content.x,
                                  content.y,
                                  content_size.x,
                                  content_size.y,
                                  left,
                                  top,
                                  std::max({a.x, b.x, c.x, d.x}) - left,
                                  std::max({a.y, b.y, c.y, d.y}) - top,
                                  el->GetScrollLeft(),
                                  el->GetScrollTop(),
                                  el->GetScrollWidth(),
                                  el->GetScrollHeight()};
        std::copy(values, values + 16, out);
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_viewport(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, float* out,
                              int capacity)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!out || capacity < 4)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        const auto& box = el->GetBox();
        const auto origin = box.GetPosition(URUI::BoxArea::Content) - box.GetPosition(URUI::BoxArea::Border);
        out[0] = origin.x;
        out[1] = origin.y;
        out[2] = std::max(0.f, el->GetClientWidth() - box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Left) -
                                   box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Right));
        out[3] = std::max(0.f, el->GetClientHeight() - box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Top) -
                                   box.GetEdge(URUI::BoxArea::Padding, URUI::BoxEdge::Bottom));
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_element_point(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int to_world,
                           float x, float y, float* out)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!out || !std::isfinite(x) || !std::isfinite(y))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        URUI::Vector2f point{x, y};
        if (to_world)
        {
            point = ToWorldPoint(el, point);
        }
        else
        {
            if (!el->Project(point))
            {
                return URUI_ERR_NOT_READY;
            }
            point -= el->GetAbsoluteOffset(URUI::BoxArea::Border);
        }
        out[0] = point.x;
        out[1] = point.y;
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    uint64_t urui_element_pick(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, float x, float y)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK || !std::isfinite(x) || !std::isfinite(y))
        {
            return 0;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document)
        {
            return 0;
        }
        ActiveSurfaceScope scope(surface);
        auto* el = surface->context->GetElementAtPoint({x, y});
        return el ? surface->elementReferences.Track(el, document) : 0;
    }
    catch (...)
    {
        return 0;
    }

    int urui_element_focus(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int focus)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* document = ResolveDocument(*surface, owner);
        auto* el = ResolveElement(*surface, owner, token);
        if (!el || !document)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        if (focus)
        {
            return el->Focus(true) ? URUI_OK : URUI_ABSENT;
        }
        if (surface->context->GetFocusElement() == el)
        {
            document->Focus();
        }
        else
        {
            el->Blur();
        }
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    uint64_t urui_focused_element(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner)
    try
    {
        SurfaceContext* surface = nullptr;
        if (LookupSurface(surfaceHandle, surface, true, false) != URUI_OK)
        {
            return 0;
        }
        auto* document = ResolveDocument(*surface, owner);
        if (!document)
        {
            return 0;
        }
        auto* focused = surface->context->GetFocusElement();
        return focused && focused != document && focused->IsVisible(true) &&
                       !surface->context->IsDocumentBlockedByModal(document)
                   ? surface->elementReferences.Track(focused, document)
                   : 0;
    }
    catch (...)
    {
        return 0;
    }

    int urui_element_scroll(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, float x, float y)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!std::isfinite(x) || !std::isfinite(y))
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        el->SetScrollLeft(x);
        el->SetScrollTop(y);
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_control_value_read(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, char* out,
                                int capacity, int* required)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        if (IsCheckableControl(el))
        {
            return EngineRuntime::CopyString(el->HasAttribute("checked") ? "true" : "false", out, capacity, required);
        }
        auto* control = urui_dynamic_cast<URUI::ElementFormControl*>(el);
        if (!control)
        {
            return URUI_ERR_NOT_READY;
        }
        return EngineRuntime::CopyString(control->GetValue(), out, capacity, required);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_control_range_info(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                URUIRangeInfo* info)
    try
    {
        if (!info)
        {
            return URUI_ERR_INVALID_ARG;
        }
        *info = {};
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* element = ResolveElement(*surface, owner, token);
        if (!element)
        {
            return URUI_ERR_NOT_READY;
        }
        auto* input = urui_dynamic_cast<URUI::ElementFormControlInput*>(element);
        URUI::RangeControlInfo values{};
        if (!input || !input->GetRangeInfo(values))
        {
            return URUI_ERR_NOT_READY;
        }
        *info = {values.minimum, values.maximum, values.step, values.step_base, values.flags};
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_control_value_write(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                                 const char* value)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!value)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = ResolveElement(*surface, owner, token);
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        CancelCompositionFor(*surface, el);
        if (IsCheckableControl(el))
        {
            if (std::strcmp(value, "true") == 0)
            {
                el->SetAttribute("checked", "");
            }
            else if (std::strcmp(value, "false") == 0)
            {
                el->RemoveAttribute("checked");
            }
            else
            {
                return URUI_ERR_INVALID_ARG;
            }
            return URUI_OK;
        }
        auto* control = urui_dynamic_cast<URUI::ElementFormControl*>(el);
        if (!control)
        {
            return URUI_ERR_NOT_READY;
        }
        control->SetValue(value);
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_select_option_count(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* el = urui_dynamic_cast<URUI::ElementFormControlSelect*>(ResolveElement(*surface, owner, token));
        return el ? el->GetNumOptions() : URUI_ERR_NOT_READY;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_select_option_read(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int index,
                                char* out, int capacity, int* required)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* el = urui_dynamic_cast<URUI::ElementFormControlSelect*>(ResolveElement(*surface, owner, token));
        auto* option = el ? el->GetOption(index) : nullptr;
        if (!option)
        {
            return URUI_ERR_INVALID_ARG;
        }
        return EngineRuntime::CopyString(option->GetAttribute<URUI::String>("value", ""), out, capacity, required);
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_select_option_add(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token,
                               const char* value)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        if (!value)
        {
            return URUI_ERR_INVALID_ARG;
        }
        auto* el = urui_dynamic_cast<URUI::ElementFormControlSelect*>(ResolveElement(*surface, owner, token));
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        return el->Add(SafeHtmlEscape(value), value) >= 0 ? URUI_OK : URUI_ERR_NOT_READY;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_select_options_clear(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* el = urui_dynamic_cast<URUI::ElementFormControlSelect*>(ResolveElement(*surface, owner, token));
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        el->RemoveAll();
        return URUI_OK;
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }

    int urui_select_selection(URUISurfaceHandle surfaceHandle, URUIDocumentHandle owner, uint64_t token, int index)
    try
    {
        SurfaceContext* surface = nullptr;
        const int status = LookupSurface(surfaceHandle, surface, true, false);
        if (status != URUI_OK)
        {
            return status;
        }
        auto* el = urui_dynamic_cast<URUI::ElementFormControlSelect*>(ResolveElement(*surface, owner, token));
        if (!el)
        {
            return URUI_ERR_NOT_READY;
        }
        ActiveSurfaceScope scope(surface);
        if (index >= 0)
        {
            if (index >= el->GetNumOptions())
            {
                return URUI_ERR_INVALID_ARG;
            }
            el->SetSelection(index);
        }
        return el->GetSelection();
    }
    catch (...)
    {
        return URUI_ERR_INTERNAL;
    }
}
