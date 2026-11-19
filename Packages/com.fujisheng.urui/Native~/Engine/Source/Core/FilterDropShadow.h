#pragma once

#include "../../Include/URUI/Core/Filter.h"
#include "../../Include/URUI/Core/ID.h"
#include "../../Include/URUI/Core/NumericValue.h"

namespace URUI
{

    class FilterDropShadow : public Filter
    {
    public:
        bool Initialise(Colourb color, NumericValue offset_x, NumericValue offset_y, NumericValue sigma);

        CompiledFilter CompileFilter(Element* element) const override;

        void ExtendInkOverflow(Element* element, Rectanglef& scissor_region) const override;

    private:
        Colourb color;
        NumericValue value_offset_x;
        NumericValue value_offset_y;
        NumericValue value_sigma;
    };

    class FilterDropShadowInstancer : public FilterInstancer
    {
    public:
        FilterDropShadowInstancer();

        SharedPtr<Filter> InstanceFilter(const String& name, const PropertyDictionary& properties) override;

    private:
        struct PropertyIds
        {
            PropertyId color;
            PropertyId offset_x;
            PropertyId offset_y;
            PropertyId sigma;
        };
        PropertyIds ids;
    };

}
