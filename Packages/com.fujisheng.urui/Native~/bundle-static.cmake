# Package the bridge and its static dependencies as one Unity plugin archive.
set(urui_inputs "${URUI_BRIDGE}" "${URUI_CORE}" "${URUI_DEBUGGER}" "${URUI_FREETYPE}")
if(URUI_APPLE)
  find_program(urui_libtool libtool REQUIRED)
  execute_process(COMMAND "${urui_libtool}" -static -o "${URUI_ARCHIVE}" ${urui_inputs}
    RESULT_VARIABLE urui_result COMMAND_ERROR_IS_FATAL ANY)
else()
  set(urui_mri "CREATE \"${URUI_ARCHIVE}\"\n")
  foreach(urui_input IN LISTS urui_inputs)
    string(APPEND urui_mri "ADDLIB \"${urui_input}\"\n")
  endforeach()
  string(APPEND urui_mri "SAVE\nEND\n")
  file(WRITE "${URUI_ARCHIVE}.mri" "${urui_mri}")
  execute_process(COMMAND "${URUI_AR}" -M INPUT_FILE "${URUI_ARCHIVE}.mri"
    RESULT_VARIABLE urui_result COMMAND_ERROR_IS_FATAL ANY)
endif()
