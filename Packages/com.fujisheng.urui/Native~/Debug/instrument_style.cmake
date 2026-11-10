# Copy/instrument one translation unit in the build tree. Original URUI
# sources remain untouched; original parse/apply statements retain order.
set(style_source "${urui_engine_SOURCE_DIR}/Source/Core/Element.cpp")
file(READ "${style_source}" style_code)
set(parse_original "bool Element::SetProperty(const String& name, const String& value)\n{")
set(parse_probe "${parse_original}\n\turui_style_probe::ParseScope parse_probe;")
string(FIND "${style_code}" "${parse_original}" found_parse)
if(found_parse LESS 0)
 message(FATAL_ERROR "URUI style probe parser anchor changed")
endif()
string(REPLACE "${parse_original}" "${parse_probe}" style_code "${style_code}")
set(apply_original "\tfor (auto& property : properties.GetProperties())\n\t{\n\t\tif (!meta->style.SetProperty")
set(apply_probe [=[
 parse_probe.Complete();
 if (urui_style_probe::active && urui_style_probe::mode == 2) {
  auto& probe = *urui_style_probe::active;
  urui_style_probe::Scope compare(&probe.compare_ns);
  bool all_equal = true, all_supported = true;
  for (auto& p : properties.GetProperties()) {
   ++probe.properties;
   const Property* old = meta->style.GetLocalStyleProperties().GetProperty(p.first);
   bool supported = false;
   bool equal = old && urui_style_probe::Equal(*old, p.second, supported);
   if (old && supported) ++probe.comparable;
   if (equal) ++probe.equal_properties;
   if (old) all_supported &= supported;
   all_equal &= supported && equal;
  }
  if (!properties.GetCustomProperties().empty() || !properties.GetVarShorthands().empty()) { all_supported = false; all_equal = false; }
  if (!all_supported) ++probe.unsupported_calls;
  if (all_equal && !properties.GetProperties().empty()) ++probe.equal_calls;
 }
 urui_style_probe::Scope apply_timer(urui_style_probe::active ? &urui_style_probe::active->apply_ns : nullptr);
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
