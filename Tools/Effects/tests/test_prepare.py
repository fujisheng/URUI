import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


class PrepareTests(unittest.TestCase):
    def tool(self):
        path = Path(__file__).resolve().parents[1] / 'validate.py'
        self.assertTrue(path.is_file(), '尚未实现隔离验证项目准备工具')
        spec = importlib.util.spec_from_file_location('effect_validate', path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def test_unowned_project_is_never_overwritten(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder) / 'existing'
            project.mkdir()
            sentinel = project / 'user.txt'
            sentinel.write_text('保留')
            with self.assertRaises(ValueError):
                self.tool().prepare_project(project, {'test': '/unused'}, 'standalone')
            self.assertEqual('保留', sentinel.read_text())

    def test_standalone_has_no_coffee_fui_or_ugui_dependencies(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder) / 'isolated'
            self.tool().prepare_project(project, {'com.fujisheng.urui': '/core',
                'com.fujisheng.urui.effect': '/effect'}, 'standalone')
            manifest = json.loads((project / 'Packages/manifest.json').read_text())
            self.assertEqual('file:/effect', manifest['dependencies']['com.fujisheng.urui.effect'])
            self.assertTrue(all(not any(word in key for word in ('coffee', 'fui', 'ugui', 'textmeshpro'))
                                for key in manifest['dependencies']))
            self.assertIn('com.fujisheng.urui.effect', manifest['testables'])
            self.assertTrue((project / 'ProjectSettings/ProjectVersion.txt').is_file())

    def test_running_project_is_not_reconfigured(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder) / 'isolated'
            tool = self.tool()
            tool.prepare_project(project, {'com.fujisheng.urui': '/core'}, 'baseline')
            (project / 'Temp').mkdir()
            (project / 'Temp/UnityLockfile').touch()
            with self.assertRaises(ValueError):
                tool.prepare_project(project, {'different': '/different'}, 'baseline')
