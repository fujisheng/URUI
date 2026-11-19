#include "../../../Include/URUI/Core/Elements/ElementFormControlSelect.h"
#include "../../../Include/URUI/Core/ElementText.h"
#include "../../../Include/URUI/Core/ElementUtilities.h"
#include "../../../Include/URUI/Core/Event.h"
#include "WidgetDropDown.h"

namespace URUI
{

    URUI_RTTI_Define(ElementFormControlSelect)

    ElementFormControlSelect::ElementFormControlSelect(const String& tag) : ElementFormControl(tag), widget(nullptr)
    {
        widget = new WidgetDropDown(this);
    }

    ElementFormControlSelect::~ElementFormControlSelect()
    {
        delete widget;
    }

    String ElementFormControlSelect::GetValue() const
    {
        return GetAttribute("value", String());
    }

    void ElementFormControlSelect::SetValue(const String& value)
    {
        SetAttribute("value", value);
    }

    void ElementFormControlSelect::SetSelection(int selection)
    {
        widget->SetSelection(widget->GetOption(selection));
    }

    int ElementFormControlSelect::GetSelection() const
    {
        return widget->GetSelection();
    }

    void ElementFormControlSelect::GetInnerHTML(String& content) const
    {
        auto* select = const_cast<ElementFormControlSelect*>(this);
        for (int i = 0; i < select->GetNumOptions(); ++i)
        {
            select->GetOption(i)->GetHTML(content);
        }
    }

    Element* ElementFormControlSelect::GetOption(int index)
    {
        return widget->GetOption(index);
    }

    int ElementFormControlSelect::GetNumOptions()
    {
        return widget->GetNumOptions();
    }

    int ElementFormControlSelect::Add(const String& html, const String& value, int before, bool selectable)
    {
        return widget->AddOption(html, value, before, false, selectable);
    }

    int ElementFormControlSelect::Add(ElementPtr element, int before)
    {
        return widget->AddOption(std::move(element), before);
    }

    void ElementFormControlSelect::Remove(int index)
    {
        widget->RemoveOption(index);
    }

    void ElementFormControlSelect::RemoveAll()
    {
        widget->ClearOptions();
    }

    void ElementFormControlSelect::ShowSelectBox()
    {
        widget->ShowSelectBox();
    }

    void ElementFormControlSelect::HideSelectBox()
    {
        widget->HideSelectBox();
    }
    void ElementFormControlSelect::CancelSelectBox()
    {
        widget->CancelSelectBox();
    }

    bool ElementFormControlSelect::IsSelectBoxVisible()
    {
        return widget->IsSelectBoxVisible();
    }

    void ElementFormControlSelect::OnUpdate()
    {
        ElementFormControl::OnUpdate();

        widget->OnUpdate();
    }

    void ElementFormControlSelect::OnRender()
    {
        ElementFormControl::OnRender();

        widget->OnRender();
    }

    void ElementFormControlSelect::OnLayout()
    {
        widget->OnLayout();
    }

    void ElementFormControlSelect::OnChildAdd(Element* child)
    {
        if (widget)
        {
            widget->OnChildAdd(child);
        }
    }

    void ElementFormControlSelect::OnChildRemove(Element* child)
    {
        if (widget)
        {
            widget->OnChildRemove(child);
        }
    }

    bool ElementFormControlSelect::GetIntrinsicDimensions(Vector2f& intrinsic_dimensions, float& /*ratio*/)
    {
        intrinsic_dimensions.x = 128 * ElementUtilities::GetDensityIndependentPixelRatio(this);
        intrinsic_dimensions.y = GetLineHeight();

        return true;
    }

    void ElementFormControlSelect::OnAttributeChange(const ElementAttributes& changed_attributes)
    {
        URUI_ASSERT(widget);

        ElementFormControl::OnAttributeChange(changed_attributes);

        auto it = changed_attributes.find("value");
        if (it != changed_attributes.end())
        {
            widget->OnValueChange(it->second.Get<String>());
        }
    }

}
