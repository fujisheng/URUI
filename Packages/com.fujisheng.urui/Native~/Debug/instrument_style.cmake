# Copy/instrument one translation unit in the build tree. Original URUI
# sources remain untouched; original parse/apply statements retain order.
set(style_source "${urui_engine_SOURCE_DIR}/Source/Core/Element.cpp")
file(READ "${style_source}" style_code)
set(parse_original "bool Element::SetProperty(const String& name, const String& value)\n    {")
set(parse_probe "${parse_original}\n        URUI::StyleProbe::ParseScope parseProbe;")
string(FIND "${style_code}" "${parse_original}" found_parse)
if(found_parse LESS 0)
 message(FATAL_ERROR "URUI style probe parser anchor changed")
endif()
string(REPLACE "${parse_original}" "${parse_probe}" style_code "${style_code}")
set(apply_original "        for (auto& property : properties.GetProperties())\n        {\n            if (!meta->style.SetProperty")
set(apply_probe [=[
        parseProbe.Complete();
        if (URUI::StyleProbe::active && URUI::StyleProbe::mode == 2)
        {
            auto& probe = *URUI::StyleProbe::active;
            URUI::StyleProbe::Scope compare(&probe.compare_ns);
            bool allEqual = true;
            bool allSupported = true;
            for (auto& entry : properties.GetProperties())
            {
                ++probe.properties;
                const Property* old = meta->style.GetLocalStyleProperties().GetProperty(entry.first);
                bool supported = false;
                bool equal = old && URUI::StyleProbe::Equal(*old, entry.second, supported);
                if (old && supported)
                {
                    ++probe.comparable;
                }
                if (equal)
                {
                    ++probe.equal_properties;
                }
                if (old)
                {
                    allSupported &= supported;
                }
                allEqual &= supported && equal;
            }
            if (!properties.GetCustomProperties().empty() || !properties.GetVarShorthands().empty())
            {
                allSupported = false;
                allEqual = false;
            }
            if (!allSupported)
            {
                ++probe.unsupported_calls;
            }
            if (allEqual && !properties.GetProperties().empty())
            {
                ++probe.equal_calls;
            }
        }
        URUI::StyleProbe::Scope applyTimer(URUI::StyleProbe::active ? &URUI::StyleProbe::active->apply_ns : nullptr);
        for (auto& property : properties.GetProperties())
        {
            if (!meta->style.SetProperty]=])
string(FIND "${style_code}" "${apply_original}" found_apply)
if(found_apply LESS 0)
 message(FATAL_ERROR "URUI style probe apply anchor changed")
endif()
string(REPLACE "${apply_original}" "${apply_probe}" style_code "${style_code}")
# Preserve relative private includes and replace only the public relative path.
string(REPLACE "\"../../Include/URUI/" "\"URUI/" style_code "${style_code}")
set(generated_style "${CMAKE_CURRENT_BINARY_DIR}/Element.styleprobe.cpp")
file(WRITE "${generated_style}" "#include \"Debug/style_probe.h\"\n${style_code}")
get_target_property(core_sources urui_core SOURCES)
list(REMOVE_ITEM core_sources Element.cpp)
set_property(TARGET urui_core PROPERTY SOURCES "${core_sources};${generated_style}")
set_source_files_properties("${generated_style}" TARGET_DIRECTORY urui_core PROPERTIES INCLUDE_DIRECTORIES "${CMAKE_CURRENT_SOURCE_DIR};${urui_engine_SOURCE_DIR}/Source/Core")
