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

include("CMake/Utilities.cmake")
include("CMake/Dependencies.cmake")

add_subdirectory("Source")

add_library(urui INTERFACE)
add_library(URUI::URUI ALIAS urui)
target_link_libraries(urui INTERFACE urui_core)
