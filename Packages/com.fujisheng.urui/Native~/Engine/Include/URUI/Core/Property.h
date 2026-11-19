#pragma once

#include "Header.h"
#include "CascadePriority.h"
#include "NumericValue.h"
#include "Unit.h"
#include "Variant.h"
#include <type_traits>

namespace URUI
{

    enum class CssWideKeyword
    {
        Inherit,
        Initial,
        Unset
    };
    class PropertyDefinition;

    struct URUI_CORE_API PropertySource
    {
        PropertySource(String path, int line_number, String rule_name)
            : path(std::move(path)), line_number(line_number), rule_name(std::move(rule_name))
        {
        }
        String path;
        int line_number;
        String rule_name;
    };

    class URUI_CORE_API Property
    {
    public:
        Property();
        template <typename PropertyType>
        Property(PropertyType value, Unit unit, CascadePriority priority = {})
            : value(value), unit(unit), priority(priority)
        {
            definition = nullptr;
            parser_index = -1;
        }
        template <typename EnumType, typename = typename std::enable_if_t<std::is_enum_v<EnumType>, EnumType>>
        Property(EnumType value) : value(static_cast<int>(value)), unit(Unit::KEYWORD)
        {
        }

        /// Get the value of the property as a string.
        String ToString() const;

        /// Get the value of the property as a numeric value, if applicable.
        NumericValue GetNumericValue() const;

        /// Templatised accessor.
        template <typename T> T Get() const
        {
            return value.Get<T>();
        }

        bool operator==(const Property& other) const
        {
            return unit == other.unit && value == other.value;
        }
        bool operator!=(const Property& other) const
        {
            return !(*this == other);
        }

        Variant value;
        Unit unit;
        CascadePriority priority;

        const PropertyDefinition* definition = nullptr;
        int parser_index = -1;

        SharedPtr<const PropertySource> source;
    };

}
