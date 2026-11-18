#include "HtmlBuilder.h"
#include "CompiledDocument.h"
#include "HtmlParser.h"
#include "../../Include/URUI/Core/Factory.h"
#include "../../Include/URUI/Core/Context.h"
#include "../../Include/URUI/Core/ContextInstancer.h"
#include "../../Include/URUI/Core/Core.h"
#include "../../Include/URUI/Core/ElementDocument.h"
#include "../../Include/URUI/Core/ElementInstancer.h"
#include "../../Include/URUI/Core/ElementText.h"
#include "../../Include/URUI/Core/ElementUtilities.h"
#include "../../Include/URUI/Core/Elements/ElementForm.h"
#include "../../Include/URUI/Core/Elements/ElementFormControlInput.h"
#include "../../Include/URUI/Core/Elements/ElementFormControlSelect.h"
#include "../../Include/URUI/Core/Elements/ElementFormControlTextArea.h"
#include "../../Include/URUI/Core/Elements/ElementProgress.h"
#include "../../Include/URUI/Core/StreamMemory.h"
#include "../../Include/URUI/Core/StyleSheet.h"
#include "../../Include/URUI/Core/StyleSheetContainer.h"
#include "../../Include/URUI/Core/SystemInterface.h"
#include "ContextInstancerDefault.h"
#include "ControlledLifetimeResource.h"
#include "DecoratorGradient.h"
#include "DecoratorNinePatch.h"
#include "DecoratorShader.h"
#include "DecoratorText.h"
#include "DecoratorTiledBox.h"
#include "DecoratorTiledHorizontal.h"
#include "DecoratorTiledImage.h"
#include "DecoratorTiledVertical.h"
#include "Elements/ElementImage.h"
#include "Elements/ElementLabel.h"
#include "Elements/ElementTextSelection.h"
#include "EventInstancerDefault.h"
#include "FilterBasic.h"
#include "FilterBlur.h"
#include "FilterDropShadow.h"
#include "FontEffectBlur.h"
#include "FontEffectGlow.h"
#include "FontEffectOutline.h"
#include "FontEffectShadow.h"
#include "PluginRegistry.h"
#include "StreamFile.h"
#include "StyleSheetFactory.h"
#include <algorithm>

namespace URUI {

namespace {
// 标准button与其余内置节点由同一工厂拥有，保留原桥接表单行为。
class HtmlButton final : public ElementFormControl
{
public:
    explicit HtmlButton(const String& tag):ElementFormControl(tag){}
    String GetValue() const override {return GetAttribute<String>("value", "");}
    void SetValue(const String& value) override {SetAttribute("value", value);}
    bool IsSubmitted() override {return false;}
};
ElementInstancerGeneric<HtmlButton> button_instancer;
}

// Default instancers are constructed and destroyed on Initialise and Shutdown, respectively.
struct DefaultInstancers {
	UniquePtr<ContextInstancer> context_default;
	UniquePtr<EventInstancer> event_default;

	// Basic elements
	ElementInstancerElement element_default;
	ElementInstancerText element_text;
	ElementInstancerGeneric<ElementImage> element_img;
	ElementInstancerGeneric<ElementDocument> element_document;
	ElementInstancerGeneric<ElementTemplate> element_template;
	ElementInstancerGeneric<ElementComment> element_comment;

	// Control elements
	ElementInstancerGeneric<ElementForm> form;
	ElementInstancerGeneric<ElementFormControlInput> input;
	ElementInstancerGeneric<ElementFormControlSelect> select;
	ElementInstancerGeneric<ElementLabel> element_label;

	ElementInstancerGeneric<ElementFormControlTextArea> textarea;
	ElementInstancerGeneric<ElementTextSelection> selection;

	ElementInstancerGeneric<ElementProgress> progress;

	// Decorators
	DecoratorTextInstancer decorator_text;
	DecoratorTiledHorizontalInstancer decorator_tiled_horizontal;
	DecoratorTiledVerticalInstancer decorator_tiled_vertical;
	DecoratorTiledBoxInstancer decorator_tiled_box;
	DecoratorTiledImageInstancer decorator_image;
	DecoratorTiledImageInstancer decorator_css_image{true};
	DecoratorNinePatchInstancer decorator_ninepatch;
	DecoratorShaderInstancer decorator_shader;
	DecoratorStraightGradientInstancer decorator_straight_gradient;
	DecoratorLinearGradientInstancer decorator_linear_gradient;
	DecoratorRadialGradientInstancer decorator_radial_gradient;
	DecoratorConicGradientInstancer decorator_conic_gradient;

	// Filters
	FilterBasicInstancer filter_hue_rotate = {FilterBasicInstancer::ValueType::Angle, "0rad"};
	FilterBasicInstancer filter_basic_d0 = {FilterBasicInstancer::ValueType::NumberPercent, "0"};
	FilterBasicInstancer filter_basic_d1 = {FilterBasicInstancer::ValueType::NumberPercent, "1"};
	FilterBlurInstancer filter_blur;
	FilterDropShadowInstancer filter_drop_shadow;

	// Font effects
	FontEffectBlurInstancer font_effect_blur;
	FontEffectGlowInstancer font_effect_glow;
	FontEffectOutlineInstancer font_effect_outline;
	FontEffectShadowInstancer font_effect_shadow;

};

struct FactoryData {
	DefaultInstancers default_instancers;
	UnorderedMap<String, ElementInstancer*> element_instancers;
	UnorderedMap<String, DecoratorInstancer*> decorator_instancers;
	UnorderedMap<String, FilterInstancer*> filter_instancers;
	UnorderedMap<String, FontEffectInstancer*> font_effect_instancers;
    bool initializing = true, opaque_rendering_instancers = false;
};

static ControlledLifetimeResource<FactoryData> factory_data;

static ContextInstancer* context_instancer = nullptr;
static EventInstancer* event_instancer = nullptr;

Factory::Factory() {}

Factory::~Factory() {}

void Factory::Initialise()
try
{
	factory_data.Initialize();

	DefaultInstancers& default_instancers = factory_data->default_instancers;

	// Default context instancer
	if (!context_instancer)
	{
		default_instancers.context_default = MakeUnique<ContextInstancerDefault>();
		context_instancer = default_instancers.context_default.get();
	}

	// Default event instancer
	if (!event_instancer)
	{
		default_instancers.event_default = MakeUnique<EventInstancerDefault>();
		event_instancer = default_instancers.event_default.get();
	}

	// Basic element instancers
	RegisterElementInstancer("*", &default_instancers.element_default);
	RegisterElementInstancer("img", &default_instancers.element_img);
	RegisterElementInstancer("#text", &default_instancers.element_text);
	RegisterElementInstancer("html", &default_instancers.element_document);
	RegisterElementInstancer("template", &default_instancers.element_template);
	RegisterElementInstancer("#comment", &default_instancers.element_comment);
    RegisterElementInstancer("button", &button_instancer);

	// Control element instancers
	RegisterElementInstancer("form", &default_instancers.form);
	RegisterElementInstancer("input", &default_instancers.input);
	RegisterElementInstancer("select", &default_instancers.select);
	RegisterElementInstancer("label", &default_instancers.element_label);

	RegisterElementInstancer("textarea", &default_instancers.textarea);
	RegisterElementInstancer("#selection", &default_instancers.selection);

	RegisterElementInstancer("progress", &default_instancers.progress);
	RegisterElementInstancer("progressbar", &default_instancers.progress);

	// Decorator instancers
	RegisterDecoratorInstancer("text", &default_instancers.decorator_text);
	RegisterDecoratorInstancer("tiled-horizontal", &default_instancers.decorator_tiled_horizontal);
	RegisterDecoratorInstancer("tiled-vertical", &default_instancers.decorator_tiled_vertical);
	RegisterDecoratorInstancer("tiled-box", &default_instancers.decorator_tiled_box);
	RegisterDecoratorInstancer("image", &default_instancers.decorator_image);
	RegisterDecoratorInstancer("urui-css-image", &default_instancers.decorator_css_image);
	RegisterDecoratorInstancer("ninepatch", &default_instancers.decorator_ninepatch);
	RegisterDecoratorInstancer("shader", &default_instancers.decorator_shader);

	RegisterDecoratorInstancer("gradient", &default_instancers.decorator_straight_gradient);
	RegisterDecoratorInstancer("horizontal-gradient", &default_instancers.decorator_straight_gradient);
	RegisterDecoratorInstancer("vertical-gradient", &default_instancers.decorator_straight_gradient);

	RegisterDecoratorInstancer("linear-gradient", &default_instancers.decorator_linear_gradient);
	RegisterDecoratorInstancer("repeating-linear-gradient", &default_instancers.decorator_linear_gradient);
	RegisterDecoratorInstancer("radial-gradient", &default_instancers.decorator_radial_gradient);
	RegisterDecoratorInstancer("repeating-radial-gradient", &default_instancers.decorator_radial_gradient);
	RegisterDecoratorInstancer("conic-gradient", &default_instancers.decorator_conic_gradient);
	RegisterDecoratorInstancer("repeating-conic-gradient", &default_instancers.decorator_conic_gradient);

	// Filter instancers
	RegisterFilterInstancer("hue-rotate", &default_instancers.filter_hue_rotate);
	RegisterFilterInstancer("brightness", &default_instancers.filter_basic_d1);
	RegisterFilterInstancer("contrast", &default_instancers.filter_basic_d1);
	RegisterFilterInstancer("grayscale", &default_instancers.filter_basic_d0);
	RegisterFilterInstancer("invert", &default_instancers.filter_basic_d0);
	RegisterFilterInstancer("opacity", &default_instancers.filter_basic_d1);
	RegisterFilterInstancer("saturate", &default_instancers.filter_basic_d1);
	RegisterFilterInstancer("sepia", &default_instancers.filter_basic_d0);

	RegisterFilterInstancer("blur", &default_instancers.filter_blur);
	RegisterFilterInstancer("drop-shadow", &default_instancers.filter_drop_shadow);

	// Font effect instancers
	RegisterFontEffectInstancer("blur", &default_instancers.font_effect_blur);
	RegisterFontEffectInstancer("glow", &default_instancers.font_effect_glow);
	RegisterFontEffectInstancer("outline", &default_instancers.font_effect_outline);
	RegisterFontEffectInstancer("dilate", &default_instancers.font_effect_outline);
	RegisterFontEffectInstancer("shadow", &default_instancers.font_effect_shadow);
    factory_data->initializing = false;
}
catch (...)
{
	if (factory_data) Factory::Shutdown();
	throw;
}


void Factory::Shutdown()
{
	context_instancer = nullptr;
	event_instancer = nullptr;

	factory_data.Shutdown();
}

void Factory::RegisterContextInstancer(ContextInstancer* instancer)
{
	context_instancer = instancer;
}

ContextPtr Factory::InstanceContext(const String& name, RenderManager* render_manager, TextInputHandler* text_input_handler)
{
	ContextPtr new_context = context_instancer->InstanceContext(name, render_manager, text_input_handler);
	if (new_context)
		new_context->SetInstancer(context_instancer);
	return new_context;
}

void Factory::RegisterElementInstancer(const String& name, ElementInstancer* instancer)
{
	factory_data->element_instancers[StringUtilities::ToLower(name)] = instancer;
}

ElementInstancer* Factory::GetElementInstancer(const String& tag)
{
	auto instancer_iterator = factory_data->element_instancers.find(tag);
	if (instancer_iterator == factory_data->element_instancers.end())
	{
		instancer_iterator = factory_data->element_instancers.find("*");
		if (instancer_iterator == factory_data->element_instancers.end())
			return nullptr;
	}

	return instancer_iterator->second;
}

ElementPtr Factory::InstanceElement(Element* parent, const String& instancer_name, const String& tag, const ElementAttributes& attributes,
	const PropertyDictionary* compiled_style)
{
	if (ElementInstancer* instancer = GetElementInstancer(instancer_name))
	{
		if (ElementPtr element = instancer->InstanceElement(parent, tag, attributes))
		{
			element->SetInstancer(instancer);
			if (parent) {
				element->SetOwnerDocument(parent->GetOwnerDocument(), false);
				element->document_specification = parent->document_specification;
			}
			element->SetAttributes(attributes, compiled_style);

			PluginRegistry::NotifyElementCreate(element.get());
			return element;
		}
	}

	return nullptr;
}

bool Factory::InstanceElementText(Element* parent, const String& text)
{
	if (!parent) return false;
	auto element = Factory::InstanceElement(parent, "#text", "#text", {});
	auto* text_element = urui_dynamic_cast<ElementText*>(element.get());
	if (!text_element) return false;
	text_element->SetText(text);
	parent->AppendChild(std::move(element));
	return true;
}

bool Factory::InstanceElementStream(Element* parent, Stream* stream)
{
	if (!parent || !stream) return false;
	String input;
	stream->Read(input, stream->Length());
	auto result = ParseHtmlFragment(input, parent->GetTagName(), stream->GetSourceURL().GetURL());
	if (!result) return false;
	Vector<Diagnostic> diagnostics;
	return ReplaceInnerHtml(*parent, *result.document, diagnostics);
}

ElementPtr Factory::InstanceDocumentStream(Context* context, Stream* stream, const String& document_base_tag)
{
	if (!stream || document_base_tag != "html") return nullptr;
	String input;
	stream->Read(input, stream->Length());
	auto result = ParseHtmlDocument(input, stream->GetSourceURL().GetURL());
	if (!result) {
		for (const auto& diagnostic : result.diagnostics)
			Log::Message(Log::LT_ERROR, "%s:%u:%u [%s] %s", diagnostic.sourceUrl.c_str(), diagnostic.span.line,
				diagnostic.span.column, diagnostic.code.c_str(), diagnostic.message.c_str());
		return nullptr;
	}
	Vector<Diagnostic> diagnostics;
	auto element = InstanceParsedHtmlDocument(context, *result.document, diagnostics);
	for (const auto& diagnostic : diagnostics)
		Log::Message(Log::LT_ERROR, "%s:%u:%u [%s] %s", diagnostic.sourceUrl.c_str(), diagnostic.span.line,
			diagnostic.span.column, diagnostic.code.c_str(), diagnostic.message.c_str());
	return element;
}

ElementPtr Factory::InstanceParsedHtmlDocument(Context* context, const HtmlDocument& parsed, Vector<Diagnostic>& diagnostics,
	SharedPtr<ResourceContext> resources)
{
	auto element = Factory::InstanceElement(nullptr, "html", "html", {});
	auto* document = urui_dynamic_cast<ElementDocument*>(element.get());
	if (!document) return nullptr;
	document->context = context;
	document->SetResourceContext(std::move(resources));
	if (!BuildHtmlDocument(*document, parsed, diagnostics)) return nullptr;
	return element;
}

ElementPtr Factory::InstanceCompiledDocument(Context* context, CompiledDocument&& compiled, Vector<Diagnostic>& diagnostics,
	SharedPtr<ResourceContext> resources)
{
	auto element = Factory::InstanceElement(nullptr, "html", "html", {});
	auto* document = urui_dynamic_cast<ElementDocument*>(element.get());
	if (!document) return nullptr;
	document->context = context;
	document->SetResourceContext(std::move(resources));
	if (!BuildCompiledDocument(*document, std::move(compiled), diagnostics)) return nullptr;
	return element;
}

void Factory::RegisterDecoratorInstancer(const String& name, DecoratorInstancer* instancer)
{
	URUI_ASSERT(instancer);
    if(!factory_data->initializing)factory_data->opaque_rendering_instancers=true;
	factory_data->decorator_instancers[StringUtilities::ToLower(name)] = instancer;
}

DecoratorInstancer* Factory::GetDecoratorInstancer(const String& name)
{
	auto iterator = factory_data->decorator_instancers.find(name);
	if (iterator == factory_data->decorator_instancers.end())
		return nullptr;

	return iterator->second;
}

void Factory::RegisterFilterInstancer(const String& name, FilterInstancer* instancer)
{
	URUI_ASSERT(instancer);
    if(!factory_data->initializing)factory_data->opaque_rendering_instancers=true;
	factory_data->filter_instancers[StringUtilities::ToLower(name)] = instancer;
}

FilterInstancer* Factory::GetFilterInstancer(const String& name)
{
	auto iterator = factory_data->filter_instancers.find(name);
	if (iterator == factory_data->filter_instancers.end())
		return nullptr;

	return iterator->second;
}

void Factory::RegisterFontEffectInstancer(const String& name, FontEffectInstancer* instancer)
{
	URUI_ASSERT(instancer);
    if(!factory_data->initializing)factory_data->opaque_rendering_instancers=true;
	factory_data->font_effect_instancers[StringUtilities::ToLower(name)] = instancer;
}

FontEffectInstancer* Factory::GetFontEffectInstancer(const String& name)
{
	auto iterator = factory_data->font_effect_instancers.find(name);
	if (iterator == factory_data->font_effect_instancers.end())
		return nullptr;

	return iterator->second;
}

bool Factory::IsNativeRecordingElementInstancer(const ElementInstancer* instancer)
{
    if(!factory_data||!instancer)return false;
    const auto& defaults=factory_data->default_instancers;
    return instancer==&defaults.element_default||instancer==&defaults.element_text||instancer==&defaults.element_img||
        instancer==&defaults.element_document||instancer==&defaults.element_template||instancer==&defaults.element_comment||
        instancer==&button_instancer||instancer==&defaults.form||instancer==&defaults.element_label||instancer==&defaults.progress;
}
bool Factory::HasOpaqueRenderingInstancers()
{
    return !factory_data||factory_data->opaque_rendering_instancers;
}

SharedPtr<StyleSheetContainer> Factory::InstanceStyleSheetString(const String& string)
{
	auto memory_stream = MakeUnique<StreamMemory>((const byte*)string.c_str(), string.size());
	return InstanceStyleSheetStream(memory_stream.get());
}

SharedPtr<StyleSheetContainer> Factory::InstanceStyleSheetFile(const String& file_name)
{
	auto file_stream = MakeUnique<StreamFile>();
	file_stream->Open(file_name);
	return InstanceStyleSheetStream(file_stream.get());
}

SharedPtr<StyleSheetContainer> Factory::InstanceStyleSheetStream(Stream* stream)
{
	SharedPtr<StyleSheetContainer> style_sheet_container = MakeShared<StyleSheetContainer>();
	if (style_sheet_container->LoadStyleSheetContainer(stream))
	{
		return style_sheet_container;
	}
	return nullptr;
}

void Factory::ClearStyleSheetCache()
{
	StyleSheetFactory::ClearStyleSheetCache();
}

void Factory::RegisterEventInstancer(EventInstancer* instancer)
{
	event_instancer = instancer;
}

EventPtr Factory::InstanceEvent(Element* target, EventId id, const String& type, const Dictionary& parameters, bool interruptible)
{
	EventPtr event = event_instancer->InstanceEvent(target, id, type, parameters, interruptible);
	if (event)
		event->instancer = event_instancer;
	return event;
}

} // namespace URUI
