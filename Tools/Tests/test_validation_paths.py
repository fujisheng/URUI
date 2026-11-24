"""验证工具不得把真实工程或源码目录当作可丢弃工程。"""
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


TOOLS = Path(__file__).resolve().parents[1]


class ValidationPathsTests(unittest.TestCase):
    def test_protected_projects_are_rejected_before_writes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tools = root / 'URUI/Tools'
            tools.mkdir(parents=True)
            client = root / 'Client'
            (client / 'Packages').mkdir(parents=True)
            (client / 'ProjectSettings').mkdir()
            manifest = client / 'Packages/manifest.json'
            manifest.write_text('SENTINEL', encoding='utf-8')
            (client / 'ProjectSettings/ProjectVersion.txt').write_text('fixture', encoding='utf-8')
            alias = root / 'client-alias'
            alias.symlink_to(client, target_is_directory=True)
            targets = [client, client / 'nested', root, tools.parent,
                       tools.parent / 'Packages', tools.parent / 'Packages/test',
                       client / '../Client', alias]
            for name in ['verify-authoring.py', 'verify-element-runtime.py']:
                shutil.copy2(TOOLS / name, tools / name)
                for target in targets:
                    with self.subTest(tool=name, target=target):
                        result = subprocess.run(
                            [sys.executable, str(tools / name), '--project', str(target)],
                            capture_output=True, text=True)
                        self.assertEqual(2, result.returncode, result.stderr)
                        self.assertIn('protected', result.stderr)
                        self.assertEqual('SENTINEL', manifest.read_text(encoding='utf-8'))
                        self.assertFalse((client / 'Library').exists())
                        self.assertFalse((client / 'nested').exists())
                        self.assertFalse((tools.parent / 'Packages').exists())

    def test_legacy_workspace_tools_fail_before_writes(self):
        for name in ['verify-borrowed-images.py']:
            with self.subTest(tool=name), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                tools = root / 'URUI/Tools'
                tools.mkdir(parents=True)
                shutil.copy2(TOOLS / name, tools / name)
                result = subprocess.run([sys.executable, str(tools / name)],
                                        capture_output=True, text=True)
                self.assertNotEqual(0, result.returncode)
                self.assertIn('No files changed', result.stderr)
                self.assertFalse((root / 'Assets').exists())
                self.assertFalse((root / 'Artifacts').exists())


if __name__ == '__main__':
    unittest.main()
