#pragma once

#include "Event.h"
#include "Header.h"
#include "ObserverPtr.h"

namespace URUI {

class Event;
class Element;

/**
    Abstract interface class for handling events.
 */

class URUI_CORE_API EventListener : public EnableObserverPtr<EventListener> {
public:
	virtual ~EventListener() {}

	/// Process the incoming Event
	virtual void ProcessEvent(Event& event) = 0;

	/// Called when the listener has been attached to a new Element
	virtual void OnAttach(Element* /*element*/) {}

	/// Called when the listener has been detached from an Element
	virtual void OnDetach(Element* /*element*/) {}
};

} // namespace URUI
