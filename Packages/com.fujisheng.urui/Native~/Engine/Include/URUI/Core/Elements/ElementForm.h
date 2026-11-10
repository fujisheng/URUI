#pragma once

#include "../Element.h"
#include "../Header.h"

namespace URUI {

/**
    A specialisation of the generic Element representing a form element.
 */

class URUI_CORE_API ElementForm : public Element {
public:
	URUI_RTTI_DeclareWithParent(ElementForm, Element)

	/// Constructs a new ElementForm. This should not be called directly; use the Factory instead.
	/// @param[in] tag The tag the element was declared as in HTML.
	ElementForm(const String& tag);
	virtual ~ElementForm();

	/// Submits the form.
	/// @param[in] name The name of the item doing the submit
	/// @param[in] submit_value The value to send through as the 'submit' parameter.
	void Submit(const String& name = "", const String& submit_value = "");
};

} // namespace URUI
