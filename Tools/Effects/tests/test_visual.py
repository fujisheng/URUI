import importlib.util
from pathlib import Path
import tempfile
import unittest
from PIL import Image


class VisualTests(unittest.TestCase):
    def test_pixel_differences_and_missing_inputs_fail(self):
        spec = importlib.util.spec_from_file_location('visual_validate', Path(__file__).resolve().parents[1] / 'validate.py')
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as folder:
            a = Path(folder) / 'a.png'; b = Path(folder) / 'b.png'
            Image.new('RGBA', (4, 4), (50, 60, 70, 255)).save(a)
            self.assertNotEqual('passed', module.compare_pixel_images(a, b, 4 / 255, .5 / 255)['status'])
            Image.new('RGBA', (4, 4), (50, 60, 70, 255)).save(b)
            self.assertEqual('passed', module.compare_pixel_images(a, b, 4 / 255, .5 / 255)['status'])
            Image.new('RGBA', (4, 4), (55, 60, 70, 255)).save(b)
            self.assertEqual('failed', module.compare_pixel_images(a, b, 4 / 255, .5 / 255)['status'])
