import importlib.util
from pathlib import Path
import tempfile
import unittest


class ReferenceClockTests(unittest.TestCase):
    def test_clock_wrapper_changes_only_identity_and_time_input(self):
        spec = importlib.util.spec_from_file_location('clock_validate', Path(__file__).resolve().parents[1] / 'validate.py')
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        include = '#include "Packages/com.coffee.ui-effect/Shaders/UIEffect.cginc"'
        source = 'Shader "Hidden/UI/Default (UIEffect)"\n' + include
        result = module.make_reference_clock_shader(source)
        self.assertIn('Hidden/URUI/Reference Clock (UIEffect)', result)
        self.assertIn('#define _Time _ReferenceTime\n', result)
        self.assertIn(include, result)
        self.assertIn('#undef _Time', result)
        with self.assertRaises(ValueError): module.make_reference_clock_shader('wrong source')
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            module.prepare_reference_assets(root, source)
            target = root / 'Assets/Reference'
            self.assertTrue((target / 'ClockRuntime/TimedReferenceEffect.cs').is_file())
            self.assertFalse((target / 'BaselinePerformanceTests.cs').exists())
