"""Real native borrowed-image contracts; accepts a candidate library path (no Unity)."""
import ctypes as C
from pathlib import Path
import sys
import unittest

LIBRARY = (Path(sys.argv.pop(1)) if len(sys.argv) > 1 else
           Path(__file__).parents[2] / "Runtime/Plugins/macOS/libURUIBridge.dylib").resolve()
LIB = C.CDLL(str(LIBRARY))
U, I, S, P = C.c_uint64, C.c_int, C.c_char_p, C.c_void_p
HIGH = 1 << 63


class Command(C.Structure):
    _fields_ = [("geometry", U), ("texture", U), ("tx", C.c_float), ("ty", C.c_float),
                ("clip_enabled", I), ("clip_x", I), ("clip_y", I), ("clip_w", I), ("clip_h", I)]


class TextureProtocol(unittest.TestCase):
    def fn(self, name, result, *args):
        fn = getattr(LIB, name, None)
        self.assertIsNotNone(fn, "Missing texture protocol capability: " + name)
        fn.restype, fn.argtypes = result, list(args)
        return fn

    def setUp(self):
        self.assertEqual(self.fn("urui_texture_protocol_version", I)(), 1)
        self.assertEqual(self.fn("urui_bridge_abi_version", I)(), 3)
        self.assertEqual(self.fn("urui_element_api_version", I)(), 1)
        self.assertEqual(C.sizeof(Command), 48)
        self.external = self.fn("urui_register_external_texture", I, S, I, I, U)
        self.rgba = self.fn("urui_register_rgba_texture", I, S, I, I, P, I, P)
        self.unregister = self.fn("urui_unregister_texture", I, S)
        self.kind = self.fn("urui_get_texture_kind", I, U)
        self.info = self.fn("urui_get_texture_info", I, U, P, P, P)
        self.copy = self.fn("urui_copy_texture", I, U, P, I)
        self.exists = self.fn("urui_texture_exists", I, U)
        self.load = self.fn("urui_load_document", I, S)
        self.unload = self.fn("urui_unload_document", None)
        self.destroy = self.fn("urui_destroy", None)
        self.create = self.fn("urui_create", I, I, I, S)
        self.error = self.fn("urui_last_error", S)
        self.addCleanup(self.destroy)
        self.assertEqual(self.create(128, 128, b"/System/Library/Fonts/Supplemental/Arial.ttf"), 1)

    def dimensions(self, resource):
        width, height, size = I(-1), I(-1), I(-1)
        self.assertEqual(self.info(resource, C.byref(width), C.byref(height), C.byref(size)), 1)
        return width.value, height.value, size.value

    def register_rgba(self, source=b"owned"):
        # Hand-derived premultiplied RGBA: (200,100,50,128)->(100,50,25,128).
        pixels = (C.c_ubyte * 8)(200, 100, 50, 128, 11, 22, 33, 255)
        resource = U()
        self.assertEqual(self.rgba(source, 2, 1, pixels, 8, C.byref(resource)), 1)
        return resource.value

    def image_commands(self, *sources):
        markup = ("<rml><head><style>body{margin:0}img{width:16px;height:8px}</style></head><body>" +
                  "".join("<img src='" + source + "'/>" for source in sources) + "</body></rml>").encode()
        self.assertEqual(self.load(markup), 1)
        self.fn("urui_update", None)()
        self.fn("urui_render", None)()
        count = self.fn("urui_command_count", I)()
        commands = (Command * count)()
        self.assertEqual(self.fn("urui_get_commands", I, P, I)(commands, count), count)
        return [command.texture for command in commands if command.texture]

    def test_borrowed_metadata_has_no_pixels_and_copy_rejects_without_writes(self):
        resource = HIGH + 17
        self.assertEqual(self.external(b"borrowed", 64, 32, resource), 1)
        self.assertEqual(self.kind(resource), 2)
        self.assertEqual(self.exists(resource), 1)
        self.assertEqual(self.dimensions(resource), (64, 32, 0))
        out = (C.c_ubyte * 4)(11, 22, 33, 44)
        for capacity in [0, 4]:
            self.assertLess(self.copy(resource, out, capacity), 1)
            self.assertIn(b"borrowed", self.error().lower())
            self.assertEqual(list(out), [11, 22, 33, 44])
        self.assertLess(self.copy(resource, None, 0), 1)

    def test_existing_rgba_is_owned_low_domain_and_premultiplied_once(self):
        resource = self.register_rgba()
        self.assertGreater(resource, 0)
        self.assertLess(resource, HIGH)
        self.assertEqual(self.kind(resource), 1)
        self.assertEqual(self.dimensions(resource), (2, 1, 8))
        out = (C.c_ubyte * 8)()
        self.assertEqual(self.copy(resource, out, 8), 1)
        self.assertEqual(list(out), [100, 50, 25, 128, 11, 22, 33, 255])
        self.assertLess(self.copy(resource, out, 7), 1)

    def test_aliases_emit_logical_id_and_survive_rml_release_until_last_unregister(self):
        resource = HIGH + 31
        self.assertEqual(self.external(b"a", 20, 10, resource), 1)
        self.assertEqual(self.external(b"b", 20, 10, resource), 1)
        self.assertEqual(self.image_commands("a", "b"), [resource, resource])
        self.unload()
        self.assertEqual(self.dimensions(resource), (20, 10, 0))
        self.assertEqual(self.unregister(b"a"), 1)
        self.assertEqual(self.kind(resource), 2)
        self.assertEqual(self.image_commands("b"), [resource])
        self.unload()
        self.assertEqual(self.unregister(b"b"), 1)
        self.assertEqual(self.kind(resource), 0)
        self.assertEqual(self.exists(resource), 0)
        self.assertEqual(self.info(resource, None, None, None), 0)
        self.assertEqual(self.unregister(b"b"), 0)

    def test_same_normalized_alias_registration_does_not_leak_a_reference(self):
        resource = HIGH
        for source in [b"folder/image", b"folder\\image", b"folder/image"]:
            self.assertEqual(self.external(source, 4, 3, resource), 1)
        self.assertEqual(self.unregister(b"folder/image"), 1)
        self.assertEqual(self.kind(resource), 0)

    def test_rebinding_borrowed_alias_preserves_other_alias_and_removes_stale_record(self):
        old, replacement = HIGH + 41, HIGH + 42
        self.assertEqual(self.external(b"a", 8, 4, old), 1)
        self.assertEqual(self.external(b"b", 8, 4, old), 1)
        self.assertEqual(self.external(b"a", 9, 5, replacement), 1)
        self.assertEqual(self.dimensions(old), (8, 4, 0))
        self.assertEqual(self.image_commands("a", "b"), [replacement, old])
        self.unload()
        self.assertEqual(self.unregister(b"b"), 1)
        self.assertEqual(self.kind(old), 0)
        self.assertEqual(self.kind(replacement), 2)
        self.assertEqual(self.unregister(b"a"), 1)
        self.assertEqual(self.kind(replacement), 0)

    def test_rgba_rebind_preserves_borrowed_aliases_and_owned_replacement_lifetime(self):
        borrowed = HIGH + 51
        self.assertEqual(self.external(b"a", 8, 4, borrowed), 1)
        self.assertEqual(self.external(b"b", 8, 4, borrowed), 1)
        owned = self.register_rgba(b"a")
        self.assertEqual(self.kind(borrowed), 2)
        self.assertEqual(self.image_commands("a", "b"), [owned, borrowed])
        self.unload()
        self.assertEqual(self.unregister(b"a"), 1)
        self.assertEqual(self.kind(owned), 0)
        self.assertEqual(self.kind(borrowed), 2)
        self.assertEqual(self.unregister(b"b"), 1)
        self.assertEqual(self.kind(borrowed), 0)

    def test_external_rebind_removes_previous_owned_and_final_borrowed_records(self):
        owned = self.register_rgba(b"a")
        first, second = HIGH + 61, (1 << 64) - 1
        self.assertEqual(self.external(b"a", 2, 1, first), 1)
        self.assertEqual(self.kind(owned), 0)
        self.assertEqual(self.external(b"a", 3, 2, second), 1)
        self.assertEqual(self.kind(first), 0)
        self.assertEqual(self.image_commands("a"), [second])

    def test_invalid_external_registration_preserves_existing_source_and_metadata(self):
        resource = HIGH + 71
        owned = self.register_rgba()
        self.assertEqual(self.external(b"a", 8, 4, resource), 1)
        invalid = [(b"a", 8, 4, 0), (b"a", 8, 4, owned), (b"a", 8, 4, HIGH - 1),
                   (b"a", 0, 4, HIGH + 72), (b"a", -1, 4, HIGH + 72),
                   (b"a", 8, 0, HIGH + 72), (b"a", 8, -1, HIGH + 72),
                   (b"a", 8193, 4, HIGH + 72), (b"a", 8, 8193, HIGH + 72),
                   (b"a", 2147483647, 2147483647, HIGH + 72),
                   (b"a", 9, 4, resource), (b"a", 8, 5, resource),
                   (b"other", 9, 4, resource), (None, 8, 4, HIGH + 72),
                   (b"", 8, 4, HIGH + 72), (b"/", 8, 4, HIGH + 72)]
        for args in invalid:
            with self.subTest(args=args):
                self.assertLess(self.external(*args), 0)
                self.assertEqual(self.dimensions(resource), (8, 4, 0))
                self.assertEqual(self.kind(HIGH + 72), 0)
                self.assertEqual(self.kind(owned), 1)
        self.assertEqual(self.image_commands("a"), [resource])

    def test_invalid_rgba_rebind_cannot_erase_borrowed_metadata(self):
        resource = HIGH + 81
        self.assertEqual(self.external(b"a", 8, 4, resource), 1)
        data = (C.c_ubyte * 4)(255, 255, 255, 255)
        handle = U(123)
        self.assertLess(self.rgba(b"a", 2, 1, data, 4, C.byref(handle)), 0)
        self.assertEqual(handle.value, 123)
        self.assertEqual(self.image_commands("a"), [resource])

    def test_active_document_rejects_all_registration_and_unregister_mutation(self):
        resource = HIGH + 91
        self.assertEqual(self.external(b"a", 8, 4, resource), 1)
        self.assertEqual(self.image_commands("a"), [resource])
        self.assertEqual(self.external(b"a", 9, 5, HIGH + 92), -2)
        self.assertEqual(self.external(b"b", 8, 4, resource), -2)
        self.assertEqual(self.unregister(b"a"), -2)
        pixels, handle = (C.c_ubyte * 4)(1, 2, 3, 255), U(123)
        self.assertEqual(self.rgba(b"a", 1, 1, pixels, 4, C.byref(handle)), -2)
        self.assertEqual(handle.value, 123)
        self.assertEqual(self.dimensions(resource), (8, 4, 0))
        self.assertEqual(self.kind(HIGH + 92), 0)
        self.unload()
        self.assertEqual(self.unregister(b"b"), 0)
        self.assertEqual(self.image_commands("a"), [resource])

    def test_generated_font_textures_remain_owned_and_copyable(self):
        self.assertEqual(self.external(b"a", 8, 4, HIGH + 101), 1)
        self.assertEqual(self.load(b"<rml><head><style>body{font-family:Arial;font-size:20px}</style></head><body>Hello</body></rml>"), 1)
        self.fn("urui_update", None)()
        self.fn("urui_render", None)()
        count = self.fn("urui_command_count", I)()
        commands = (Command * count)()
        self.fn("urui_get_commands", I, P, I)(commands, count)
        generated = {command.texture for command in commands if command.texture}
        self.assertTrue(generated, self.error())
        for resource in generated:
            self.assertLess(resource, HIGH)
            self.assertEqual(self.kind(resource), 1)
            width, height, size = self.dimensions(resource)
            self.assertGreater(width, 0)
            self.assertGreater(height, 0)
            self.assertEqual(size, width * height * 4)
            self.assertEqual(self.copy(resource, (C.c_ubyte * size)(), size), 1)

    def test_context_teardown_invalidates_all_records_and_handles_missing_without_writes(self):
        resource = HIGH + 111
        self.assertEqual(self.external(b"a", 8, 4, resource), 1)
        owned = self.register_rgba()
        self.destroy()
        for identifier in [resource, owned, 0, HIGH + 112]:
            self.assertEqual(self.kind(identifier), 0)
            width, height, size = I(11), I(22), I(33)
            self.assertEqual(self.info(identifier, C.byref(width), C.byref(height), C.byref(size)), 0)
            self.assertEqual((width.value, height.value, size.value), (11, 22, 33))
        self.assertLess(self.external(b"a", 8, 4, resource), 0)
        self.assertEqual(self.create(128, 128, None), 1)
        self.assertEqual(self.kind(resource), 0)
        self.assertEqual(self.external(b"new", 7, 3, HIGH + 113), 1)
        self.assertEqual(self.kind(resource), 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
