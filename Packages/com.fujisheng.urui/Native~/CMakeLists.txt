cmake_minimum_required(VERSION 3.21)
project(URUIBridge LANGUAGES C CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
option(URUI_DEBUG "Compile native diagnostic probes, profiling and selftests" OFF)

# Build a self-contained plugin with static RmlUi and static FreeType.
# Packaged plugins expose ABI 3 on every target.
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

# Versioned source ships with the package; no network or machine-local cache is needed.
add_subdirectory(ThirdParty/freetype freetype)
if(NOT TARGET Freetype::Freetype)
  add_library(Freetype::Freetype ALIAS freetype)
endif()
add_subdirectory(ThirdParty/RmlUi rmlui)
set(rmlui_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/ThirdParty/RmlUi")

if(EMSCRIPTEN OR CMAKE_SYSTEM_NAME STREQUAL "iOS")
  add_library(URUIBridge STATIC bridge.cpp)
  set_target_properties(URUIBridge PROPERTIES ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_CURRENT_BINARY_DIR}/bridge")
else()
  add_library(URUIBridge SHARED bridge.cpp)
endif()
# The upstream debugger is not part of a production Unity plugin.
target_link_libraries(URUIBridge PRIVATE RmlUi::Core)
target_compile_definitions(URUIBridge PRIVATE URUI_EXPORTS)
if(MSVC)
  target_compile_options(URUIBridge PRIVATE /W4 /utf-8)
else()
  target_compile_options(URUIBridge PRIVATE -Wall -Wextra -Wno-unused-parameter)
endif()
set_target_properties(URUIBridge PROPERTIES OUTPUT_NAME URUIBridge)
if(WIN32)
  set_target_properties(URUIBridge PROPERTIES PREFIX "")
endif()
if(EMSCRIPTEN)
  # Preserve allocation-failure handling in the bridge and its dependencies.
  target_compile_options(URUIBridge PRIVATE -fexceptions)
  target_compile_options(rmlui_core PRIVATE -fexceptions)
endif()
if(URUI_DEBUG)
  target_sources(URUIBridge PRIVATE Debug/diagnostics.cpp)
  target_compile_definitions(URUIBridge PRIVATE URUI_DEBUG=1)
  target_compile_definitions(rmlui_core PRIVATE URUI_DEBUG=1)
  include("${CMAKE_CURRENT_SOURCE_DIR}/Debug/instrument_style.cmake")
  include("${CMAKE_CURRENT_SOURCE_DIR}/Debug/lookup_queue.cmake")
endif()

if(EMSCRIPTEN OR CMAKE_SYSTEM_NAME STREQUAL "iOS")
  set(urui_plugin_archive "${CMAKE_CURRENT_BINARY_DIR}/libURUIBridge.a")
  add_custom_command(OUTPUT "${urui_plugin_archive}"
    COMMAND "${CMAKE_COMMAND}"
      "-DURUI_ARCHIVE=${urui_plugin_archive}"
      "-DURUI_AR=${CMAKE_AR}"
      "-DURUI_APPLE=${APPLE}"
      "-DURUI_BRIDGE=$<TARGET_FILE:URUIBridge>"
      "-DURUI_CORE=$<TARGET_FILE:rmlui_core>"
      "-DURUI_FREETYPE=$<TARGET_FILE:freetype>"
      -P "${CMAKE_CURRENT_SOURCE_DIR}/bundle-static.cmake"
    DEPENDS URUIBridge rmlui_core freetype
      "${CMAKE_CURRENT_SOURCE_DIR}/bundle-static.cmake"
    VERBATIM)
  add_custom_target(URUIBridgePlugin ALL DEPENDS "${urui_plugin_archive}")
endif()

option(URUI_BUILD_TESTS "Build standalone native contract tests" OFF)
if(URUI_BUILD_TESTS)
 enable_testing()
 foreach(test core_frame prepared_batch prepared_delta native_frame debug_gate)
  add_executable(${test}_tests tests/${test}_tests.cpp)
  target_include_directories(${test}_tests PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
  target_compile_options(${test}_tests PRIVATE -UNDEBUG)
  if(URUI_DEBUG)
    target_compile_definitions(${test}_tests PRIVATE URUI_DEBUG=1)
  endif()
  add_test(NAME ${test} COMMAND ${test}_tests)
 endforeach()
endif()
