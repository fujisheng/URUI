#[[
	Set up external dependencies required to build URUI itself.

	Packages are configured as soft dependencies (i.e. not REQUIRED), so that a consuming project can declare them
	by other means, without an error being emitted here. For the same reason, instead of relying on variables like
	*_NOTFOUND variables, we check directly for the existence of the target.
]]

if(URUI_FONT_ENGINE STREQUAL "freetype")
	find_package("Freetype")

	if(FREETYPE_VERSION_STRING)
		if(FREETYPE_VERSION_STRING VERSION_EQUAL "2.11.0" AND CMAKE_CXX_COMPILER_ID STREQUAL "MSVC")
			message(WARNING "Using Freetype 2.11.0 with MSVC can cause issues, please upgrade to Freetype 2.11.1 or newer.")
		endif()
	endif()

	report_dependency_found_or_error("Freetype" "Freetype" Freetype::Freetype "Freetype font engine enabled")
endif()





if(NOT URUI_IS_CONFIG_FILE)
	if(URUI_TRACY_PROFILING AND URUI_TRACY_CONFIGURATION)
		enable_configuration_type(Tracy Release ON)
	else()
		enable_configuration_type(Tracy Release OFF)
	endif()
endif()


if(URUI_TRACY_PROFILING)
	find_package(Tracy CONFIG QUIET)

	if(URUI_IS_CONFIG_FILE)
		report_dependency_found_or_error("Tracy" "Tracy" Tracy::TracyClient)
	endif()

	if(NOT TARGET Tracy::TracyClient)
		message(STATUS "Trying to add Tracy from subdirectory 'Dependencies/tracy'.")
		add_subdirectory("Dependencies/tracy")

		if(NOT TARGET Tracy::TracyClient)
			message(FATAL_ERROR "Tracy client not found. Either "
				"(a) make sure target Tracy::TracyClient is available from parent project, "
				"(b) Tracy can be found as a config package, or "
				"(c) Tracy source files are located in 'Dependencies/Tracy'.")
		endif()

		if(URUI_IS_ROOT_PROJECT)
			# Tracy does not export its targets to the build tree. Do that for it here, otherwise CMake will emit an
			# error about target `TracyClient` not being located in any export set.
			export(EXPORT TracyConfig
				NAMESPACE Tracy::
				FILE TracyTargets.cmake
			)
		endif()
	endif()
endif()
