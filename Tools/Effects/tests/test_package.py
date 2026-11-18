import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


class PackageTests(unittest.TestCase):
    def test_runtime_dependency_leak_is_rejected(self):
        path = Path(__file__).resolve().parents[1] / 'validate.py'
        spec = importlib.util.spec_from_file_location('effect_validate', path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.assertTrue(hasattr(module, 'package_contract'), '包门禁必须拒绝运行时旧框架依赖')
        with tempfile.TemporaryDirectory() as folder:
            package = Path(folder)
            (package / 'package.json').write_text(json.dumps({'name': 'com.fujisheng.urui.effect',
                'dependencies': {'com.fujisheng.urui': '0.4.0-preview.1', 'com.coffee.ui-effect': '5.8.4'}}))
            errors = module.package_contract(package)
            self.assertTrue(any('com.coffee.ui-effect' in error for error in errors))
