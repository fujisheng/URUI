"""Real bridge contracts for document-owned identity and DOM operations (no Player)."""
import ctypes as C
from pathlib import Path
import sys
import subprocess
import unittest

LIBRARY = Path(sys.argv.pop(1)) if len(sys.argv) > 1 else Path(__file__).parents[2] / "Runtime/Plugins/macOS/libURUIBridge.dylib"
LIB = C.CDLL(str(LIBRARY))
U = C.c_uint64
I = C.c_int
S = C.c_char_p
P = C.c_void_p


class Elements(unittest.TestCase):
    def fn(self, name, result, *args):
        value = getattr(LIB, name, None)
        self.assertIsNotNone(value, "Element API missing: " + name)
        value.restype, value.argtypes = result, list(args)
        return value

    def setUp(self):
        self.root_fn = self.fn("urui_element_root", U)
        self.create = self.fn("urui_element_create", U, S)
        self.parent = self.fn("urui_element_parent", U, U)
        self.child_count = self.fn("urui_element_child_count", I, U)
        self.child = self.fn("urui_element_child_at", U, U, I)
        self.query_fn = self.fn("urui_element_query", I, U, S, S, P, I)
        self.read_fn = self.fn("urui_element_read", I, U, I, S, P, I, P)
        self.write = self.fn("urui_element_write", I, U, I, S, S)
        self.erase = self.fn("urui_element_erase", I, U, I, S)
        self.classes = self.fn("urui_element_class", I, U, S, I)
        self.insert = self.fn("urui_element_insert", I, U, U, I)
        self.detach = self.fn("urui_element_detach", I, U)
        self.clear = self.fn("urui_element_clear", I, U)
        self.destroy = self.fn("urui_element_destroy", I, U)
        self.valid = self.fn("urui_element_ref_valid", I, U)
        self.attached = self.fn("urui_element_attached", I, U)
        self.pop = self.fn("urui_element_pop_click", I, P)
        self.assertEqual(self.fn("urui_bridge_abi_version", I)(), 3)
        self.assertEqual(self.fn("urui_create", I, I, I, S)(640, 480, b"/System/Library/Fonts/Supplemental/Arial.ttf"), 1)
        markup = b"<rml><head><style>body{margin:0;}#panel{width:200px;height:100px;background-color:red;}</style></head><body><div id='panel'><div id='first' class='item'/><div><span id='deep' class='item'/></div><span id='last' class='item'/></div></body></rml>"
        self.assertEqual(self.fn("urui_load_document", I, S)(markup), 1)
        self.root = self.root_fn()

    def tearDown(self):
        self.fn("urui_destroy", None)()

    def query(self, root, name=None, cls=None):
        name = None if name is None else name.encode()
        cls = None if cls is None else cls.encode()
        count = self.query_fn(root, name, cls, None, 0)
        self.assertGreaterEqual(count, 0)
        values = (U * count)()
        self.assertEqual(self.query_fn(root, name, cls, values, count), count)
        return list(values)

    def read(self, token, kind=0, name=None):
        required = I()
        name = None if name is None else name.encode()
        result = self.read_fn(token, kind, name, None, 0, C.byref(required))
        if result == 0:
            return None
        self.assertEqual(result, -3)
        buffer = C.create_string_buffer(required.value)
        self.assertEqual(self.read_fn(token, kind, name, buffer, len(buffer), C.byref(required)), 1)
        return buffer.value.decode()

    def test_root_query_order_and_literal_scopes(self):
        self.assertNotEqual(self.root, 0)
        self.assertEqual(self.root_fn(), self.root)
        self.assertEqual(self.parent(self.root), 0)
        panel = self.query(self.root, "panel")[0]
        self.assertEqual(self.query(panel, "panel"), [panel])
        self.assertEqual([self.read(t) for t in self.query(panel, cls="item")], ["first", "deep", "last"])
        self.assertEqual(self.query(self.query(panel, "first")[0], "last"), [])
        self.assertEqual(self.query(panel, "#document"), [])

    def test_detach_reparent_destroy_and_generation(self):
        panel = self.query(self.root, "panel")[0]
        created = self.create(b"div")
        self.assertNotEqual(created, 0)
        self.assertEqual(self.valid(created), 1)
        self.assertEqual(self.attached(created), 0)
        self.assertEqual(self.insert(panel, created, -1), 1)
        self.assertEqual(self.child(panel, 3), created)
        self.assertEqual(self.detach(created), 1)
        self.assertEqual(self.valid(created), 1)
        self.assertEqual(self.parent(created), 0)
        self.assertEqual(self.insert(self.root, created, 0), 1)
        self.assertEqual(self.child(self.root, 0), created)
        self.assertEqual(self.destroy(created), 1)
        self.assertEqual(self.valid(created), 0)
        for _ in range(128):
            replacement = self.create(b"div")
            self.assertNotEqual(replacement, created)
            self.assertEqual(self.destroy(replacement), 1)
        self.fn("urui_unload_document", None)()
        self.assertEqual(self.valid(panel), 0)

    def test_insert_validation_preserves_order_and_clear_retains_nodes(self):
        panel = self.query(self.root, "panel")[0]
        first = self.child(panel, 0)
        self.assertLess(self.insert(first, panel, 0), 0)
        self.assertLess(self.insert(panel, first, 99), 0)
        self.assertEqual(self.child(panel, 0), first)
        self.assertEqual(self.insert(panel, first, 2), 1)
        self.assertEqual(self.child(panel, 2), first)
        self.assertLess(self.detach(self.root), 0)
        self.assertLess(self.destroy(self.root), 0)
        nodes = [self.child(panel, i) for i in range(3)]
        self.assertEqual(self.clear(panel), 1)
        self.assertEqual(self.child_count(panel), 0)
        self.assertTrue(all(self.valid(n) == 1 and self.parent(n) == 0 for n in nodes))
        self.assertEqual(self.insert(nodes[0], nodes[1], -1), 1)
        self.assertEqual(self.destroy(nodes[0]), 1)
        self.assertEqual(self.valid(nodes[1]), 0)

    def test_mutation_uses_identity_and_preserves_exact_text(self):
        panel = self.query(self.root, "panel")[0]
        a, b = self.create(b"div"), self.create(b"div")
        for t in [a, b]:
            self.assertEqual(self.write(t, 0, None, b"same"), 1)
            self.assertEqual(self.insert(panel, t, -1), 1)
        self.assertEqual(self.query(panel, "same"), [a, b])
        self.assertEqual(self.write(a, 0, None, "重命名".encode()), 1)
        text = "  <tag>& {{literal}}\n中文 🎮  "
        self.assertEqual(self.write(a, 1, None, text.encode()), 1)
        self.assertEqual(self.read(a, 2), text)
        self.assertEqual(self.read(b, 2), "")
        self.assertEqual(self.write(a, 2, b"title", "提示".encode()), 1)
        self.assertEqual(self.read(a, 3, "title"), "提示")
        self.assertEqual(self.erase(a, 0, b"title"), 1)
        self.assertIsNone(self.read(a, 3, "title"))
        self.assertEqual(self.classes(a, b"active", 1), 1)
        self.assertIn("active", self.read(a, 3, "class"))
        self.assertEqual(self.classes(a, b"active", -1), 1)
        self.assertEqual(self.classes(a, b"active", 0), 1)
        self.assertEqual(self.classes(a, b"active", -1), 0)
        self.assertLess(self.classes(a, b"two classes", 1), 0)

    def test_queued_click_cannot_retarget_replacement(self):
        panel = self.query(self.root, "panel")[0]
        self.fn("urui_update", None)()
        self.fn("urui_mouse_move", None, I, I)(10, 70)
        self.fn("urui_mouse_button", None, I, I)(0, 1)
        self.fn("urui_mouse_button", None, I, I)(0, 0)
        self.assertEqual(self.destroy(panel), 1)
        target = U()
        self.assertEqual(self.pop(C.byref(target)), 0)

    def test_detached_markup_rejected_without_native_crash(self):
        # A template parser branch in RmlUi requires an owning document. Run the
        # regression in a child so the unfixed SIGSEGV is an assertion, not a lost suite.
        code = f"""
import ctypes as C, resource
resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
l=C.CDLL({str(LIBRARY.resolve())!r})
l.urui_create.argtypes=[C.c_int,C.c_int,C.c_char_p]
l.urui_create(640,480,None)
l.urui_load_document.argtypes=[C.c_char_p]
l.urui_load_document(b'<rml><body/></rml>')
l.urui_element_create.argtypes=[C.c_char_p]
l.urui_element_create.restype=C.c_uint64
t=l.urui_element_create(b'div')
l.urui_element_write.argtypes=[C.c_uint64,C.c_int,C.c_char_p,C.c_char_p]
result=l.urui_element_write(t,3,None,b"<template src='missing'/>")
l.urui_destroy()
raise SystemExit(0 if result < 0 else 2)
"""
        child = subprocess.run([sys.executable, "-c", code], capture_output=True, text=True)
        self.assertEqual(child.returncode, 0, "Detached markup must reject safely, child=" + str(child.returncode) + " " + child.stderr)

    def test_identity_budget_reports_parent_and_query_failure(self):
        leaf = self.query(self.root, "deep")[0]
        created = [self.create(b"div") for _ in range(65534)]
        self.assertTrue(all(created))
        self.assertEqual(self.attached(leaf), 1)
        failure = (1 << 64) - 1
        self.assertEqual(self.parent(leaf), failure, "An existing parent must not become a false null at the token budget")
        find = self.fn("urui_element_find", U, U, S, S)
        self.assertEqual(find(self.root, b"panel", None), failure)
        self.assertEqual(find(self.root, None, None), self.root, "Existing canonical tokens remain usable")
        self.assertEqual(self.destroy(created[0]), 1)
        self.assertNotIn(self.parent(leaf), [0, failure], "Releasing a destroyed node restores capacity")


if __name__ == "__main__":
    unittest.main(verbosity=2)
