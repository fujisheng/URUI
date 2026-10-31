cmake_minimum_required(VERSION 3.21)
project(RmlUnityBridge LANGUAGES C CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Build a self-contained plugin: static RmlUi + static FreeType inside a single .so.
# The included Mac plugin is ABI 2. The inherited Android ABI-1 importer is disabled.
set(BUILD_SHARED_LIBS OFF CACHE BOOL "Static third-party dependencies" FORCE)
set(RMLUI_SAMPLES OFF CACHE BOOL "" FORCE)
set(RMLUI_TESTS OFF CACHE BOOL "" FORCE)
set(RMLUI_LUA_BINDINGS OFF CACHE BOOL "" FORCE)
set(RMLUI_SVG_PLUGIN OFF CACHE BOOL "" FORCE)
set(RMLUI_FONT_ENGINE freetype CACHE STRING "" FORCE)
set(FT_DISABLE_ZLIB ON CACHE BOOL "" FORCE)
set(FT_DISABLE_BZIP2 ON CACHE BOOL "" FORCE)
set(FT_DISABLE_PNG ON CACHE BOOL "" FORCE)
set(FT_DISABLE_HARFBUZZ ON CACHE BOOL "" FORCE)
set(FT_DISABLE_BROTLI ON CACHE BOOL "" FORCE)

include(FetchContent)
FetchContent_Declare(freetype
  GIT_REPOSITORY https://github.com/freetype/freetype.git
  GIT_TAG VER-2-13-3
  GIT_SHALLOW TRUE)
FetchContent_MakeAvailable(freetype)
if(NOT TARGET Freetype::Freetype)
  add_library(Freetype::Freetype ALIAS freetype)
endif()

FetchContent_Declare(rmlui
  GIT_REPOSITORY https://github.com/mikke89/RmlUi.git
  GIT_TAG 6.3
  GIT_SHALLOW TRUE)
FetchContent_MakeAvailable(rmlui)

add_library(RmlUnityBridge SHARED bridge.cpp)
target_link_libraries(RmlUnityBridge PRIVATE RmlUi::RmlUi)
target_compile_definitions(RmlUnityBridge PRIVATE RMLUNITY_EXPORTS)
if(MSVC)
  target_compile_options(RmlUnityBridge PRIVATE /W4 /utf-8)
else()
  target_compile_options(RmlUnityBridge PRIVATE -Wall -Wextra -Wno-unused-parameter)
endif()
set_target_properties(RmlUnityBridge PROPERTIES OUTPUT_NAME RmlUnityBridge)
option(RMLUNITY_STYLE_PROBE "Instrument original string property path (diagnostic only)" OFF)
if(RMLUNITY_STYLE_PROBE)
 include("${CMAKE_CURRENT_SOURCE_DIR}/instrument_style.cmake")
endif()
option(RMLUNITY_LOOKUP_QUEUE "Optional lookup diagnostics/queue experiment" OFF)
if(RMLUNITY_LOOKUP_QUEUE)
 include("${CMAKE_CURRENT_SOURCE_DIR}/lookup_queue.cmake")
endif()
