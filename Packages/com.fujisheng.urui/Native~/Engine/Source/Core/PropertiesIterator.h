#pragma once

#include "../../Include/URUI/Core/PropertyIdSet.h"
#include "../../Include/URUI/Core/Types.h"
#include "ElementStyle.h"
#include "../../Include/URUI/Core/PropertySpecification.h"
#include "PropertyShorthandDefinition.h"
#include <variant>

namespace URUI
{

    // An iterator for local properties defined on an element.
    // Note: Modifying the underlying style invalidates the iterator.
    class PlainPropertiesIterator
    {
    public:
        using ValueType = Pair<PropertyId, const Property&>;
        using PropertyIt = PropertyMap::const_iterator;
        PlainPropertiesIterator(const PropertyMap& inline_properties, const PropertyMap* definition_properties,
                                const PropertyMap* animations = nullptr, const PropertyMap* transitions = nullptr,
                                const PropertyMap* hints = nullptr)
        {
            const PropertyMap* candidates[5] = {&inline_properties, definition_properties, animations, transitions, hints};
            for (unsigned i = 0; i < 5; ++i)
            {
                if (candidates[i] && !candidates[i]->empty())
                {
                    maps[map_count] = candidates[i];
                    iterators[map_count] = candidates[i]->begin();
                    ++map_count;
                }
            }
            Proceed();
        }
        PlainPropertiesIterator& operator++()
        {
            ++iterators[phase];
            Proceed();
            return *this;
        }
        ValueType operator*() const
        {
            const auto& entry = *iterators[phase];
            const Property* winner = &entry.second;
            for (unsigned i = phase + 1; i < map_count; ++i)
            {
                const auto candidate = maps[i]->find(entry.first);
                if (candidate != maps[i]->end() && winner->priority < candidate->second.priority)
                {
                    winner = &candidate->second;
                }
            }
            return {entry.first, *winner};
        }
        bool AtEnd() const
        {
            return phase == map_count;
        }

    private:
        void Proceed()
        {
            if (map_count <= 1)
            {
                if (phase < map_count && iterators[phase] == maps[phase]->end())
                {
                    phase = map_count;
                }
                return;
            }
            for (; phase < map_count; ++phase)
            {
                for (; iterators[phase] != maps[phase]->end(); ++iterators[phase])
                {
                    const auto id = iterators[phase]->first;
                    if (!visited.Contains(id))
                    {
                        visited.Insert(id);
                        return;
                    }
                }
            }
        }
        const PropertyMap* maps[5];
        PropertyIt iterators[5];
        unsigned map_count = 0;
        unsigned phase = 0;
        PropertyIdSet visited;
    };

    class AllPropertiesIterator
    {
    public:
        using ValueType = Pair<String, const Property&>;
        using PlainIt = PropertyMap::const_iterator;
        using CustomIt = UnorderedMap<String, Property>::const_iterator;
        using ShorthandIt = UnorderedMap<ShorthandId, Property>::const_iterator;

        AllPropertiesIterator(const PropertyDictionary& inline_properties,
                              const PropertyDictionary* definition_properties, ElementStyle* filter_inherited_by,
                              const PropertySpecification& specification)
            : specification(specification), inline_properties{inline_properties},
              definition_properties{definition_properties}, filter_inherited_by{filter_inherited_by}
        {
            SetIterators(inline_properties);
            UpdatePhase();
            SkipFilter();
        }

        AllPropertiesIterator& operator++()
        {
            Increment();
            UpdatePhase();
            SkipFilter();
            return *this;
        }

        ValueType operator*() const
        {
            auto best = [](const Property& local, const Property* sheet) -> const Property&
            { return sheet && local.priority < sheet->priority ? *sheet : local; };
            if (it_plain != it_plain_end)
            {
                return {specification.GetPropertyName(it_plain->first),
                        best(it_plain->second,
                             definition_properties ? definition_properties->GetProperty(it_plain->first) : nullptr)};
            }
            if (it_custom != it_custom_end)
            {
                return {it_custom->first,
                        best(it_custom->second, definition_properties
                                                    ? definition_properties->GetCustomProperty(it_custom->first)
                                                    : nullptr)};
            }
            URUI_ASSERT(it_shorthand != it_shorthand_end);
            return {specification.GetShorthandName(it_shorthand->first),
                    best(it_shorthand->second, definition_properties
                                                   ? definition_properties->GetVarShorthand(it_shorthand->first)
                                                   : nullptr)};
        }

        bool AtEnd() const
        {
            return phase == Phase::End;
        }

    private:
        void Increment()
        {
            URUI_ASSERT(!AtEnd());
            if (it_plain != it_plain_end)
            {
                ++it_plain;
            }
            else if (it_custom != it_custom_end)
            {
                ++it_custom;
            }
            else if (it_shorthand != it_shorthand_end)
            {
                ++it_shorthand;
            }
        }

        void SkipFilter()
        {
            while (true)
            {
                bool increment = false;
                if (HasVisitedCurrent())
                {
                    increment = true;
                }
                else if (filter_inherited_by && !AtEnd())
                {
                    if (it_plain != it_plain_end)
                    {
                        increment = (filter_inherited_by->GetSpecifiedProperty(it_plain->first) != &operator*().second);
                    }
                    else if (it_custom != it_custom_end)
                    {
                        increment =
                            (filter_inherited_by->GetSpecifiedCustomProperty(it_custom->first) != &operator*().second);
                    }
                    else if (it_shorthand != it_shorthand_end)
                    {
                        increment =
                            (filter_inherited_by->GetSpecifiedShorthand(it_shorthand->first) != &operator*().second);
                    }
                }

                if (!increment && it_plain != it_plain_end)
                {
                    // Skip shorthand placeholders, they are defined directly by shorthands which are iterated over later.
                    increment = (it_plain->second.unit == Unit::SHORTHAND_PLACEHOLDER);
                }

                if (increment)
                {
                    Increment();
                    UpdatePhase();
                }
                else
                {
                    break;
                }
            }
        }

        void UpdatePhase()
        {
            if (it_plain != it_plain_end)
            {
                return;
            }
            if (it_custom != it_custom_end)
            {
                return;
            }
            if (it_shorthand != it_shorthand_end)
            {
                return;
            }

            if (phase == Phase::InlineProperties && definition_properties)
            {
                SetIterators(*definition_properties);
                phase = Phase::DefinitionProperties;
                UpdatePhase();
            }
            else
            {
                phase = Phase::End;
            }
        }

        bool HasVisitedCurrent() const
        {
            if (phase != Phase::DefinitionProperties)
            {
                return false;
            }

            if (it_plain != it_plain_end)
            {
                return inline_properties.GetProperties().count(it_plain->first);
            }
            if (it_custom != it_custom_end)
            {
                return inline_properties.GetCustomProperties().count(it_custom->first);
            }

            URUI_ASSERT(it_shorthand != it_shorthand_end);
            return inline_properties.GetVarShorthands().count(it_shorthand->first);
        }

        void SetIterators(const PropertyDictionary& dictionary)
        {
            it_plain = dictionary.GetProperties().begin();
            it_plain_end = dictionary.GetProperties().end();
            it_custom = dictionary.GetCustomProperties().begin();
            it_custom_end = dictionary.GetCustomProperties().end();
            it_shorthand = dictionary.GetVarShorthands().begin();
            it_shorthand_end = dictionary.GetVarShorthands().end();
        }

        enum class Phase
        {
            InlineProperties,
            DefinitionProperties,
            End
        };
        Phase phase = Phase::InlineProperties;

        const PropertyDictionary& inline_properties;
        const PropertyDictionary* definition_properties;
        const PropertySpecification& specification;
        ElementStyle* filter_inherited_by;

        PlainIt it_plain;
        PlainIt it_plain_end;
        CustomIt it_custom;
        CustomIt it_custom_end;
        ShorthandIt it_shorthand;
        ShorthandIt it_shorthand_end;
    };

}
