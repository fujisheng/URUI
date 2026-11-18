import importlib.util
from pathlib import Path
import tempfile
import unittest


class ResultTests(unittest.TestCase):
    def tool(self):
        path = Path(__file__).resolve().parents[1] / 'validate.py'
        spec = importlib.util.spec_from_file_location('effect_validate', path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        self.assertTrue(hasattr(module, 'read_test_result'), '必须拒绝零测试的假通过')
        return module

    def test_zero_tests_is_not_a_passing_gate(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'result.xml'
            path.write_text('<test-run result="Passed" total="0" passed="0" failed="0"/>')
            self.assertEqual('insufficient', self.tool().read_test_result(path)['status'])

    def test_actual_failed_case_remains_visible(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'result.xml'
            path.write_text('<test-run result="Failed" total="2" passed="1" failed="1">'
                '<test-case fullname="Colors.Transparent" result="Failed">'
                '<failure><message>黑边</message></failure></test-case></test-run>')
            result = self.tool().read_test_result(path)
            self.assertEqual('failed', result['status'])
            self.assertEqual('Colors.Transparent', result['failures'][0]['test'])
            self.assertEqual('黑边', result['failures'][0]['message'])

    def test_positive_passing_run_is_accepted(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'result.xml'
            path.write_text('<test-run result="Passed" total="2" passed="2" failed="0"/>')
            self.assertEqual('passed', self.tool().read_test_result(path)['status'])
