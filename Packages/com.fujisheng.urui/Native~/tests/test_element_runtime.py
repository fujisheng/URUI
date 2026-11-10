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
    _fields_ = [("document", U), ("target", U), ("current", U), ("type", I), ("phase", I),
                ("pointer", I), ("button", I), ("buttons", I), ("modifiers", I),
                ("key", I), ("kind", I), ("x", F), ("y", F), ("dx", F), ("dy", F), ("text", S)]


assert C.sizeof(Event) == 80 and Event.text.offset == 72

CALLBACK = C.CFUNCTYPE(I, C.POINTER(Event))


class ElementRuntime(unittest.TestCase):
    def fn(self, name, result, *args):
        value = getattr(LIB, name, None)
        self.assertIsNotNone(value, "Missing element runtime capability: " + name)
        value.restype, value.argtypes = result, list(args)
        return value

    def setUp(self):
        self.assertEqual(self.fn("urui_bridge_abi_version", I)(), 8)
        self.assertEqual(self.fn("urui_element_api_version", I)(), 2)
        self.assertEqual(self.fn("urui_create", I, I, I)(640, 480), 1)
        self.addCleanup(self.fn("urui_destroy", None))
        markup = b"""<!doctype html><html><head><style>@font-face{font-family:Acceptance;src:url('/runtime-font.ttf');}body{margin:0;font-family:Acceptance;}#panel{display:block;position:absolute;left:20px;top:30px;width:200px;height:120px;}#child{display:block;width:50%;height:40px;}</style></head><body><div id='panel'><div id='child'></div><input id='text' type='text' value='hello'/><input id='check' type='checkbox'/></div></body></html>"""
        owner = U()
        self.assertEqual(self.fn("urui_document_create", I, P)(C.byref(owner)), 1)
        self.owner = owner.value
        font = (Path(__file__).parents[2] / "Runtime/Resources/URUI/Fonts/LiberationSans-Regular.ttf.bytes").read_bytes()
        buffer = C.create_string_buffer(font)
        self.assertEqual(self.fn("urui_document_register_resource", I, U, S, P, I)(self.owner, b"/runtime-font.ttf", buffer, len(font)), 1)
        self.assertEqual(self.fn("urui_document_load_html", I, U, S, S)(self.owner, markup, b"/runtime.html"), 1)
        self.assertEqual(self.fn("urui_document_set_presentation", I, U, I, I, I, I)(self.owner, 0, 0, 1, 0), 1)
        self.root = self.fn("urui_document_root", U, U)(self.owner)
        self.find = self.fn("urui_element_find", U, U, U, S, S)
        self.fn("urui_update", None)()

    def token(self, name):
        value = self.find(self.owner, self.root, name.encode(), None)
        self.assertNotIn(value, [0, (1 << 64) - 1])
        return value

    def read(self, fn, *args):
        required = I()
        self.assertEqual(fn(*args, None, 0, C.byref(required)), -3)
        out = C.create_string_buffer(required.value)
        self.assertEqual(fn(*args, out, len(out), C.byref(required)), 1)
        return out.value.decode()

    def test_percentage_layout_and_style_sources(self):
        token = self.token("child")
        box = (F * 16)()
        metrics = self.fn("urui_element_box", I, U, U, P, I)
        self.assertEqual(metrics(self.owner, token, box, 16), 1)
        self.assertAlmostEqual(box[2], 100, places=2)
        self.assertAlmostEqual(box[3], 40, places=2)
        self.assertAlmostEqual(box[8], 20, places=2)
        style = self.fn("urui_element_style_read", I, U, U, I, S, P, I, P)
        required = I()
        self.assertEqual(style(self.owner, token, 1, b"width", None, 0, C.byref(required)), 0)
        self.assertEqual(self.fn("urui_element_write", I, U, U, I, S, S)(self.owner, token, 4, b"width", b"75%"), 1)
        self.assertEqual(self.read(style, self.owner, token, 1, b"width"), "75%")
        self.fn("urui_update", None)()
        self.assertEqual(metrics(self.owner, token, box, 16), 1)
        self.assertAlmostEqual(box[2], 150, places=2)
        self.assertEqual(self.fn("urui_element_erase", I, U, U, I, S)(self.owner, token, 1, b"width"), 1)
        self.fn("urui_update", None)()
        metrics(self.owner, token, box, 16)
        self.assertAlmostEqual(box[2], 100, places=2)

    def test_refresh_state_matches_layout_and_preserves_token_ownership(self):
        state = self.fn("urui_element_refresh_state", I, U, U, P, I)
        token = self.token("child")
        actual, box = (F * 4)(), (F * 16)()
        self.assertEqual(state(self.owner, token, actual, 4), 3)
        self.assertEqual(self.fn("urui_element_box", I, U, U, P, I)(self.owner, token, box, 16), 1)
        self.assertEqual(list(actual), list(box)[:4])
        self.assertEqual(state(self.owner, token, actual, 3), -1)
        self.assertEqual(state(self.owner, token, None, 4), -1)
        other = U()
        self.assertEqual(self.fn("urui_document_create", I, P)(C.byref(other)), 1)
        self.assertEqual(state(other.value, token, actual, 4), 0)
        self.assertEqual(self.fn("urui_element_detach", I, U, U)(self.owner, token), 1)
        self.assertEqual(state(self.owner, token, actual, 4), 1)
        self.assertEqual(self.fn("urui_element_destroy", I, U, U)(self.owner, token), 1)
        self.assertEqual(state(self.owner, token, actual, 4), 0)

    def test_focus_and_real_form_value(self):
        text = self.token("text")
        focus = self.fn("urui_element_focus", I, U, U, I)
        self.assertEqual(focus(self.owner, text, 1), 1)
        self.assertEqual(self.fn("urui_focused_element", U, U)(self.owner), text)
        read = self.fn("urui_control_value_read", I, U, U, P, I, P)
        write = self.fn("urui_control_value_write", I, U, U, S)
        self.assertEqual(self.read(read, self.owner, text), "hello")
        self.assertEqual(write(self.owner, text, "中文 🎮".encode()), 1)
        self.assertEqual(self.read(read, self.owner, text), "中文 🎮")
        check = self.token("check")
        self.assertEqual(self.read(read, self.owner, check), "false")
        self.assertEqual(write(self.owner, check, b"true"), 1)
        self.assertEqual(self.read(read, self.owner, check), "true")
        self.assertEqual(focus(self.owner, text, 0), 1)
        self.assertEqual(self.fn("urui_focused_element", U, U)(self.owner), 0)

    def test_pick_has_no_name_requirement_and_rejects_invalid_geometry(self):
        child = self.token("child")
        self.assertEqual(self.fn("urui_element_write", I, U, U, I, S, S)(self.owner, child, 0, None, b""), 1)
        pick = self.fn("urui_element_pick", U, U, F, F)
        self.assertEqual(pick(self.owner, 25, 35), child)
        self.assertEqual(pick(self.owner, float("nan"), 35), 0)
        metrics = self.fn("urui_element_box", I, U, U, P, I)
        self.assertLess(metrics(self.owner, 0, (F * 16)(), 16), 0)
        self.assertLess(metrics(self.owner, child, (F * 8)(), 8), 0)
        result = []
        thread = threading.Thread(target=lambda: result.append(metrics(self.owner, child, (F * 16)(), 16)))
        thread.start()
        thread.join()
        self.assertLess(result[0], 0)

    def listen(self, token, event_type, capture=0):
        self.assertEqual(self.fn("urui_element_listen", I, U, U, I, I, I)(self.owner, token, event_type, capture, 1), 1)

    def callback(self, receive):
        self.event_owners = []
        def dispatch(pointer):
            self.event_owners.append(pointer.contents.document)
            return receive(pointer)
        callback = CALLBACK(dispatch)
        self.addCleanup(lambda: self.assertTrue(all(owner == self.owner for owner in self.event_owners), "Event document ownership"))
        self.addCleanup(lambda: self.fn("urui_set_event_callback", I, P)(None))
        self.assertEqual(self.fn("urui_set_event_callback", I, P)(callback), 1)
        self.keep_callback = callback

    def pointer(self, phase, x=25, y=35, pointer=0, kind=0):
        return self.fn("urui_pointer_event", I, I, I, I, F, F, I, I, F, F, I)(pointer, kind, phase, x, y, 0, 1 if phase == 0 else 0, 0, 0, 0)

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

    def test_pointer_capture_and_concurrent_touch_cancel(self):
        child = self.token("child")
        seen = []
        self.callback(lambda p: seen.append((p.contents.type, p.contents.pointer, p.contents.target)) or 0)
        for kind in [2, 3, 4, 5]:
            self.listen(child, kind)
        self.assertEqual(self.pointer(0, pointer=11, kind=1), 1)
        self.assertEqual(self.pointer(0, pointer=12, kind=1), 1)
        capture = self.fn("urui_pointer_capture", I, U, I, U)
        self.assertEqual(capture(self.owner, 11, child), 1)
        self.assertEqual(self.pointer(1, 500, 400, pointer=11, kind=1), 1)
        self.assertEqual(self.pointer(3, 500, 400, pointer=11, kind=1), 1)
        self.assertIn((3, 11, child), seen)
        self.assertIn((5, 11, child), seen)
        self.assertEqual(self.fn("urui_pointer_captured", U, U, I)(self.owner, 11), 0)
        self.assertEqual(self.pointer(2, pointer=12, kind=1), 1)

    def test_text_keyboard_and_composition_commit_cancel(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, U, I)(self.owner, token, 1)
        read = self.fn("urui_control_value_read", I, U, U, P, I, P)
        self.fn("urui_control_value_write", I, U, U, S)(self.owner, token, b"")
        self.assertEqual(self.fn("urui_text_input", I, S)("中文🎮".encode()), 1)
        self.assertEqual(self.read(read, self.owner, token), "中文🎮")
        composition = self.fn("urui_text_composition", I, S, I)
        self.assertEqual(composition("拼音".encode(), 0), 1)
        self.assertEqual(composition(b"", 2), 1)
        self.assertEqual(self.read(read, self.owner, token), "中文🎮")
        self.assertEqual(composition("测试".encode(), 0), 1)
        self.assertEqual(composition("测试".encode(), 1), 1)
        self.assertEqual(self.read(read, self.owner, token), "中文🎮测试")
        self.assertEqual(self.fn("urui_key_event", I, I, I, I)(8, 1, 0), 1)
        self.assertEqual(self.read(read, self.owner, token), "中文🎮测")

    def test_composition_cancel_restores_unicode_selection_and_programmatic_value_wins(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, U, I)(self.owner, token, 1)
        write = self.fn("urui_control_value_write", I, U, U, S)
        read = self.fn("urui_control_value_read", I, U, U, P, I, P)
        selection = self.fn("urui_text_selection", I, P, P, I)
        composition = self.fn("urui_text_composition", I, S, I)
        write(self.owner, token, "A🎮B".encode())
        start, end = I(1), I(2)
        self.assertEqual(selection(C.byref(start), C.byref(end), 1), 1)
        self.assertEqual(composition("新字符".encode(), 0), 1)
        self.assertEqual(composition(b"", 2), 1)
        selection(C.byref(start), C.byref(end), 0)
        self.assertEqual((start.value, end.value), (1, 2))
        self.assertEqual(self.read(read, self.owner, token), "A🎮B")
        self.assertEqual(composition("预览".encode(), 0), 1)
        self.assertEqual(write(self.owner, token, b"replacement"), 1)
        self.assertEqual(composition(b"", 2), 1)
        self.assertEqual(self.read(read, self.owner, token), "replacement")

    def test_mac_command_clipboard_and_deferred_context_teardown(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, U, I)(self.owner, token, 1)
        key = self.fn("urui_key_event", I, I, I, I)
        self.assertEqual(key(97, 1, 8), 1)
        self.assertEqual(key(99, 1, 8), 1)
        clipboard = self.fn("urui_clipboard_read", I, P, I, P)
        self.assertEqual(self.read(clipboard), "hello")
        self.callback(lambda p: self.fn("urui_destroy", None)() or 0)
        self.listen(self.token("child"), 2)
        self.assertEqual(self.pointer(0), 1)
        self.assertEqual(self.fn("urui_process_deferred_operations", I)(), 1)
        self.assertEqual(self.fn("urui_document_root", U, U)(self.owner), 0)

    def test_focus_change_cancels_uncommitted_text(self):
        token = self.token("text")
        focus = self.fn("urui_element_focus", I, U, U, I)
        focus(self.owner, token, 1)
        composition = self.fn("urui_text_composition", I, S, I)
        self.assertEqual(composition("未提交".encode(), 0), 1)
        focus(self.owner, self.token("check"), 1)
        read = self.fn("urui_control_value_read", I, U, U, P, I, P)
        self.assertEqual(self.read(read, self.owner, token), "hello")

    def test_detached_select_options_use_native_widget_storage(self):
        token = self.fn("urui_element_create", U, U, S)(self.owner, b"select")
        count = self.fn("urui_select_option_count", I, U, U)
        add = self.fn("urui_select_option_add", I, U, U, S)
        read = self.fn("urui_select_option_read", I, U, U, I, P, I, P)
        self.assertEqual(add(self.owner, token, b"A"), 1)
        self.assertEqual(add(self.owner, token, "<中文>".encode()), 1)
        self.assertEqual(count(self.owner, token), 2)
        self.assertEqual(self.read(read, self.owner, token, 1), "<中文>")
        self.assertEqual(self.fn("urui_control_value_write", I, U, U, S)(self.owner, token, "<中文>".encode()), 1)
        self.assertEqual(self.read(self.fn("urui_control_value_read", I, U, U, P, I, P), self.owner, token), "<中文>")
        self.assertEqual(self.fn("urui_select_options_clear", I, U, U)(self.owner, token), 1)
        self.assertEqual(count(self.owner, token), 0)

    def test_keyboard_payload_and_text_event_are_not_stale_pointer_data(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, U, I)(self.owner, token, 1)
        seen = []
        self.callback(lambda p: seen.append((p.contents.type, p.contents.key, p.contents.text)) or 0)
        self.listen(token, 14)
        self.listen(token, 9)
        self.fn("urui_text_input", I, S)("文字".encode())
        self.assertIn((14, 0, "文字".encode()), seen)
        self.assertEqual(self.fn("urui_key_event", I, I, I, I)(304, 1, 1), 1)
        self.assertIn((9, 304, b""), seen)

    def test_subtree_cancel_releases_only_its_own_capture(self):
        child, other = self.token("child"), self.token("check")
        self.pointer(0, pointer=41, kind=1)
        self.pointer(0, pointer=42, kind=1)
        capture = self.fn("urui_pointer_capture", I, U, I, U)
        capture(self.owner, 41, child)
        capture(self.owner, 42, other)
        self.assertEqual(self.fn("urui_cancel_pointers", I, U, U)(self.owner, child), 1)
        captured = self.fn("urui_pointer_captured", U, U, I)
        self.assertEqual(captured(self.owner, 41), 0)
        self.assertEqual(captured(self.owner, 42), other)


if __name__ == "__main__":
    unittest.main(verbosity=2)
