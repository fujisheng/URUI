import importlib.util
from pathlib import Path
import unittest


class SamplingManifestTests(unittest.TestCase):
    def test_missing_duplicate_or_wrong_kernel_cannot_pass(self):
        spec = importlib.util.spec_from_file_location('sampling_validate', Path(__file__).resolve().parents[1] / 'validate.py')
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        expected = ['SamplingFilter.None.000', 'SamplingFilter.BlurFast.050']
        a = {'id': expected[0], 'nominalKernelSamples': 0, 'processingPasses': 0, 'leasedTargets': 1}
        b = {'id': expected[1], 'nominalKernelSamples': 25, 'processingPasses': 0, 'leasedTargets': 1}
        self.assertEqual([], module.audit_sampling_capture_manifest({'cases': [a, b]}, expected))
        self.assertTrue(module.audit_sampling_capture_manifest({'cases': [a]}, expected))
        self.assertTrue(module.audit_sampling_capture_manifest({'cases': [a, b, b]}, expected))
        self.assertTrue(module.audit_sampling_capture_manifest({'cases': [a, dict(b, nominalKernelSamples=169)]}, expected))
        self.assertTrue(module.audit_sampling_capture_manifest({'cases': [a, dict(b, leasedTargets=2)]}, expected))
