import importlib.util
from pathlib import Path
import unittest


class BlendMatrixTests(unittest.TestCase):
    def test_missing_duplicate_or_nonfinite_blend_results_fail(self):
        spec=importlib.util.spec_from_file_location('blend_validate',Path(__file__).resolve().parents[1]/'validate.py')
        module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
        modes=['Zero','One']
        rows=[dict(source=a,destination=b,maximumError=0.001) for a in modes for b in modes]
        self.assertEqual([],module.audit_blend_matrix(rows,modes))
        for invalid in [rows[:-1],rows+rows[:1],rows[:-1]+[dict(rows[-1],maximumError=float('nan'))],rows[:-1]+[dict(rows[-1],maximumError=.01)]]:
            self.assertTrue(module.audit_blend_matrix(invalid,modes))
