# Using CMake 3.10 as minimum to support all platforms of interest
# https://github.com/mikke89/RmlUi/issues/198#issuecomment-1246957062
cmake_minimum_required(VERSION 3.10...3.27)

project("URUI"
	VERSION "6.3"
	DESCRIPTION "C++ user interface library based on the HTML and CSS standards"
	LANGUAGES "C" "CXX"
)

set(URUI_ENGINE_VERSION_RELEASE TRUE)

if(CMAKE_PROJECT_NAME STREQUAL PROJECT_NAME)
	set(URUI_IS_ROOT_PROJECT TRUE)
endif()

# Add lists of available options for those that support it
include("CMake/OptionsLists.cmake")

option(BUILD_SHARED_LIBS "CMake standard option. Choose whether to build shared URUI libraries." ON)

# Declare project-specific options. Naming conventions:
#   - Use "URUI_" prefix to make all options specific to this project easily identifiable, and avoid colliding with any parent project variables.
#   - Do not include negations (such as "not" and "disable"), to avoid situations with double negation.
#   - Do not include a verb prefix (such as "enable" and "build"), as these are often superfluous.
set(URUI_FONT_ENGINE "freetype" CACHE STRING "Font engine to build URUI with. If set to \"none\", URUI won't build with any of the included font engines and some samples might be disabled.")
set_property(CACHE URUI_FONT_ENGINE PROPERTY STRINGS ${URUI_FONT_ENGINE_OPTIONS})
if(NOT URUI_FONT_ENGINE IN_LIST URUI_FONT_ENGINE_OPTIONS)
	message(FATAL_ERROR "The URUI font engine '${URUI_FONT_ENGINE}' specified in URUI_FONT_ENGINE is not valid. Available options: ${URUI_FONT_ENGINE_OPTIONS}")
endif()
if(URUI_FONT_ENGINE STREQUAL "none")
	set(URUI_FONT_ENGINE_ENABLED FALSE)
else()
	set(URUI_FONT_ENGINE_ENABLED TRUE)
endif()

option(URUI_THIRDPARTY_CONTAINERS "Enable integrated third-party containers for improved performance, rather than their standard library counterparts." ON)

option(URUI_MATRIX_ROW_MAJOR "Use row-major matrices. Column-major matrices are used by default." OFF)

option(URUI_PRECOMPILED_HEADERS "Enable precompiled headers for URUI." ON)

option(URUI_COMPILER_OPTIONS "Enable recommended compiler-specific options for the project, such as for supported warning level, standards conformance, and multiprocess builds. Turn off for full control over compiler flags." ON)

option(URUI_WARNINGS_AS_ERRORS "Treat compiler warnings as errors." OFF)
mark_as_advanced(URUI_WARNINGS_AS_ERRORS)

option(URUI_TRACY_PROFILING "Enable profiling with Tracy. Source files can optionally be placed in `Dependencies/tracy`." OFF)
if(URUI_TRACY_PROFILING)
	option(URUI_TRACY_MEMORY_PROFILING "Overload global operator new/delete to track memory allocations in Tracy." ON)
endif()
if(URUI_TRACY_PROFILING AND CMAKE_CONFIGURATION_TYPES)
	option(URUI_TRACY_CONFIGURATION "Enable a separate Tracy configuration type for multi-config generators such as Visual Studio, otherwise enable Tracy in all configurations." ON)
endif()

option(URUI_CUSTOM_CONFIGURATION "Customize the URUI configuration file to override the default configuration and types." OFF)
set(URUI_CUSTOM_CONFIGURATION_FILE "" CACHE STRING "Custom configuration file to be included in place of <URUI/Config/Config.h>.")
set(URUI_CUSTOM_INCLUDE_DIRS "" CACHE STRING "Extra include directories (use with URUI_CUSTOM_CONFIGURATION_FILE).")
set(URUI_CUSTOM_LINK_LIBRARIES "" CACHE STRING "Extra link libraries (use with URUI_CUSTOM_CONFIGURATION_FILE).")
if(URUI_CUSTOM_CONFIGURATION)
	mark_as_advanced(CLEAR URUI_CUSTOM_CONFIGURATION_FILE URUI_CUSTOM_INCLUDE_DIRS URUI_CUSTOM_LINK_LIBRARIES)
else()
	mark_as_advanced(FORCE URUI_CUSTOM_CONFIGURATION_FILE URUI_CUSTOM_INCLUDE_DIRS URUI_CUSTOM_LINK_LIBRARIES)
	if(URUI_CUSTOM_CONFIGURATION_FILE OR URUI_CUSTOM_INCLUDE_DIRS OR URUI_CUSTOM_LINK_LIBRARIES)
		message(STATUS "URUI_CUSTOM_CONFIGURATION is disabled, but custom configuration variables are set. They will have no effect.")
	endif()
endif()

set(URUI_INSTALL_TARGETS_DIR "" CACHE STRING "Override the install directory for the generated CMake targets.")
mark_as_advanced(URUI_INSTALL_TARGETS_DIR)
option(URUI_INSTALL_LICENSES_AND_BUILD_INFO "Install license files and build info intended for the github actions workflow." OFF)
mark_as_advanced(URUI_INSTALL_LICENSES_AND_BUILD_INFO)

# Add custom CMake modules path for external dependencies
list(APPEND CMAKE_PREFIX_PATH "${CMAKE_CURRENT_SOURCE_DIR}/Dependencies")

include(GNUInstallDirs)
if(NOT URUI_INSTALL_TARGETS_DIR)
	set(URUI_INSTALL_TARGETS_DIR "${CMAKE_INSTALL_LIBDIR}/cmake/URUI")
endif()

include("CMake/Utilities.cmake")
include("CMake/Dependencies.cmake")

include("CMake/RuntimeUtilities.cmake")
setup_binary_output_directories()
setup_runtime_dependency_set_arg()

add_subdirectory("Source")





# Add an interface library to include all sub-libraries.
# URUI_CMAKE_MINIMUM_VERSION_RAISE_NOTICE:
# From CMake 3.13 we can place target_link_libraries in the same source as each sub-library is declared, see CMP0079.
add_library(urui INTERFACE)
add_library(URUI::URUI ALIAS urui)
target_link_libraries(urui INTERFACE urui_core)
set_target_properties(urui PROPERTIES EXPORT_NAME "URUI")
install(TARGETS urui EXPORT URUITargets)

if(URUI_IS_ROOT_PROJECT)
	# Skip installing any targets when URUI is included using `add_subdirectory`. Otherwise, if any of URUI's linked
	# dependencies are not exported, then this results in errors about the dependency not being in any export set.
	install(EXPORT URUITargets
		DESTINATION "${URUI_INSTALL_TARGETS_DIR}"
		NAMESPACE URUI::
		FILE URUITargets.cmake
	)
endif()

install(DIRECTORY
	"${CMAKE_CURRENT_SOURCE_DIR}/Include/URUI"
	DESTINATION "${CMAKE_INSTALL_INCLUDEDIR}"
)

include(CMakePackageConfigHelpers)

configure_file(
	"${CMAKE_CURRENT_SOURCE_DIR}/CMake/URUIConfig.cmake.in"
	"${CMAKE_CURRENT_BINARY_DIR}/install/URUIConfig.cmake"
	@ONLY
)
write_basic_package_version_file("${CMAKE_CURRENT_BINARY_DIR}/install/URUIConfigVersion.cmake"
	COMPATIBILITY ExactVersion
)
install(FILES
	"${CMAKE_CURRENT_BINARY_DIR}/install/URUIConfig.cmake"
	"${CMAKE_CURRENT_BINARY_DIR}/install/URUIConfigVersion.cmake"
	"${CMAKE_CURRENT_SOURCE_DIR}/CMake/Dependencies.cmake"
	DESTINATION
	"${URUI_INSTALL_TARGETS_DIR}"
)

if(URUI_IS_ROOT_PROJECT)
	# Export build tree targets if URUI is the top-level project.
	export(EXPORT URUITargets
		NAMESPACE URUI::
		FILE URUIConfig.cmake
	)
endif()

include("CMake/PackageUtilities.cmake")
if(URUI_INSTALL_LICENSES_AND_BUILD_INFO)
	install_licenses()
	install_build_info()
endif()
if(URUI_INSTALL_DEPENDENCIES_DIR)
	install_vcpkg_dependencies()
endif()

install_runtime_dependencies()
