import copy
import sys
from pathlib import Path
import unittest
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from performance import assess

class PerformanceBudgetTests(unittest.TestCase):
    def setUp(self):
        self.raw=dict(unity='6',graphics='Metal',cpu='M4',colorSpace='Gamma',width=1280,height=720,warmup=300,samples=1000,rounds=3,allocationCounterAvailable=False,cases=[])
        for n in range(13):
            for r in range(3):
                self.raw['cases'].append(dict(name=str(n),round=r,elements=100,documents=1,particles=0,synchronizeMs=[1.0]*1000,renderSubmitWallMs=[.1]*1000,allocatedBytes=[-1]*1000))
        self.budget=dict(cpuRegressionFactor=1.1,cpuRegressionSlackMs=.2)
    def test_same_samples_pass_without_claiming_zero_allocations(self):
        result=assess(self.raw,copy.deepcopy(self.raw),self.budget)
        self.assertEqual(result['status'],'passed_cpu_budget')
        self.assertFalse(result['cases'][0]['allocation']['available'])
    def test_regression_cannot_pass_on_relative_percentage_alone(self):
        candidate=copy.deepcopy(self.raw)
        for row in candidate['cases']:row['renderSubmitWallMs']=[.301]*1000
        self.assertEqual(assess(self.raw,candidate,self.budget)['status'],'failed')
    def test_missing_round_and_environment_change_fail_closed(self):
        candidate=copy.deepcopy(self.raw);candidate['cases'].pop();candidate['colorSpace']='Linear'
        result=assess(self.raw,candidate,self.budget)
        self.assertEqual(result['status'],'failed');self.assertGreaterEqual(len(result['errors']),2)
