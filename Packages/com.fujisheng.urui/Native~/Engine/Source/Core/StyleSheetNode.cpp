#include "StyleSheetNode.h"
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/StyleSheet.h"
#include "StyleSheetFactory.h"
#include "StyleSheetSelector.h"
#include <algorithm>
#include <tuple>

namespace URUI
{

    static size_t ChildSelectorHash(const CompoundSelector& selector)
    {
        size_t value = 0;
        auto add = [&](size_t item) { value ^= item + size_t(0x9e3779b9) + (value << 6) + (value >> 2); };
        auto text = [&](const String& item) { add(Hash<String>{}(item)); };
        auto list = [&](const StringList& items)
        {
            add(items.size());
            for (const auto& item : items)
            {
                text(item);
            }
        };
        text(selector.tag);
        text(selector.id);
        list(selector.additional_ids);
        list(selector.class_names);
        list(selector.pseudo_class_names);
        add(selector.attributes.size());
        for (const auto& attribute : selector.attributes)
        {
            add(size_t(attribute.type));
            text(attribute.name);
            text(attribute.value);
        }
        add(selector.structural_selectors.size());
        for (const auto& structural : selector.structural_selectors)
        {
            add(size_t(structural.type));
            add(size_t(structural.a));
            add(size_t(structural.b));
        }
        // 不扫描嵌套选择器树；候选命中后仍以完整相等比较为准。
        add(size_t(selector.combinator));
        return value;
    }

    static inline bool IsTextElement(const Element* element)
    {
        return !element->GetTagName().empty() && element->GetTagName()[0] == '#';
    }

    StyleSheetNode::StyleSheetNode()
    {
        CalculateAndSetSpecificity();
    }

    StyleSheetNode::StyleSheetNode(StyleSheetNode* parent, const CompoundSelector& selector)
        : parent(parent), selector(selector)
    {
        CalculateAndSetSpecificity();
    }

    StyleSheetNode::StyleSheetNode(StyleSheetNode* parent, CompoundSelector&& selector)
        : parent(parent), selector(std::move(selector))
    {
        CalculateAndSetSpecificity();
    }

    StyleSheetNode* StyleSheetNode::GetOrCreateChildNode(const CompoundSelector& other)
    {
        if (auto* found = FindChild(other))
        {
            return found;
        }
        return AppendChild(MakeUnique<StyleSheetNode>(this, other));
    }

    StyleSheetNode* StyleSheetNode::GetOrCreateChildNode(CompoundSelector&& other)
    {
        if (auto* found = FindChild(other))
        {
            return found;
        }
        return AppendChild(MakeUnique<StyleSheetNode>(this, std::move(other)));
    }

    StyleSheetNode* StyleSheetNode::FindChild(const CompoundSelector& other)
    {
        if (children.size() < 16)
        {
            for (const auto& child : children)
            {
                if (child->selector == other)
                {
                    return child.get();
                }
            }
            return nullptr;
        }
        if (!child_index || indexed_children != children.size())
        {
            auto next = MakeUnique<ChildIndex>();
            next->reserve(children.size());
            for (size_t index = 0; index < children.size(); index++)
            {
                next->emplace(ChildSelectorHash(children[index]->selector), index);
            }
            child_index = std::move(next);
            indexed_children = children.size();
        }
        const auto candidates = child_index->equal_range(ChildSelectorHash(other));
        size_t selected = children.size();
        for (auto it = candidates.first; it != candidates.second; ++it)
        {
            if (it->second < selected && children[it->second]->selector == other)
            {
                selected = it->second;
            }
        }
        // 即便输入树带有重复要求，也与原有遍历一样选择最早的子节点。
        return selected < children.size() ? children[selected].get() : nullptr;
    }

    StyleSheetNode* StyleSheetNode::AppendChild(UniquePtr<StyleSheetNode> child)
    {
        auto* result = child.get();
        children.push_back(std::move(child));
        try
        {
            if (child_index)
            {
                child_index->emplace(ChildSelectorHash(result->selector), children.size() - 1);
                indexed_children = children.size();
            }
        }
        catch (...)
        {
            // 索引扩容失败时同时撤销节点，避免悬空候选或半次插入。
            child_index.reset();
            indexed_children = 0;
            children.pop_back();
            throw;
        }
        return result;
    }

    void StyleSheetNode::MergeHierarchy(StyleSheetNode* node, uint64_t source_order_offset)
    {
        URUI_ZoneScoped;

        // Merge the other node's properties into ours.
        properties.Merge(node->properties, source_order_offset);

        for (const auto& other_child : node->children)
        {
            StyleSheetNode* local_node = GetOrCreateChildNode(other_child->selector);
            local_node->MergeHierarchy(other_child.get(), source_order_offset);
        }
    }

    UniquePtr<StyleSheetNode> StyleSheetNode::DeepCopy(StyleSheetNode* in_parent) const
    {
        URUI_ZoneScoped;

        auto node = MakeUnique<StyleSheetNode>(in_parent, selector);

        node->properties = properties;
        node->children.resize(children.size());

        for (size_t i = 0; i < children.size(); i++)
        {
            node->children[i] = children[i]->DeepCopy(node.get());
        }

        return node;
    }

    namespace
    {
        void CollectSelectorDependencies(const CompoundSelector& selector, StyleSheetIndex& index, bool is_non_subject);

        void CollectTreeDependencies(const StyleSheetNode* node, StyleSheetIndex& index, bool is_non_subject)
        {
            if (!node)
            {
                return;
            }
            CollectSelectorDependencies(node->GetSelector(), index, is_non_subject);
            for (const auto& child : node->GetChildren())
            {
                CollectTreeDependencies(child.get(), index, is_non_subject);
            }
        }

        void CollectSelectorDependencies(const CompoundSelector& selector, StyleSheetIndex& index, bool is_non_subject)
        {
            if (selector.combinator == SelectorCombinator::NextSibling ||
                selector.combinator == SelectorCombinator::SubsequentSibling)
            {
                index.has_sibling_combinator = true;
            }
            for (const String& pseudo : selector.pseudo_class_names)
            {
                index.pseudo_classes_any.insert(pseudo);
                if (is_non_subject)
                {
                    index.pseudo_classes_non_subject.insert(pseudo);
                }
            }
            for (const StructuralSelector& structural : selector.structural_selectors)
            {
                if (structural.selector_tree && structural.selector_tree->root)
                {
                    CollectTreeDependencies(structural.selector_tree->root.get(), index, is_non_subject);
                }
            }
        }
    }

    void StyleSheetNode::BuildIndex(StyleSheetIndex& styled_node_index) const
    {
        const bool is_non_subject = !children.empty();
        CollectSelectorDependencies(selector, styled_node_index, is_non_subject);

        // If this has properties defined, then we insert it into the styled node index.
        if (!properties.Empty())
        {
            auto IndexInsertNode =
                [](StyleSheetIndex::NodeIndex& node_index, const String& key, const StyleSheetNode* node)
            {
                node_index[Hash<String>()(key)].push_back(node);
            };

            // Add this node to the appropriate index for looking up applicable nodes later. Prioritize the most unique requirement first and the most
            // general requirement last. This way we are able to rule out as many nodes as possible as quickly as possible.
            if (!selector.id.empty())
            {
                IndexInsertNode(styled_node_index.ids, selector.id, this);
            }
            else if (!selector.class_names.empty())
            {
                // @performance Right now we just use the first class for simplicity. Later we may want to devise a better strategy to try to add the
                // class with the most unique name. For example by adding the class from this node's list that has the fewest existing matches.
                IndexInsertNode(styled_node_index.classes, selector.class_names.front(), this);
            }
            else if (!selector.tag.empty())
            {
                IndexInsertNode(styled_node_index.tags, selector.tag, this);
            }
            else if (!selector.attributes.empty())
            {
                styled_node_index.other_attr.push_back(this);
            }
            else
            {
                styled_node_index.other.push_back(this);
            }
        }

        for (auto& child : children)
        {
            child->BuildIndex(styled_node_index);
        }
    }

    void StyleSheetNode::SetOrigin(CssOrigin origin)
    {
        properties.SetOrigin(origin);
        for (auto& child : children)
        {
            child->SetOrigin(origin);
        }
    }

    Specificity StyleSheetNode::GetSpecificity() const
    {
        return specificity;
    }

    void StyleSheetNode::ImportProperties(const PropertyDictionary& _properties, uint64_t rule_source_order)
    {
        properties.Import(_properties, &specificity, rule_source_order);
    }

    const PropertyDictionary& StyleSheetNode::GetProperties() const
    {
        return properties;
    }

    bool StyleSheetNode::Match(const Element* element, const Element* scope) const
    {
        if (!selector.tag.empty() && selector.tag != element->GetTagName())
        {
            return false;
        }

        if (!selector.id.empty() && selector.id != element->GetId())
        {
            return false;
        }
        for (const auto& id : selector.additional_ids)
        {
            if (id != element->GetId())
            {
                return false;
            }
        }

        for (auto& name : selector.class_names)
        {
            if (!element->IsClassSet(name))
            {
                return false;
            }
        }

        for (auto& name : selector.pseudo_class_names)
        {
            if (!element->IsPseudoClassSet(name))
            {
                return false;
            }
        }

        if (!selector.attributes.empty() && !MatchAttributes(element))
        {
            return false;
        }

        if (!selector.structural_selectors.empty() && !MatchStructuralSelector(element, scope))
        {
            return false;
        }

        return true;
    }

    bool StyleSheetNode::MatchStructuralSelector(const Element* element, const Element* scope) const
    {
        for (auto& node_selector : selector.structural_selectors)
        {
            if (!IsSelectorApplicable(element, node_selector, scope))
            {
                return false;
            }
        }

        return true;
    }

    bool StyleSheetNode::MatchAttributes(const Element* element) const
    {
        for (const AttributeSelector& attribute : selector.attributes)
        {
            const Variant* variant = element->GetAttribute(attribute.name);
            if (!variant)
            {
                return false;
            }
            if (attribute.type == AttributeSelectorType::Always)
            {
                continue;
            }

            String buffer;
            const String* element_value_ptr = &buffer;
            if (variant->GetType() == Variant::STRING)
            {
                element_value_ptr = &variant->GetReference<String>();
            }
            else
            {
                variant->GetInto(buffer);
            }

            const String& element_value = *element_value_ptr;
            const String& css_value = attribute.value;

            auto BeginsWith = [](const String& target, const String& prefix)
            { return prefix.size() <= target.size() && std::equal(prefix.begin(), prefix.end(), target.begin()); };
            auto EndsWith = [](const String& target, const String& suffix)
            { return suffix.size() <= target.size() && std::equal(suffix.rbegin(), suffix.rend(), target.rbegin()); };

            switch (attribute.type)
            {
            case AttributeSelectorType::Always:
                break;
            case AttributeSelectorType::Equal:
                if (element_value != css_value)
                {
                    return false;
                }
                break;
            case AttributeSelectorType::InList:
            {
                bool found = false;
                for (size_t index = element_value.find(css_value); index != String::npos;
                     index = element_value.find(css_value, index + 1))
                {
                    const size_t index_right = index + css_value.size();
                    const bool whitespace_left = (index == 0 || element_value[index - 1] == ' ');
                    const bool whitespace_right =
                        (index_right == element_value.size() || element_value[index_right] == ' ');

                    if (whitespace_left && whitespace_right)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    return false;
                }
            }
            break;
            case AttributeSelectorType::BeginsWithThenHyphen:
                // Begins with 'css_value' followed by a hyphen, or matches exactly.
                if (!BeginsWith(element_value, css_value) ||
                    (element_value.size() != css_value.size() && element_value[css_value.size()] != '-'))
                {
                    return false;
                }
                break;
            case AttributeSelectorType::BeginsWith:
                if (!BeginsWith(element_value, css_value))
                {
                    return false;
                }
                break;
            case AttributeSelectorType::EndsWith:
                if (!EndsWith(element_value, css_value))
                {
                    return false;
                }
                break;
            case AttributeSelectorType::Contains:
                if (element_value.find(css_value) == String::npos)
                {
                    return false;
                }
                break;
            }
        }
        return true;
    }

    bool StyleSheetNode::TraverseMatch(const Element* element, const Element* scope) const
    {
        URUI_ASSERT(parent);
        if (!parent->parent)
        {
            return true;
        }

        switch (selector.combinator)
        {
        case SelectorCombinator::Descendant:
        case SelectorCombinator::Child:
        {
            // Try to match the next element parent. If it succeeds we continue on to the next node, otherwise we try an alternate path through the
            // hierarchy using the next element parent. Repeat until we run out of elements.
            const auto* document = element->GetOwnerDocument();
            for (element = element->GetParentNode(); element && element->GetOwnerDocument() == document;
                 element = element->GetParentNode())
            {
                if (parent->Match(element, scope) && parent->TraverseMatch(element, scope))
                {
                    return true;
                }
                // If the node has a child combinator we must match this first ancestor.
                else if (selector.combinator == SelectorCombinator::Child)
                {
                    return false;
                }
            }
        }
        break;
        case SelectorCombinator::NextSibling:
        case SelectorCombinator::SubsequentSibling:
        {
            Element* parent_element = element->GetParentNode();
            if (!parent_element || parent_element->GetOwnerDocument() != element->GetOwnerDocument())
            {
                return false;
            }

            const int preceding_sibling_index = [element, parent_element]
            {
                const int num_children = parent_element->GetNumChildren(true);
                for (int i = 0; i < num_children; i++)
                {
                    if (parent_element->GetChild(i) == element)
                    {
                        return i - 1;
                    }
                }
                return -1;
            }();

            // Try to match the previous sibling. If it succeeds we continue on to the next node, otherwise we try to again with its previous sibling.
            for (int i = preceding_sibling_index; i >= 0; i--)
            {
                element = parent_element->GetChild(i);

                // First check if our sibling is a text element and if so skip it. For the descendant/child combinator above we can omit this step since
                // text elements don't have children and thus any ancestor is not a text element.
                if (IsTextElement(element))
                {
                    continue;
                }
                else if (parent->Match(element, scope) && parent->TraverseMatch(element, scope))
                {
                    return true;
                }
                // If the node has a next-sibling combinator we must match this first sibling.
                else if (selector.combinator == SelectorCombinator::NextSibling)
                {
                    return false;
                }
            }
        }
        break;
        }

        // We have run out of element ancestors before we matched every node. Bail out.
        return false;
    }

    bool StyleSheetNode::IsApplicable(const Element* element, const Element* scope) const
    {
        // Determine whether the element matches the current node and its entire lineage. The entire hierarchy of the element's document will be
        // considered during the match as necessary.

        // We could in principle just call Match() here and then go on with the ancestor style nodes. Instead, we test the requirements of this node in a
        // particular order for performance reasons.
        for (const String& name : selector.pseudo_class_names)
        {
            if (!element->IsPseudoClassSet(name))
            {
                return false;
            }
        }

        if (!selector.tag.empty() && selector.tag != element->GetTagName())
        {
            return false;
        }

        for (const String& name : selector.class_names)
        {
            if (!element->IsClassSet(name))
            {
                return false;
            }
        }

        if (!selector.id.empty() && selector.id != element->GetId())
        {
            return false;
        }
        for (const auto& id : selector.additional_ids)
        {
            if (id != element->GetId())
            {
                return false;
            }
        }

        if (!selector.attributes.empty() && !MatchAttributes(element))
        {
            return false;
        }

        // Check the structural selector requirements last as they can be quite slow.
        if (!selector.structural_selectors.empty() && !MatchStructuralSelector(element, scope))
        {
            return false;
        }

        // Walk up through all our parent nodes, each one of them must be matched by some ancestor or sibling element.
        if (parent && !TraverseMatch(element, scope))
        {
            return false;
        }

        return true;
    }

    void StyleSheetNode::CalculateAndSetSpecificity()
    {
        // First calculate the specificity of this node alone.
        specificity = {};

        if (!selector.tag.empty())
        {
            specificity += SelectorSpecificity::Tag;
        }

        if (!selector.id.empty())
        {
            specificity.ids += 1 + uint32_t(selector.additional_ids.size());
        }

        specificity.classes += uint32_t(selector.class_names.size());
        specificity.classes += uint32_t(selector.attributes.size());
        specificity.classes += uint32_t(selector.pseudo_class_names.size());

        for (const StructuralSelector& selector : selector.structural_selectors)
        {
            specificity += selector.specificity;
        }

        // Then add our parent's specificity onto ours.
        if (parent)
        {
            specificity += parent->specificity;
        }
    }

}
