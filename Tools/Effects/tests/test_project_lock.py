import importlib.util
from pathlib import Path
import tempfile
import unittest


class ProjectLockTests(unittest.TestCase):
    def tool(self):
        spec = importlib.util.spec_from_file_location('lock_validate', Path(__file__).resolve().parents[1] / 'validate.py')
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def test_abandoned_lock_is_removed_only_when_no_editor_or_file_holder_exists(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder)
            lock = project / 'Temp/UnityLockfile'
            lock.parent.mkdir(); lock.touch()
            self.tool().require_idle_project(project)
            self.assertFalse(lock.exists())

    def test_open_lock_is_rejected_and_preserved(self):
        with tempfile.TemporaryDirectory() as folder:
            project = Path(folder)
            lock = project / 'Temp/UnityLockfile'
            lock.parent.mkdir()
            with lock.open('w'):
                with self.assertRaises(ValueError):
                    self.tool().require_idle_project(project)
                self.assertTrue(lock.exists())
