#!/usr/bin/env python3
"""准备隔离的 URUI 效果验证项目，并对证据执行失败关闭的门禁。"""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[2]


def require_idle_project(project):
    project = Path(project).resolve()
    processes = subprocess.run(['ps', '-axo', 'pid=,args='], capture_output=True, text=True, check=True)
    pattern = re.compile(r'^\s*\d+\s+\S*/Unity\.app/Contents/MacOS/Unity\s')
    project_argument = re.compile(r' -projectPath ' + re.escape(str(project)) + r'(?:\s|$)')
    if any(pattern.search(line) and project_argument.search(line) for line in processes.stdout.splitlines()):
        raise ValueError('隔离项目 Editor 仍在运行，不能启动第二个 Editor')
    lock = project / 'Temp/UnityLockfile'
    if lock.exists():
        holders = subprocess.run(['/usr/sbin/lsof', '-F', 'p', str(lock)], capture_output=True, text=True)
        if holders.returncode != 1 or holders.stdout:
            raise ValueError('隔离项目锁仍有持有者或无法验证，保留锁并拒绝运行')
        lock.unlink()


def prepare_project(project, packages, kind):
    project = Path(project).resolve()
    marker = project / 'urui-effect-validation.json'
    if project.exists() and not marker.is_file():
        raise ValueError('拒绝覆盖无归属目录：' + str(project))
    if (project / 'Temp/UnityLockfile').exists():
        raise ValueError('验证项目运行中，拒绝修改：' + str(project))
    if marker.is_file() and json.loads(marker.read_text()).get('owner') != 'URUI.Effect':
        raise ValueError('验证目录归属不符：' + str(project))
    if kind == 'standalone' and any(any(word in name for word in ('coffee', 'fui', 'ugui', 'textmeshpro'))
                                    for name in packages):
        raise ValueError('独立效果项目包含禁止的依赖')
    for name in ('Assets', 'Packages', 'ProjectSettings'):
        (project / name).mkdir(parents=True, exist_ok=True)
    dependencies = {name: 'file:' + str(Path(path).resolve()) for name, path in packages.items()}
    dependencies['com.unity.test-framework'] = '1.6.0'
    # 只供验证项目保存/读取参考 PNG；生产效果包不依赖此模块。
    dependencies['com.unity.modules.imageconversion'] = '1.0.0'
    if kind == 'reference':
        dependencies['com.unity.ugui'] = '2.0.0'
    manifest = {'dependencies': dependencies,
                'testables': [x for x in packages if x.startswith('com.fujisheng.') and 'unitycli' not in x]}
    (project / 'Packages/manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text(
        'm_EditorVersion: 6000.3.22f1\nm_EditorVersionWithRevision: 6000.3.22f1\n')
    marker.write_text(json.dumps({'owner': 'URUI.Effect', 'schemaVersion': 1, 'kind': kind}, indent=2) + '\n')
    return project


def _inventory_module():
    path = Path(__file__).with_name('inventory.py')
    spec = importlib.util.spec_from_file_location('effect_inventory', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def coverage_gate(evidence, final):
    module = _inventory_module()
    inventory = json.loads((evidence / 'source/inventory.json').read_text())
    coverage = json.loads((evidence / 'coverage/manifest.json').read_text())
    errors = module.audit_coverage(inventory, coverage, final)
    if final:
        for key, feature in coverage['features'].items():
            for path in feature.get('evidence', []):
                if not (evidence / path).is_file():
                    errors.append('证据文件缺失：' + key + ': ' + path)
    return {'status': 'failed' if errors else 'passed', 'final': final,
            'features': len(coverage['features']), 'errors': errors}


def read_test_result(path):
    path = Path(path)
    if not path.is_file():
        return {'status': 'insufficient', 'reason': '没有测试结果文件', 'failures': []}
    root = ET.parse(path).getroot()
    counts = {key: int(root.get(key, '0')) for key in ('total', 'passed', 'failed', 'skipped', 'inconclusive')}
    failures = [{'test': case.get('fullname', case.get('name')),
                 'message': case.findtext('failure/message', '')}
                for case in root.findall('.//test-case[@result="Failed"]')]
    if counts['failed'] or failures or root.get('result', '').startswith('Failed'):
        status = 'failed'
    elif not counts['total'] or not counts['passed'] or counts['skipped'] or counts['inconclusive']:
        status = 'insufficient'
    else:
        status = 'passed'
    return {'status': status, **counts, 'failures': failures, 'results': str(path)}


def run_managed(project, evidence, phase, test_platform, test_filter):
    project = project.resolve()
    marker = project / 'urui-effect-validation.json'
    if not marker.is_file() or json.loads(marker.read_text()).get('owner') != 'URUI.Effect':
        raise ValueError('只允许在已准备的隔离项目中运行测试')
    require_idle_project(project)
    run = evidence / 'runs' / (phase + '-' + str(time.time_ns()))
    run.mkdir(parents=True)
    unity = os.environ.get('URUI_UNITY') or '/Applications/Unity/Hub/Editor/6000.3.22f1/Unity.app/Contents/MacOS/Unity'
    command = [str(unity), '-batchmode', '-projectPath', str(project), '-job-worker-count', '2',
               '-runTests', '-testPlatform', test_platform, '-testFilter', test_filter,
               '-testResults', str(run / 'results.xml'), '-logFile', str(run / 'editor.log')]
    (run / 'command.json').write_text(json.dumps(command, indent=2) + '\n')
    try:
        environment = dict(os.environ, URUI_EFFECT_CAPTURE_OUTPUT=str(run / 'captures'))
        (run / 'capture-environment.json').write_text(json.dumps({'URUI_EFFECT_CAPTURE_OUTPUT': environment['URUI_EFFECT_CAPTURE_OUTPUT']}, indent=2) + '\n')
        result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=1800, env=environment)
        report = read_test_result(run / 'results.xml')
        report['exitCode'] = result.returncode
        log = (run / 'editor.log').read_text(errors='replace') if (run / 'editor.log').exists() else ''
        report['compileErrors'] = re.findall(r'^.*\berror CS\d+.*$', log, flags=re.M)
        if result.returncode or report['compileErrors']:
            report['status'] = 'failed'
    except subprocess.TimeoutExpired:
        report = {'status': 'failed', 'reason': '隔离测试超过 1800 秒'}
    report['commandFile'] = str(run / 'command.json')
    if (run / 'captures').is_dir():
        report['capturesDirectory'] = str(run / 'captures')
    (run / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    return report


def read_native_result(path):
    path = Path(path)
    if not path.is_file():
        return {'status': 'insufficient', 'reason': '没有 CTest 结果', 'tests': []}
    root = ET.parse(path).getroot()
    cases = root.findall('.//testcase')
    failed = [case.get('name') for case in cases if case.find('failure') is not None or case.find('error') is not None]
    skipped = [case.get('name') for case in cases if case.find('skipped') is not None or case.get('status') in ('notrun', 'disabled')]
    status = 'failed' if failed or int(root.get('failures', '0')) or int(root.get('errors', '0')) else (
        'passed' if cases and not skipped else 'insufficient')
    return {'status': status, 'total': len(cases), 'failures': failed, 'skipped': skipped,
            'tests': [case.get('name') for case in cases], 'results': str(path)}


def run_native(evidence, phase):
    source = ROOT / 'Packages/com.fujisheng.urui/Native~'
    build = evidence / 'native-build'
    run = evidence / 'runs' / (phase + '-native-' + str(time.time_ns()))
    run.mkdir(parents=True)
    cmake = os.environ.get('URUI_CMAKE') or shutil.which('cmake')
    if not cmake:
        raise ValueError('请在 PATH 中安装 CMake，或设置 URUI_CMAKE。')
    ctest = str(Path(cmake).with_name('ctest'))
    commands = [[cmake, '-S', str(source), '-B', str(build), '-DURUI_BUILD_TESTS=ON', '-DCMAKE_BUILD_TYPE=Release'],
                [cmake, '--build', str(build), '--parallel', '2'],
                [ctest, '--test-dir', str(build), '--output-on-failure', '--output-junit', str(run / 'tests.xml')]]
    exits = []
    (run / 'commands.json').write_text(json.dumps(commands, indent=2) + '\n')
    with (run / 'native.log').open('w') as log:
        for command in commands:
            try:
                result = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, timeout=1800)
                exits.append(result.returncode)
            except subprocess.TimeoutExpired:
                exits.append(-1)
            if exits[-1]:
                break
    report = read_native_result(run / 'tests.xml')
    report.update(exitCodes=exits, commands=str(run / 'commands.json'), log=str(run / 'native.log'))
    required = {'element_effect', 'style_contract', 'effect_recording', 'filter_contract', 'document_resources',
                'document_compositing_opacity', 'document_compositing_document', 'document_compositing_surface',
                'document_compositing_controls'}
    report['missingTests'] = sorted(required - set(report.get('tests', [])))
    if any(exits) or len(exits) != 3 or report['missingTests']:
        report['status'] = 'failed'
    (run / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    return report


def make_reference_clock_shader(source):
    identity = 'Shader "Hidden/UI/Default (UIEffect)"'
    include = '#include "Packages/com.coffee.ui-effect/Shaders/UIEffect.cginc"'
    if source.count(identity) != 1 or source.count(include) != 1:
        raise ValueError('锁定源 Shader 的身份或算法 include 已变化')
    return source.replace(identity, 'Shader "Hidden/URUI/Reference Clock (UIEffect)"').replace(
        include, 'float4 _ReferenceTime;\n            #define _Time _ReferenceTime\n            ' + include + '\n            #undef _Time')


def prepare_reference_assets(project, shader_source):
    source = ROOT / 'Tests~/EffectsReference'
    target = Path(project) / 'Assets/Reference'
    target.mkdir(parents=True, exist_ok=True)
    for name in ('ReferenceCapture.cs', 'ReferenceHarnessTests.cs', 'URUI.Effect.Reference.Tests.asmdef'):
        shutil.copy2(source / name, target / name)
    shutil.copytree(source / 'ClockRuntime', target / 'ClockRuntime', dirs_exist_ok=True)
    (target / 'ReferenceClock.shader').write_text(make_reference_clock_shader(shader_source))


def compare_pixel_images(reference, candidate, maximum, mean):
    from PIL import Image, ImageChops, ImageStat
    reference, candidate = Path(reference), Path(candidate)
    if not reference.is_file() or not candidate.is_file():
        return {'status': 'insufficient', 'reason': '缺少参考或候选图像', 'reference': str(reference), 'candidate': str(candidate)}
    with Image.open(reference) as original, Image.open(candidate) as actual:
        if original.size != actual.size:
            return {'status': 'failed', 'reason': '图像尺寸不同', 'referenceSize': original.size, 'candidateSize': actual.size}
        difference = ImageChops.difference(original.convert('RGBA'), actual.convert('RGBA'))
        max_error = max(pair[1] for pair in difference.getextrema()) / 255
        mean_error = sum(ImageStat.Stat(difference).mean) / (4 * 255)
        return {'status': 'passed' if max_error <= maximum and mean_error <= mean else 'failed',
                'maximumError': max_error, 'meanError': mean_error, 'reference': str(reference), 'candidate': str(candidate)}


def audit_sampling_capture_manifest(manifest, expected):
    cases = manifest.get('cases', [])
    errors = []
    if len(cases) != len(expected) or {case.get('id') for case in cases} != set(expected):
        errors.append('采样矩阵缺失或重复')
    baseline_targets = cases[0].get('leasedTargets', -1) if cases else -1
    for case in cases:
        name = case.get('id', '')
        kernel = next((count for mode, count in [('BlurFast', 25), ('BlurMedium', 81), ('BlurDetail', 169)]
                       if '.' + mode + '.' in name), 0)
        if case.get('nominalKernelSamples') != kernel:
            errors.append(name + ': 原始核采样数不一致')
        if case.get('processingPasses') != 0 or not 0 <= case.get('leasedTargets', -1) <= baseline_targets:
            errors.append(name + ': 叶子路径增加处理 Pass 或 RT')
    return errors


def audit_capture_ids(manifest, expected, space):
    cases = manifest.get('cases', [])
    errors = []
    if manifest.get('colorSpace') != space:
        errors.append('色彩空间不一致')
    if len(cases) != len(expected) or set(cases) != set(expected):
        errors.append('捕获矩阵缺失、重复或包含未声明项')
    return errors


def audit_blend_matrix(cases, modes):
    import math
    expected = {(source, destination) for source in modes for destination in modes}
    errors = []
    if len(cases) != len(expected) or {(row.get('source'), row.get('destination')) for row in cases} != expected:
        errors.append('Blend 因子矩阵缺失或重复')
    for row in cases:
        error = row.get('maximumError', float('nan'))
        if not isinstance(error, (int, float)) or not math.isfinite(error) or not 0 <= error < .0035:
            errors.append(str((row.get('source'), row.get('destination'))) + ': 数值比较失败')
    return errors


def geometry_visual_gate(evidence):
    tolerance = json.loads((evidence / 'reference/tolerance.json').read_text())
    source = json.loads((evidence / 'reference-e06-combinations-Gamma/captures.json').read_text())['captures']
    groups = {'gradation': ('gradationMode', 'gradationColorFilter'), 'shadow': ('shadowMode',), 'flip': ('flip',),
              'blend': ('blendType',), 'geometry-combination': ('e06',)}
    modes = ['Zero', 'One', 'DstColor', 'SrcColor', 'OneMinusDstColor', 'SrcAlpha', 'OneMinusSrcColor',
             'DstAlpha', 'OneMinusDstAlpha', 'SrcAlphaSaturate', 'OneMinusSrcAlpha']
    results, errors, numeric = [], [], {}
    for space in ('Gamma', 'Linear'):
        report = json.loads((evidence / ('e06-managed-' + space.lower() + '.json')).read_text())
        if report.get('status') != 'passed' or not report.get('capturesDirectory'):
            errors.append(space + ': 缺少通过的实际图像测试'); continue
        cases = {case.get('fullname', '') for case in ET.parse(report['results']).findall('.//test-case[@result="Passed"]')}
        for name in ('AllGradationsAndFixedKeys', 'ShadowOutlineMirrorBounds', 'AllSixteenFlipCombinations', 'RootResizeAndRemoval',
                     'EveryBlendFactorUsesDestinationAlpha', 'EveryBlendPresetMatchesReference', 'CustomBlendUnderNestedOpacityAndMask',
                     'OverflowClipsExpandedInk', 'GradientShadowFlipAndMeltDirectionMatchReference', 'CapturedInputDomainAndHaloMatchLeaf'):
            if not any(case.endswith('.' + name) for case in cases): errors.append(space + ': 缺少测试 ' + name)
        output = Path(report['capturesDirectory']) / space
        for group, properties in groups.items():
            expected = [row['id'] for row in source if row['property'] in properties]
            manifest = json.loads((output / (group + '-captures.json')).read_text())
            errors.extend(space + '/' + group + ': ' + error for error in audit_capture_ids(manifest, expected, space))
            for name in expected:
                results.append(compare_pixel_images(evidence / ('reference-e06-combinations-' + space) / (name + '.png'), output / (name + '.png'),
                                                    tolerance['maximumChannelError'], tolerance['meanChannelError']))
        matrix = json.loads((output / 'blend-numeric.json').read_text())
        if matrix.get('colorSpace') != space: errors.append(space + ': Blend 色彩空间不一致')
        errors.extend(space + ': ' + error for error in audit_blend_matrix(matrix.get('cases', []), modes))
        numeric[space] = matrix
        for name in ('RootResized', 'RootRemoved', 'ShadowClipped', 'CustomBlendNested', 'CapturedDomainMelt', 'CapturedDomainOutline'):
            if not (output / (name + '.png')).is_file(): errors.append(space + ': 缺少边界图像 ' + name)
    if len(results) != 100 or any(result['status'] != 'passed' for result in results): errors.append('渐变/几何/混合像素比较未全部通过')
    result = {'status': 'failed' if errors else 'passed', 'phase': 'E06', 'comparisons': results, 'blendNumeric': numeric, 'errors': errors,
              'source': 'locked UIEffect algorithms; BlendType reference uses serialized field initialization because its public setter is defective'}
    (evidence / 'visual-e06.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    return result


def transition_visual_gate(evidence):
    tolerance = json.loads((evidence / 'reference/tolerance.json').read_text())
    source = json.loads((evidence / 'reference-e05-Gamma/captures.json').read_text())['captures']
    groups = {'transition': ('transitionFilter',), 'detail': ('detailFilter', 'edgeMode'),
              'pattern': ('patternArea',), 'combination': ('e05',)}
    results, errors = [], []
    for space in ('Gamma', 'Linear'):
        report = json.loads((evidence / ('e05-managed-' + space.lower() + '.json')).read_text())
        if report.get('status') != 'passed' or not report.get('capturesDirectory'):
            errors.append(space + ': 缺少通过的实际图像测试'); continue
        cases = {case.get('fullname', '') for case in ET.parse(report['results']).findall('.//test-case[@result="Passed"]')}
        for name in ('EveryTransitionMatchesReference', 'EveryDetailAndEdgeMatchesReference', 'PatternAreaAllInnerEdge',
                     'DetailMaskThresholdEndpoints', 'TransitionThenDetailThenEdge', 'MeltAndBurnMoveSourceCoordinates',
                     'StaticTimeDoesNotInvalidate', 'DynamicTextureReplacementReprocesses', 'TextAndSubtreeUseSameParameters'):
            if not any(case.endswith('.' + name) for case in cases): errors.append(space + ': 缺少测试 ' + name)
        output = Path(report['capturesDirectory']) / space
        for group, properties in groups.items():
            expected = [row['id'] for row in source if row['property'] in properties]
            manifest = json.loads((output / (group + '-captures.json')).read_text())
            errors.extend(space + '/' + group + ': ' + error for error in audit_capture_ids(manifest, expected, space))
            for name in expected:
                results.append(compare_pixel_images(evidence / ('reference-e05-' + space) / (name + '.png'), output / (name + '.png'),
                                                    tolerance['maximumChannelError'], tolerance['meanChannelError']))
        for name in ('TextTransition', 'SubtreeTransition', 'MeltCoordinateProbe', 'BurnCoordinateProbe', 'DetailMaskZero', 'DetailMaskOne', 'DynamicPattern', 'StaticTime'):
            if not (output / (name + '.png')).is_file(): errors.append(space + ': 缺少边界图像 ' + name)
    if len(results) != 174 or any(result['status'] != 'passed' for result in results): errors.append('转场/细节/边缘像素比较未全部通过')
    result = {'status': 'failed' if errors else 'passed', 'phase': 'E05', 'comparisons': results, 'errors': errors,
              'source': 'locked UIEffect 5.8.4; fixed per-material time for moving texture/autoplay references'}
    (evidence / 'visual-e05.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    return result


def sampling_visual_gate(evidence):
    tolerance = json.loads((evidence / 'reference/tolerance.json').read_text())
    source = json.loads((evidence / 'reference-sampling-Gamma/captures.json').read_text())
    expected = [row['id'] for row in source['captures'] if row['property'] == 'samplingFilter']
    results, errors = [], []
    for space in ('Gamma', 'Linear'):
        report = json.loads((evidence / ('e04-managed-' + space.lower() + '.json')).read_text())
        if report.get('status') != 'passed' or not report.get('capturesDirectory'):
            errors.append(space + ': 缺少通过的实际图像测试'); continue
        cases = {case.get('fullname', '') for case in ET.parse(report['results']).findall('.//test-case[@result="Passed"]')}
        for name in ('AllSamplingModesMatchReference', 'AtlasNeighborCannotBleed', 'NineSliceAndFilledImageStayWithinSource',
                     'TextAtlasDoesNotSampleAnotherGlyph', 'SamplingExpansionHonorsClip', 'SamplingZeroIsIdentity',
                     'OriginalGeometryFreezesUvAndPixels', 'SubtreeExpansionIsResolvedBeforeAllocatingInput'):
            if not any(case.endswith('.' + name) for case in cases): errors.append(space + ': 缺少测试 ' + name)
        output = Path(report['capturesDirectory']) / space
        manifest = json.loads((output / 'sampling-captures.json').read_text())
        if manifest.get('colorSpace') != space: errors.append(space + ': 色彩空间错误')
        errors.extend(space + ': ' + error for error in audit_sampling_capture_manifest(manifest, expected))
        for name in expected:
            results.append(compare_pixel_images(evidence / ('reference-sampling-' + space) / (name + '.png'), output / (name + '.png'),
                                                tolerance['maximumChannelError'], tolerance['meanChannelError']))
        for name in ('SamplingClip.png', 'TextSamplingInput.png', 'SamplingZero-none.png', 'AtlasNonUniform.png', 'AtlasTiny.png'):
            if not (output / name).is_file(): errors.append(space + ': 缺少边界图像 ' + name)
    if len(results) != 48 or any(result['status'] != 'passed' for result in results): errors.append('采样像素比较未全部通过')
    result = {'status': 'failed' if errors else 'passed', 'phase': 'E04', 'comparisons': results, 'errors': errors,
              'costMeaning': 'nominalKernelSamples 是原算法逻辑核采样数；不是 GPU 实测；叶子新增处理 Pass/RT 均为零'}
    (evidence / 'visual-e04.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    return result


def visual_gate(evidence, phase):
    if phase == 'E06':
        return geometry_visual_gate(evidence)
    if phase == 'E05':
        return transition_visual_gate(evidence)
    if phase == 'E04':
        return sampling_visual_gate(evidence)
    if phase != 'E03':
        return {'status': 'not_implemented', 'reason': '尚未为此阶段定义完整图像矩阵'}
    tolerance = json.loads((evidence / 'reference/tolerance.json').read_text())
    source = json.loads((evidence / 'reference/captures.json').read_text())
    expected = ['Neutral'] + [row['id'] for row in source['captures'] if row['property'] in ('toneFilter', 'colorFilter', 'targetMode')]
    results = []; errors = []
    for space, folder in [('Gamma', 'reference'), ('Linear', 'reference-linear')]:
        report = json.loads((evidence / ('e03-managed-' + space.lower() + '.json')).read_text())
        if report.get('status') != 'passed' or not report.get('capturesDirectory'):
            errors.append(space + ' 缺少本轮实际通过的图像测试'); continue
        cases = {case.get('fullname', '') for case in ET.parse(report['results']).findall('.//test-case[@result="Passed"]')}
        for name in ('EveryToneAndColorModeMatchesReference', 'GammaAndLinearAreExplicit', 'TargetHueWrapsAtZero',
                     'PremultipliedTransparentHdrRemainsFinite', 'FilterFusionPreservesOrderAndClamping'):
            if not any(case.endswith('.' + name) for case in cases): errors.append(space + ': 缺少测试 ' + name)
        output = Path(report['capturesDirectory']) / space
        manifest = json.loads((output / 'color-captures.json').read_text())
        if manifest['colorSpace'] != space or set(manifest['cases']) != set(expected): errors.append(space + ': 图像矩阵不完整')
        results.append(compare_pixel_images(evidence / folder / 'Input.png', output / 'Input.png', 0, 0))
        for name in expected:
            results.append(compare_pixel_images(evidence / folder / (name + '.png'), output / (name + '.png'),
                                                tolerance['maximumChannelError'], tolerance['meanChannelError']))
        results.append(compare_pixel_images(evidence / 'reference/css-browser-final/CssFilterDirect-160x48/browser.png',
                                            output / 'CssFilterDirect.png', tolerance['maximumChannelError'], tolerance['meanChannelError']))
    if len(results) != 42 or any(result['status'] != 'passed' for result in results): errors.append('像素比较未全部通过')
    report = {'status': 'failed' if errors else 'passed', 'phase': phase, 'comparisons': results, 'errors': errors,
              'source': 'locked UIEffect 5.8.4 for extension algorithms; actual Edge screenshot for standard CSS'}
    (evidence / 'visual-e03.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n')
    return report


def package_contract(package):
    package = Path(package)
    data = json.loads((package / 'package.json').read_text())
    errors = []
    if data.get('name') != 'com.fujisheng.urui.effect':
        errors.append('包身份不正确')
    allowed = {'com.fujisheng.urui', 'com.unity.modules.particlesystem'}
    for name in data.get('dependencies', {}):
        if name not in allowed:
            errors.append('禁止的生产依赖：' + name)
    runtime = package / 'Runtime'
    for path in runtime.rglob('*.asmdef'):
        assembly = json.loads(path.read_text())
        for reference in assembly.get('references', []):
            if reference != 'URUI.Runtime':
                errors.append('未声明的运行时程序集依赖：' + reference)
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['prepare', 'coverage', 'native', 'managed', 'visual', 'performance', 'package'])
    parser.add_argument('--phase', required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--project', type=Path)
    parser.add_argument('--unitycli-package', type=Path, help='可选的 UnityCLI 本地包，用于验证工具桥接')
    parser.add_argument('--kind', choices=['baseline', 'reference', 'standalone'], default='standalone')
    parser.add_argument('--platform', choices=['EditMode', 'PlayMode'], default='PlayMode')
    parser.add_argument('--test-filter', default='URUI.Effect.Tests')
    parser.add_argument('--results', type=Path, help='只审计现有 NUnit XML，不宣称重新运行')
    args = parser.parse_args()
    evidence = args.evidence.resolve()
    evidence.mkdir(parents=True, exist_ok=True)
    if args.command == 'prepare':
        if args.project is None:
            parser.error('prepare 需要 --project')
        package_root = ROOT / 'Packages'
        if args.kind in ('baseline', 'reference'):
            package_root = evidence / 'baseline/packages'
        packages = {}
        names = ['com.fujisheng.urui', 'com.fujisheng.urui.effect'] if args.kind == 'standalone' else [
            'com.fujisheng.urui', 'com.fujisheng.fui.urui.effect']
        if args.kind == 'reference':
            names.append('com.coffee.ui-effect')
        for name in names:
            path = package_root / name
            if not (path / 'package.json').is_file():
                raise ValueError('缺少验证包：' + str(path))
            packages[name] = path
        if args.unitycli_package:
            packages['com.fujisheng.unitycli'] = args.unitycli_package.resolve()
        project = prepare_project(args.project, packages, args.kind)
        if args.kind == 'reference':
            prepare_reference_assets(project, (package_root / 'com.coffee.ui-effect/Shaders/UIEffect.shader').read_text(encoding='utf-8-sig'))
        if args.kind == 'standalone':
            gallery = Path(packages['com.fujisheng.urui.effect']) / 'Samples~/EffectGallery'
            target = project / 'Assets/Resources/URUI.Effect.Gallery'
            target.mkdir(parents=True, exist_ok=True)
            names = {'Gallery.html': 'GalleryDocument.html', 'Gallery.css': 'GalleryStyle.css',
                     'Gallery.shader': 'GalleryProgram.shader'}
            for source in gallery.iterdir():
                if source.is_file():
                    name = source.name
                    if name.endswith('.meta'):
                        name = names.get(name[:-5], name[:-5]) + '.meta'
                    else:
                        name = names.get(name, name)
                    shutil.copy2(source, target / name)
        result = {'status': 'prepared', 'kind': args.kind, 'project': str(project)}
    elif args.command == 'coverage':
        result = coverage_gate(evidence, args.phase == 'E10')
    elif args.command == 'managed':
        if args.results:
            result = read_test_result(args.results)
            result['auditOnly'] = True
        elif args.project:
            result = run_managed(args.project, evidence, args.phase, args.platform, args.test_filter)
        else:
            parser.error('managed 需要 --project 或 --results')
    elif args.command == 'native':
        result = run_native(evidence, args.phase)
    elif args.command == 'visual':
        result = visual_gate(evidence, args.phase)
    elif args.command == 'performance':
        from performance import gate
        result = gate(evidence)
    elif args.command == 'package':
        package = ROOT / 'Packages/com.fujisheng.urui.effect'
        errors = package_contract(package)
        if not args.project or not args.results:
            parser.error('package 需要干净 --project 和已执行的 --results NUnit XML')
        manifest = json.loads((args.project / 'Packages/manifest.json').read_text())
        for name in manifest['dependencies']:
            if any(word in name for word in ('coffee', 'fui', 'ugui', 'textmeshpro')):
                errors.append('隔离项目泄漏依赖：' + name)
        tests = read_test_result(args.results)
        if tests['status'] != 'passed':
            errors.append('功能测试未通过')
        cases = {x.get('fullname', '') for x in ET.parse(args.results).findall('.//test-case[@result="Passed"]')}
        for name in ('PackageHasOnlyAllowedRuntimeDependencies', 'ExistingEffectGalleryRunsWithoutFui'):
            if not any(x.endswith('.' + name) for x in cases):
                errors.append('缺少独立性验证：' + name)
        result = {'status': 'failed' if errors else 'passed', 'package': str(package),
                  'project': str(args.project), 'functional': tests, 'errors': errors}
    else:
        result = {'status': 'not_implemented', 'command': args.command,
                  'reason': '此门禁尚未实现；不能据此声明验证通过'}
    print(json.dumps(result, ensure_ascii=False))
    return 0 if result['status'] in ('prepared', 'passed', 'passed_cpu_budget') else 1


if __name__ == '__main__':
    sys.exit(main())
