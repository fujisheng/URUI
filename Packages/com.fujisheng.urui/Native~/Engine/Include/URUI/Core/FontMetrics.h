#pragma once

#include "Header.h"

namespace URUI
{

    struct FontMetrics
    {
        // Specified font size [px].
        float size;

        // Distance above the baseline to the upper grid coordinate [px, positive above baseline].
        float ascent;
        // Distance below the baseline to the lower grid coordinate [px, positive below baseline].
        float descent;
        // Font-specified distance between two consecutive baselines [px].
        float line_spacing;

        // Height of the lowercase 'x' character [px].
        float x_height;

        // Position of the underline relative to the baseline [px, positive below baseline].
        float underline_position;
        // Width of underline [px].
        float underline_thickness;

        // True if the ellipsis character (U+2026) is available in this font.
        bool has_ellipsis;
    };

}
