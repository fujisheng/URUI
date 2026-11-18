#pragma once

#include "Header.h"

namespace URUI {

class Context;
class ContextInstancer;
class Decorator;
class DecoratorInstancer;
class Element;
class ElementDocument;
class ResourceContext;
class ElementInstancer;
class Event;
class EventInstancer;
class EventListener;
class FontEffect;
class FontEffectInstancer;
class Filter;
class FilterInstancer;
class StyleSheetContainer;
class PropertyDictionary;
class PropertySpecification;
class DecoratorInstancerInterface;
class RenderManager;
class TextInputHandler;
struct HtmlDocument;
struct CompiledDocument;
struct Diagnostic;
enum class EventId : uint16_t;

/**
    The Factory contains a registry of instancers for different types.
 */

class URUI_CORE_API Factory {
public:
	/// Initialise the element factory
	static void Initialise();
	/// Cleanup and shutdown the factory
	static void Shutdown();

	/// Registers a non-owning pointer to the instancer used to instance contexts.
	/// @param[in] instancer The new context instancer.
	/// @lifetime The instancer must be kept alive until after the call to URUI::Shutdown.
	static void RegisterContextInstancer(ContextInstancer* instancer);
	/// Instances a new context.
	/// @param[in] name The name of the new context.
	/// @param[in] render_manager The render manager used for the new context.
	/// @param[in] text_input_handler The text input handler used for the new context.
	/// @return The new context, or nullptr if no context could be created.
	static ContextPtr InstanceContext(const String& name, RenderManager* render_manager, TextInputHandler* text_input_handler);

	/// Registers a non-owning pointer to the element instancer that will be used to instance an element when the specified tag is encountered.
	/// @param[in] name Name of the instancer; elements with this as their tag will use this instancer.
	/// @param[in] instancer The instancer to call when the tag is encountered.
	/// @lifetime The instancer must be kept alive until after the call to URUI::Shutdown.
	static void RegisterElementInstancer(const String& name, ElementInstancer* instancer);
	/// Returns the element instancer for the specified tag.
	/// @param[in] tag Name of the tag to get the instancer for.
	/// @return The requested element instancer, or nullptr if no such instancer is registered.
	static ElementInstancer* GetElementInstancer(const String& tag);
	/// Instances a single element.
	/// @param[in] parent The parent of the new element, or nullptr for a root tag.
	/// @param[in] instancer The name of the instancer to create the element with.
	/// @param[in] tag The tag of the element to be instanced.
	/// @param[in] attributes The attributes to instance the element with.
	/// @return The instanced element, or nullptr if the instancing failed.
	static ElementPtr InstanceElement(Element* parent, const String& instancer, const String& tag, const ElementAttributes& attributes,
		const PropertyDictionary* compiled_style = nullptr);

	/// Instances one pure-text node; HTML syntax is never interpreted or decoded.
	/// @param[in] parent The element any instanced elements will be parented to.
	/// @param[in] text The text to instance the element (or elements) from.
	/// @return True if the string was parsed without error, false otherwise.
	static bool InstanceElementText(Element* parent, const String& text);
	/// Instances an element tree based on the stream.
	/// @param[in] parent The element the stream elements will be added to.
	/// @param[in] stream The stream to read the element HTML from.
	/// @return True if the stream was parsed without error, false otherwise.
	static bool InstanceElementStream(Element* parent, Stream* stream);
	/// Instances a document from a stream.
	/// @param[in] context The context that is creating the document.
	/// @param[in] stream The stream to instance from.
	/// @param[in] document_base_tag The tag used to wrap the document, eg. 'html'.
	/// @return The instanced document, or nullptr if an error occurred.
	static ElementPtr InstanceDocumentStream(Context* context, Stream* stream, const String& document_base_tag);
	static ElementPtr InstanceParsedHtmlDocument(Context* context, const HtmlDocument& document, Vector<Diagnostic>& diagnostics,
		SharedPtr<ResourceContext> resources = nullptr);
	static ElementPtr InstanceCompiledDocument(Context* context, CompiledDocument&& compiled, Vector<Diagnostic>& diagnostics,
		SharedPtr<ResourceContext> resources = nullptr);

	/// Registers a non-owning pointer to an instancer that will be used to instance decorators.
	/// @param[in] name The name of the decorator the instancer will be called for.
	/// @param[in] instancer The instancer to call when the decorator name is encountered.
	/// @lifetime The instancer must be kept alive until after the call to URUI::Shutdown.
	static void RegisterDecoratorInstancer(const String& name, DecoratorInstancer* instancer);
	/// Retrieves a decorator instancer registered with the factory.
	/// @param[in] name The name of the desired decorator type.
	/// @return The decorator instancer if it exists, nullptr otherwise.
	static DecoratorInstancer* GetDecoratorInstancer(const String& name);

	/// Registers a non-owning pointer to an instancer that will be used to instance filters.
	/// @param[in] name The name of the filter the instancer will be called for.
	/// @param[in] instancer The instancer to call when the filter name is encountered.
	/// @lifetime The instancer must be kept alive until after the call to URUI::Shutdown.
	static void RegisterFilterInstancer(const String& name, FilterInstancer* instancer);
	/// Retrieves a filter instancer registered with the factory.
	/// @param[in] name The name of the desired filter type.
	/// @return The filter instancer if it exists, nullptr otherwise.
	static FilterInstancer* GetFilterInstancer(const String& name);

	/// Registers a non-owning pointer to an instancer that will be used to instance font effects.
	/// @param[in] name The name of the font effect the instancer will be called for.
	/// @param[in] instancer The instancer to call when the font effect name is encountered.
	/// @lifetime The instancer must be kept alive until after the call to URUI::Shutdown.
	static void RegisterFontEffectInstancer(const String& name, FontEffectInstancer* instancer);
	/// Retrieves a --urui-font-effect instancer registered with the factory.
	/// @param[in] name The name of the desired --urui-font-effect type.
	/// @return The --urui-font-effect instancer if it exists, nullptr otherwise.
	static FontEffectInstancer* GetFontEffectInstancer(const String& name);
    // 仅识别工厂自有的内置实例，不将用户派生类的OnRender副作用当作静态内容。
    static bool IsNativeRecordingElementInstancer(const ElementInstancer* instancer);
    static bool HasOpaqueRenderingInstancers();

	/// Creates a style sheet from a user-generated string.
	/// @param[in] string The contents of the style sheet.
	/// @return A pointer to the newly created style sheet.
	static SharedPtr<StyleSheetContainer> InstanceStyleSheetString(const String& string);
	/// Creates a style sheet from a file.
	/// @param[in] file_name The location of the style sheet file.
	/// @return A pointer to the newly created style sheet.
	static SharedPtr<StyleSheetContainer> InstanceStyleSheetFile(const String& file_name);
	/// Creates a style sheet from a Stream.
	/// @param[in] stream A pointer to the stream containing the style sheet's contents.
	/// @return A pointer to the newly created style sheet.
	static SharedPtr<StyleSheetContainer> InstanceStyleSheetStream(Stream* stream);
	/// Clears the style sheet cache. This will force style sheets to be reloaded.
	static void ClearStyleSheetCache();

	/// Registers an instancer for all events.
	/// @param[in] instancer The instancer to be called.
	/// @lifetime The instancer must be kept alive until after the call to URUI::Shutdown.
	static void RegisterEventInstancer(EventInstancer* instancer);
	/// Instance an event object
	/// @param[in] target Target element of this event.
	/// @param[in] id ID of this event.
	/// @param[in] type Name of this event type.
	/// @param[in] parameters Additional parameters for this event.
	/// @param[in] interruptible If the event propagation can be stopped.
	/// @return The instanced event.
	static EventPtr InstanceEvent(Element* target, EventId id, const String& type, const Dictionary& parameters, bool interruptible);

private:
	Factory();
	~Factory();
};

} // namespace URUI
