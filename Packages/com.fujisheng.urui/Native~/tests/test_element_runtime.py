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
    _fields_ = [("target", U), ("current", U), ("type", I), ("phase", I),
                ("pointer", I), ("button", I), ("buttons", I), ("modifiers", I),
                ("key", I), ("kind", I), ("x", F), ("y", F), ("dx", F), ("dy", F), ("text", S)]


CALLBACK = C.CFUNCTYPE(I, C.POINTER(Event))


class ElementRuntime(unittest.TestCase):
    def fn(self, name, result, *args):
        value = getattr(LIB, name, None)
        self.assertIsNotNone(value, "Missing element runtime capability: " + name)
        value.restype, value.argtypes = result, list(args)
        return value

    def setUp(self):
        self.fn("urui_element_api_version", I)()
        self.assertEqual(self.fn("urui_create", I, I, I, S)(640, 480, b"/System/Library/Fonts/Supplemental/Arial.ttf"), 1)
        self.addCleanup(self.fn("urui_destroy", None))
        markup = b"""<rml><head><style>body{margin:0;font-family:Arial;}#panel{display:block;position:absolute;left:20px;top:30px;width:200px;height:120px;}#child{display:block;width:50%;height:40px;}</style></head><body><div id='panel'><div id='child'/><input id='text' type='text' value='hello'/><input id='check' type='checkbox'/></div></body></rml>"""
        self.assertEqual(self.fn("urui_load_document", I, S)(markup), 1)
        self.root = self.fn("urui_element_root", U)()
        self.find = self.fn("urui_element_find", U, U, S, S)
        self.fn("urui_update", None)()

    def token(self, name):
        value = self.find(self.root, name.encode(), None)
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
        metrics = self.fn("urui_element_box", I, U, P, I)
        self.assertEqual(metrics(token, box, 16), 1)
        self.assertAlmostEqual(box[2], 100, places=2)
        self.assertAlmostEqual(box[3], 40, places=2)
        self.assertAlmostEqual(box[8], 20, places=2)
        style = self.fn("urui_element_style_read", I, U, I, S, P, I, P)
        required = I()
        self.assertEqual(style(token, 1, b"width", None, 0, C.byref(required)), 0)
        self.assertEqual(self.fn("urui_element_write", I, U, I, S, S)(token, 4, b"width", b"75%"), 1)
        self.assertEqual(self.read(style, token, 1, b"width"), "75%")
        self.fn("urui_update", None)()
        self.assertEqual(metrics(token, box, 16), 1)
        self.assertAlmostEqual(box[2], 150, places=2)
        self.assertEqual(self.fn("urui_element_erase", I, U, I, S)(token, 1, b"width"), 1)
        self.fn("urui_update", None)()
        metrics(token, box, 16)
        self.assertAlmostEqual(box[2], 100, places=2)

    def test_focus_and_real_form_value(self):
        text = self.token("text")
        focus = self.fn("urui_element_focus", I, U, I)
        self.assertEqual(focus(text, 1), 1)
        self.assertEqual(self.fn("urui_focused_element", U)(), text)
        read = self.fn("urui_control_value_read", I, U, P, I, P)
        write = self.fn("urui_control_value_write", I, U, S)
        self.assertEqual(self.read(read, text), "hello")
        self.assertEqual(write(text, "中文 🎮".encode()), 1)
        self.assertEqual(self.read(read, text), "中文 🎮")
        check = self.token("check")
        self.assertEqual(self.read(read, check), "false")
        self.assertEqual(write(check, b"true"), 1)
        self.assertEqual(self.read(read, check), "true")
        self.assertEqual(focus(text, 0), 1)
        self.assertEqual(self.fn("urui_focused_element", U)(), 0)

    def test_pick_has_no_name_requirement_and_rejects_invalid_geometry(self):
        child = self.token("child")
        self.assertEqual(self.fn("urui_element_write", I, U, I, S, S)(child, 0, None, b""), 1)
        pick = self.fn("urui_element_pick", U, F, F)
        self.assertEqual(pick(25, 35), child)
        self.assertEqual(pick(float("nan"), 35), 0)
        metrics = self.fn("urui_element_box", I, U, P, I)
        self.assertLess(metrics(0, (F * 16)(), 16), 0)
        self.assertLess(metrics(child, (F * 8)(), 8), 0)
        result = []
        thread = threading.Thread(target=lambda: result.append(metrics(child, (F * 16)(), 16)))
        thread.start()
        thread.join()
        self.assertLess(result[0], 0)

    def listen(self, token, event_type, capture=0):
        self.assertEqual(self.fn("urui_element_listen", I, U, I, I, I)(token, event_type, capture, 1), 1)

    def callback(self, receive):
        callback = CALLBACK(receive)
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
        capture = self.fn("urui_pointer_capture", I, I, U)
        self.assertEqual(capture(11, child), 1)
        self.assertEqual(self.pointer(1, 500, 400, pointer=11, kind=1), 1)
        self.assertEqual(self.pointer(3, 500, 400, pointer=11, kind=1), 1)
        self.assertIn((3, 11, child), seen)
        self.assertIn((5, 11, child), seen)
        self.assertEqual(self.fn("urui_pointer_captured", U, I)(11), 0)
        self.assertEqual(self.pointer(2, pointer=12, kind=1), 1)

    def test_text_keyboard_and_composition_commit_cancel(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, I)(token, 1)
        read = self.fn("urui_control_value_read", I, U, P, I, P)
        self.fn("urui_control_value_write", I, U, S)(token, b"")
        self.assertEqual(self.fn("urui_text_input", I, S)("中文🎮".encode()), 1)
        self.assertEqual(self.read(read, token), "中文🎮")
        composition = self.fn("urui_text_composition", I, S, I)
        self.assertEqual(composition("拼音".encode(), 0), 1)
        self.assertEqual(composition(b"", 2), 1)
        self.assertEqual(self.read(read, token), "中文🎮")
        self.assertEqual(composition("测试".encode(), 0), 1)
        self.assertEqual(composition("测试".encode(), 1), 1)
        self.assertEqual(self.read(read, token), "中文🎮测试")
        self.assertEqual(self.fn("urui_key_event", I, I, I, I)(8, 1, 0), 1)
        self.assertEqual(self.read(read, token), "中文🎮测")

    def test_composition_cancel_restores_unicode_selection_and_programmatic_value_wins(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, I)(token, 1)
        write = self.fn("urui_control_value_write", I, U, S)
        read = self.fn("urui_control_value_read", I, U, P, I, P)
        selection = self.fn("urui_text_selection", I, P, P, I)
        composition = self.fn("urui_text_composition", I, S, I)
        write(token, "A🎮B".encode())
        start, end = I(1), I(2)
        self.assertEqual(selection(C.byref(start), C.byref(end), 1), 1)
        self.assertEqual(composition("新字符".encode(), 0), 1)
        self.assertEqual(composition(b"", 2), 1)
        selection(C.byref(start), C.byref(end), 0)
        self.assertEqual((start.value, end.value), (1, 2))
        self.assertEqual(self.read(read, token), "A🎮B")
        self.assertEqual(composition("预览".encode(), 0), 1)
        self.assertEqual(write(token, b"replacement"), 1)
        self.assertEqual(composition(b"", 2), 1)
        self.assertEqual(self.read(read, token), "replacement")

    def test_mac_command_clipboard_and_deferred_context_teardown(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, I)(token, 1)
        key = self.fn("urui_key_event", I, I, I, I)
        self.assertEqual(key(97, 1, 8), 1)
        self.assertEqual(key(99, 1, 8), 1)
        clipboard = self.fn("urui_clipboard_read", I, P, I, P)
        self.assertEqual(self.read(clipboard), "hello")
        self.callback(lambda p: self.fn("urui_destroy", None)() or 0)
        self.listen(self.token("child"), 2)
        self.assertEqual(self.pointer(0), 1)
        self.assertEqual(self.fn("urui_process_deferred_operations", I)(), 1)
        self.assertEqual(self.fn("urui_element_root", U)(), 0)

    def test_focus_change_cancels_uncommitted_text(self):
        token = self.token("text")
        focus = self.fn("urui_element_focus", I, U, I)
        focus(token, 1)
        composition = self.fn("urui_text_composition", I, S, I)
        self.assertEqual(composition("未提交".encode(), 0), 1)
        focus(self.token("check"), 1)
        read = self.fn("urui_control_value_read", I, U, P, I, P)
        self.assertEqual(self.read(read, token), "hello")

    def test_detached_select_options_use_native_widget_storage(self):
        token = self.fn("urui_element_create", U, S)(b"select")
        count = self.fn("urui_select_option_count", I, U)
        add = self.fn("urui_select_option_add", I, U, S)
        read = self.fn("urui_select_option_read", I, U, I, P, I, P)
        self.assertEqual(add(token, b"A"), 1)
        self.assertEqual(add(token, "<中文>".encode()), 1)
        self.assertEqual(count(token), 2)
        self.assertEqual(self.read(read, token, 1), "<中文>")
        self.assertEqual(self.fn("urui_control_value_write", I, U, S)(token, "<中文>".encode()), 1)
        self.assertEqual(self.read(self.fn("urui_control_value_read", I, U, P, I, P), token), "<中文>")
        self.assertEqual(self.fn("urui_select_options_clear", I, U)(token), 1)
        self.assertEqual(count(token), 0)

    def test_keyboard_payload_and_text_event_are_not_stale_pointer_data(self):
        token = self.token("text")
        self.fn("urui_element_focus", I, U, I)(token, 1)
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
        capture = self.fn("urui_pointer_capture", I, I, U)
        capture(41, child)
        capture(42, other)
        self.assertEqual(self.fn("urui_cancel_pointers", I, U)(child), 1)
        captured = self.fn("urui_pointer_captured", U, I)
        self.assertEqual(captured(41), 0)
        self.assertEqual(captured(42), other)


if __name__ == "__main__":
    unittest.main(verbosity=2)
