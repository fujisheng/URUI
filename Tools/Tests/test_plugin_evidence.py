"""A rebuilt binary must never inherit another revision's runtime acceptance."""
import ast
from pathlib import Path
import unittest


class PluginEvidenceTests(unittest.TestCase):
    def test_rebuilt_binary_drops_stale_runtime_and_link_evidence(self):
        # Load the pure metadata function without executing toolchain discovery/builds.
        path = Path(__file__).resolve().parents[1] / "build-native-platforms.py"
        tree = ast.parse(path.read_text())
        function = next(node for node in tree.body if isinstance(node, ast.FunctionDef)
                        and node.name == "invalidate_runtime_evidence")
        namespace = {}
        exec(compile(ast.Module(body=[function], type_ignores=[]), str(path), "exec"), namespace)
        entry = {"platform": "webgl", "sha256": "new", "abi_version": 9,
                 "render_plan_version": 2, "compiled": True,
                 "runtime_status": "passed", "runtime_validation": {"sha256": "old"},
                 "protocol_versions": {"urui_bridge_abi_version": 8},
                 "player_validation": {"passed": True, "nativeSha256": "old"},
                 "static_link_validation": {"sha256": "old", "linkExitCode": 0},
                 "build_audit": "old-build.json"}
        namespace["invalidate_runtime_evidence"](entry)
        self.assertEqual("not_run", entry["runtime_status"])
        self.assertIsNone(entry["runtime_validation"])
        for name in ["protocol_versions", "player_validation", "static_link_validation", "build_audit"]:
            self.assertNotIn(name, entry)
        self.assertEqual("new", entry["sha256"])
        self.assertEqual(9, entry["abi_version"])
        self.assertEqual(2, entry["render_plan_version"])
        self.assertTrue(entry["compiled"])
        self.assertEqual("webgl", entry["platform"])


if __name__ == "__main__":
    unittest.main()
