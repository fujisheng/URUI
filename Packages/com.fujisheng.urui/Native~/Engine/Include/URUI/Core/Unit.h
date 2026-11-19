#pragma once

#include "Header.h"
#include <type_traits>

namespace URUI
{

    enum class Unit
    {
        UNKNOWN = 0,

        // Basic types.
        // generic keyword; fetch as <int>
        KEYWORD = 1 << 0,
        // generic string; fetch as <String>
        STRING = 1 << 1,
        // colour; fetch as <Colourb>
        COLOUR = 1 << 2,
        // ratio defined as x/y; fetch as <Vector2f>
        RATIO = 1 << 3,

        // Numeric values.
        // number unsuffixed; fetch as <float>
        NUMBER = 1 << 4,
        // number suffixed by '%'; fetch as <float>
        PERCENT = 1 << 5,
        // number suffixed by 'px'; fetch as <float>
        PX = 1 << 6,

        // Context-relative values.
        // density-independent pixel; number suffixed by 'dp'; fetch as <float>
        DP = 1 << 7,
        // viewport-width percentage; number suffixed by 'vw'; fetch as <float>
        VW = 1 << 8,
        // viewport-height percentage; number suffixed by 'vh'; fetch as <float>
        VH = 1 << 9,
        // dots per px unit; number suffixed by 'x'; fetch as <float>
        X = 1 << 10,

        // Font-relative values.
        // number suffixed by 'em'; fetch as <float>
        EM = 1 << 11,
        // number suffixed by 'rem'; fetch as <float>
        REM = 1 << 12,

        // Values based on pixels-per-inch.
        // number suffixed by 'in'; fetch as <float>
        INCH = 1 << 13,
        // number suffixed by 'cm'; fetch as <float>
        CM = 1 << 14,
        // number suffixed by 'mm'; fetch as <float>
        MM = 1 << 15,
        // number suffixed by 'pt'; fetch as <float>
        PT = 1 << 16,
        // number suffixed by 'pc'; fetch as <float>
        PC = 1 << 17,
        PPI_UNIT = INCH | CM | MM | PT | PC,

        // Angles.
        // number suffixed by 'deg'; fetch as <float>
        DEG = 1 << 18,
        // number suffixed by 'rad'; fetch as <float>
        RAD = 1 << 19,

        // Values tied to specific types.
        // transform; fetch as <TransformPtr>, may be empty
        TRANSFORM = 1 << 20,
        // transition; fetch as <TransitionList>
        TRANSITION = 1 << 21,
        // animation; fetch as <AnimationList>
        ANIMATION = 1 << 22,
        // decorator; fetch as <DecoratorsPtr>
        DECORATOR = 1 << 23,
        // decorator; fetch as <FiltersPtr>
        FILTER = 1 << 24,
        // --urui-font-effect; fetch as <FontEffectsPtr>
        FONTEFFECT = 1 << 25,
        // color stop list; fetch as <ColorStopList>
        COLORSTOPLIST = 1 << 26,
        // shadow list; fetch as <BoxShadowList>
        BOXSHADOWLIST = 1 << 27,

        // Custom properties.
        // substituted and resolved at compute time; fetch as <String>
        VAR_EXPRESSION = 1 << 28,
        SHORTHAND_PLACEHOLDER =
            // dependent on a shorthand that must be substituted and resolved at compute time; always empty
            1 << 29,

        // inherit/initial/unset; fetch as CssWideKeyword
        CSS_WIDE = 1 << 30,

        LENGTH = PX | DP | VW | VH | EM | REM | PPI_UNIT,
        LENGTH_PERCENT = LENGTH | PERCENT,
        NUMBER_PERCENT = NUMBER | PERCENT,
        NUMBER_LENGTH_PERCENT = NUMBER | LENGTH | PERCENT,
        DP_SCALABLE_LENGTH = DP | PPI_UNIT,
        ANGLE = DEG | RAD,
        NUMERIC = NUMBER_LENGTH_PERCENT | ANGLE | X
    };

    // Type alias hint for indicating that the value can contain multiple units OR-ed together.
    using Units = Unit;

    // OR operator for combining multiple units where applicable.
    inline Units operator|(Units lhs, Units rhs)
    {
        using underlying_t = std::underlying_type<Units>::type;
        return static_cast<Units>(static_cast<underlying_t>(lhs) | static_cast<underlying_t>(rhs));
    }
    // AND operator for combining multiple units where applicable.
    inline Units operator&(Units lhs, Units rhs)
    {
        using underlying_t = std::underlying_type<Units>::type;
        return static_cast<Units>(static_cast<underlying_t>(lhs) & static_cast<underlying_t>(rhs));
    }

    // Returns true if any unit is set.
    inline bool Any(Units units)
    {
        return units != Unit::UNKNOWN;
    }

}
