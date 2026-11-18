import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class NamingAuditTests(unittest.TestCase):
    def run_audit(self, text, name="Source/Core/Example.cpp"):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        root = Path(temporary.name)
        file = (root / "URUI/Packages/com.fujisheng.urui/Native~/Engine" / name).resolve()
        file.parent.mkdir(parents=True)
        file.write_text(text)
        output = root / "report.json"
        run = subprocess.run([sys.executable, str(Path(__file__).with_name("audit_names.py")),
                              "--root", str(root), "--output", str(output), "--final"],
                             capture_output=True, text=True)
        return run, json.loads(output.read_text()) if output.exists() else {}

    def test_rejects_legacy_namespace(self):
        run, report = self.run_audit("namespace Rml { void Draw(); }\n")
        self.assertEqual(run.returncode, 1, run.stderr)
        self.assertEqual(len(report["violations"]), 1)

    def test_preserves_original_copyright(self):
        run, report = self.run_audit(
            "// Copyright (c) 2008-2026 RmlUi contributors\nnamespace URUI {}\n")
        self.assertEqual(run.returncode, 0, run.stderr)
        self.assertEqual(report["violations"], [])

    def test_provenance_does_not_allow_active_code(self):
        run, _ = self.run_audit('#include <RmlUi/Core.h>\nnamespace URUI {}\n')
        self.assertEqual(run.returncode, 1, run.stderr)

    def test_does_not_confuse_engine_and_probe_debug_macros(self):
        run, report = self.run_audit(
            "#ifdef URUI_ENGINE_DEBUG\nvoid Assert();\n#endif\n"
            "#ifdef URUI_DEBUG\nvoid Probe();\n#endif\n")
        self.assertEqual(run.returncode, 0, run.stderr)
        self.assertEqual(report["violations"], [])

    def test_rejects_removed_managed_api_names(self):
        for text in ["document.LoadMarkup(text);", "element.SetInnerMarkup(text);", "document.FontPath = path;", "var root = document.Root;"]:
            run, _ = self.run_audit(text)
            self.assertEqual(run.returncode, 1, text)

    def test_rejects_legacy_name_in_package_readme(self):
        run, _ = self.run_audit("# RmlUi wrapper\n", "../../README.md")
        self.assertEqual(run.returncode, 1, run.stderr)


if __name__ == "__main__":
    unittest.main()
