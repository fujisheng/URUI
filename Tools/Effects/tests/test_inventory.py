import copy
import importlib.util
import os
from pathlib import Path
import unittest


SOURCE = os.environ.get('URUI_UIEFFECT_PACKAGE')


@unittest.skipUnless(SOURCE, 'Set URUI_UIEFFECT_PACKAGE to the extracted UIEffect 5.8.4 package.')
class InventoryTests(unittest.TestCase):
    def tool(self):
        path = Path(__file__).resolve().parents[1] / 'inventory.py'
        self.assertTrue(path.is_file(), '尚未实现完整效果清单工具')
        spec = importlib.util.spec_from_file_location('effect_inventory', path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def test_all_enum_members_are_present(self):
        data = self.tool().read_inventory(Path(SOURCE))
        self.assertEqual('5.8.4', data['version'])
        enums = data['enums']
        self.assertEqual(13, len(enums))
        self.assertEqual(['None', 'Shadow', 'Shadow3', 'Outline', 'Outline8', 'Mirror'],
                         [x['name'] for x in enums['ShadowMode']])
        self.assertEqual(8, enums['Flip'][-1]['value'])
        self.assertEqual(6, enums['GradationColorFilter'][-1]['value'])
        self.assertEqual(12, len(enums['GradationMode']))

    def test_serialized_gradient_and_alias_are_classified(self):
        tool = self.tool()
        data = tool.read_inventory(Path(SOURCE))
        gradient = next(x for x in data['fields'] if x['id'] == 'UIEffect.m_GradationGradient')
        self.assertEqual('Gradient', gradient['type'])
        self.assertEqual('--urui-fx-gradation-gradient', gradient['css'])
        alias = next(x for x in data['properties'] if x['id'] == 'UIEffect.shadowGlow')
        self.assertEqual('shadowColorGlow', alias['aliasOf'])
        self.assertTrue(any(x['name'] == 'SetGradientKeys' for x in data['methods']))
        self.assertTrue(any(x['name'] == 'actualSamplingScale' for x in data['properties']))
        transition = next(x for x in data['fields'] if x['id'] == 'UIEffect.m_TransitionRate')
        self.assertEqual('--urui-fx-transition-progress', transition['css'])

    def test_missing_feature_fails_coverage(self):
        tool = self.tool()
        data = tool.read_inventory(Path(SOURCE))
        coverage = tool.new_coverage(data)
        self.assertEqual([], tool.audit_coverage(data, coverage))
        del coverage['features']['enum:TransitionFilter.Melt']
        self.assertIn('enum:TransitionFilter.Melt', '\n'.join(tool.audit_coverage(data, coverage)))

    def test_nested_tween_enums_are_not_mistaken_for_properties(self):
        data = self.tool().read_inventory(Path(SOURCE))
        self.assertIn('nestedEnums', data)
        self.assertEqual(['Once', 'Loop', 'PingPongOnce', 'PingPongLoop'],
                         [x['name'] for x in data['nestedEnums']['UIEffectTweener.WrapMode']])
        event = next(x for x in data['nestedEnums']['UIEffectTweener.CullingMask'] if x['name'] == 'Event')
        self.assertEqual(-2147483648, event['value'])
        self.assertFalse(any(x['type'] in ('enum', 'class') for x in data['properties']))

    def test_pending_and_missing_evidence_cannot_pass_final_gate(self):
        tool = self.tool()
        data = tool.read_inventory(Path(SOURCE))
        coverage = tool.new_coverage(data)
        self.assertTrue(tool.audit_coverage(data, coverage, final=True))
        item = coverage['features']['enum:TransitionFilter.Dissolve']
        item['status'] = 'passed'
        item['implementation'] = 'Runtime/Rendering/UIEffectInstance.cs'
        self.assertTrue(any('Dissolve' in x for x in tool.audit_coverage(data, coverage, final=True)))

    def test_vectors_and_root_references_have_real_target_contracts(self):
        data = self.tool().read_inventory(Path(SOURCE))
        scale = next(x for x in data['fields'] if x['id'] == 'UIEffect.m_TransitionTexScale')
        self.assertEqual(['--urui-fx-transition-texture-scale-x', '--urui-fx-transition-texture-scale-y'],
                         scale.get('cssComponents'))
        root = next(x for x in data['fields'] if x['id'] == 'UIEffect.m_CustomRoot')
        self.assertIsNone(root['css'])
        self.assertEqual('data-urui-effect-root', root.get('attribute'))


if __name__ == '__main__':
    unittest.main()
