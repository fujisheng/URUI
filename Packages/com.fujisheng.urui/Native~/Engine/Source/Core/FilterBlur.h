#pragma once

#include "../../Include/URUI/Core/Filter.h"
#include "../../Include/URUI/Core/ID.h"
#include "../../Include/URUI/Core/NumericValue.h"

namespace URUI
{

    class FilterBlur : public Filter
    {
    public:
        bool Initialise(NumericValue sigma);

        CompiledFilter CompileFilter(Element* element) const override;

        void ExtendInkOverflow(Element* element, Rectanglef& scissor_region) const override;

    private:
        NumericValue sigma_value;
    };

    class FilterBlurInstancer : public FilterInstancer
    {
    public:
        FilterBlurInstancer();

        SharedPtr<Filter> InstanceFilter(const String& name, const PropertyDictionary& properties) override;

    private:
        struct PropertyIds
        {
            PropertyId sigma;
        };
        PropertyIds ids;
    };

}
