#include "DocumentFontContext.h"
#include "../../Include/URUI/Core/Core.h"
#include "../../Include/URUI/Core/Context.h"
#include "../../Include/URUI/Core/Element.h"
#include "../../Include/URUI/Core/ElementInstancer.h"
#include "../../Include/URUI/Core/Factory.h"
#include "../../Include/URUI/Core/FileInterface.h"
#include "../../Include/URUI/Core/FontEngineInterface.h"
#include "../../Include/URUI/Core/Plugin.h"
#include "../../Include/URUI/Core/RenderInterface.h"
#include "../../Include/URUI/Core/RenderManager.h"
#include "../../Include/URUI/Core/StyleSheetSpecification.h"
#include "../../Include/URUI/Core/SystemInterface.h"
#include "../../Include/URUI/Core/TextInputHandler.h"
#include "../../Include/URUI/Core/Types.h"
#include "BoxShadowCache.h"
#include "ComputeProperty.h"
#include "ControlledLifetimeResource.h"
#include "ElementMeta.h"
#include "ElementStyle.h"
#include "EventSpecification.h"
#include "FileInterfaceDefault.h"
#include "Layout/LayoutPools.h"
#include "PluginRegistry.h"
#include "RenderManagerAccess.h"
#include "StyleSheetFactory.h"
#include "StyleSheetParser.h"

#ifdef URUI_FONT_ENGINE_FREETYPE
	#include "FontEngineDefault/FontEngineInterfaceDefault.h"
#endif



#include <algorithm>

namespace URUI {

static RenderInterface* render_interface = nullptr;
static SystemInterface* system_interface = nullptr;
static FileInterface* file_interface = nullptr;
static FontEngineInterface* font_interface = nullptr;
static TextInputHandler* text_input_handler = nullptr;

struct CoreData {
	// Default interfaces should be created and destroyed on Initialise and Shutdown, respectively.
	UniquePtr<SystemInterface> default_system_interface;
	UniquePtr<FileInterface> default_file_interface;
	UniquePtr<FontEngineInterface> default_font_interface;
	UniquePtr<TextInputHandler> default_text_input_handler;

	SmallUnorderedMap<RenderInterface*, UniquePtr<RenderManager>> render_managers;
	UnorderedMap<String, ContextPtr> contexts;
};

static ControlledLifetimeResource<CoreData> core_data;

static bool initialised = false;

enum class InitializationStep {
	None, ElementPools, MetaPool, LayoutPools, ComputeProperties, CoreData,
	Events, Observers, Font, FontSources, ElementStyle, StyleSpecification,
	StyleParser, StyleFactory, Factory, Shadows, Plugins
};
static InitializationStep initialization_step = InitializationStep::None;
static void ReleaseCore();

#ifndef URUI_ENGINE_VERSION
	#define URUI_ENGINE_VERSION "custom"
#endif

bool Initialise()
{
	URUI_ASSERTMSG(!initialised, "URUI::Initialise() called, but URUI is already initialised!");
	// Keep caller-owned interfaces available if initialization cannot complete.
	auto* previous_render = render_interface;
	auto* previous_system = system_interface;
	auto* previous_file = file_interface;
	auto* previous_font = font_interface;
	auto* previous_input = text_input_handler;
	auto rollback = [&] {
		ReleaseCore();
		render_interface = previous_render;
		system_interface = previous_system;
		file_interface = previous_file;
		font_interface = previous_font;
		text_input_handler = previous_input;
	};
	try
	{
		Detail::InitializeElementInstancerPools();
		initialization_step = InitializationStep::ElementPools;
		ElementMetaPool::Initialize();
		initialization_step = InitializationStep::MetaPool;
		LayoutPools::Initialize();
		initialization_step = InitializationStep::LayoutPools;
		InitializeComputeProperty();
		initialization_step = InitializationStep::ComputeProperties;
		core_data.Initialize();
		initialization_step = InitializationStep::CoreData;

		if (!system_interface)
		{
			core_data->default_system_interface = MakeUnique<SystemInterface>();
			system_interface = core_data->default_system_interface.get();
		}
		if (!file_interface)
		{
#ifndef URUI_NO_FILE_INTERFACE_DEFAULT
			core_data->default_file_interface = MakeUnique<FileInterfaceDefault>();
			file_interface = core_data->default_file_interface.get();
#else
			Log::Message(Log::LT_ERROR, "No file interface set!");
			rollback();
			return false;
#endif
		}
		if (!font_interface)
		{
#ifdef URUI_FONT_ENGINE_FREETYPE
			core_data->default_font_interface = MakeUnique<FontEngineInterfaceDefault>();
			font_interface = core_data->default_font_interface.get();
#else
			Log::Message(Log::LT_ERROR, "No font engine interface set!");
			rollback();
			return false;
#endif
		}
		if (!text_input_handler)
		{
			core_data->default_text_input_handler = MakeUnique<TextInputHandler>();
			text_input_handler = core_data->default_text_input_handler.get();
		}
		EventSpecificationInterface::Initialize();
		initialization_step = InitializationStep::Events;
		Detail::InitializeObserverPtrPool();
		initialization_step = InitializationStep::Observers;
		if (render_interface)
			core_data->render_managers[render_interface] = MakeUnique<RenderManager>(render_interface);
		font_interface->Initialize();
		initialization_step = InitializationStep::Font;
		DocumentFontContext::InitializeSourceCache();
		initialization_step = InitializationStep::FontSources;
		ElementStyle::Initialize();
		initialization_step = InitializationStep::ElementStyle;
		StyleSheetSpecification::Initialise();
		initialization_step = InitializationStep::StyleSpecification;
		StyleSheetParser::Initialise();
		initialization_step = InitializationStep::StyleParser;
		StyleSheetFactory::Initialise();
		initialization_step = InitializationStep::StyleFactory;
		Factory::Initialise();
		initialization_step = InitializationStep::Factory;
		BoxShadowCache::Initialize();
		initialization_step = InitializationStep::Shadows;
		// A plugin callback may throw after registering its own resources.
		initialization_step = InitializationStep::Plugins;
		PluginRegistry::NotifyInitialise();
		initialised = true;
		return true;
	}
	catch (...)
	{
		rollback();
		throw;
	}
}

static void ReleaseCore()
{
	const auto step = initialization_step;
	if (core_data) core_data->contexts.clear();
	if (step >= InitializationStep::Plugins) PluginRegistry::NotifyShutdown();
	if (step >= InitializationStep::Shadows) BoxShadowCache::Shutdown();
	if (step >= InitializationStep::Factory) Factory::Shutdown();
	if (step >= InitializationStep::StyleFactory) StyleSheetFactory::Shutdown();
	if (step >= InitializationStep::StyleParser) StyleSheetParser::Shutdown();
	if (step >= InitializationStep::StyleSpecification) StyleSheetSpecification::Shutdown();
	if (step >= InitializationStep::ElementStyle) ElementStyle::Shutdown();
	if (step >= InitializationStep::Font) font_interface->Shutdown();
	if (step >= InitializationStep::FontSources) DocumentFontContext::ShutdownSourceCache();
	if (core_data) core_data->render_managers.clear();
	if (step >= InitializationStep::Observers) Detail::ShutdownObserverPtrPool();
	initialised = false;
	text_input_handler = nullptr;
	font_interface = nullptr;
	render_interface = nullptr;
	file_interface = nullptr;
	system_interface = nullptr;
	if (core_data) core_data.Shutdown();
	if (step >= InitializationStep::Events) EventSpecificationInterface::Shutdown();
	if (step >= InitializationStep::ComputeProperties) ShutdownComputeProperty();
	if (step >= InitializationStep::LayoutPools) LayoutPools::Shutdown();
	if (step >= InitializationStep::MetaPool) ElementMetaPool::Shutdown();
	if (step >= InitializationStep::ElementPools) Detail::ShutdownElementInstancerPools();
	initialization_step = InitializationStep::None;
}

void Shutdown()
{
	URUI_ASSERTMSG(initialised, "URUI::Shutdown() called, but URUI is not initialised!");
	ReleaseCore();
}

String GetVersion()
{
	return URUI_ENGINE_VERSION;
}

void SetSystemInterface(SystemInterface* _system_interface)
{
	system_interface = _system_interface;
}

SystemInterface* GetSystemInterface()
{
	return system_interface;
}

void SetRenderInterface(RenderInterface* _render_interface)
{
	render_interface = _render_interface;
}

RenderInterface* GetRenderInterface()
{
	return render_interface;
}

void SetFileInterface(FileInterface* _file_interface)
{
	file_interface = _file_interface;
}

FileInterface* GetFileInterface()
{
	return file_interface;
}

void SetFontEngineInterface(FontEngineInterface* _font_interface)
{
	font_interface = _font_interface;
}

FontEngineInterface* GetFontEngineInterface()
{
	return font_interface;
}

void SetTextInputHandler(TextInputHandler* _text_input_handler)
{
	text_input_handler = _text_input_handler;
}

TextInputHandler* GetTextInputHandler()
{
	return text_input_handler;
}

Context* CreateContext(const String& name, const Vector2i dimensions, RenderInterface* render_interface_for_context,
	TextInputHandler* text_input_handler_for_context)
{
	if (!initialised)
		return nullptr;

	if (!render_interface_for_context)
		render_interface_for_context = render_interface;

	if (!text_input_handler_for_context)
		text_input_handler_for_context = text_input_handler;

	if (!render_interface_for_context)
	{
		Log::Message(Log::LT_WARNING, "Failed to create context '%s', no render interface specified and no default render interface exists.",
			name.c_str());
		return nullptr;
	}

	if (GetContext(name))
	{
		Log::Message(Log::LT_WARNING, "Failed to create context '%s', context already exists.", name.c_str());
		return nullptr;
	}

	// Each unique render interface gets its own render manager.
	auto& render_manager = core_data->render_managers[render_interface_for_context];
	if (!render_manager)
		render_manager = MakeUnique<RenderManager>(render_interface_for_context);

	ContextPtr new_context = Factory::InstanceContext(name, render_manager.get(), text_input_handler_for_context);
	if (!new_context)
	{
		Log::Message(Log::LT_WARNING, "Failed to instance context '%s', instancer returned nullptr.", name.c_str());
		return nullptr;
	}

	new_context->SetDimensions(dimensions);

	Context* new_context_raw = new_context.get();
	core_data->contexts[name] = std::move(new_context);

	PluginRegistry::NotifyContextCreate(new_context_raw);

	return new_context_raw;
}

bool RemoveContext(const String& name)
{
	return core_data->contexts.erase(name) != 0;
}

Context* GetContext(const String& name)
{
	auto it = core_data->contexts.find(name);
	if (it == core_data->contexts.end())
		return nullptr;

	return it->second.get();
}

Context* GetContext(int index)
{
	if (index < 0 || index >= GetNumContexts())
		return nullptr;

	auto it = core_data->contexts.begin();
	std::advance(it, index);

	if (it == core_data->contexts.end())
		return nullptr;

	return it->second.get();
}

int GetNumContexts()
{
	return (int)core_data->contexts.size();
}

bool LoadFontFace(const String& file_path, bool fallback_face, Style::FontWeight weight, int face_index)
{
	return font_interface->LoadFontFace(file_path, face_index, fallback_face, weight);
}

bool LoadFontFace(const String& file_path, const String& family, Style::FontStyle style, Style::FontWeight weight, bool fallback_face, int face_index)
{
	return font_interface->LoadFontFace(file_path, face_index, family, style, weight, fallback_face);
}

bool LoadFontFace(Span<const byte> data, const String& family, Style::FontStyle style, Style::FontWeight weight, bool fallback_face, int face_index)
{
	return font_interface->LoadFontFace(data, face_index, family, style, weight, fallback_face);
}

void RegisterPlugin(Plugin* plugin)
{
	if (initialised)
		plugin->OnInitialise();

	PluginRegistry::RegisterPlugin(plugin);
}

void UnregisterPlugin(Plugin* plugin)
{
	PluginRegistry::UnregisterPlugin(plugin);

	if (initialised)
		plugin->OnShutdown();
}

EventId RegisterEventType(const String& type, bool interruptible, bool bubbles, DefaultActionPhase default_action_phase)
{
	return EventSpecificationInterface::InsertOrReplaceCustom(type, interruptible, bubbles, default_action_phase);
}

StringList GetTextureSourceList()
{
	StringList result;
	if (!core_data)
		return result;
	for (const auto& render_manager : core_data->render_managers)
	{
		RenderManagerAccess::GetTextureSourceList(render_manager.second.get(), result);
	}
	return result;
}

void ReleaseTextures(RenderInterface* match_render_interface)
{
	if (!core_data)
		return;
	for (auto& render_manager : core_data->render_managers)
	{
		if (!match_render_interface || render_manager.first == match_render_interface)
			RenderManagerAccess::ReleaseAllTextures(render_manager.second.get());
	}
}

bool ReleaseTexture(const String& source, RenderInterface* match_render_interface)
{
	bool result = false;
	if (!core_data)
		return result;
	for (auto& render_manager : core_data->render_managers)
	{
		if (!match_render_interface || render_manager.first == match_render_interface)
		{
			if (RenderManagerAccess::ReleaseTexture(render_manager.second.get(), source))
				result = true;
		}
	}
	return result;
}

void ReleaseCompiledGeometry(RenderInterface* match_render_interface)
{
	if (!core_data)
		return;
	for (auto& render_manager : core_data->render_managers)
	{
		if (!match_render_interface || render_manager.first == match_render_interface)
			RenderManagerAccess::ReleaseAllCompiledGeometry(render_manager.second.get());
	}
}

void ReleaseFontResources()
{
	if (!font_interface)
		return;

	for (const auto& name_context : core_data->contexts)
		name_context.second->GetRootElement()->DirtyFontFaceRecursive();

	font_interface->ReleaseFontResources();

	for (const auto& name_context : core_data->contexts)
		name_context.second->Update();
}

void ReleaseRenderManagers()
{
	auto& contexts = core_data->contexts;
	auto& render_managers = core_data->render_managers;

	ReleaseFontResources();

	for (auto it = render_managers.begin(); it != render_managers.end();)
	{
		RenderManager* render_manager = it->second.get();
		const auto num_contexts_using_manager = std::count_if(contexts.begin(), contexts.end(),
			[&](const auto& context_pair) { return &context_pair.second->GetRenderManager() == render_manager; });

		if (num_contexts_using_manager == 0)
			it = render_managers.erase(it);
		else
			++it;
	}
}

// Functions that need to be accessible within the Core library, but not publicly.
namespace CoreInternal {

	bool HasRenderManager(RenderInterface* match_render_interface)
	{
		return core_data && core_data->render_managers.find(match_render_interface) != core_data->render_managers.end();
	}

} // namespace CoreInternal

} // namespace URUI
