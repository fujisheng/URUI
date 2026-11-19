// Document-owned DOM operations. All handles resolve through the same weak
// registry as rendering references; no native pointer crosses this boundary.
#include <sstream>
#include <cctype>

namespace URUI::Elements
{
    int ReplaceHtml(URUI::Element& element, const char* source)
    {
        const auto* owner = element.GetOwnerDocument();
        const URUI::String url = owner ? owner->GetSourceURL() : "urui:fragment";
        auto parsed = URUI::ParseHtmlFragment(source, element.GetTagName(), url.empty() ? "urui:fragment" : url);
        if (!parsed)
        {
            error_message = parsed.diagnostics.empty() ? "Invalid HTML fragment" : parsed.diagnostics.front().message;
            return -2;
        }
        URUI::Vector<URUI::Diagnostic> diagnostics;
        if (!URUI::ReplaceInnerHtml(element, *parsed.document, diagnostics))
        {
            error_message = diagnostics.empty() ? "HTML fragment construction failed" : diagnostics.front().message;
            return -2;
        }
        error_message.clear();
        return 1;
    }

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
    URUI::ElementDocument* Document(URUIDocumentHandle owner)
    {
        if (!elementReferences.Owner() || !documentRegistry)
        {
            return nullptr;
        }
        auto* state = documentRegistry->Find(owner);
        return state ? state->Element() : nullptr;
    }
    URUI::Element* Get(URUIDocumentHandle owner, uint64_t token)
    {
        auto* document = Document(owner);
        auto* element = elementReferences.Resolve(token, document);
        if (!element)
        {
            error_message = "Invalid element identity or context thread";
        }
        return element;
    }
    bool ClassToken(const char* value)
    {
        if (!value || !*value)
        {
            return false;
        }
        for (auto* p = value; *p; ++p)
        {
            if (std::isspace(static_cast<unsigned char>(*p)))
            {
                return false;
            }
        }
        return true;
    }
    std::vector<URUI::Element*> Query(URUI::Element* root, const char* name, const char* class_name, bool first = false)
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
}

extern "C"
{
    uint64_t urui_element_create(URUIDocumentHandle document_handle, const char* tag)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!tag || !*tag)
        {
            return 0;
        }
        for (auto* p = tag; *p; ++p)
        {
            if (!(std::isalnum(static_cast<unsigned char>(*p)) || *p == '-' || *p == '_' || *p == ':'))
            {
                return 0;
            }
        }
        return elementReferences.Create(document, tag);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return 0;
    }
    uint64_t urui_element_clone(URUIDocumentHandle document_handle, uint64_t token)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (URUI::Interaction::callbackDepth)
        {
            return 0;
        }
        auto* source = URUI::Elements::Get(document_handle, token);
        return source ? elementReferences.Clone(source, document) : 0;
    }
    catch (...)
    {
        try
        {
            error_message = "Native template clone failed";
        }
        catch (...)
        {
        }
        return 0;
    }
    int urui_element_number(URUIDocumentHandle document_handle, uint64_t token, const char* property, float value,
                            int unit)
    try
    {
        if (!property || !*property || !std::isfinite(value) || unit < 0 || unit > 4 ||
            URUI::Interaction::callbackDepth)
        {
            return -1;
        }
        auto* el = URUI::Elements::Get(document_handle, token);
        if (!el)
        {
            return -1;
        }
        if (std::strncmp(property, "--", 2) == 0 &&
            el->GetPropertySpecification().GetPropertyId(property) == URUI::PropertyId::Invalid)
        {
            if (unit == 4)
            {
                return -1;
            }
            const auto units = unit == 0   ? URUI::Unit::NUMBER
                               : unit == 1 ? URUI::Unit::PX
                               : unit == 2 ? URUI::Unit::PERCENT
                                           : URUI::Unit::DP;
            el->GetStyle()->SetCustomProperty(property, URUI::Property(value, units));
            return 1;
        }
        const auto* definition = el->GetPropertySpecification().GetProperty(property);
        URUI::PropertyIdSet properties;
        if (definition)
        {
            properties.Insert(definition->GetId());
        }
        else
        {
            const auto shorthand = el->GetPropertySpecification().GetShorthandId(property);
            if (shorthand == URUI::ShorthandId::Invalid)
            {
                return -1;
            }
            properties = el->GetPropertySpecification().GetShorthandUnderlyingProperties(shorthand);
        }
        if (properties.Empty())
        {
            return -1;
        }
        for (const auto id : properties)
        {
            if (!URUI::Elements::AcceptNumber(el->GetPropertySpecification(), id, unit))
            {
                return -1;
            }
            if (unit == 4)
            {
                if (value < 0 || value > 1024 || std::floor(value) != value)
                {
                    return -1;
                }
                URUI::String keyword;
                URUI::Property parsed;
                const auto* def = el->GetPropertySpecification().GetProperty(id);
                // URUI writes keyword text but returns false even for a valid keyword.
                // Validate by parsing the result back into the same registered value.
                def->GetValue(keyword, URUI::Property(int(value), URUI::Unit::KEYWORD));
                if (!def->ParseValue(parsed, keyword) || parsed.unit != URUI::Unit::KEYWORD ||
                    parsed.Get<int>() != int(value))
                {
                    return -1;
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
                return -2;
            }
        }
        return 1;
    }
    catch (...)
    {
        try
        {
            error_message = "Native numeric value failed";
        }
        catch (...)
        {
        }
        return -2;
    }
    int urui_element_color(URUIDocumentHandle document_handle, uint64_t token, const char* property, int red, int green,
                           int blue, int alpha)
    try
    {
        if (!property || red < 0 || red > 255 || green < 0 || green > 255 || blue < 0 || blue > 255 || alpha < 0 ||
            alpha > 255 || URUI::Interaction::callbackDepth)
        {
            return -1;
        }
        auto* element = URUI::Elements::Get(document_handle, token);
        const auto* definition = element ? element->GetPropertySpecification().GetProperty(property) : nullptr;
        if (!element || !definition || definition->GetDefaultValue()->unit != URUI::Unit::COLOUR)
        {
            return -1;
        }
        return element->SetProperty(definition->GetId(),
                                    URUI::Property(URUI::Colourb(red, green, blue, alpha), URUI::Unit::COLOUR))
                   ? 1
                   : -2;
    }
    catch (...)
    {
        try
        {
            error_message = "Native color value failed";
        }
        catch (...)
        {
        }
        return -2;
    }
    int urui_element_trs(URUIDocumentHandle document_handle, uint64_t token, const float* values, int count)
    try
    {
        if (!values || count != 12 || URUI::Interaction::callbackDepth)
        {
            return -1;
        }
        for (int i = 0; i < count; ++i)
        {
            if (!std::isfinite(values[i]))
            {
                return -1;
            }
        }
        const float axisLength = values[3] * values[3] + values[4] * values[4] + values[5] * values[5];
        if (!std::isfinite(axisLength) || axisLength <= 0)
        {
            return -1;
        }
        auto* element = URUI::Elements::Get(document_handle, token);
        if (!element)
        {
            return -1;
        }
        URUI::Transform::PrimitiveList primitives;
        primitives.emplace_back(URUI::Transforms::Translate3D(values[0], values[1], values[2], URUI::Unit::PX));
        primitives.emplace_back(URUI::Transforms::Rotate3D(values[3], values[4], values[5], values[6]));
        primitives.emplace_back(URUI::Transforms::Scale3D(values[7], values[8], values[9]));
        if (!element->SetProperty(URUI::PropertyId::TransformOriginX,
                                  URUI::Property(values[10], URUI::Unit::PERCENT)) ||
            !element->SetProperty(URUI::PropertyId::TransformOriginY, URUI::Property(values[11], URUI::Unit::PERCENT)))
        {
            return -2;
        }
        return element->SetProperty(URUI::PropertyId::Transform, URUI::Transform::MakeProperty(std::move(primitives)))
                   ? 1
                   : -2;
    }
    catch (...)
    {
        try
        {
            error_message = "Native transform value failed";
        }
        catch (...)
        {
        }
        return -2;
    }
    uint64_t urui_element_parent(URUIDocumentHandle document_handle, uint64_t token)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        auto* el = URUI::Elements::Get(document_handle, token);
        if (!el)
        {
            return UINT64_MAX;
        }
        if (el == document || !el->GetParentNode())
        {
            return 0;
        }
        const auto parent = elementReferences.Track(el->GetParentNode(), document);
        return parent ? parent : UINT64_MAX;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return UINT64_MAX;
    }
    int urui_element_child_count(URUIDocumentHandle document_handle, uint64_t token)
    try
    {
        auto* el = URUI::Elements::Get(document_handle, token);
        return el ? el->GetNumChildren() : -1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    uint64_t urui_element_child_at(URUIDocumentHandle document_handle, uint64_t token, int index)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        auto* el = URUI::Elements::Get(document_handle, token);
        return el && index >= 0 && index < el->GetNumChildren() ? elementReferences.Track(el->GetChild(index), document)
                                                                : 0;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return 0;
    }
    uint64_t urui_element_find(URUIDocumentHandle document_handle, uint64_t token, const char* name,
                               const char* class_name)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        auto* root = URUI::Elements::Get(document_handle, token);
        if (!root || (class_name && !URUI::Elements::ClassToken(class_name)))
        {
            return UINT64_MAX;
        }
        auto result = URUI::Elements::Query(root, name, class_name, true);
        if (result.empty())
        {
            return 0;
        }
        const auto found = elementReferences.Track(result.front(), document);
        return found ? found : UINT64_MAX;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return UINT64_MAX;
    }
    int urui_element_query(URUIDocumentHandle document_handle, uint64_t token, const char* name, const char* class_name,
                           uint64_t* out, int capacity)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        auto* root = URUI::Elements::Get(document_handle, token);
        if (!root || capacity < 0 || (!out && capacity > 0) || (class_name && !URUI::Elements::ClassToken(class_name)))
        {
            return -1;
        }
        auto matches = URUI::Elements::Query(root, name, class_name);
        if (matches.size() > ElementReferences::MaxOutstanding)
        {
            return -2;
        }
        const int count = static_cast<int>(matches.size());
        if (!out || capacity < count)
        {
            return count;
        }
        // Resolve every identity before exposing a result; no partial snapshot is returned.
        std::vector<uint64_t> tokens;
        tokens.reserve(matches.size());
        for (auto* el : matches)
        {
            auto identity = elementReferences.Track(el, document);
            if (!identity)
            {
                return -2;
            }
            tokens.push_back(identity);
        }
        std::copy(tokens.begin(), tokens.end(), out);
        return count;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    int urui_element_read(URUIDocumentHandle document_handle, uint64_t token, int field, const char* name, char* out,
                          int capacity, int* required)
    try
    {
        auto* el = URUI::Elements::Get(document_handle, token);
        if (!el || !required || capacity < 0 || (!out && capacity > 0))
        {
            return -1;
        }
        *required = 0;
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
                        return -2;
                    }
                    value += static_cast<URUI::ElementText*>(child)->GetText();
                }
            }
            break;
        case 3:
            if (!name || !*name)
            {
                return -1;
            }
            if (std::strcmp(name, "class") == 0)
            {
                value = el->GetClassNames();
                if (value.empty() && !el->HasAttribute(name))
                {
                    return 0;
                }
            }
            else
            {
                if (!el->HasAttribute(name))
                {
                    return 0;
                }
                value = el->GetAttribute<URUI::String>(name, "");
            }
            break;
        case 4:
            value = el->GetInnerHTML();
            break;
        default:
            return -1;
        }
        if (value.size() >= static_cast<size_t>(INT32_MAX))
        {
            return -2;
        }
        *required = static_cast<int>(value.size() + 1);
        if (!out || capacity < *required)
        {
            return -3;
        }
        std::memcpy(out, value.c_str(), value.size() + 1);
        return 1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    int urui_element_write(URUIDocumentHandle document_handle, uint64_t token, int field, const char* name,
                           const char* value)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        auto* el = URUI::Elements::Get(document_handle, token);
        if (!el || !value)
        {
            return -1;
        }
        switch (field)
        {
        case 0:
            el->SetAttribute("id", URUI::String(value));
            break;
        case 1:
            if (el->GetTagName() == "#text")
            {
                static_cast<URUI::ElementText*>(el)->SetText(value);
            }
            else
            {
                // Plain text is never parsed as markup, entities or a data expression.
                auto text = document->CreateTextNode(value);
                if (!text)
                {
                    return -2;
                }
                while (el->GetNumChildren())
                {
                    el->RemoveChild(el->GetChild(0));
                }
                if (*value)
                {
                    el->AppendChild(std::move(text));
                }
            }
            elementReferences.Prune();
            break;
        case 2:
            if (!name || !*name)
            {
                return -1;
            }
            el->SetAttribute(name, URUI::String(value));
            break;
        case 3:
            if (el->GetTagName() == "#text" || el->GetTagName() == "#comment")
            {
                error_message = "Attach the element before parsing inner HTML";
                return -1;
            }
            if (URUI::Elements::ReplaceHtml(*el, value) != 1)
            {
                return -2;
            }
            elementReferences.Prune();
            break;
        case 4:
            if (!name || !*name)
            {
                return -1;
            }
            if (!el->SetProperty(name, value))
            {
                return -2;
            }
            break;
        default:
            return -1;
        }
        error_message.clear();
        return 1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    int urui_element_erase(URUIDocumentHandle document_handle, uint64_t token, int field, const char* name)
    try
    {
        auto* el = URUI::Elements::Get(document_handle, token);
        if (!el || !name || !*name || (field != 0 && field != 1))
        {
            return -1;
        }
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
        return 1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    int urui_element_class(URUIDocumentHandle document_handle, uint64_t token, const char* name, int action)
    try
    {
        auto* el = URUI::Elements::Get(document_handle, token);
        if (!el || !URUI::Elements::ClassToken(name) || action < -1 || action > 1)
        {
            return -1;
        }
        if (action == -1)
        {
            return el->IsClassSet(name) ? 1 : 0;
        }
        if (el->IsClassSet(name) == (action == 1))
        {
            return 1;
        }
        std::istringstream source(el->GetClassNames());
        URUI::String result;
        URUI::String current;
        while (source >> current)
        {
            if (current != name)
            {
                if (!result.empty())
                {
                    result += ' ';
                }
                result += current;
            }
        }
        if (action == 1)
        {
            if (!result.empty())
            {
                result += ' ';
            }
            result += name;
        }
        // Update through the attribute path so class queries and attribute reads agree.
        el->SetAttribute("class", result);
        return 1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    int urui_element_insert(URUIDocumentHandle document_handle, uint64_t parent, uint64_t child, int index)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        return elementReferences.Insert(URUI::Elements::Get(document_handle, parent),
                                        URUI::Elements::Get(document_handle, child), index, document);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    int urui_element_detach(URUIDocumentHandle document_handle, uint64_t token)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        return elementReferences.Detach(URUI::Elements::Get(document_handle, token), document);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    int urui_element_clear(URUIDocumentHandle document_handle, uint64_t token)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        return elementReferences.ClearChildren(URUI::Elements::Get(document_handle, token), document);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    int urui_element_destroy(URUIDocumentHandle document_handle, uint64_t token)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        return elementReferences.Destroy(URUI::Elements::Get(document_handle, token), document);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    int urui_element_attached(URUIDocumentHandle document_handle, uint64_t token)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        auto* el = URUI::Elements::Get(document_handle, token);
        return el ? (elementReferences.Attached(el, document) ? 1 : 0) : -1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
    int urui_element_pop_click(URUIDocumentHandle document_handle, uint64_t* target)
    try
    {
        auto* document = URUI::Elements::Document(document_handle);
        if (!target || !elementReferences.Owner())
        {
            return -1;
        }
        *target = 0;
        for (auto it = clicks.begin(); it != clicks.end();)
        {
            auto current = *it;
            if (!current)
            {
                it = clicks.erase(it);
                continue;
            }
            if (current->GetOwnerDocument() != document)
            {
                ++it;
                continue;
            }
            it = clicks.erase(it);
            if (current && elementReferences.Attached(current.get(), document))
            {
                *target = elementReferences.Track(current.get(), document);
                return *target ? 1 : -2;
            }
        }
        return 0;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try
        {
            error_message = "Native element operation failed";
        }
        catch (...)
        {
        }
        return -1;
    }
}
