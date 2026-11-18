"""成本报告必须区分实际样本与可比较结论。"""
import copy
import unittest

from measure import compare_measurements


class MeasurementTests(unittest.TestCase):
    def setUp(self):
        self.sample = {
            "schemaVersion": 1,
            "environment": {"os": "test", "cpu": "test", "runtime": "native", "isolated": True},
            "cases": [{"name": "load", "unit": "ms", "method": "register+load+layout", "inputs": {"page.html": "a" * 64},
                       "samples": [1, 2, 3, 4]}],
        }

    def test_same_protocol_reports_absolute_statistics(self):
        report = compare_measurements(self.sample, self.sample)
        self.assertEqual(report["status"], "reported")
        self.assertEqual(report["cases"][0]["candidate"]["median"], 2.5)
        self.assertTrue(report["cases"][0]["comparable"])

    def test_missing_baseline_keeps_candidate_observations(self):
        report = compare_measurements(None, self.sample)
        self.assertEqual(report["status"], "insufficient")
        self.assertEqual(report["cases"][0]["candidate"]["count"], 4)
        self.assertFalse(report["cases"][0]["comparable"])

    def test_changed_inputs_are_not_comparable(self):
        candidate = copy.deepcopy(self.sample)
        candidate["cases"][0]["inputs"]["page.html"] = "b" * 64
        self.assertFalse(compare_measurements(self.sample, candidate)["cases"][0]["comparable"])

    def test_changed_environment_or_method_is_not_comparable(self):
        candidate = copy.deepcopy(self.sample)
        candidate["environment"]["runtime"] = "Player"
        self.assertEqual(compare_measurements(self.sample, candidate)["status"], "insufficient")
        candidate = copy.deepcopy(self.sample)
        candidate["cases"][0]["method"] = "load without registration"
        self.assertEqual(compare_measurements(self.sample, candidate)["status"], "insufficient")

    def test_uncontrolled_editor_prevents_comparison(self):
        candidate = copy.deepcopy(self.sample)
        candidate["environment"]["isolated"] = False
        self.assertEqual(compare_measurements(candidate, candidate)["status"], "insufficient")

    def test_missing_or_invalid_samples_are_insufficient(self):
        for values in [[], [float("nan")], [float("inf")], [-1], [True]]:
            candidate = copy.deepcopy(self.sample)
            candidate["cases"][0]["samples"] = values
            self.assertEqual(compare_measurements(self.sample, candidate)["status"], "insufficient")

    def test_unknown_schema_or_duplicate_workload_is_insufficient(self):
        candidate = copy.deepcopy(self.sample)
        candidate["schemaVersion"] = 2
        self.assertEqual(compare_measurements(self.sample, candidate)["status"], "insufficient")
        candidate = copy.deepcopy(self.sample)
        candidate["cases"].append(copy.deepcopy(candidate["cases"][0]))
        self.assertEqual(compare_measurements(candidate, candidate)["status"], "insufficient")


if __name__ == "__main__":
    unittest.main()
