#pragma once

#include "Factory.h"
#include "PropertyDictionary.h"
#include "Types.h"
#include "StyleTypes.h"
#include "Utilities.h"

namespace URUI
{

    class Decorator;
    class DecoratorInstancer;
    class StyleSheet;
    class StyleSheetNode;

    struct KeyframeBlock
    {
        KeyframeBlock(float normalized_time) : normalized_time(normalized_time)
        {
        }
        // [0, 1]
        float normalized_time;
        PropertyDictionary properties;
    };
    struct Keyframes
    {
        Vector<PropertyId> property_ids;
        Vector<KeyframeBlock> blocks;
    };
    using KeyframesMap = UnorderedMap<String, Keyframes>;

    struct FontFaceRule
    {
        String family;
        StringList sources;
        Style::FontStyle style = Style::FontStyle::Normal;
        Style::FontWeight weight = Style::FontWeight::Normal;
        int face_index = 0;
        bool fallback = false;
    };

    struct NamedDecorator
    {
        String type;
        DecoratorInstancer* instancer;
        PropertyDictionary properties;
    };
    using NamedDecoratorMap = UnorderedMap<String, NamedDecorator>;

    struct DecoratorDeclaration
    {
        String type;
        DecoratorInstancer* instancer;
        PropertyDictionary properties;
        BoxArea paint_area;
        size_t image_layer = 0;
    };
    struct DecoratorDeclarationList
    {
        Vector<DecoratorDeclaration> list;
        String value;
        PropertyId image_property = PropertyId::Invalid;
        size_t layer_count = 1;
    };
    struct FilterDeclaration
    {
        String type;
        FilterInstancer* instancer;
        PropertyDictionary properties;
    };
    struct FilterDeclarationList
    {
        Vector<FilterDeclaration> list;
        String value;
    };

    enum class MediaQueryModifier
    {
        None,
        // passes only if the query is false instead of true
        Not
    };

    struct MediaBlock
    {
        MediaBlock()
        {
        }
        MediaBlock(PropertyDictionary _properties, SharedPtr<StyleSheet> _stylesheet, MediaQueryModifier _modifier)
            : properties(std::move(_properties)), stylesheet(std::move(_stylesheet)), modifier(_modifier)
        {
        }

        // Media query properties
        PropertyDictionary properties;
        SharedPtr<StyleSheet> stylesheet;
        MediaQueryModifier modifier = MediaQueryModifier::None;
    };
    using MediaBlockList = Vector<MediaBlock>;

    /**
   StyleSheetIndex contains a cached index of all styled nodes for quick lookup when finding applicable style nodes for the current state of a given
   element.
 */
    struct StyleSheetIndex
    {
        using NodeList = Vector<const StyleSheetNode*>;
        using NodeIndex = UnorderedMap<size_t, NodeList>;

        // The following objects are given in prioritized order. Any nodes in the first object will not be contained in the next one and so on.
        NodeIndex ids;
        NodeIndex classes;
        NodeIndex tags;
        NodeList other;
    };
}

namespace std
{
    // Hash specialization for the node list, so it can be used as key in UnorderedMap.
    template <> struct hash<::URUI::StyleSheetIndex::NodeList>
    {
        size_t operator()(const ::URUI::StyleSheetIndex::NodeList& nodes) const noexcept
        {
            size_t seed = 0;
            for (const ::URUI::StyleSheetNode* node : nodes)
            {
                ::URUI::Utilities::HashCombine(seed, node);
            }
            return seed;
        }
    };
}
