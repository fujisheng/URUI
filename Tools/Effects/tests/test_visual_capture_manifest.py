import importlib.util
from pathlib import Path
import unittest


class VisualCaptureManifestTests(unittest.TestCase):
    def test_matrix_rejects_duplicates_missing_cases_and_wrong_space(self):
        spec = importlib.util.spec_from_file_location('visual_validate', Path(__file__).resolve().parents[1] / 'validate.py')
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        expected = ['TransitionFilter.Fade.000', 'TransitionFilter.Fade.025']
        valid = {'colorSpace': 'Gamma', 'cases': list(expected)}
        self.assertEqual([], module.audit_capture_ids(valid, expected, 'Gamma'))
        for invalid in [dict(valid, cases=expected + expected[:1]), dict(valid, cases=expected[:1]), dict(valid, colorSpace='Linear')]:
            self.assertTrue(module.audit_capture_ids(invalid, expected, 'Gamma'))
