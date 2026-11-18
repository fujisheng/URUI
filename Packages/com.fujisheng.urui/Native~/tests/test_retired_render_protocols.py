"""The formal bridge has one render-plan protocol; retired research APIs are absent."""
import ctypes,sys
lib=ctypes.CDLL(sys.argv[1])
retired=['urui_core_frame_api_version', 'urui_core_frame_copy', 'urui_native_frame_acquire', 'urui_native_frame_api_version', 'urui_native_frame_create', 'urui_native_frame_create_mode', 'urui_native_frame_destroy', 'urui_native_frame_mesh', 'urui_native_frame_prepare_matrix', 'urui_native_frame_prepare_matrix_mode', 'urui_native_frame_release', 'urui_prepared_acquire', 'urui_prepared_api_version', 'urui_prepared_copy_batch', 'urui_prepared_copy_draws', 'urui_prepared_copy_incremental', 'urui_prepared_create', 'urui_prepared_delta_api_version', 'urui_prepared_delta_enable', 'urui_prepared_destroy', 'urui_prepared_release']
remaining=[name for name in retired if hasattr(lib,name)]
assert not remaining,"Retired render protocols are still exported: "+", ".join(remaining)
assert lib.urui_bridge_abi_version()==11
assert lib.urui_render_plan_version()==8
print("Single render-plan ABI11: retired exports absent")
