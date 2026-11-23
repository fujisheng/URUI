"""The formal bridge has one render-plan protocol and clean ABI v13; retired APIs are absent."""
import ctypes, sys

lib = ctypes.CDLL(sys.argv[1])
retired = [
    'urui_core_frame_api_version', 'urui_core_frame_copy', 'urui_native_frame_acquire',
    'urui_native_frame_api_version', 'urui_native_frame_create', 'urui_native_frame_create_mode',
    'urui_native_frame_destroy', 'urui_native_frame_mesh', 'urui_native_frame_prepare_matrix',
    'urui_native_frame_prepare_matrix_mode', 'urui_native_frame_release', 'urui_prepared_acquire',
    'urui_prepared_api_version', 'urui_prepared_copy_batch', 'urui_prepared_copy_draws',
    'urui_prepared_copy_incremental', 'urui_prepared_create', 'urui_prepared_delta_api_version',
    'urui_prepared_delta_enable', 'urui_prepared_destroy', 'urui_prepared_release',
    # Retired in ABI v13:
    'urui_create', 'urui_destroy', 'urui_last_error', 'urui_render_plan_stamp',
    'urui_get_layer_regions', 'urui_get_effect_input_regions', 'urui_element_effect_expansion',
    'urui_element_effect_transforms', 'urui_effect_ink_bounds', 'urui_get_command_transforms',
    'urui_get_child_count', 'urui_get_child_id_utf8', 'urui_reorder_element_to_end',
    'urui_hit_test_id', 'urui_get_element_box', 'urui_element_exists', 'urui_set_attribute',
    'urui_set_style', 'urui_set_translate_scale_2d', 'urui_set_inner_html', 'urui_append_element',
    'urui_remove_element', 'urui_get_attribute_utf8', 'urui_get_text_utf8', 'urui_set_text',
    'urui_get_scroll_top', 'urui_set_pixel_style', 'urui_pop_event', 'urui_element_count',
    'urui_element_ref_count',
]
remaining = [name for name in retired if hasattr(lib, name)]
assert not remaining, "Retired exports are still present: " + ", ".join(remaining)
assert lib.urui_bridge_abi_version() == 13
assert lib.urui_render_plan_version() == 9
assert lib.urui_element_api_version() == 4
print("Single render-plan ABI13/Plan9: retired exports absent")
