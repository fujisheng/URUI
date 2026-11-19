#pragma once

#include "../../../Include/URUI/Core/Element.h"
#include "../../../Include/URUI/Core/EventListener.h"
#include "../../../Include/URUI/Core/Header.h"

namespace URUI
{

    /**
    A specialisation of the generic Core::Element representing a label element.

 */

    class ElementLabel : public Element, public EventListener
    {
    public:
        URUI_RTTI_DeclareWithParent(ElementLabel, Element)

        ElementLabel(const String& tag);
        virtual ~ElementLabel();

    protected:
        void OnPseudoClassChange(const String& pseudo_class, bool activate) override;

        void ProcessEvent(Event& event) override;

    private:
        Element* GetTarget();

        bool disable_click = false;
    };

}
