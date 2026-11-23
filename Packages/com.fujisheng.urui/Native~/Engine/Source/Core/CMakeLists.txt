# Declare URUI core library
# Not explicitly setting library type so that it can be chosen by consumer using BUILD_SHARED_LIBS. Header files are not
# necessary, but are included to improve navigation and code completion on IDEs and language servers.
add_library(urui_core
	CssTokenizer.cpp
	CssParser.cpp
	CssImageGeometry.cpp
	DocumentFontContext.cpp
	HtmlBuilder.cpp
	CompiledDocument.cpp
	HtmlParser.cpp
	Box.cpp
	BoxShadowCache.h
	BoxShadowCache.cpp
	BoxShadowHash.h
	CallbackTexture.cpp
	Clock.cpp
	Clock.h
	CompiledFilterShader.cpp
	ComputedValues.cpp
	ComputeProperty.cpp
	ComputeProperty.h
	Context.cpp
	ContextInstancer.cpp
	ContextInstancerDefault.cpp
	ContextInstancerDefault.h
	ControlledLifetimeResource.h
	ConvolutionFilter.cpp
	Core.cpp
	Decorator.cpp
	DecoratorGradient.cpp
	DecoratorGradient.h
	DecoratorNinePatch.cpp
	DecoratorNinePatch.h
	DecoratorShader.cpp
	DecoratorShader.h
	DecoratorText.cpp
	DecoratorText.h
	DecoratorTiled.cpp
	DecoratorTiled.h
	DecoratorTiledBox.cpp
	DecoratorTiledBox.h
	DecoratorTiledHorizontal.cpp
	DecoratorTiledHorizontal.h
	DecoratorTiledImage.cpp
	DecoratorTiledImage.h
	DecoratorTiledVertical.cpp
	DecoratorTiledVertical.h
	DecoratorUtilities.cpp
	DecoratorUtilities.h
	DocumentHeader.h
	EffectSpecification.cpp
	Element.cpp
	ElementAnimation.cpp
	ElementAnimation.h
	ElementBackgroundBorder.cpp
	ElementBackgroundBorder.h
	ElementDefinition.cpp
	ElementDefinition.h
	ElementDocument.cpp
	ElementEffects.cpp
	ElementEffects.h
	ElementInstancer.cpp
	ElementMeta.cpp
	ElementMeta.h
	ElementScroll.cpp
	ElementStyle.cpp
	ElementStyle.h
	ElementText.cpp
	ElementUtilities.cpp
	Event.cpp
	EventDispatcher.cpp
	EventDispatcher.h
	EventInstancer.cpp
	EventInstancerDefault.cpp
	EventInstancerDefault.h
	EventSpecification.cpp
	EventSpecification.h
	Factory.cpp
	FileInterface.cpp
	FileInterfaceDefault.cpp
	FileInterfaceDefault.h
	Filter.cpp
	FilterBasic.cpp
	FilterBasic.h
	FilterBlur.cpp
	FilterBlur.h
	FilterDropShadow.cpp
	FilterDropShadow.h
	FontEffect.cpp
	FontEffectBlur.cpp
	FontEffectBlur.h
	FontEffectGlow.cpp
	FontEffectGlow.h
	FontEffectInstancer.cpp
	FontEffectOutline.cpp
	FontEffectOutline.h
	FontEffectShadow.cpp
	FontEffectShadow.h
	FontEngineInterface.cpp
	Geometry.cpp
	GeometryBackgroundBorder.cpp
	GeometryBackgroundBorder.h
	GeometryBoxShadow.cpp
	GeometryBoxShadow.h
	IdNameMap.h
	Log.cpp
	LogDefault.cpp
	LogDefault.h
	Math.cpp
	Memory.cpp
	Memory.h
	MeshUtilities.cpp
	ObserverPtr.cpp
	Plugin.cpp
	PluginRegistry.cpp
	PluginRegistry.h
	Pool.h
	precompiled.h
	Profiling.cpp
	PropertiesIterator.h
	PropertiesIteratorView.cpp
	Property.cpp
	PropertyDefinition.cpp
	PropertyDictionary.cpp
	PropertyParserAnimation.cpp
	PropertyParserAnimation.h
	PropertyParserBoxShadow.cpp
	PropertyParserBoxShadow.h
	PropertyParserColorStopList.cpp
	PropertyParserColorStopList.h
	PropertyParserColour.cpp
	PropertyParserColour.h
	PropertyParserDecorator.cpp
	PropertyParserDecorator.h
	PropertyParserFilter.cpp
	PropertyParserFilter.h
	PropertyParserFontEffect.cpp
	PropertyParserFontEffect.h
	PropertyParserKeyword.cpp
	PropertyParserKeyword.h
	PropertyParserNumber.cpp
	PropertyParserNumber.h
	PropertyParserRatio.cpp
	PropertyParserRatio.h
	PropertyParserString.cpp
	PropertyParserString.h
	PropertyParserTransform.cpp
	PropertyParserTransform.h
	PropertyShorthandDefinition.h
	PropertySpecification.cpp
	RenderInterface.cpp
	RenderManager.cpp
	RenderManagerAccess.cpp
	RenderManagerAccess.h
	Object.cpp
	ScrollController.cpp
	ScrollController.h
	Spritesheet.cpp
	Stream.cpp
	StreamFile.cpp
	StreamFile.h
	StreamMemory.cpp
	StringUtilities.cpp
	StyleSheet.cpp
	StyleSheetContainer.cpp
	StyleSheetFactory.cpp
	StyleSheetFactory.h
	StyleSheetNode.cpp
	StyleSheetNode.h
	StyleSheetParser.cpp
	StyleSheetParser.h
	StyleSheetSelector.cpp
	StyleSheetSelector.h
	StyleSheetSpecification.cpp
	SystemInterface.cpp
	Texture.cpp
	TextureDatabase.cpp
	TextureDatabase.h
	TextureLayout.cpp
	TextureLayout.h
	TextureLayoutRectangle.cpp
	TextureLayoutRectangle.h
	TextureLayoutRow.cpp
	TextureLayoutRow.h
	TextureLayoutTexture.cpp
	TextureLayoutTexture.h
	Traits.cpp
	Transform.cpp
	TransformPrimitive.cpp
	TransformState.cpp
	TransformState.h
	TransformUtilities.cpp
	TransformUtilities.h
	Tween.cpp
	TypeConverter.cpp
	URL.cpp
	Variant.cpp
	WidgetScroll.cpp
	WidgetScroll.h
)

# Add public headers as files in the project (it's not necessary but convenient for IDE integration)
# Setting them as PRIVATE so that it's addition doesn't propagate, it won't affect availability since
# the entire include directory has already been declared as public
target_sources(urui_core PRIVATE
	"${PROJECT_SOURCE_DIR}/Include/URUI/Config/Config.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Box.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/CallbackTexture.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Colour.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Colour.inl"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/CompiledFilterShader.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ComputedValues.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Containers/itlib/flat_map.hpp"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Containers/itlib/flat_set.hpp"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Containers/robin_hood.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Context.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ContextInstancer.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ConvolutionFilter.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Core.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Debug.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/DecorationTypes.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Decorator.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Dictionary.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/EffectSpecification.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Element.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Element.inl"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ElementDocument.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ElementInstancer.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ElementScroll.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Elements/ElementForm.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Elements/ElementFormControl.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Elements/ElementFormControlInput.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Elements/ElementFormControlSelect.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Elements/ElementFormControlTextArea.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Elements/ElementProgress.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ElementText.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ElementUtilities.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Event.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/EventInstancer.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/EventListener.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Factory.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/FileInterface.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Filter.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/FontEffect.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/FontEffectInstancer.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/FontEngineInterface.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/FontGlyph.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/FontMetrics.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Geometry.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/DrawParameters.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Header.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ID.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Input.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Log.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Math.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Matrix4.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Matrix4.inl"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Mesh.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/MeshUtilities.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/NumericValue.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ObserverPtr.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Platform.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Plugin.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Profiling.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/PropertiesIteratorView.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Property.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/PropertyDefinition.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/PropertyDictionary.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/PropertyIdSet.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/PropertyParser.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/PropertySpecification.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Rectangle.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/RenderBox.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/RenderInterface.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/RenderManager.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Object.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/ScrollTypes.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Span.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Spritesheet.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/StableVector.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Stream.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/StreamMemory.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/StringUtilities.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/StyleSheet.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/StyleSheetContainer.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/StyleSheetSpecification.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/StyleSheetTypes.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/StyleTypes.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/SystemInterface.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/TextInputContext.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/TextInputHandler.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/TextShapingContext.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Texture.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Traits.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Transform.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/TransformPrimitive.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Tween.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/TypeConverter.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/TypeConverter.inl"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Types.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/UniqueRenderResource.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Unit.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/URL.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Utilities.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Variant.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Variant.inl"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Vector2.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Vector2.inl"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Vector3.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Vector3.inl"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Vector4.h"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Vector4.inl"
	"${PROJECT_SOURCE_DIR}/Include/URUI/Core/Vertex.h"
)

set_common_target_options(urui_core)

target_include_directories(urui_core PRIVATE "${PROJECT_SOURCE_DIR}/Include")
target_include_directories(urui_core INTERFACE
	"$<BUILD_INTERFACE:${PROJECT_SOURCE_DIR}/Include>"
	"$<INSTALL_INTERFACE:${CMAKE_INSTALL_INCLUDEDIR}>"
)

add_library(URUI::Core ALIAS urui_core)

set_target_properties(urui_core PROPERTIES
	# Add export name so that it can be exported with a namespaced name instead
	# of using the name we actually used to declare the target
	EXPORT_NAME "Core"

	# Change output name of the final library file
	OUTPUT_NAME "urui"
)

generate_urui_version_string()
target_compile_definitions(urui_core PRIVATE "URUI_ENGINE_VERSION=\"${URUI_ENGINE_VERSION_SHORT}\"")

add_subdirectory("Elements")
add_subdirectory("Layout")

# Set up definitions to export functions and classes as appropriate
get_target_property(urui_core_TYPE urui_core "TYPE")
if(urui_core_TYPE STREQUAL "STATIC_LIBRARY")
	# If URUI is being compiled as a static library, notify code to disable usage of __dllspec()
	# Since public headers also detect it, it needs to be a public definition
	target_compile_definitions(urui_core PUBLIC "URUI_STATIC_LIB")
elseif(urui_core_TYPE STREQUAL "SHARED_LIBRARY")
	# If URUI is being compiled as a shared library, notify code to export functions using __dllspec()
	# For applications consuming the library, the headers will automatically use dllimport
	# Platform and compiler handling is already done by code via pre-processor macros
	target_compile_definitions(urui_core PRIVATE "URUI_CORE_EXPORTS")
endif()
unset(urui_core_TYPE)

if(URUI_FONT_ENGINE STREQUAL "freetype")
	# Include the source files for the default font engine.
	add_subdirectory("FontEngineDefault")

	target_link_libraries(urui_core PRIVATE Freetype::Freetype)
endif()

target_link_libraries(urui_core PRIVATE gumbo)

if(NOT URUI_THIRDPARTY_CONTAINERS)
	target_compile_definitions(urui_core PUBLIC "URUI_NO_THIRDPARTY_CONTAINERS")
	message(STATUS "Disabling third-party containers for URUI.")
endif()

if(URUI_MATRIX_ROW_MAJOR)
	target_compile_definitions(urui_core PUBLIC "URUI_MATRIX_ROW_MAJOR")
	message(STATUS "Configuring URUI to use row-major matrix types.")
endif()

if(URUI_PRECOMPILED_HEADERS AND CMAKE_VERSION VERSION_GREATER_EQUAL "3.16")
	target_precompile_headers(urui_core PRIVATE "${PROJECT_SOURCE_DIR}/Source/Core/precompiled.h")
elseif(URUI_PRECOMPILED_HEADERS)
	message(STATUS "Could not enable precompiled headers, requires CMake version 3.16 or greater.")
endif()
