import importlib.util
from pathlib import Path
import tempfile
import unittest


class NativeResultTests(unittest.TestCase):
    def test_ctest_empty_or_failed_is_not_accepted(self):
        spec = importlib.util.spec_from_file_location('native_validate', Path(__file__).resolve().parents[1] / 'validate.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'tests.xml'
            path.write_text('<testsuite tests="0" failures="0"/>')
            self.assertEqual('insufficient', module.read_native_result(path)['status'])
            path.write_text('<testsuite tests="1" failures="1"><testcase name="style"><failure message="ABI"/></testcase></testsuite>')
            self.assertEqual('failed', module.read_native_result(path)['status'])
            path.write_text('<testsuite tests="2" failures="0"><testcase name="style"/><testcase name="effect"/></testsuite>')
            self.assertEqual('passed', module.read_native_result(path)['status'])
