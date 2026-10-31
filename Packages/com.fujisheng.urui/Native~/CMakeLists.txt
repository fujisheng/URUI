cmake_minimum_required(VERSION 3.21)
project(URUIBridge LANGUAGES C CXX)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# Build a self-contained plugin with static RmlUi and static FreeType.
# The packaged macOS bridge exposes ABI 2.
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
target_link_libraries(URUIBridge PRIVATE RmlUi::RmlUi)
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
  target_compile_options(rmlui_debugger PRIVATE -fexceptions)
endif()
option(URUI_STYLE_PROBE "Instrument original string property path (diagnostic only)" OFF)
if(URUI_STYLE_PROBE)
 include("${CMAKE_CURRENT_SOURCE_DIR}/instrument_style.cmake")
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
      "-DURUI_DEBUGGER=$<TARGET_FILE:rmlui_debugger>"
      "-DURUI_FREETYPE=$<TARGET_FILE:freetype>"
      -P "${CMAKE_CURRENT_SOURCE_DIR}/bundle-static.cmake"
    DEPENDS URUIBridge rmlui_core rmlui_debugger freetype
      "${CMAKE_CURRENT_SOURCE_DIR}/bundle-static.cmake"
    VERBATIM)
  add_custom_target(URUIBridgePlugin ALL DEPENDS "${urui_plugin_archive}")
endif()
option(URUI_LOOKUP_QUEUE "Optional lookup diagnostics/queue experiment" OFF)
if(URUI_LOOKUP_QUEUE)
 include("${CMAKE_CURRENT_SOURCE_DIR}/lookup_queue.cmake")
endif()

option(URUI_BUILD_TESTS "Build standalone native contract tests" OFF)
if(URUI_BUILD_TESTS)
 enable_testing()
 foreach(test core_frame prepared_batch prepared_delta native_frame)
  add_executable(${test}_tests tests/${test}_tests.cpp)
  target_include_directories(${test}_tests PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
  target_compile_options(${test}_tests PRIVATE -UNDEBUG)
  add_test(NAME ${test} COMMAND ${test}_tests)
 endforeach()
endif()
