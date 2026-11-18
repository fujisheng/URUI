"""只接受环境、输入和采样协议一致的实际成本，不把缺测填成零。"""
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import re


def percentile(values, fraction):
    ordered = sorted(values)
    if not ordered or any(not isinstance(v, (int, float)) or not math.isfinite(v) or v < 0 for v in ordered):
        raise ValueError('性能样本必须有限、非负且非空')
    return ordered[math.ceil(len(ordered) * fraction) - 1]


def os_from_log(path):
    found = re.search(r'^macOS version:\s*(.+)$', Path(path).read_text(errors='replace'), re.M)
    if not found:
        raise ValueError('缺少实际 Editor OS 记录')
    return found.group(1).strip()


def assess(baseline, candidate, budget):
    keys = ['unity', 'graphics', 'cpu', 'colorSpace', 'width', 'height', 'warmup', 'samples', 'rounds']
    errors = [key + ' 不一致' for key in keys if baseline.get(key) != candidate.get(key)]
    if (candidate.get('warmup'), candidate.get('samples'), candidate.get('rounds')) != (300, 1000, 3):
        errors.append('不是冻结的300/1000/3采样协议')
    before = {(r['name'], r['round']): r for r in baseline['cases']}
    after = {(r['name'], r['round']): r for r in candidate['cases']}
    if len(before) != len(baseline['cases']) or len(after) != len(candidate['cases']) or before.keys() != after.keys() or len(after) != 39:
        errors.append('工作负载/轮次不完整或重复')
    rows = []
    for name in sorted({key[0] for key in before}):
        old = [before[(name, r)] for r in range(3) if (name, r) in before]
        new = [after[(name, r)] for r in range(3) if (name, r) in after]
        if len(old) != 3 or len(new) != 3:
            continue
        if any(a.get(key) != b.get(key) for a, b in zip(old, new) for key in ['elements', 'documents']):
            errors.append(name + ': 输入规模改变')
        row = {'name': name, 'elements': new[0]['elements'], 'documents': new[0]['documents'],
               'particlesBaseline': [r['particles'] for r in old], 'particlesCandidate': [r['particles'] for r in new]}
        for metric in ['synchronizeMs', 'renderSubmitWallMs']:
            if any(len(r.get(metric, [])) != 1000 for r in old + new):
                errors.append(name + ': 样本数量错误'); continue
            a = [v for r in old for v in r[metric]]; b = [v for r in new for v in r[metric]]
            original = {key: percentile(a, p) for key, p in [('p50', .5), ('p95', .95), ('p99', .99)]}
            actual = {key: percentile(b, p) for key, p in [('p50', .5), ('p95', .95), ('p99', .99)]}
            limit = max(original['p95'] * budget['cpuRegressionFactor'], original['p95'] + budget['cpuRegressionSlackMs'])
            row[metric] = {'baseline': original, 'candidate': actual, 'p95LimitMs': limit,
                           'passed': actual['p95'] <= limit}
            if actual['p95'] > limit:
                errors.append(name + '/' + metric + ': 超过冻结的P95预算')
        row['targets'] = {key: max(r.get(key, -1) for r in new) for key in ['peakLeasedBytes', 'peakLiveBytes', 'peakIdleBytes', 'createdTargets', 'reusedTargets', 'filterPasses']}
        row['allocation'] = {'available': candidate.get('allocationCounterAvailable', False),
                             'scope': '同步参数更新、SynchronizeLayout与Camera.Render；不含Unity原生和完整帧'}
        if row['allocation']['available']:
            row['allocation']['p95Bytes'] = percentile([v for r in new for v in r['allocatedBytes']], .95)
        else:
            row['allocation']['reason'] = '4096字节校准探针未通过；不是零GC'
        row['graphicsCounters'] = {}
        for metric, flag in [('drawCalls', 'drawCallsAvailable'), ('setPassCalls', 'setPassCallsAvailable')]:
            values = [v for r in new for v in r.get(metric, [])]
            available = candidate.get(flag, False) and len(values) == 3000 and all(v >= 0 for v in values) and any(v > 0 for v in values)
            row['graphicsCounters'][metric] = {'available': available}
            if available:
                row['graphicsCounters'][metric]['p95'] = percentile(values, .95)
        rows.append(row)
    return {'status': 'passed_cpu_budget' if not errors else 'failed', 'cases': rows, 'errors': errors,
            'gpu': {'available': False, 'reason': 'CPU提交墙钟不是GPU耗时；实际Player可用性另报'},
            'graphicsCounterScope': candidate.get('graphicsCounterScope'), 'samplesPerMetric': 39000}


def gate(evidence):
    evidence = Path(evidence)
    baseline = json.loads((evidence / 'baseline/effect-costs.json').read_text())
    candidate = json.loads((evidence / 'performance/effect-costs.json').read_text())
    budget = json.loads((evidence / 'baseline/budget.json').read_text())
    isolation = json.loads((evidence / 'performance/isolation.json').read_text())
    inputs = json.loads((evidence / 'e10-performance-inputs.json').read_text())
    result = assess(baseline, candidate, budget)
    if isolation.get('status') != 'passed' or isolation.get('otherUnityProcesses'):
        result['errors'].append('缺少排除竞争进程的证据')
    logs = {'baseline': evidence / 'baseline/performance-editor.log', 'candidate': Path(isolation['editorLog'])}
    environments = {kind: os_from_log(path) for kind, path in logs.items()}
    if environments['baseline'] != environments['candidate']:
        result['errors'].append('OS改变')
    # 复用已有 HtmlCss 的环境、输入、方法一致性门禁，不从历史摘要推算数据。
    path = Path(__file__).parents[1] / 'HtmlCss/measure.py'
    spec = importlib.util.spec_from_file_location('urui_measure', path); measure = importlib.util.module_from_spec(spec); spec.loader.exec_module(measure)
    def convert(raw, os_name):
        environment = {'os': os_name, 'cpu': raw['cpu'], 'runtime': raw['unity'], 'graphics': raw['graphics'],
                       'colorSpace': raw['colorSpace'], 'isolated': True, 'width': raw['width'], 'height': raw['height']}
        cases = []
        for name in sorted({r['name'] for r in raw['cases']}):
            rounds = sorted([r for r in raw['cases'] if r['name'] == name], key=lambda r: r['round'])
            identity = {'name': name, 'elements': rounds[0]['elements'], 'documents': rounds[0]['documents'],
                        'seed': 1234, 'viewport': [1280, 720], 'warmup': 300, 'samples': 1000, 'rounds': 3,
                        'workloads': inputs['workloadsSha256']}
            for metric in ['synchronizeMs', 'renderSubmitWallMs']:
                cases.append({'name': name + '/' + metric, 'samples': [v for r in rounds for v in r[metric]],
                              'inputs': {'workload': hashlib.sha256(json.dumps(identity, sort_keys=True).encode()).hexdigest()},
                              'unit': 'ms', 'method': raw['method']})
        return {'schemaVersion': 1, 'environment': environment, 'cases': cases}
    comparison = measure.compare_measurements(convert(baseline, environments['baseline']), convert(candidate, environments['candidate']))
    (evidence / 'performance/comparability.json').write_text(json.dumps(comparison, indent=2) + '\n')
    if comparison['status'] != 'reported':
        result['errors'].append('输入、环境或方法不可比')
    result['environment'] = comparison['environment']
    result['status'] = 'failed' if result['errors'] else 'passed_cpu_budget'
    (evidence / 'performance/acceptance.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    return result
