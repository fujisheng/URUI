#include "HtmlDocument.h"
#include "../../Include/URUI/Core/Context.h"
#include "../../Include/URUI/Core/ComputedValues.h"
#include "../../Include/URUI/Core/ContextInstancer.h"
#include "../../Include/URUI/Core/Core.h"
#include "../../Include/URUI/Core/Debug.h"
#include "../../Include/URUI/Core/ElementDocument.h"
#include "../../Include/URUI/Core/ElementText.h"
#include "../../Include/URUI/Core/ElementUtilities.h"
#include "../../Include/URUI/Core/Factory.h"
#include "../../Include/URUI/Core/FontEngineInterface.h"
#include "../../Include/URUI/Core/FontMetrics.h"
#include "../../Include/URUI/Core/Profiling.h"
#include "../../Include/URUI/Core/RenderManager.h"
#include "../../Include/URUI/Core/StreamMemory.h"
#include "../../Include/URUI/Core/SystemInterface.h"
#include "DocumentFontContext.h"
#include "ElementStyle.h"
#include "../../Include/URUI/Core/PropertySpecification.h"
#include "EventDispatcher.h"
#include "PluginRegistry.h"
#include "ScrollController.h"
#include "StreamFile.h"
#include <algorithm>
#include <clocale>
#include <iterator>
#include <limits>

namespace URUI {

static constexpr float DOUBLE_CLICK_TIME = 0.5f;    // [s]
static constexpr float DOUBLE_CLICK_MAX_DIST = 3.f; // [dp]
static constexpr float UNIT_SCROLL_LENGTH = 80.f;   // [dp]

// If the user stops scrolling for this amount of time in seconds before touch/click release, don't apply inertia.
static constexpr float SCROLL_INERTIA_DELAY = 0.1f;
static constexpr float TOUCH_MOVEMENT_DECAY_RATE = 5.0f;
static constexpr float TOUCH_CLICK_MAX_DISTANCE = DOUBLE_CLICK_MAX_DIST; // [dp]

static void DebugVerifyLocaleSetting()
{
#ifdef URUI_ENGINE_DEBUG
	constexpr float expected_value = 1000.5f;
	const URUI::String expected_string = "1000.5";
	const URUI::String formatted_string = URUI::ToString(expected_value);
	const float parsed_value = URUI::FromString<float>(expected_string);

	const char* description = "URUI expects the global locale to be set to the default minimal \"C\" locale, please see `std::setlocale`.";
	if (formatted_string != expected_string)
	{
		URUI::Log::Message(URUI::Log::LT_ERROR,
			"Incompatible locale setting detected while formatting %f. Formatted: \"%s\". Expected: \"%s\". Current locale: %s. %s", expected_value,
			formatted_string.c_str(), expected_string.c_str(), std::setlocale(LC_ALL, nullptr), description);
	}
	if (parsed_value != expected_value)
	{
		URUI::Log::Message(URUI::Log::LT_ERROR,
			"Incompatible locale setting detected while parsing \"%s\". Parsed: %f. Expected: %f. Current locale: %s. %s", expected_string.c_str(),
			parsed_value, expected_value, std::setlocale(LC_ALL, nullptr), description);
	}
#endif
}

Context::Context(const String& name, RenderManager* render_manager, TextInputHandler* text_input_handler) :
	name(name), render_manager(render_manager), text_input_handler(text_input_handler)
{
	instancer = nullptr;

	root = Factory::InstanceElement(nullptr, "*", "#root", ElementAttributes());
	// Document show/focus can run before the first Context update.
	root->ForceLocalStackingContext();
	root->SetId(name);
	root->SetOffset(Vector2f(0, 0), nullptr);
	root->SetProperty(PropertyId::ZIndex, Property(0, Unit::NUMBER));

	document_focus_history.push_back(root.get());
	focus = root.get();
	hover = nullptr;
	active = nullptr;
	drag = nullptr;

	drag_started = false;
	drag_verbose = false;
	drag_clone = nullptr;
	drag_hover = nullptr;
	last_click_element = nullptr;
	last_click_time = 0;

	mouse_active = false;
	enable_cursor = true;

	scroll_controller = MakeUnique<ScrollController>();
}

Context::~Context()
{
	PluginRegistry::NotifyContextDestroy(this);

	UnloadAllDocuments();

	ReleaseUnloadedDocuments();

	root.reset();

	cursor_proxy.reset();

	instancer = nullptr;
}

const String& Context::GetName() const
{
	return name;
}

void Context::SetDimensions(const Vector2i _dimensions)
{
	if (dimensions != _dimensions)
	{
		dimensions = _dimensions;
		render_manager->SetViewport(dimensions);
		root->SetBox(Box(Vector2f(dimensions)));
		root->DirtyLayout();

		for (int i = 0; i < root->GetNumChildren(); ++i)
		{
			ElementDocument* document = root->GetChild(i)->GetOwnerDocument();
			if (document != nullptr)
			{
				document->DirtyMediaQueries();
				document->DirtyVwAndVhProperties();
				document->DirtyLayout();
				document->DirtyPosition();
				document->DispatchEvent(EventId::Resize, Dictionary());
			}
		}
	}
}

Vector2i Context::GetDimensions() const
{
	return dimensions;
}

void Context::SetDensityIndependentPixelRatio(float dp_ratio)
{
	if (density_independent_pixel_ratio != dp_ratio)
	{
		density_independent_pixel_ratio = dp_ratio;

		for (int i = 0; i < root->GetNumChildren(true); ++i)
		{
			ElementDocument* document = root->GetChild(i)->GetOwnerDocument();
			if (document)
			{
				document->DirtyMediaQueries();
				document->OnDpRatioChangeRecursive();
			}
		}
	}
}

float Context::GetDensityIndependentPixelRatio() const
{
	return density_independent_pixel_ratio;
}

bool Context::Update()
{
	URUI_ZoneScoped;
	DebugVerifyLocaleSetting();

	next_update_timeout = std::numeric_limits<double>::infinity();

	if (scroll_controller->Update(mouse_position, density_independent_pixel_ratio))
		RequestNextUpdate(0);

	// Update the hover chain to detect any new or moved elements under the mouse.
	if (mouse_active)
		UpdateHoverChain(mouse_position);

	// The style definition of each document should be independent of each other. By manually resetting these flags we avoid unnecessary definition
	// lookups in unrelated documents, such as when adding a new document. Adding an element dirties the parent definition, which in this case is the
	// root. By extension the definition of all the other documents are also dirtied, unnecessarily.
	root->dirty_definition = false;
	root->dirty_child_definitions = false;

	root->Update(density_independent_pixel_ratio, Vector2f(dimensions));

	for (int i = 0; i < root->GetNumChildren(); ++i)
	{
		if (auto doc = root->GetChild(i)->GetOwnerDocument())
		{
			doc->UpdateLayout();
			doc->UpdatePosition();
			doc->UpdatePendingPresentation();
		}
	}

	// Release any documents that were unloaded during the update.
	ReleaseUnloadedDocuments();

	return true;
}

bool Context::Render()
try
{
	URUI_ZoneScoped;

	render_manager->PrepareRender(dimensions);

	root->Render();

	// Render the cursor proxy so that any attached drag clone will be rendered below the cursor.
	if (drag_clone)
	{
		static_cast<ElementDocument&>(*cursor_proxy).UpdateDocument();
		cursor_proxy->SetOffset(
			Vector2f((float)Math::Clamp(mouse_position.x, 0, dimensions.x), (float)Math::Clamp(mouse_position.y, 0, dimensions.y)), nullptr);
		cursor_proxy->Render();
	}

	render_manager->ResetState();

	return true;
}
catch (...)
{
	render_manager->AbortRender();
	throw;
}

ElementDocument* Context::CreateDocument(const String& instancer_name)
{
	ElementPtr element = Factory::InstanceElement(nullptr, instancer_name, documents_base_tag, ElementAttributes());
	if (!element)
	{
		Log::Message(Log::LT_ERROR, "Failed to instance document on instancer_name '%s', instancer returned nullptr.", instancer_name.c_str());
		return nullptr;
	}

	ElementDocument* document = urui_dynamic_cast<ElementDocument*>(element.get());
	if (!document)
	{
		Log::Message(Log::LT_ERROR,
			"Failed to instance document on instancer_name '%s', Found type '%s', was expecting derivative of ElementDocument.",
			instancer_name.c_str(), urui_type_name(*element));
		return nullptr;
	}

	document->context = this;
	root->AppendChild(std::move(element));

	PluginRegistry::NotifyDocumentLoad(document);

	return document;
}

ElementDocument* Context::LoadDocument(const String& document_path)
{
	auto stream = MakeUnique<StreamFile>();

	if (!stream->Open(document_path))
		return nullptr;

	ElementDocument* document = LoadDocument(stream.get());

	return document;
}

ElementDocument* Context::LoadDocument(Stream* stream)
{
	DebugVerifyLocaleSetting();
	PluginRegistry::NotifyDocumentOpen(this, stream->GetSourceURL().GetURL());

	ElementPtr element = Factory::InstanceDocumentStream(this, stream, GetDocumentsBaseTag());
	return AttachLoadedDocument(std::move(element));
}

ElementDocument* Context::LoadParsedHtmlDocument(const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics)
{
	DebugVerifyLocaleSetting();
	PluginRegistry::NotifyDocumentOpen(this, parsed.sourceUrl);
	return AttachLoadedDocument(Factory::InstanceParsedHtmlDocument(this, parsed, diagnostics));
}

ElementDocument* Context::AttachLoadedDocument(ElementPtr element)
{
	if (!element)
		return nullptr;

	ElementDocument* document = urui_static_cast<ElementDocument*>(element.get());

	// Reserve retirement storage while the candidate still has an owning local pointer.
	// An exception during its first update must not leave an unpublished tree attached.
	unloaded_documents.reserve(unloaded_documents.size() + 1);
	root->AppendChild(std::move(element));
	try
	{

	// The 'load' event is fired before updating the document, because the user might
	// need to initalize things before running an update. The drawback is that computed
	// values and layouting are not performed yet, resulting in default values when
	// querying such information in the event handler.
	PluginRegistry::NotifyDocumentLoad(document);
	document->DispatchEvent(EventId::Load, Dictionary());

	document->UpdateDocument();

	return document;
	}
	catch (...)
	{
		UnloadDocument(document);
		throw;
	}
}

ElementDocument* Context::LoadDocumentFromMemory(const String& string, const String& source_url)
{
	// Open the stream based on the string contents.
	auto stream = MakeUnique<StreamMemory>(reinterpret_cast<const byte*>(string.c_str()), string.size());

	stream->SetSourceURL(source_url);

	// Load the document from the stream.
	ElementDocument* document = LoadDocument(stream.get());

	return document;
}

void Context::ReserveDocumentRetirement()
{
	unloaded_documents.reserve(unloaded_documents.size() + 1);
}

void Context::UnloadDocument(ElementDocument* _document)
{
	// Has this document already been unloaded?
	for (size_t i = 0; i < unloaded_documents.size(); ++i)
	{
		if (unloaded_documents[i].get() == _document)
			return;
	}

	ElementDocument* document = _document;
	const bool had_focus = focus && focus->GetOwnerDocument() == document;
	const bool was_presented = document->IsPresented();

	if (document->GetParentNode() == root.get())
	{
		// All storage is prepared before the host revokes old identities. Unload
		// notifications must not interrupt the structural commit under memory pressure.
		ReserveDocumentRetirement();
		try { document->DispatchEvent(EventId::Unload, Dictionary()); } catch (...) {}
		try { PluginRegistry::NotifyDocumentUnload(document); } catch (...) {}
		try { root->OnChildRemove(document); } catch (...) {}
		for (auto it = root->children.begin(); it != root->children.end(); ++it)
		{
			if (it->get() != document) continue;
			if (size_t(it - root->children.begin()) >= root->children.size() - root->num_non_dom_children)
				--root->num_non_dom_children;
			// RemoveChild can dispatch focus/hover events while holding the only
			// owning pointer. Detach directly, then clear every input reference below.
			document->parent = nullptr;
			unloaded_documents.push_back(std::move(*it));
			root->children.erase(it);
			root->DirtyLayout(); root->DirtyStackingContext(); root->DirtyDefinition(Element::DirtyNodes::Self);
			break;
		}
	}
	auto belongs = [document](Element* element) { return element && element->GetOwnerDocument() == document; };
	auto clear_chain = [&](auto& chain) {
		for (auto it = chain.begin(); it != chain.end();)
			if (belongs(*it)) it = chain.erase(it); else ++it;
	};
	clear_chain(hover_chain); clear_chain(active_chain); clear_chain(drag_hover_chain);
	if (belongs(root->focus)) root->focus = nullptr;
	if (belongs(hover)) hover = nullptr;
	if (belongs(active)) active = nullptr;
	if (belongs(drag_hover)) drag_hover = nullptr;
	if (belongs(scroll_controller->GetTarget())) scroll_controller->Reset();
	for (auto it = touch_states.begin(); it != touch_states.end();)
		if (belongs(it->second.scroll_container)) it = touch_states.erase(it); else ++it;

	// Remove the item from the focus history.
	ElementList::iterator itr = std::find(document_focus_history.begin(), document_focus_history.end(), document);
	if (itr != document_focus_history.end())
		document_focus_history.erase(itr);

	// Focus to the previous document if the old document is the current focus.
	if (had_focus)
	{
		focus = nullptr;
		try {
			for (auto it = document_focus_history.rbegin(); it != document_focus_history.rend(); ++it)
				if ((*it)->IsVisible(true) && (*it)->GetFocusLeafNode()->Focus()) break;
			if (!focus) root->Focus();
		} catch (...) { focus = nullptr; root->focus = nullptr; }
	}

	// Clear the active element if the old document is the active element.
	if (active && active->GetOwnerDocument() == document)
	{
		active = nullptr;
	}

	// Clear other pointers to elements whose owner was deleted
	if (drag && drag->GetOwnerDocument() == document)
	{
		drag = nullptr;
		ReleaseDragClone();
	}

	if (drag_hover && drag_hover->GetOwnerDocument() == document)
	{
		drag_hover = nullptr;
	}

	// Rebuild the hover state.
	if (was_presented || had_focus) {
		try { UpdateHoverChain(mouse_position); }
		catch (...) { hover = nullptr; hover_chain.clear(); }
	}
}

void Context::UnloadAllDocuments()
{
	// Unload all children.
	while (root->GetNumChildren(true) > 0)
		UnloadDocument(root->GetChild(0)->GetOwnerDocument());

	// The element lists may point to elements that are getting removed.
	active_chain.clear();
	hover_chain.clear();
	drag_hover_chain.clear();
}

void Context::EnableMouseCursor(bool enable)
{
	// The cursor is set to an invalid name so that it is forced to update in the next update loop.
	cursor_name = ":reset:";
	enable_cursor = enable;
}

void Context::ActivateTheme(const String& theme_name, bool activate)
{
	bool theme_changed = false;

	if (activate)
		theme_changed = active_themes.insert(theme_name).second;
	else
		theme_changed = (active_themes.erase(theme_name) > 0);

	if (theme_changed)
	{
		for (int i = 0; i < root->GetNumChildren(true); ++i)
		{
			if (ElementDocument* document = root->GetChild(i)->GetOwnerDocument())
				document->DirtyMediaQueries();
		}
	}
}

bool Context::IsThemeActive(const String& theme_name) const
{
	return active_themes.count(theme_name);
}

ElementDocument* Context::GetDocument(const String& id)
{
	for (int i = 0; i < root->GetNumChildren(); i++)
	{
		ElementDocument* document = root->GetChild(i)->GetOwnerDocument();
		if (document == nullptr)
			continue;

		if (document->GetId() == id)
			return document;
	}

	return nullptr;
}

ElementDocument* Context::GetDocument(int index)
{
	Element* element = root->GetChild(index);
	if (element == nullptr)
		return nullptr;

	return element->GetOwnerDocument();
}

int Context::GetNumDocuments() const
{
	return root->GetNumChildren();
}

Element* Context::GetHoverElement()
{
	return hover;
}

Element* Context::GetFocusElement()
{
	return focus;
}

Element* Context::GetRootElement()
{
	return root.get();
}

void Context::PullDocumentToFront(ElementDocument* document)
{
	if (document != root->GetLastChild())
	{
		// Calling RemoveChild() / AppendChild() would be cleaner, but that dirties the document's layout
		// unnecessarily, so we'll go under the hood here.
		for (int i = 0; i < root->GetNumChildren(); ++i)
		{
			if (root->GetChild(i) == document)
			{
				ElementPtr element = std::move(root->children[i]);
				root->children.erase(root->children.begin() + i);
				root->children.insert(root->children.begin() + root->GetNumChildren(), std::move(element));

				root->DirtyStackingContext();
			}
		}
	}
}

void Context::PushDocumentToBack(ElementDocument* document)
{
	if (document != root->GetFirstChild())
	{
		// See PullDocumentToFront().
		for (int i = 0; i < root->GetNumChildren(); ++i)
		{
			if (root->GetChild(i) == document)
			{
				ElementPtr element = std::move(root->children[i]);
				root->children.erase(root->children.begin() + i);
				root->children.insert(root->children.begin(), std::move(element));

				root->DirtyStackingContext();
			}
		}
	}
}

void Context::UnfocusDocument(ElementDocument* document)
{
	auto it = std::find(document_focus_history.begin(), document_focus_history.end(), document);
	if (it != document_focus_history.end())
		document_focus_history.erase(it);

	if (!focus || focus->GetOwnerDocument() != document) return;
	for (auto previous = document_focus_history.rbegin(); previous != document_focus_history.rend(); ++previous)
		if (*previous != root.get() && (*previous)->IsVisible(true) && (*previous)->GetFocusLeafNode()->Focus()) return;
	// The context root still points down the old focus chain. Focus the root
	// itself, rather than asking for its now-hidden leaf.
	if (!root->Focus()) {
		OnFocusChange(nullptr, false);
		root->focus = nullptr;
	}
}

void Context::AddEventListener(const String& event, EventListener* listener, bool in_capture_phase)
{
	root->AddEventListener(event, listener, in_capture_phase);
}

void Context::RemoveEventListener(const String& event, EventListener* listener, bool in_capture_phase)
{
	root->RemoveEventListener(event, listener, in_capture_phase);
}

bool Context::ProcessKeyDown(Input::KeyIdentifier key_identifier, int key_modifier_state)
{
	// Generate the parameters for the key event.
	Dictionary parameters;
	GenerateKeyEventParameters(parameters, key_identifier);
	GenerateKeyModifierEventParameters(parameters, key_modifier_state);

	if (focus)
		return focus->DispatchEvent(EventId::Keydown, parameters);
	else
		return root->DispatchEvent(EventId::Keydown, parameters);
}

bool Context::ProcessKeyUp(Input::KeyIdentifier key_identifier, int key_modifier_state)
{
	// Generate the parameters for the key event.
	Dictionary parameters;
	GenerateKeyEventParameters(parameters, key_identifier);
	GenerateKeyModifierEventParameters(parameters, key_modifier_state);

	if (focus)
		return focus->DispatchEvent(EventId::Keyup, parameters);
	else
		return root->DispatchEvent(EventId::Keyup, parameters);
}

bool Context::ProcessTextInput(char character)
{
	// Only the standard ASCII character set is a valid subset of UTF-8.
	if (static_cast<unsigned char>(character) > 127)
		return false;
	return ProcessTextInput(static_cast<Character>(character));
}

bool Context::ProcessTextInput(Character character)
{
	// Generate the parameters for the key event.
	String text = StringUtilities::ToUTF8(character);
	return ProcessTextInput(text);
}

bool Context::ProcessTextInput(const String& string)
{
	Element* target = (focus ? focus : root.get());

	Dictionary parameters;
	parameters["text"] = string;

	bool consumed = target->DispatchEvent(EventId::Textinput, parameters);

	return consumed;
}

bool Context::ProcessMouseMove(int x, int y, int key_modifier_state)
{
	// Check whether the mouse moved since the last event came through.
	Vector2i old_mouse_position = mouse_position;
	mouse_position = {x, y};
	const bool mouse_moved = (mouse_position != old_mouse_position || !mouse_active);
	mouse_active = true;

	// Update the current hover chain. This will send all necessary 'onmouseout', 'onmouseover', 'ondragout' and 'ondragover' messages.
	Dictionary parameters, drag_parameters;
	UpdateHoverChain(old_mouse_position, key_modifier_state, &parameters, &drag_parameters);

	// Dispatch any 'onmousemove' events.
	if (mouse_moved)
	{
		if (hover)
		{
			hover->DispatchEvent(EventId::Mousemove, parameters);

			if (drag_hover && drag_verbose)
				drag_hover->DispatchEvent(EventId::Dragmove, drag_parameters);
		}
	}

	return !IsMouseInteracting();
}

static Element* FindFocusElement(Element* element)
{
	ElementDocument* owner_document = element->GetOwnerDocument();
	if (!owner_document || owner_document->GetComputedValues().focus() == Style::Focus::None)
		return nullptr;

	while (element && element->GetComputedValues().focus() == Style::Focus::None)
	{
		element = element->GetParentNode();
	}

	return element;
}

bool Context::ProcessMouseButtonDown(int button_index, int key_modifier_state)
{
	Dictionary parameters;
	GenerateMouseEventParameters(parameters, button_index);
	GenerateKeyModifierEventParameters(parameters, key_modifier_state);

	bool propagate = true;

	if (button_index == 0)
	{
		Element* new_focus = hover;

		// Set the currently hovered element to focus if it isn't already the focus.
		if (hover)
		{
			new_focus = FindFocusElement(hover);
			if (new_focus && new_focus != focus)
			{
				if (!new_focus->Focus())
					return !IsMouseInteracting();
			}
		}

		// Save the just-pressed-on element as the pressed element.
		active = new_focus;

		// Call 'onmousedown' on every item in the hover chain, and copy the hover chain to the active chain.
		if (hover)
			propagate = hover->DispatchEvent(EventId::Mousedown, parameters);

		if (propagate)
		{
			// Check for a double-click on an element; if one has occured, we send the 'dblclick' event to the hover
			// element. If not, we'll start a timer to catch the next one.
			float mouse_distance_squared = float((mouse_position - last_click_mouse_position).SquaredMagnitude());
			float max_mouse_distance = DOUBLE_CLICK_MAX_DIST * density_independent_pixel_ratio;

			double click_time = GetSystemInterface()->GetElapsedTime();

			if (active == last_click_element && float(click_time - last_click_time) < DOUBLE_CLICK_TIME &&
				mouse_distance_squared < max_mouse_distance * max_mouse_distance)
			{
				if (hover)
					propagate = hover->DispatchEvent(EventId::Dblclick, parameters);

				last_click_element = nullptr;
				last_click_time = 0;
			}
			else
			{
				last_click_element = active;
				last_click_time = click_time;
			}
		}

		last_click_mouse_position = mouse_position;

		active_chain.insert(active_chain.end(), hover_chain.begin(), hover_chain.end());

		if (propagate)
		{
			// Traverse down the hierarchy of the newly focused element (if any), and see if we can begin dragging it.
			drag_started = false;
			drag = hover;
			while (drag)
			{
				Style::Drag drag_style = drag->GetComputedValues().drag();
				switch (drag_style)
				{
				case Style::Drag::None: drag = drag->GetParentNode(); continue;
				case Style::Drag::Block: drag = nullptr; continue;
				default: drag_verbose = (drag_style == Style::Drag::DragDrop || drag_style == Style::Drag::Clone);
				}

				break;
			}
		}
	}
	else
	{
		// Not the primary mouse button, so we're not doing any special processing.
		if (hover)
			propagate = hover->DispatchEvent(EventId::Mousedown, parameters);
	}

	if (scroll_controller->GetMode() == ScrollController::Mode::Autoscroll)
	{
		scroll_controller->Reset();
	}
	else if (button_index == 2 && hover && propagate)
	{
		Dictionary scroll_parameters;
		GenerateMouseEventParameters(scroll_parameters);
		GenerateKeyModifierEventParameters(scroll_parameters, key_modifier_state);
		scroll_parameters["autoscroll"] = true;

		// Dispatch a mouse scroll event, this gives elements an opportunity to block autoscroll from being initialized.
		if (hover->DispatchEvent(EventId::Mousescroll, scroll_parameters))
			scroll_controller->ActivateAutoscroll(hover->GetClosestScrollableContainer(), mouse_position);
	}

	return !IsMouseInteracting();
}

bool Context::ProcessMouseButtonUp(int button_index, int key_modifier_state)
{
	Dictionary parameters;
	GenerateMouseEventParameters(parameters, button_index);
	GenerateKeyModifierEventParameters(parameters, key_modifier_state);

	// We want to return the interaction state before handling the mouse up events, so that any active element that is released is considered to
	// capture the event.
	const bool result = !IsMouseInteracting();

	// Process primary click.
	if (button_index == 0)
	{
		// The elements in the new hover chain have the 'onmouseup' event called on them.
		if (hover)
			hover->DispatchEvent(EventId::Mouseup, parameters);

		// If the active element (the one that was being hovered over when the mouse button was pressed) is still being
		// hovered over, we click it.
		if (hover && active && active == FindFocusElement(hover))
		{
			active->DispatchEvent(EventId::Click, parameters);
		}

		ResetActiveChain();

		if (drag)
		{
			if (drag_started)
			{
				Dictionary drag_parameters;
				GenerateMouseEventParameters(drag_parameters);
				GenerateDragEventParameters(drag_parameters);
				GenerateKeyModifierEventParameters(drag_parameters, key_modifier_state);

				if (drag_hover)
				{
					if (drag_verbose)
					{
						drag_hover->DispatchEvent(EventId::Dragdrop, drag_parameters);
						// User may have removed the element, do an extra check.
						if (drag_hover)
							drag_hover->DispatchEvent(EventId::Dragout, drag_parameters);
					}
				}

				if (drag)
					drag->DispatchEvent(EventId::Dragend, drag_parameters);

				ReleaseDragClone();
			}

			drag = nullptr;
			drag_hover = nullptr;
			drag_hover_chain.clear();

			// We may have changes under our mouse, this ensures that the hover chain is properly updated
			ProcessMouseMove(mouse_position.x, mouse_position.y, key_modifier_state);
		}
	}
	else
	{
		// Not the left mouse button, so we're not doing any special processing.
		if (hover)
			hover->DispatchEvent(EventId::Mouseup, parameters);
	}

	// If we have autoscrolled while holding the middle mouse button, release the autoscroll mode now.
	if (scroll_controller->HasAutoscrollMoved())
		scroll_controller->Reset();

	return result;
}

bool Context::ProcessMouseWheel(float wheel_delta, int key_modifier_state)
{
	return ProcessMouseWheel(Vector2f{0.f, wheel_delta}, key_modifier_state);
}

bool Context::ProcessMouseWheel(Vector2f wheel_delta, int key_modifier_state)
{
	if (scroll_controller->GetMode() == ScrollController::Mode::Autoscroll)
	{
		scroll_controller->Reset();
		return false;
	}
	else if (!hover)
	{
		scroll_controller->Reset();
		return true;
	}

	Dictionary scroll_parameters;
	GenerateMouseEventParameters(scroll_parameters);
	GenerateKeyModifierEventParameters(scroll_parameters, key_modifier_state);
	scroll_parameters["wheel_delta_x"] = wheel_delta.x;
	scroll_parameters["wheel_delta_y"] = wheel_delta.y;

	// Dispatch a mouse scroll event, this gives elements an opportunity to block scrolling from being performed.
	if (!hover->DispatchEvent(EventId::Mousescroll, scroll_parameters))
		return false;

	const float unit_scroll_length = UNIT_SCROLL_LENGTH * density_independent_pixel_ratio;
	const Vector2f scroll_length = wheel_delta * unit_scroll_length;
	Element* target = hover->GetClosestScrollableContainer();

	if (scroll_controller->GetMode() == ScrollController::Mode::Smoothscroll && scroll_controller->GetTarget() == target)
		scroll_controller->IncrementSmoothscrollTarget(scroll_length);
	else
		scroll_controller->ActivateSmoothscroll(target, scroll_length, ScrollBehavior::Auto);

	return target == nullptr;
}

bool Context::ProcessMouseLeave()
{
	mouse_active = false;

	// Update the hover chain. Now that 'mouse_active' is disabled this will remove the hover state from all elements.
	UpdateHoverChain(mouse_position);

	return !IsMouseInteracting();
}

bool Context::IsMouseInteracting() const
{
	return (hover && hover != root.get()) || (active && active != root.get()) || scroll_controller->GetMode() == ScrollController::Mode::Autoscroll;
}

Context::TouchState* Context::LookupTouch(TouchId identifier)
{
	auto touch_it = touch_states.find(identifier);
	return touch_it != touch_states.end() ? &touch_it->second : nullptr;
}

bool Context::ProcessTouchStart(const TouchList& touches, int key_modifier_state)
{
	bool result = true;
	for (const auto& touch : touches)
		result &= ProcessTouchStart(touch, key_modifier_state);
	return result;
}

bool Context::ProcessTouchMove(const TouchList& touches, int key_modifier_state)
{
	bool result = true;
	for (const auto& touch : touches)
		result &= ProcessTouchMove(touch, key_modifier_state);
	return result;
}

bool Context::ProcessTouchEnd(const TouchList& touches, int key_modifier_state)
{
	bool result = true;
	for (const auto& touch : touches)
		result &= ProcessTouchEnd(touch, key_modifier_state);
	return result;
}

bool Context::ProcessTouchCancel(const TouchList& touches)
{
	bool result = true;
	for (const auto& touch : touches)
		result &= ProcessTouchCancel(touch);
	return result;
}

bool Context::ProcessTouchStart(const Touch& touch, int key_modifier_state)
{
	TouchState* state = LookupTouch(touch.identifier);
	URUI_ASSERTMSG(state == nullptr, "Receiving touch start event for an already started touch.");
	if (!state)
	{
		auto it_inserted = touch_states.emplace(touch.identifier, TouchState()).first;
		state = &it_inserted->second;
	}

	state->start_position = touch.position;
	state->inertia_position = touch.position;
	state->last_position = touch.position;
	state->scrolling_start_time_x = 0;
	state->scrolling_start_time_y = 0;
	state->scrolling_last_time = GetSystemInterface()->GetElapsedTime();

	Element* touch_element = GetElementAtPoint(touch.position);
	state->scroll_container = touch_element ? touch_element->GetClosestScrollableContainer() : nullptr;

	// reset any scrolling when we touch the element
	if (state->scroll_container && scroll_controller->GetTarget() == state->scroll_container)
		scroll_controller->Reset();

	ProcessMouseMove(static_cast<int>(touch.position.x), static_cast<int>(touch.position.y), key_modifier_state);

	// always assume touch press/release events are handled as left mouse button
	return ProcessMouseButtonDown(0, key_modifier_state);
}

bool Context::ProcessTouchMove(const Touch& touch, int key_modifier_state)
{
	TouchState* state = LookupTouch(touch.identifier);
	if (!state)
		return true;

	state->scrolling_last_time = GetSystemInterface()->GetElapsedTime();

	if (state->scroll_container)
	{
		const Vector2f delta = touch.position - state->last_position;

		if (drag)
		{
			// Don't scroll and reset scrolling state when dragging any element (scrollbars and others)
			state->inertia_position = touch.position;
			state->last_position = touch.position;
			state->scrolling_start_time_x = 0;
			state->scrolling_start_time_y = 0;
		}
		else if (delta.x != 0 || delta.y != 0)
		{
			// Use instant scrolling when touch is pressed even when default scroll behavior is smooth.
			scroll_controller->InstantScrollOnTarget(state->scroll_container, -delta);

			const double current_time = GetSystemInterface()->GetElapsedTime();

			enum { Horizontal = 0, Vertical = 1 };
			for (int axis : {Horizontal, Vertical})
			{
				bool& state_scrolling_positive = (axis == Horizontal ? state->scrolling_right : state->scrolling_down);
				double& state_scrolling_start_time = (axis == Horizontal ? state->scrolling_start_time_x : state->scrolling_start_time_y);

				// Time set to 0 means no touch move events happened before and direction is unclear.
				const bool scroll_start = (state_scrolling_start_time == 0);

				// If the user changes direction, reset the start time and position.
				const bool going_positive = (delta[axis] > 0);
				if (delta[axis] != 0 && (going_positive != state_scrolling_positive || scroll_start))
				{
					state->inertia_position[axis] = touch.position[axis];
					state_scrolling_positive = going_positive;
					state_scrolling_start_time = state->scrolling_last_time;
				}
				else
				{
					// Move inertia position towards end position with a weight of e^-kt to better capture and
					// calculate velocity of the very last touch movements before touch release.
					const float elapsed_time = static_cast<float>(current_time - state_scrolling_start_time);
					const float weight = Math::Exp(-elapsed_time * TOUCH_MOVEMENT_DECAY_RATE);

					state->inertia_position[axis] = touch.position[axis] - (touch.position[axis] - state->inertia_position[axis]) * weight;
					state_scrolling_start_time = current_time - (current_time - state_scrolling_start_time) * weight;
				}
			}

			const float touch_max_distance = TOUCH_CLICK_MAX_DISTANCE * density_independent_pixel_ratio;
			const Vector2f delta_from_start = touch.position - state->start_position;
			if (delta_from_start.SquaredMagnitude() >= touch_max_distance * touch_max_distance)
				ResetActiveChain();
		}
	}

	state->last_position = touch.position;

	return ProcessMouseMove(static_cast<int>(touch.position.x), static_cast<int>(touch.position.y), key_modifier_state);
}

bool Context::ProcessTouchEnd(const Touch& touch, int key_modifier_state)
{
	TouchState* state = LookupTouch(touch.identifier);
	if (!state)
		return true;

	if (state->scroll_container)
	{
		double current_time = GetSystemInterface()->GetElapsedTime();
		double time_since_last_move = current_time - state->scrolling_last_time;
		if (time_since_last_move < SCROLL_INERTIA_DELAY)
		{
			// apply scrolling inertia
			Vector2f delta = touch.position - state->inertia_position;

			Vector2f velocity = -delta;
			float elapsed_time_x = static_cast<float>(current_time - state->scrolling_start_time_x);
			float elapsed_time_y = static_cast<float>(current_time - state->scrolling_start_time_y);
			if (elapsed_time_x > 0)
				velocity.x /= elapsed_time_x;
			if (elapsed_time_y > 0)
				velocity.y /= elapsed_time_y;

			scroll_controller->ActivateInertia(state->scroll_container, velocity);
		}
	}

	touch_states.erase(touch.identifier);

	ProcessMouseMove(static_cast<int>(touch.position.x), static_cast<int>(touch.position.y), key_modifier_state);

	// always assume touch press/release events are handled as left mouse button
	return ProcessMouseButtonUp(0, key_modifier_state);
}

bool Context::ProcessTouchCancel(const Touch& touch)
{
	TouchState* state = LookupTouch(touch.identifier);
	if (!state)
		return false;

	touch_states.erase(touch.identifier);

	return ProcessMouseButtonUp(0, 0);
}

void Context::SetDefaultScrollBehavior(ScrollBehavior scroll_behavior, float speed_factor)
{
	scroll_controller->SetDefaultScrollBehavior(scroll_behavior, speed_factor);
}

RenderManager& Context::GetRenderManager()
{
	return *render_manager;
}

TextInputHandler* Context::GetTextInputHandler() const
{
	return text_input_handler;
}

void Context::SetInstancer(ContextInstancer* _instancer)
{
	URUI_ASSERT(instancer == nullptr);
	instancer = _instancer;
}

void Context::OnElementDetach(Element* element)
{
	auto it_hover = hover_chain.find(element);
	if (it_hover != hover_chain.end())
	{
		Dictionary parameters;
		GenerateMouseEventParameters(parameters, -1);
		element->DispatchEvent(EventId::Mouseout, parameters);

		hover_chain.erase(it_hover);

		if (hover == element)
			hover = nullptr;
	}

	auto it_active = std::find(active_chain.begin(), active_chain.end(), element);
	if (it_active != active_chain.end())
	{
		active_chain.erase(it_active);

		if (active == element)
			active = nullptr;
	}

	if (drag)
	{
		auto it = drag_hover_chain.find(element);
		if (it != drag_hover_chain.end())
		{
			drag_hover_chain.erase(it);

			if (drag_hover == element)
				drag_hover = nullptr;
		}

		if (drag == element)
		{
			// The dragged element is being removed, silently cancel the drag operation
			if (drag_started)
				ReleaseDragClone();

			drag = nullptr;
			drag_hover = nullptr;
			drag_hover_chain.clear();
		}
	}

	// Focus normally cleared and set by parent during Element::RemoveChild.
	// However, there are some exceptions, such as when an there are multiple
	// ElementDocuments in the hierarchy above the current element.
	if (element == focus)
		focus = nullptr;

	// If the element is a document lower down in the hierarchy, we may need to remove it from the focus history.
	if (element->GetOwnerDocument() == element)
	{
		auto it = std::find(document_focus_history.begin(), document_focus_history.end(), element);
		if (it != document_focus_history.end())
			document_focus_history.erase(it);
	}

	if (scroll_controller->GetTarget() == element)
		scroll_controller->Reset();

	// Clear TouchState if we're touching element
	for (auto touch_it = touch_states.begin(); touch_it != touch_states.end();)
	{
		if (touch_it->second.scroll_container == element)
			touch_it = touch_states.erase(touch_it);
		else
			++touch_it;
	}
}

bool Context::OnFocusChange(Element* new_focus, bool focus_visible)
{
	ElementSet old_chain;
	ElementSet new_chain;

	Element* old_focus = focus;
	ElementDocument* old_document = old_focus ? old_focus->GetOwnerDocument() : nullptr;
	ElementDocument* new_document = new_focus ? new_focus->GetOwnerDocument() : nullptr;

	// Modality blocks documents below the top visible modal. Overlays above that
	// document can still receive input, regardless of the current focus owner.
	if (new_focus && IsDocumentBlockedByModal(new_document))
		return false;

	// If the document of the new focus has been closed, deny the request.
	if (std::find_if(unloaded_documents.begin(), unloaded_documents.end(),
			[&](const auto& unloaded_document) { return unloaded_document.get() == new_document; }) != unloaded_documents.end())
	{
		return false;
	}

	// Build the old chains
	Element* element = old_focus;
	while (element)
	{
		old_chain.insert(element);
		element = element->GetParentNode();
	}

	// Build the new chain
	element = new_focus;
	while (element)
	{
		new_chain.insert(element);
		element = element->GetParentNode();
	}

	// Prepare storage before committing focus. Notifications cannot leave the
	// context pointing at a different element than the parent focus chain.
	Dictionary parameters;
	if (focus_visible)
		parameters["focus_visible"] = true;
	if (old_document != new_document && new_document)
		document_focus_history.reserve(document_focus_history.size() + 1);

	focus = new_focus;
	if (new_focus) {
		new_focus->focus = nullptr;
		for (Element* element = new_focus; element->GetParentNode(); element = element->GetParentNode())
			element->GetParentNode()->focus = element;
	} else root->focus = nullptr;

	// Update the focus history
	if (old_document != new_document)
	{
		// If documents have changed, add the new document to the end of the history
		ElementList::iterator itr = std::find(document_focus_history.begin(), document_focus_history.end(), new_document);
		if (itr != document_focus_history.end())
			document_focus_history.erase(itr);

		if (new_document != nullptr)
			document_focus_history.push_back(new_document);
	}
	try { SendEvents(old_chain, new_chain, EventId::Blur, parameters); } catch (...) {}
	if (focus == new_focus) {
		try { SendEvents(new_chain, old_chain, EventId::Focus, parameters); } catch (...) {}
	}

	return focus == new_focus;
}

void Context::GenerateClickEvent(Element* element)
{
	Dictionary parameters;
	GenerateMouseEventParameters(parameters, 0);

	element->DispatchEvent(EventId::Click, parameters);
}

void Context::UpdateHoverChain(Vector2i old_mouse_position, int key_modifier_state, Dictionary* out_parameters, Dictionary* out_drag_parameters)
{
	const Vector2f position(mouse_position);

	Dictionary local_parameters, local_drag_parameters;
	Dictionary& parameters = out_parameters ? *out_parameters : local_parameters;
	Dictionary& drag_parameters = out_drag_parameters ? *out_drag_parameters : local_drag_parameters;

	// Generate the parameters for the mouse events (there could be a few!).
	GenerateMouseEventParameters(parameters);
	GenerateKeyModifierEventParameters(parameters, key_modifier_state);

	GenerateMouseEventParameters(drag_parameters);
	GenerateDragEventParameters(drag_parameters);
	GenerateKeyModifierEventParameters(drag_parameters, key_modifier_state);

	// Send out drag events.
	if (drag)
	{
		if (mouse_position != old_mouse_position)
		{
			if (!drag_started)
			{
				Dictionary drag_start_parameters = drag_parameters;
				drag_start_parameters["mouse_x"] = old_mouse_position.x;
				drag_start_parameters["mouse_y"] = old_mouse_position.y;
				drag->DispatchEvent(EventId::Dragstart, drag_start_parameters);
				drag_started = true;

				if (drag->GetComputedValues().drag() == Style::Drag::Clone)
				{
					// Clone the element and attach it to the mouse cursor.
					CreateDragClone(drag);
				}
			}

			drag->DispatchEvent(EventId::Drag, drag_parameters);
		}
	}

	hover = mouse_active ? GetElementAtPoint(position) : nullptr;

	if (enable_cursor)
	{
		String new_cursor_name;

		if (scroll_controller->GetMode() == ScrollController::Mode::Autoscroll)
			new_cursor_name = scroll_controller->GetAutoscrollCursor(mouse_position, density_independent_pixel_ratio);
		else if (drag)
			new_cursor_name = drag->GetComputedValues().cursor();
		else if (hover)
			new_cursor_name = hover->GetComputedValues().cursor();

		if (new_cursor_name != cursor_name)
		{
			GetSystemInterface()->SetMouseCursor(new_cursor_name);
			cursor_name = new_cursor_name;
		}
	}

	// Build the new hover chain.
	ElementSet new_hover_chain;
	Element* element = hover;
	while (element != nullptr)
	{
		new_hover_chain.insert(element);
		element = element->GetParentNode();
	}

	// Send mouseout / mouseover events.
	SendEvents(hover_chain, new_hover_chain, EventId::Mouseout, parameters);
	SendEvents(new_hover_chain, hover_chain, EventId::Mouseover, parameters);

	// Send out drag events.
	if (drag && mouse_active)
	{
		drag_hover = GetElementAtPoint(position, drag);

		ElementSet new_drag_hover_chain;
		element = drag_hover;
		while (element != nullptr)
		{
			new_drag_hover_chain.insert(element);
			element = element->GetParentNode();
		}

		if (drag_started && drag_verbose)
		{
			// Send out ondragover and ondragout events as appropriate.
			SendEvents(drag_hover_chain, new_drag_hover_chain, EventId::Dragout, drag_parameters);
			SendEvents(new_drag_hover_chain, drag_hover_chain, EventId::Dragover, drag_parameters);
		}

		drag_hover_chain.swap(new_drag_hover_chain);
	}

	// Swap the new chain in.
	hover_chain.swap(new_hover_chain);
}

void Context::ResetActiveChain()
{
	// Unset the 'active' pseudo-class on all the elements in the active chain; because they may not necessarily
	// have had 'onmouseup' called on them, we can't guarantee this has happened already.
	for (Element* element : active_chain)
		element->SetPseudoClass("active", false);

	active_chain.clear();
	active = nullptr;
}

int Context::GetTopModalIndex() const
{
	if (root->stacking_context_dirty) root->BuildLocalStackingContext();
	for (int i = int(root->stacking_context.size()) - 1; i >= 0; --i) {
		auto* candidate = root->stacking_context[i];
		auto* document = candidate->GetOwnerDocument();
		if (document == candidate && document->IsPresented() && document->IsVisible(true) && document->IsModal()) return i;
	}
	return -1;
}

bool Context::IsDocumentBlockedByModal(const ElementDocument* document) const
{
	if (document && document->IsFocusableFromModal()) return false;
	// Match the stable document layer/order sort without rebuilding the allocating
	// paint stack. Input cancellation must remain usable during an OOM commit.
	const ElementDocument* top_modal = nullptr;
	size_t modal_index = 0, document_index = root->children.size();
	for (size_t i = 0; i < root->children.size(); ++i) {
		auto* candidate = root->children[i]->GetOwnerDocument();
		if (candidate != root->children[i].get()) continue;
		if (candidate == document) document_index = i;
		if (!candidate->IsPresented() || !candidate->IsVisible(true) || !candidate->IsModal()) continue;
		if (!top_modal || std::make_pair(candidate->GetPresentationLayer(), candidate->GetPresentationOrder()) >=
			std::make_pair(top_modal->GetPresentationLayer(), top_modal->GetPresentationOrder())) {
			top_modal = candidate; modal_index = i;
		}
	}
	if (!top_modal) return false;
	if (!document || document_index == root->children.size()) return true;
	const auto order = std::make_pair(document->GetPresentationLayer(), document->GetPresentationOrder());
	const auto barrier = std::make_pair(top_modal->GetPresentationLayer(), top_modal->GetPresentationOrder());
	return order < barrier || (order == barrier && document_index < modal_index);
}

Element* Context::GetElementAtPoint(Vector2f point, const Element* ignore_element, Element* element) const
{
	if (!element)
	{
		if (ignore_element == root.get())
			return nullptr;

		element = root.get();
	}

	const int modal_index = element == root.get() ? GetTopModalIndex() : -1;

	// Check any elements within our stacking context. We want to return the lowest-down element
	// that is under the cursor.
	if (element->local_stacking_context)
	{
		if (element->stacking_context_dirty)
			element->BuildLocalStackingContext();

		for (int i = (int)element->stacking_context.size() - 1; i >= 0; --i)
		{
			Element* stacking_child = element->stacking_context[i];
			if (ignore_element)
			{
				// Check if the element is a descendant of the element we're ignoring.
				Element* element_hierarchy = stacking_child;
				while (element_hierarchy)
				{
					if (element_hierarchy == ignore_element)
						break;

					element_hierarchy = element_hierarchy->GetParentNode();
				}

				if (element_hierarchy)
					continue;
			}

			if (modal_index >= 0 && i < modal_index)
			{
				ElementDocument* child_document = stacking_child->GetOwnerDocument();
				if (!child_document || !child_document->IsFocusableFromModal())
					continue;
			}

			Element* child_element = GetElementAtPoint(point, ignore_element, stacking_child);
			if (child_element)
				return child_element;
		}
	}

	// A hidden element can still have visible descendants, which were visited above.
	if (!element->IsVisible() || element->effect_input_disabled || element->GetComputedValues().pointer_events() == Style::PointerEvents::None)
		return nullptr;

	// Projection may fail if we have a singular transformation matrix.
	bool projection_result = element->Project(point);

	// Check if the point is actually within this element.
	bool within_element = false;
	Element* hit = element;
	if (auto* text = urui_dynamic_cast<ElementText*>(element)) {
		// Inline text offsets refer to a baseline. A previous intrinsic-size probe's box is not its hit rectangle.
		// Pointer targets are the containing element, as in browser HTML hit testing.
		if (projection_result && text->GetFontFaceHandle()) {
			const auto& metrics = GetFontEngineInterface()->GetFontMetrics(text->GetFontFaceHandle());
			const auto height = text->GetComputedValues().line_height().value;
			const auto baseline_offset = metrics.ascent + (height - metrics.ascent - metrics.descent) * .5f;
			for (const auto& line : text->GetLines()) {
				if (line.text.empty()) continue;
				const auto baseline = text->GetAbsoluteOffset() + line.position;
				const auto width = line.width > 0 ? line.width : ElementUtilities::GetStringWidth(text, line.text);
				if (point.x >= baseline.x && point.x <= baseline.x + width &&
					point.y >= baseline.y - baseline_offset && point.y <= baseline.y - baseline_offset + height) {
					within_element = true; break;
				}
			}
		}
		hit = text->GetParentNode();
	} else within_element = projection_result && element->IsPointWithinElement(point);
	if (hit && hit->effect_input_disabled)
		return nullptr;
	if (within_element)
	{
		// The element may have been clipped out of view if it overflows an ancestor, so check its clipping region.
		Rectanglei clip_region;
		if (ElementUtilities::GetClippingRegion(element, clip_region))
			within_element = clip_region.Contains(Vector2i(point));
	}

	if (within_element)
		return hit;

	return nullptr;
}

void Context::CreateDragClone(Element* element)
{
	ReleaseDragClone();
	auto* document = element->GetOwnerDocument();
	if (!document) return;

	// Instance the drag clone.
	ElementPtr element_drag_clone = element->Clone();
	if (!element_drag_clone)
	{
		Log::Message(Log::LT_ERROR, "Unable to duplicate drag clone.");
		return;
	}

	cursor_proxy = Factory::InstanceElement(nullptr, documents_base_tag, documents_base_tag, ElementAttributes());
	ElementDocument* cursor_proxy_document = urui_dynamic_cast<ElementDocument*>(cursor_proxy.get());
	URUI_ASSERT(cursor_proxy_document);
	cursor_proxy_document->context = this;
	cursor_proxy_document->presented = true;

	// The cursor proxy takes the style from its cloned element's document. The latter may define style rules for `<body>` which we don't want on the
	// proxy. Thus, we override some properties here that we in particular don't want to inherit from the client document, especially those that
	// result in decoration of the body element.
	cursor_proxy_document->SetProperty(PropertyId::BackgroundColor, Property(Colourb(255, 255, 255, 0), Unit::COLOUR));
	cursor_proxy_document->SetProperty(PropertyId::BorderTopWidth, Property(0, Unit::PX));
	cursor_proxy_document->SetProperty(PropertyId::BorderRightWidth, Property(0, Unit::PX));
	cursor_proxy_document->SetProperty(PropertyId::BorderBottomWidth, Property(0, Unit::PX));
	cursor_proxy_document->SetProperty(PropertyId::BorderLeftWidth, Property(0, Unit::PX));
	cursor_proxy_document->SetProperty(PropertyId::Decorator, Property());
	cursor_proxy_document->SetProperty(PropertyId::OverflowX, Property(Style::Overflow::Visible));
	cursor_proxy_document->SetProperty(PropertyId::OverflowY, Property(Style::Overflow::Visible));

	// The proxy is an independent paint document, with the source document's
	// resource/font provenance and a frozen property registry for this drag.
	auto& proxy = *cursor_proxy_document;
	proxy.style_specification = document->GetStyleSpecification().Clone();
	proxy.document_specification = proxy.style_specification;
	proxy.font_context = MakeUnique<DocumentFontContext>(*document->font_context);
	proxy.SetResourceContext(document->GetResourceContext());
	proxy.source_url = document->GetSourceURL();
	proxy.SetStyleSheetContainer(document->style_sheet_container);
	proxy.SetProperty(PropertyId::FontSize, Property(document->GetComputedValues().font_size(), Unit::PX));

	// Preserve inherited values separately from the document root, so rem still
	// refers to the source HTML root while em/family/variables follow the parent.
	auto inheritance = Factory::InstanceElement(&proxy, "*", "urui-drag-context", {});
	if (!inheritance) { cursor_proxy.reset(); return; }
	inheritance->SetProperty(PropertyId::Display, Property(Style::Display::Block));
	inheritance->SetProperty(PropertyId::Position, Property(Style::Position::Relative));
	if (auto* parent = element->GetParentNode()) {
		for (const auto id : document->GetStyleSpecification().GetRegisteredInheritedProperties())
			if (const auto* value = parent->GetProperty(id)) inheritance->SetProperty(id, *value);
		inheritance->SetProperty(PropertyId::FontSize, Property(parent->GetComputedValues().font_size(), Unit::PX));
		UnorderedSet<String> names;
		for (auto* ancestor = parent; ancestor && ancestor->GetOwnerDocument() == document; ancestor = ancestor->GetParentNode())
			ancestor->GetStyle()->AppendCustomPropertyNames(names);
		for (const auto& name : names)
			if (const auto* value = parent->GetProperty(name)) inheritance->GetStyle()->SetCustomProperty(name, *value);
		const auto size = parent->GetBox().GetSize(BoxArea::Content);
		inheritance->SetProperty(PropertyId::Width, Property(size.x, Unit::PX));
		inheritance->SetProperty(PropertyId::Height, Property(size.y, Unit::PX));
	}
	drag_clone = element_drag_clone.get();
	inheritance->AppendChild(std::move(element_drag_clone));
	cursor_proxy->AppendChild(std::move(inheritance));

	// Position the clone. Use projected mouse coordinates to handle any ancestor transforms.
	const Vector2f absolute_pos = element->GetAbsoluteOffset(BoxArea::Border);
	Vector2f projected_mouse_position = Vector2f(mouse_position);
	if (Element* parent = element->GetParentNode())
		parent->Project(projected_mouse_position);

	drag_clone->SetProperty(PropertyId::Position, Property(Style::Position::Absolute));
	drag_clone->SetProperty(PropertyId::Left, Property(absolute_pos.x - projected_mouse_position.x, Unit::PX));
	drag_clone->SetProperty(PropertyId::Top, Property(absolute_pos.y - projected_mouse_position.y, Unit::PX));
	// We remove margins so that percentage- and auto-margins are evaluated correctly.
	drag_clone->SetProperty(PropertyId::MarginLeft, Property(0.f, Unit::PX));
	drag_clone->SetProperty(PropertyId::MarginTop, Property(0.f, Unit::PX));
	drag_clone->SetPseudoClass("drag", true);
}

void Context::ReleaseDragClone()
{
	// Clear the raw pointer before destruction can notify this Context.
	drag_clone = nullptr;
	cursor_proxy.reset();
}

void Context::PerformSmoothscrollOnTarget(Element* target, Vector2f delta_offset, ScrollBehavior scroll_behavior)
{
	scroll_controller->ActivateSmoothscroll(target, delta_offset, scroll_behavior);
}

void Context::GenerateKeyEventParameters(Dictionary& parameters, Input::KeyIdentifier key_identifier)
{
	parameters["key_identifier"] = (int)key_identifier;
}

void Context::GenerateMouseEventParameters(Dictionary& parameters, int button_index)
{
	parameters.reserve(3);
	parameters["mouse_x"] = mouse_position.x;
	parameters["mouse_y"] = mouse_position.y;
	if (button_index >= 0)
		parameters["button"] = button_index;
}

void Context::GenerateKeyModifierEventParameters(Dictionary& parameters, int key_modifier_state)
{
	static const String property_names[] = {"ctrl_key", "shift_key", "alt_key", "meta_key", "caps_lock_key", "num_lock_key", "scroll_lock_key"};

	for (int i = 0; i < 7; i++)
		parameters[property_names[i]] = (int)((key_modifier_state & (1 << i)) > 0);
}

void Context::GenerateDragEventParameters(Dictionary& parameters)
{
	parameters["drag_element"] = (void*)drag;
}

void Context::ReleaseUnloadedDocuments()
{
	if (!unloaded_documents.empty())
	{
		OwnedElementList documents = std::move(unloaded_documents);
		unloaded_documents.clear();

		// Clear the deleted list.
		for (size_t i = 0; i < documents.size(); ++i)
			documents[i]->GetEventDispatcher()->DetachAllEvents();
		documents.clear();
	}
}

using ElementObserverList = Vector<ObserverPtr<Element>>;

class ElementObserverListBackInserter {
public:
	using iterator_category = std::output_iterator_tag;
	using value_type = void;
	using difference_type = void;
	using pointer = void;
	using reference = void;
	using container_type = ElementObserverList;

	ElementObserverListBackInserter(ElementObserverList& elements) : elements(&elements) {}
	ElementObserverListBackInserter& operator=(Element* element)
	{
		elements->push_back(element->GetObserverPtr());
		return *this;
	}
	ElementObserverListBackInserter& operator*() { return *this; }
	ElementObserverListBackInserter& operator++() { return *this; }
	ElementObserverListBackInserter& operator++(int) { return *this; }

private:
	ElementObserverList* elements;
};

void Context::SendEvents(const ElementSet& old_items, const ElementSet& new_items, EventId id, const Dictionary& parameters)
{
	// We put our elements in observer pointers in case some of them are deleted during dispatch.
	ElementObserverList elements;
	std::set_difference(old_items.begin(), old_items.end(), new_items.begin(), new_items.end(), ElementObserverListBackInserter(elements));
	for (auto& element : elements)
	{
		if (element)
			element->DispatchEvent(id, parameters);
	}
}

void Context::Release()
{
	if (instancer)
	{
		instancer->ReleaseContext(this);
	}
}

void Context::SetDocumentsBaseTag(const String& tag)
{
	documents_base_tag = tag;
}

const String& Context::GetDocumentsBaseTag()
{
	return documents_base_tag;
}

void Context::RequestNextUpdate(double delay)
{
	URUI_ASSERT(delay >= 0.0);
	next_update_timeout = Math::Min(next_update_timeout, delay);
}

double Context::GetNextUpdateDelay() const
{
	return next_update_timeout;
}

} // namespace URUI
