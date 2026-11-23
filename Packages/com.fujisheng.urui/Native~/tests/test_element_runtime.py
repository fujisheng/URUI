"""Element runtime contracts against the real native library; no Unity/Player claims."""
import ctypes as C
from pathlib import Path
import sys
import threading
import unittest

LIBRARY = Path(sys.argv.pop(1)).resolve()
LIB = C.CDLL(str(LIBRARY))
U, I, F, S, P = C.c_uint64, C.c_int, C.c_float, C.c_char_p, C.c_void_p


class Event(C.Structure):
    _fields_ = [("surface", U), ("document", U), ("target", U), ("current", U), ("type", I), ("phase", I),
                ("pointer", I), ("button", I), ("buttons", I), ("modifiers", I),
                ("key", I), ("kind", I), ("x", F), ("y", F), ("dx", F), ("dy", F), ("text", S)]


assert C.sizeof(Event) == 88 and Event.text.offset == 80

CALLBACK = C.CFUNCTYPE(I, C.POINTER(Event))


class ElementRuntime(unittest.TestCase):
    def fn(self, name, result, *args):
        value = getattr(LIB, name, None)
        self.assertIsNotNone(value, "Missing element runtime capability: " + name)
        value.restype, value.argtypes = result, list(args)
        return value

    def setUp(self):
        self.assertEqual(self.fn("urui_bridge_abi_version", I)(), 13)
        self.assertEqual(self.fn("urui_element_api_version", I)(), 4)
        surface = U()
        self.assertEqual(self.fn("urui_surface_create", I, I, I, P)(640, 480, C.byref(surface)), 1)
        self.surface = surface.value
        self.addCleanup(self.cleanup_surface)
        markup = b"""<!doctype html><html><head><style>@font-face{font-family:Acceptance;src:url('/runtime-font.ttf');}body{margin:0;font-family:Acceptance;}#panel{display:block;position:absolute;left:20px;top:30px;width:200px;height:120px;}#child{display:block;width:50%;height:40px;}</style></head><body><div id='panel'><div id='child'></div><input id='text' type='text' value='hello'/><input id='check' type='checkbox'/></div></body></html>"""
        owner = U()
        self.assertEqual(self.fn("urui_document_create", I, U, P)(self.surface, C.byref(owner)), 1)
        self.owner = owner.value
        font = (Path(__file__).parents[2] / "Runtime/Resources/URUI/Fonts/LiberationSans-Regular.ttf.bytes").read_bytes()
        buffer = C.create_string_buffer(font)
        self.assertEqual(self.fn("urui_document_register_resource", I, U, U, S, P, I)(self.surface, self.owner, b"/runtime-font.ttf", buffer, len(font)), 1)
        self.assertEqual(self.fn("urui_document_load_html", I, U, U, S, S)(self.surface, self.owner, markup, b"/runtime.html"), 1)
        self.assertEqual(self.fn("urui_document_set_presentation", I, U, U, I, I, I, I)(self.surface, self.owner, 0, 0, 1, 0), 1)
        self.root = self.fn("urui_document_root", U, U, U)(self.surface, self.owner)
        self.find = self.fn("urui_element_find", U, U, U, U, S, S)
        self.assertEqual(self.fn("urui_surface_update", I, U)(self.surface), 1)

    def cleanup_surface(self):
        if self.surface:
            self.fn("urui_surface_destroy", I, U)(self.surface)
            self.surface = 0

    def token(self, name):
        value = self.find(self.surface, self.owner, self.root, name.encode(), None)
        self.assertNotIn(value, [0, (1 << 64) - 1])
        return value

    def read(self, fn, *args):
        required = I()
        self.assertEqual(fn(*args, None, 0, C.byref(required)), -5)
        out = C.create_string_buffer(required.value)
        self.assertEqual(fn(*args, out, len(out), C.byref(required)), 1)
        return out.value.decode()

    def test_percentage_layout_and_style_sources(self):
        token = self.token("child")
        box = (F * 16)()
        metrics = self.fn("urui_element_box", I, U, U, U, P, I)
        self.assertEqual(metrics(self.surface, self.owner, token, box, 16), 1)
        self.assertAlmostEqual(box[2], 100, places=2)
        self.assertAlmostEqual(box[3], 40, places=2)
        self.assertAlmostEqual(box[8], 20, places=2)
        style = self.fn("urui_element_style_read", I, U, U, U, I, S, P, I, P)
        required = I()
        self.assertEqual(style(self.surface, self.owner, token, 1, b"width", None, 0, C.byref(required)), 0)
        self.assertEqual(self.fn("urui_element_write", I, U, U, U, I, S, S)(self.surface, self.owner, token, 4, b"width", b"75%"), 1)
        self.assertEqual(self.read(style, self.surface, self.owner, token, 1, b"width"), "75%")
        self.fn("urui_surface_update", I, U)(self.surface)
        self.assertEqual(metrics(self.surface, self.owner, token, box, 16), 1)
        self.assertAlmostEqual(box[2], 150, places=2)
        self.assertEqual(self.fn("urui_element_erase", I, U, U, U, I, S)(self.surface, self.owner, token, 1, b"width"), 1)
        self.fn("urui_surface_update", I, U)(self.surface)
        metrics(self.surface, self.owner, token, box, 16)
        self.assertAlmostEqual(box[2], 100, places=2)

    def test_refresh_state_matches_layout_and_preserves_token_ownership(self):
        state = self.fn("urui_element_refresh_state", I, U, U, U, P, I)
        token = self.token("child")
        actual, box = (F * 4)(), (F * 16)()
        self.assertEqual(state(self.surface, self.owner, token, actual, 4), 3)
        self.assertEqual(self.fn("urui_element_box", I, U, U, U, P, I)(self.surface, self.owner, token, box, 16), 1)
        self.assertEqual(list(actual), list(box)[:4])
        self.assertEqual(state(self.surface, self.owner, token, actual, 3), -1)
        self.assertEqual(state(self.surface, self.owner, token, None, 4), -1)
        other = U()
        self.assertEqual(self.fn("urui_document_create", I, U, P)(self.surface, C.byref(other)), 1)
        self.assertEqual(state(self.surface, other.value, token, actual, 4), 0)
        self.assertEqual(self.fn("urui_element_detach", I, U, U, U)(self.surface, self.owner, token), 1)
        self.assertEqual(state(self.surface, self.owner, token, actual, 4), 1)
        self.assertEqual(self.fn("urui_element_destroy", I, U, U, U)(self.surface, self.owner, token), 1)
        self.assertEqual(state(self.surface, self.owner, token, actual, 4), 0)

    def test_refresh_stamp_tracks_mutations_and_rejects_foreign_thread(self):
        class Stamp(C.Structure):
            _fields_ = [("mutation", U), ("references", U)]
        self.assertEqual(C.sizeof(Stamp), 16)
        read = self.fn("urui_element_refresh_stamp", I, U, C.POINTER(Stamp))
        first, next_value = Stamp(), Stamp()
        self.assertEqual(read(self.surface, C.byref(first)), 1)
        self.assertGreater(first.mutation, 0)
        self.assertGreater(first.references, 0)
        self.fn("urui_surface_update", I, U)(self.surface)
        self.assertEqual(read(self.surface, C.byref(next_value)), 1)
        self.assertEqual(bytes(first), bytes(next_value))
        token = self.token("child")
        self.assertEqual(self.fn("urui_element_write", I, U, U, U, I, S, S)(self.surface, self.owner, token, 4, b"width", b"75%"), 1)
        self.fn("urui_surface_update", I, U)(self.surface)
        self.assertEqual(read(self.surface, C.byref(next_value)), 1)
        self.assertNotEqual(first.mutation, next_value.mutation)
        first = next_value
        self.assertEqual(self.fn("urui_element_ref_release", I, U, U, U)(self.surface, self.owner, token), 1)
        next_value = Stamp()
        self.assertEqual(read(self.surface, C.byref(next_value)), 1)
        self.assertNotEqual(first.references, next_value.references)
        foreign, result = Stamp(99, 99), []
        worker = threading.Thread(target=lambda: result.append(read(self.surface, C.byref(foreign))))
        worker.start()
        worker.join()
        self.assertEqual(result, [-3])
        self.assertEqual(bytes(foreign), bytes(Stamp()))
        self.assertEqual(read(self.surface, None), -1)
        stale_surface = self.surface
        self.cleanup_surface()
        foreign = Stamp(99, 99)
        self.assertEqual(read(stale_surface, C.byref(foreign)), -2)
        self.assertEqual(bytes(foreign), bytes(Stamp()))

    def test_focus_and_real_form_value(self):
        text = self.token("text")
        focus = self.fn("urui_element_focus", I, U, U, U, I)
        self.assertEqual(focus(self.surface, self.owner, text, 1), 1)
        self.assertEqual(self.fn("urui_focused_element", U, U, U)(self.surface, self.owner), text)
        read = self.fn("urui_control_value_read", I, U, U, U, P, I, P)
        write = self.fn("urui_control_value_write", I, U, U, U, S)
        self.assertEqual(self.read(read, self.surface, self.owner, text), "hello")
        self.assertEqual(write(self.surface, self.owner, text, "中文 🎮".encode()), 1)
        self.assertEqual(self.read(read, self.surface, self.owner, text), "中文 🎮")
        check = self.token("check")
        self.assertEqual(self.read(read, self.surface, self.owner, check), "false")
        self.assertEqual(write(self.surface, self.owner, check, b"true"), 1)
        self.assertEqual(self.read(read, self.surface, self.owner, check), "true")
        self.assertEqual(focus(self.surface, self.owner, text, 0), 1)
        self.assertEqual(self.fn("urui_focused_element", U, U, U)(self.surface, self.owner), 0)

    def test_pick_has_no_name_requirement_and_rejects_invalid_geometry(self):
        child = self.token("child")
        self.assertEqual(self.fn("urui_element_write", I, U, U, U, I, S, S)(self.surface, self.owner, child, 0, None, b""), 1)
        pick = self.fn("urui_element_pick", U, U, U, F, F)
        self.assertEqual(pick(self.surface, self.owner, 25, 35), child)
        self.assertEqual(pick(self.surface, self.owner, float("nan"), 35), 0)
        metrics = self.fn("urui_element_box", I, U, U, U, P, I)
        self.assertLess(metrics(self.surface, self.owner, 0, (F * 16)(), 16), 0)
        self.assertLess(metrics(self.surface, self.owner, child, (F * 8)(), 8), 0)
        result = []
        thread = threading.Thread(target=lambda: result.append(metrics(self.surface, self.owner, child, (F * 16)(), 16)))
        thread.start()
        thread.join()
        self.assertLess(result[0], 0)

    def listen(self, token, event_type, capture=0):
        self.assertEqual(self.fn("urui_element_listen", I, U, U, U, I, I, I)(self.surface, self.owner, token, event_type, capture, 1), 1)

    def callback(self, receive):
        self.event_owners = []
        def dispatch(pointer):
            self.assertEqual(pointer.contents.surface, self.surface)
            self.event_owners.append(pointer.contents.document)
            return receive(pointer)
        callback = CALLBACK(dispatch)
        self.addCleanup(lambda: self.assertTrue(all(owner == self.owner for owner in self.event_owners), "Event document ownership"))
        self.addCleanup(lambda: self.surface and self.fn("urui_set_event_callback", I, U, P)(self.surface, None))
        self.assertEqual(self.fn("urui_set_event_callback", I, U, P)(self.surface, callback), 1)
        self.keep_callback = callback

    def pointer(self, phase, x=25, y=35, pointer=0, kind=0):
        return self.fn("urui_pointer_event", I, U, I, I, I, F, F, I, I, F, F, I)(self.surface, pointer, kind, phase, x, y, 0, 1 if phase == 0 else 0, 0, 0, 0)

    def test_synchronous_capture_target_bubble_and_stop(self):
        panel, child = self.token("panel"), self.token("child")
        seen = []
        self.callback(lambda p: seen.append((p.contents.current, p.contents.target, p.contents.phase)) or 0)
        self.listen(panel, 2, 1)
        self.listen(child, 2)
        self.listen(panel, 2)
        self.assertEqual(self.pointer(0), 1)
        self.assertEqual([row[0] for row in seen], [panel, child, panel])
        self.assertTrue(all(row[1] == child for row in seen))
        seen.clear()
        self.callback(lambda p: seen.append(p.contents.current) or 2)
        self.pointer(0, pointer=1, kind=1)
        self.assertEqual(seen, [panel])

    def test_static_poll_observes_capture_transferred_inside_move_callback(self):
        child, panel = self.token("child"), self.token("panel")
        capture = self.fn("urui_pointer_capture", I, U, U, I, U)
        poll = self.fn("urui_pointer_poll", I, U, I, I, F, F, I, I)
        seen = []
        def receive(pointer):
            event = pointer.contents
            if event.type == 3 and event.current == event.target:
                seen.append(event.target)
                if event.target == child:
                    self.assertEqual(capture(self.surface, self.owner, 41, panel), 1)
            return 0
        self.callback(receive)
        self.listen(child, 3)
        self.listen(panel, 3)
        self.assertEqual(self.pointer(0, pointer=41), 1)
        self.assertEqual(capture(self.surface, self.owner, 41, child), 1)
        self.assertEqual(poll(self.surface, 41, 0, 25, 35, 1, 0), 1)
        self.assertEqual(seen, [child])
        self.assertEqual(poll(self.surface, 41, 0, 25, 35, 1, 0), 1)
        self.assertEqual(seen, [child, panel])

    def test_pointer_capture_and_concurrent_touch_cancel(self):
        child = self.token("child")
        seen = []
        self.callback(lambda p: seen.append((p.contents.type, p.contents.pointer, p.contents.target)) or 0)
        for kind in [2, 3, 4, 5]:
            self.listen(child, kind)
        self.assertEqual(self.pointer(0, pointer=11, kind=1), 1)
        self.assertEqual(self.pointer(0, pointer=12, kind=1), 1)
        capture = self.fn("urui_pointer_capture", I, U, U, I, U)
        self.assertEqual(capture(self.surface, self.owner, 11, child), 1)
        self.assertEqual(self.pointer(1, 500, 400, pointer=11, kind=1), 1)
        self.assertEqual(self.pointer(3, 500, 400, pointer=11, kind=1), 1)
        self.assertIn((3, 11, child), seen)
        self.assertIn((5, 11, child), seen)
        self.assertEqual(self.fn("urui_pointer_captured", U, U, U, I)(self.surface, self.owner, 11), 0)
        self.assertEqual(self.pointer(2, pointer=12, kind=1), 1)

    def test_text_keyboard_and_composition_commit_cancel(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, U, U, I)(self.surface, self.owner, token, 1)
        read = self.fn("urui_control_value_read", I, U, U, U, P, I, P)
        self.fn("urui_control_value_write", I, U, U, U, S)(self.surface, self.owner, token, b"")
        self.assertEqual(self.fn("urui_text_input", I, U, S)(self.surface, "中文🎮".encode()), 1)
        self.assertEqual(self.read(read, self.surface, self.owner, token), "中文🎮")
        composition = self.fn("urui_text_composition", I, U, S, I)
        self.assertEqual(composition(self.surface, "拼音".encode(), 0), 1)
        self.assertEqual(composition(self.surface, b"", 2), 1)
        self.assertEqual(self.read(read, self.surface, self.owner, token), "中文🎮")
        self.assertEqual(composition(self.surface, "测试".encode(), 0), 1)
        self.assertEqual(composition(self.surface, "测试".encode(), 1), 1)
        self.assertEqual(self.read(read, self.surface, self.owner, token), "中文🎮测试")
        self.assertEqual(self.fn("urui_key_event", I, U, I, I, I)(self.surface, 8, 1, 0), 1)
        self.assertEqual(self.read(read, self.surface, self.owner, token), "中文🎮测")

    def test_composition_cancel_restores_unicode_selection_and_programmatic_value_wins(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, U, U, I)(self.surface, self.owner, token, 1)
        write = self.fn("urui_control_value_write", I, U, U, U, S)
        read = self.fn("urui_control_value_read", I, U, U, U, P, I, P)
        selection = self.fn("urui_text_selection", I, U, P, P, I)
        composition = self.fn("urui_text_composition", I, U, S, I)
        write(self.surface, self.owner, token, "A🎮B".encode())
        start, end = I(1), I(2)
        self.assertEqual(selection(self.surface, C.byref(start), C.byref(end), 1), 1)
        self.assertEqual(composition(self.surface, "新字符".encode(), 0), 1)
        self.assertEqual(composition(self.surface, b"", 2), 1)
        selection(self.surface, C.byref(start), C.byref(end), 0)
        self.assertEqual((start.value, end.value), (1, 2))
        self.assertEqual(self.read(read, self.surface, self.owner, token), "A🎮B")
        self.assertEqual(composition(self.surface, "预览".encode(), 0), 1)
        self.assertEqual(write(self.surface, self.owner, token, b"replacement"), 1)
        self.assertEqual(composition(self.surface, b"", 2), 1)
        self.assertEqual(self.read(read, self.surface, self.owner, token), "replacement")

    def test_mac_command_clipboard_and_deferred_context_teardown(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, U, U, I)(self.surface, self.owner, token, 1)
        key = self.fn("urui_key_event", I, U, I, I, I)
        self.assertEqual(key(self.surface, 97, 1, 8), 1)
        self.assertEqual(key(self.surface, 99, 1, 8), 1)
        clipboard = self.fn("urui_clipboard_read", I, U, P, I, P)
        self.assertEqual(self.read(clipboard, self.surface), "hello")
        destroy_fn = self.fn("urui_surface_destroy", I, U)
        self.callback(lambda p: destroy_fn(self.surface) and 0)
        self.listen(self.token("child"), 2)
        self.assertEqual(self.pointer(0), 1)
        self.assertEqual(self.fn("urui_process_deferred_operations", I, U)(self.surface), 1)
        stale_surface = self.surface
        self.surface = 0
        self.assertEqual(self.fn("urui_document_root", U, U, U)(stale_surface, self.owner), 0)

    def test_focus_change_cancels_uncommitted_text(self):
        token = self.token("text")
        focus = self.fn("urui_element_focus", I, U, U, U, I)
        focus(self.surface, self.owner, token, 1)
        composition = self.fn("urui_text_composition", I, U, S, I)
        self.assertEqual(composition(self.surface, "未提交".encode(), 0), 1)
        focus(self.surface, self.owner, self.token("check"), 1)
        read = self.fn("urui_control_value_read", I, U, U, U, P, I, P)
        self.assertEqual(self.read(read, self.surface, self.owner, token), "hello")

    def test_detached_select_options_use_native_widget_storage(self):
        token = self.fn("urui_element_create", U, U, U, S)(self.surface, self.owner, b"select")
        count = self.fn("urui_select_option_count", I, U, U, U)
        add = self.fn("urui_select_option_add", I, U, U, U, S)
        read = self.fn("urui_select_option_read", I, U, U, U, I, P, I, P)
        self.assertEqual(add(self.surface, self.owner, token, b"A"), 1)
        self.assertEqual(add(self.surface, self.owner, token, "<中文>".encode()), 1)
        self.assertEqual(count(self.surface, self.owner, token), 2)
        self.assertEqual(self.read(read, self.surface, self.owner, token, 1), "<中文>")
        self.assertEqual(self.fn("urui_control_value_write", I, U, U, U, S)(self.surface, self.owner, token, "<中文>".encode()), 1)
        self.assertEqual(self.read(self.fn("urui_control_value_read", I, U, U, U, P, I, P), self.surface, self.owner, token), "<中文>")
        self.assertEqual(self.fn("urui_select_options_clear", I, U, U, U)(self.surface, self.owner, token), 1)
        self.assertEqual(count(self.surface, self.owner, token), 0)

    def test_keyboard_payload_and_text_event_are_not_stale_pointer_data(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, U, U, I)(self.surface, self.owner, token, 1)
        seen = []
        self.callback(lambda p: seen.append((p.contents.type, p.contents.key, p.contents.text)) or 0)
        self.listen(token, 14)
        self.listen(token, 9)
        self.fn("urui_text_input", I, U, S)(self.surface, "文字".encode())
        self.assertIn((14, 0, "文字".encode()), seen)
        self.assertEqual(self.fn("urui_key_event", I, U, I, I, I)(self.surface, 304, 1, 1), 1)
        self.assertIn((9, 304, b""), seen)

    def test_subtree_cancel_releases_only_its_own_capture(self):
        child, other = self.token("child"), self.token("check")
        self.pointer(0, pointer=41, kind=1)
        self.pointer(0, pointer=42, kind=1)
        capture = self.fn("urui_pointer_capture", I, U, U, I, U)
        capture(self.surface, self.owner, 41, child)
        capture(self.surface, self.owner, 42, other)
        self.assertEqual(self.fn("urui_cancel_pointers", I, U, U, U)(self.surface, self.owner, child), 1)
        captured = self.fn("urui_pointer_captured", U, U, U, I)
        self.assertEqual(captured(self.surface, self.owner, 41), 0)
        self.assertEqual(captured(self.surface, self.owner, 42), other)


if __name__ == "__main__":
    unittest.main(verbosity=2)
