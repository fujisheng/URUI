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
        self.root_fn = self.fn("urui_document_root", U, U, U)
        self.create = self.fn("urui_element_create", U, U, U, S)
        self.parent = self.fn("urui_element_parent", U, U, U, U)
        self.child_count = self.fn("urui_element_child_count", I, U, U, U)
        self.child = self.fn("urui_element_child_at", U, U, U, U, I)
        self.query_fn = self.fn("urui_element_query", I, U, U, U, S, S, P, I)
        self.read_fn = self.fn("urui_element_read", I, U, U, U, I, S, P, I, P)
        self.write = self.fn("urui_element_write", I, U, U, U, I, S, S)
        self.erase = self.fn("urui_element_erase", I, U, U, U, I, S)
        self.classes = self.fn("urui_element_class", I, U, U, U, S, I)
        self.insert = self.fn("urui_element_insert", I, U, U, U, U, I)
        self.insert_before = self.fn("urui_element_insert_before", I, U, U, U, U, U)
        self.detach = self.fn("urui_element_detach", I, U, U, U)
        self.clear = self.fn("urui_element_clear", I, U, U, U)
        self.destroy = self.fn("urui_element_destroy", I, U, U, U)
        self.valid = self.fn("urui_element_ref_valid", I, U, U, U)
        self.attached = self.fn("urui_element_attached", I, U, U, U)
        self.pop = self.fn("urui_element_pop_click", I, U, U, P)
        self.assertEqual(self.fn("urui_bridge_abi_version", I)(), 13)
        surface = U()
        self.assertEqual(self.fn("urui_surface_create", I, I, I, P)(640, 480, C.byref(surface)), 1)
        self.surface = surface.value
        markup = b"<!doctype html><html><head><style>body{margin:0;}#panel{width:200px;height:100px;background-color:red;}</style></head><body><div id='panel'><div id='first' class='item'></div><div><span id='deep' class='item'></span></div><span id='last' class='item'></span></div></body></html>"
        self.owner = 0
        self.new_document()
        self.load_document(markup)
        self.root = self.root_fn(self.surface, self.owner)

    def new_document(self):
        owner = U()
        self.assertEqual(self.fn("urui_document_create", I, U, P)(self.surface, C.byref(owner)), 1)
        self.owner = owner.value

    def load_document(self, html):
        self.assertEqual(self.fn("urui_document_load_html", I, U, U, S, S)(self.surface, self.owner, html, b"/elements.html"), 1)
        self.assertEqual(self.fn("urui_document_set_presentation", I, U, U, I, I, I, I)(self.surface, self.owner, 0, 0, 1, 0), 1)

    def close_document(self):
        self.assertEqual(self.fn("urui_document_close", I, U, U)(self.surface, self.owner), 1)

    def tearDown(self):
        self.fn("urui_surface_destroy", I, U)(self.surface)

    def query(self, root, name=None, cls=None):
        name = None if name is None else name.encode()
        cls = None if cls is None else cls.encode()
        count = self.query_fn(self.surface, self.owner, root, name, cls, None, 0)
        self.assertGreaterEqual(count, 0)
        values = (U * count)()
        self.assertEqual(self.query_fn(self.surface, self.owner, root, name, cls, values, count), count)
        return list(values)

    def read(self, token, kind=0, name=None):
        required = I()
        name = None if name is None else name.encode()
        result = self.read_fn(self.surface, self.owner, token, kind, name, None, 0, C.byref(required))
        if result == 0:
            return None
        self.assertEqual(result, -5)
        buffer = C.create_string_buffer(required.value)
        self.assertEqual(self.read_fn(self.surface, self.owner, token, kind, name, buffer, len(buffer), C.byref(required)), 1)
        return buffer.value.decode()

    def test_button_disabled_styles_follow_authored_and_dynamic_attribute(self):
        fixture = Path(__file__).parent / "fixtures/button-states.html"
        self.load_document(fixture.read_bytes())
        self.root = self.root_fn(self.surface, self.owner)
        update = self.fn("urui_surface_update", I, U)
        style = self.fn("urui_element_style_read", I, U, U, U, I, S, P, I, P)
        def opacity(token):
            self.assertEqual(update(self.surface), 1)
            buffer = C.create_string_buffer(80)
            required = I()
            self.assertEqual(style(self.surface, self.owner, token, 0, b"opacity", buffer, len(buffer), C.byref(required)), 1)
            return float(buffer.value.decode())
        authored = self.query(self.root, "authored")[0]
        dynamic = self.query(self.root, "dynamic")[0]
        self.assertEqual(opacity(authored), .25, "Authored disabled button must match :disabled")
        self.assertEqual(opacity(dynamic), 1)
        self.assertEqual(self.write(self.surface, self.owner, dynamic, 2, b"disabled", b""), 1)
        self.assertEqual(opacity(dynamic), .25, "SetEnabled must drive ordinary Web-like :disabled styles")
        self.assertEqual(self.erase(self.surface, self.owner, dynamic, 0, b"disabled"), 1)
        self.assertEqual(opacity(dynamic), 1)

    def test_image_preload_reports_dimensions_without_changing_dom(self):
        self.close_document()
        pixels = (C.c_ubyte * 8)(255, 0, 0, 255, 0, 255, 0, 255)
        handle = U()
        self.new_document()
        register = self.fn("urui_document_register_rgba_texture", I, U, U, S, I, I, P, I, P)
        self.assertEqual(register(self.surface, self.owner, b"/preloaded.png", 2, 1, pixels, 8, C.byref(handle)), 1)
        fixture = Path(__file__).parent / "fixtures/button-states.html"
        self.load_document(fixture.read_bytes())
        self.root = self.root_fn(self.surface, self.owner)
        before = self.child_count(self.surface, self.owner, self.root)
        preload = self.fn("urui_document_preload_image", I, U, U, S, P, P)
        width, height = I(), I()
        self.assertEqual(preload(self.surface, self.owner, b"preloaded.png", C.byref(width), C.byref(height)), 1)
        self.assertEqual((width.value, height.value), (2, 1))
        self.assertEqual(preload(self.surface, self.owner, b"missing-preload.png", C.byref(width), C.byref(height)), 0)
        self.assertEqual((width.value, height.value), (0, 0))
        self.assertEqual(self.child_count(self.surface, self.owner, self.root), before)

    def test_root_query_order_and_literal_scopes(self):
        self.assertNotEqual(self.root, 0)
        self.assertEqual(self.root_fn(self.surface, self.owner), self.root)
        self.assertEqual(self.parent(self.surface, self.owner, self.root), 0)
        panel = self.query(self.root, "panel")[0]
        self.assertEqual(self.query(panel, "panel"), [panel])
        self.assertEqual([self.read(t) for t in self.query(panel, cls="item")], ["first", "deep", "last"])
        self.assertEqual(self.query(self.query(panel, "first")[0], "last"), [])
        self.assertEqual(self.query(panel, "#document"), [])

    def test_implicit_document_and_old_input_exports_are_absent(self):
        for name in ["urui_load_document", "urui_unload_document", "urui_register_resource",
                     "urui_register_rgba_texture", "urui_register_external_texture", "urui_unregister_texture",
                     "urui_image_preload", "urui_release_image", "urui_mouse_move", "urui_mouse_button",
                     "urui_mouse_wheel", "urui_touch_event", "urui_element_root", "urui_create", "urui_destroy"]:
            with self.subTest(symbol=name):
                self.assertIsNone(getattr(LIB, name, None), "Removed compatibility export: " + name)

    def test_detach_reparent_destroy_and_generation(self):
        panel = self.query(self.root, "panel")[0]
        created = self.create(self.surface, self.owner, b"div")
        self.assertNotEqual(created, 0)
        self.assertEqual(self.valid(self.surface, self.owner, created), 1)
        self.assertEqual(self.attached(self.surface, self.owner, created), 0)
        self.assertEqual(self.insert(self.surface, self.owner, panel, created, -1), 1)
        self.assertEqual(self.child(self.surface, self.owner, panel, 3), created)
        self.assertEqual(self.detach(self.surface, self.owner, created), 1)
        self.assertEqual(self.valid(self.surface, self.owner, created), 1)
        self.assertEqual(self.parent(self.surface, self.owner, created), 0)
        self.assertEqual(self.insert(self.surface, self.owner, self.root, created, 0), 1)
        self.assertEqual(self.child(self.surface, self.owner, self.root, 0), created)
        self.assertEqual(self.destroy(self.surface, self.owner, created), 1)
        self.assertEqual(self.valid(self.surface, self.owner, created), 0)
        for _ in range(128):
            replacement = self.create(self.surface, self.owner, b"div")
            self.assertNotEqual(replacement, created)
            self.assertEqual(self.destroy(self.surface, self.owner, replacement), 1)
        self.close_document()
        self.assertEqual(self.valid(self.surface, self.owner, panel), 0)

    def test_insert_validation_preserves_order_and_clear_retains_nodes(self):
        panel = self.query(self.root, "panel")[0]
        first = self.child(self.surface, self.owner, panel, 0)
        self.assertLess(self.insert(self.surface, self.owner, first, panel, 0), 0)
        self.assertLess(self.insert(self.surface, self.owner, panel, first, 99), 0)
        self.assertEqual(self.child(self.surface, self.owner, panel, 0), first)
        self.assertEqual(self.insert(self.surface, self.owner, panel, first, 2), 1)
        self.assertEqual(self.child(self.surface, self.owner, panel, 2), first)
        mid = self.child(self.surface, self.owner, panel, 1)
        self.assertEqual(self.insert_before(self.surface, self.owner, panel, first, mid), 1)
        self.assertEqual(self.child(self.surface, self.owner, panel, 1), first)
        self.assertLess(self.detach(self.surface, self.owner, self.root), 0)
        self.assertLess(self.destroy(self.surface, self.owner, self.root), 0)
        nodes = [self.child(self.surface, self.owner, panel, i) for i in range(3)]
        self.assertEqual(self.clear(self.surface, self.owner, panel), 1)
        self.assertEqual(self.child_count(self.surface, self.owner, panel), 0)
        self.assertTrue(all(self.valid(self.surface, self.owner, n) == 1 and self.parent(self.surface, self.owner, n) == 0 for n in nodes))
        self.assertEqual(self.insert(self.surface, self.owner, nodes[0], nodes[1], -1), 1)
        self.assertEqual(self.destroy(self.surface, self.owner, nodes[0]), 1)
        self.assertEqual(self.valid(self.surface, self.owner, nodes[1]), 0)

    def test_mutation_uses_identity_and_preserves_exact_text(self):
        panel = self.query(self.root, "panel")[0]
        a, b = self.create(self.surface, self.owner, b"div"), self.create(self.surface, self.owner, b"div")
        for t in [a, b]:
            self.assertEqual(self.write(self.surface, self.owner, t, 0, None, b"same"), 1)
            self.assertEqual(self.insert(self.surface, self.owner, panel, t, -1), 1)
        self.assertEqual(self.query(panel, "same"), [a, b])
        self.assertEqual(self.write(self.surface, self.owner, a, 0, None, "重命名".encode()), 1)
        text = "  <tag>& {{literal}}\n中文 🎮  "
        self.assertEqual(self.write(self.surface, self.owner, a, 1, None, text.encode()), 1)
        self.assertEqual(self.read(a, 2), text)
        self.assertEqual(self.read(b, 2), "")
        self.assertEqual(self.write(self.surface, self.owner, a, 2, b"title", "提示".encode()), 1)
        self.assertEqual(self.read(a, 3, "title"), "提示")
        self.assertEqual(self.erase(self.surface, self.owner, a, 0, b"title"), 1)
        self.assertIsNone(self.read(a, 3, "title"))
        self.assertEqual(self.classes(self.surface, self.owner, a, b"active", 1), 1)
        self.assertIn("active", self.read(a, 3, "class"))
        self.assertEqual(self.classes(self.surface, self.owner, a, b"active", -1), 1)
        self.assertEqual(self.classes(self.surface, self.owner, a, b"active", 0), 1)
        self.assertEqual(self.classes(self.surface, self.owner, a, b"active", -1), 0)
        self.assertLess(self.classes(self.surface, self.owner, a, b"two classes", 1), 0)

    def test_queued_click_cannot_retarget_replacement(self):
        panel = self.query(self.root, "panel")[0]
        self.fn("urui_surface_update", I, U)(self.surface)
        pointer = self.fn("urui_pointer_event", I, U, I, I, I, C.c_float, C.c_float, I, I, C.c_float, C.c_float, I)
        self.assertEqual(pointer(self.surface, 0, 0, 1, 10, 70, 0, 0, 0, 0, 0), 1)
        self.assertEqual(pointer(self.surface, 0, 0, 0, 10, 70, 0, 1, 0, 0, 0), 1)
        self.assertEqual(pointer(self.surface, 0, 0, 2, 10, 70, 0, 0, 0, 0, 0), 1)
        self.assertEqual(self.destroy(self.surface, self.owner, panel), 1)
        target = U()
        self.assertEqual(self.pop(self.surface, self.owner, C.byref(target)), 0)

    def test_owned_detached_html_and_invalid_template_do_not_crash(self):
        code = f"""
import ctypes as C, resource
resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
l=C.CDLL({str(LIBRARY.resolve())!r})
surf=C.c_uint64()
l.urui_surface_create.argtypes=[C.c_int,C.c_int,C.POINTER(C.c_uint64)]
assert l.urui_surface_create(640,480,C.byref(surf))==1
owner=C.c_uint64()
l.urui_document_create.argtypes=[C.c_uint64,C.POINTER(C.c_uint64)]
assert l.urui_document_create(surf,C.byref(owner))==1
l.urui_document_load_html.argtypes=[C.c_uint64,C.c_uint64,C.c_char_p,C.c_char_p]
assert l.urui_document_load_html(surf,owner,b'<!doctype html><html><head></head><body></body></html>',b'/detached.html')==1
l.urui_element_create.argtypes=[C.c_uint64,C.c_uint64,C.c_char_p]
l.urui_element_create.restype=C.c_uint64
t=l.urui_element_create(surf,owner,b'div')
l.urui_element_set_inner_html.argtypes=[C.c_uint64,C.c_uint64,C.c_uint64,C.c_char_p,C.c_char_p]
result=l.urui_element_set_inner_html(surf,owner,t,b"<template><div></div></template>",b'/fragment.html')
invalid=l.urui_element_set_inner_html(surf,owner,t,b"<template src='missing'/>",b'/invalid-fragment.html')
l.urui_surface_destroy.argtypes=[C.c_uint64]
l.urui_surface_destroy(surf)
raise SystemExit(0 if result == 1 and invalid < 0 else 2)
"""
        child = subprocess.run([sys.executable, "-c", code], capture_output=True, text=True)
        self.assertEqual(child.returncode, 0, "Owned detached HTML must accept valid input and reject invalid input safely, child=" + str(child.returncode) + " " + child.stderr)

    def test_identity_budget_reports_parent_and_query_failure(self):
        leaf = self.query(self.root, "deep")[0]
        created = [self.create(self.surface, self.owner, b"div") for _ in range(65534)]
        self.assertTrue(all(created))
        self.assertEqual(self.attached(self.surface, self.owner, leaf), 1)
        failure = (1 << 64) - 1
        self.assertEqual(self.parent(self.surface, self.owner, leaf), failure, "An existing parent must not become a false null at the token budget")
        find = self.fn("urui_element_find", U, U, U, U, S, S)
        self.assertEqual(find(self.surface, self.owner, self.root, b"panel", None), failure)
        self.assertEqual(find(self.surface, self.owner, self.root, None, None), self.root, "Existing canonical tokens remain usable")
        self.assertEqual(self.destroy(self.surface, self.owner, created[0]), 1)
        self.assertNotIn(self.parent(self.surface, self.owner, leaf), [0, failure], "Releasing a destroyed node restores capacity")


if __name__ == "__main__":
    unittest.main(verbosity=2)
