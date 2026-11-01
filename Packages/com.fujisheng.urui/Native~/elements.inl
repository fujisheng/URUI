// Document-owned DOM operations. All handles resolve through the same weak
// registry as rendering references; no native pointer crosses this boundary.
#include <sstream>
#include <cctype>

namespace urui_elements
{
    Rml::Element* Get(uint64_t token)
    {
        auto* element = element_references.Resolve(token, document);
        if (!element)
            error_message = "Invalid element identity or context thread";
        return element;
    }
    bool ClassToken(const char* value)
    {
        if (!value || !*value)
            return false;
        for (auto* p = value; *p; ++p)
            if (std::isspace(static_cast<unsigned char>(*p)))
                return false;
        return true;
    }
    std::vector<Rml::Element*> Query(Rml::Element* root, const char* name, const char* class_name, bool first = false)
    {
        std::vector<Rml::Element*> result;
        std::vector<Rml::Element*> pending{root};
        while (!pending.empty())
        {
            auto* current = pending.back();
            pending.pop_back();
            if ((!name || current->GetId() == name) && (!class_name || current->IsClassSet(class_name)))
            {
                result.push_back(current);
                if (first) break;
            }
            for (int i = current->GetNumChildren() - 1; i >= 0; --i)
                pending.push_back(current->GetChild(i));
        }
        return result;
    }
}

extern "C"
{
    uint64_t urui_element_root(void)
    try
    {
        return element_references.Track(document, document);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return 0;
    }
    uint64_t urui_element_create(const char* tag)
    try
    {
        if (!tag || !*tag)
            return 0;
        for (auto* p = tag; *p; ++p)
            if (!(std::isalnum(static_cast<unsigned char>(*p)) || *p == '-' || *p == '_' || *p == ':'))
                return 0;
        return element_references.Create(document, tag);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return 0;
    }
    uint64_t urui_element_parent(uint64_t token)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el) return UINT64_MAX;
        if (el == document || !el->GetParentNode()) return 0;
        const auto parent = element_references.Track(el->GetParentNode(), document);
        return parent ? parent : UINT64_MAX;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return UINT64_MAX;
    }
    int urui_element_child_count(uint64_t token)
    try
    {
        auto* el = urui_elements::Get(token);
        return el ? el->GetNumChildren() : -1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    uint64_t urui_element_child_at(uint64_t token, int index)
    try
    {
        auto* el = urui_elements::Get(token);
        return el && index >= 0 && index < el->GetNumChildren() ? element_references.Track(el->GetChild(index), document) : 0;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return 0;
    }
    uint64_t urui_element_find(uint64_t token, const char* name, const char* class_name)
    try
    {
        auto* root = urui_elements::Get(token);
        if (!root || (class_name && !urui_elements::ClassToken(class_name))) return UINT64_MAX;
        auto result = urui_elements::Query(root, name, class_name, true);
        if (result.empty()) return 0;
        const auto found = element_references.Track(result.front(), document);
        return found ? found : UINT64_MAX;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return UINT64_MAX;
    }
    int urui_element_query(uint64_t token, const char* name, const char* class_name, uint64_t* out, int capacity)
    try
    {
        auto* root = urui_elements::Get(token);
        if (!root || capacity < 0 || (!out && capacity > 0) || (class_name && !urui_elements::ClassToken(class_name)))
            return -1;
        auto matches = urui_elements::Query(root, name, class_name);
        if (matches.size() > ElementReferences::MaxOutstanding)
            return -2;
        const int count = static_cast<int>(matches.size());
        if (!out || capacity < count)
            return count;
        // Resolve every identity before exposing a result; no partial snapshot is returned.
        std::vector<uint64_t> tokens;
        tokens.reserve(matches.size());
        for (auto* el : matches)
        {
            auto identity = element_references.Track(el, document);
            if (!identity)
                return -2;
            tokens.push_back(identity);
        }
        std::copy(tokens.begin(), tokens.end(), out);
        return count;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    int urui_element_read(uint64_t token, int field, const char* name, char* out, int capacity, int* required)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !required || capacity < 0 || (!out && capacity > 0))
            return -1;
        *required = 0;
        Rml::String value;
        switch (field)
        {
        case 0: value = el->GetId(); break;
        case 1: value = el->GetTagName(); break;
        case 2:
            if (el->GetTagName() == "#text")
                value = static_cast<Rml::ElementText*>(el)->GetText();
            else
                for (int i = 0; i < el->GetNumChildren(); ++i)
                {
                    auto* child = el->GetChild(i);
                    if (child->GetTagName() != "#text")
                        return -2;
                    value += static_cast<Rml::ElementText*>(child)->GetText();
                }
            break;
        case 3:
            if (!name || !*name)
                return -1;
            if (!el->HasAttribute(name))
                return 0;
            value = el->GetAttribute<Rml::String>(name, "");
            break;
        default: return -1;
        }
        if (value.size() >= static_cast<size_t>(INT32_MAX))
            return -2;
        *required = static_cast<int>(value.size() + 1);
        if (!out || capacity < *required)
            return -3;
        std::memcpy(out, value.c_str(), value.size() + 1);
        return 1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    int urui_element_write(uint64_t token, int field, const char* name, const char* value)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !value)
            return -1;
        switch (field)
        {
        case 0: el->SetAttribute("id", Rml::String(value)); break;
        case 1:
            if (el->GetTagName() == "#text")
                static_cast<Rml::ElementText*>(el)->SetText(value);
            else
            {
                // Plain text is never parsed as markup, entities or a data expression.
                auto text = document->CreateTextNode(value);
                if (!text)
                    return -2;
                while (el->GetNumChildren())
                    el->RemoveChild(el->GetChild(0));
                if (*value)
                    el->AppendChild(std::move(text));
            }
            element_references.Prune();
            break;
        case 2:
            if (!name || !*name)
                return -1;
            el->SetAttribute(name, Rml::String(value));
            break;
        case 3:
            // Template parsing in RmlUi dereferences the owner document.
            if (el->GetTagName() == "#text" || !element_references.Attached(el, document))
            {
                error_message = "Attach the element before parsing inner RML";
                return -1;
            }
            el->SetInnerRML(value);
            element_references.Prune();
            break;
        case 4:
            if (!name || !*name)
                return -1;
            if (!el->SetProperty(name, value))
                return -2;
            break;
        default: return -1;
        }
        error_message.clear();
        return 1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    int urui_element_erase(uint64_t token, int field, const char* name)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !name || !*name || (field != 0 && field != 1))
            return -1;
        if (field == 0)
            el->RemoveAttribute(name);
        else
            el->RemoveProperty(name);
        return 1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    int urui_element_class(uint64_t token, const char* name, int action)
    try
    {
        auto* el = urui_elements::Get(token);
        if (!el || !urui_elements::ClassToken(name) || action < -1 || action > 1)
            return -1;
        if (action == -1)
            return el->IsClassSet(name) ? 1 : 0;
        if (el->IsClassSet(name) == (action == 1))
            return 1;
        std::istringstream source(el->GetClassNames());
        Rml::String result, current;
        while (source >> current)
            if (current != name)
            {
                if (!result.empty()) result += ' ';
                result += current;
            }
        if (action == 1)
        {
            if (!result.empty()) result += ' ';
            result += name;
        }
        // Update through the attribute path so class queries and attribute reads agree.
        el->SetAttribute("class", result);
        return 1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    int urui_element_insert(uint64_t parent, uint64_t child, int index)
    try
    {
        return element_references.Insert(urui_elements::Get(parent), urui_elements::Get(child), index, document);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    int urui_element_detach(uint64_t token)
    try
    {
        return element_references.Detach(urui_elements::Get(token), document);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    int urui_element_clear(uint64_t token)
    try
    {
        return element_references.ClearChildren(urui_elements::Get(token), document);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    int urui_element_destroy(uint64_t token)
    try
    {
        return element_references.Destroy(urui_elements::Get(token), document);
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    int urui_element_attached(uint64_t token)
    try
    {
        auto* el = urui_elements::Get(token);
        return el ? (element_references.Attached(el, document) ? 1 : 0) : -1;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
    int urui_element_pop_click(uint64_t* target)
    try
    {
        if (!target || !element_references.Owner())
            return -1;
        *target = 0;
        while (!clicks.empty())
        {
            auto current = clicks.front();
            clicks.pop_front();
            if (current && element_references.Attached(current.get(), document))
            {
                *target = element_references.Track(current.get(), document);
                return *target ? 1 : -2;
            }
        }
        return 0;
    }
    catch (...)
    {
        // C++ failures must not unwind across the managed/native ABI.
        try { error_message = "Native element operation failed"; } catch (...) {}
        return -1;
    }
}
