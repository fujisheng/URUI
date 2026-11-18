#!/usr/bin/env python3
"""离线提取锁定 UIEffect 源码的完整作者能力，审计迁移漏项。"""
import argparse
import ast
import hashlib
import json
from pathlib import Path
import re


SURFACES = ('UIEffect', 'UIEffectBase', 'UIEffectPreset', 'UIEffectReplica', 'UIEffectTweener')
ENUM_GROUPS = {
    'ToneFilter': ('C01', 'E03'), 'ColorFilter': ('C02', 'E03'),
    'SamplingFilter': ('C03', 'E04'), 'TransitionFilter': ('C04', 'E05'),
    'TargetMode': ('C05', 'E03'), 'BlendType': ('C06', 'E06'),
    'ShadowMode': ('C07', 'E06'), 'GradationMode': ('C08', 'E06'),
    'GradationColorFilter': ('C09', 'E06'), 'EdgeMode': ('C10', 'E05'),
    'DetailFilter': ('C11', 'E05'), 'PatternArea': ('C12', 'E05'), 'Flip': ('C12', 'E06'),
}


def _without_comments(text):
    pattern = r'"(?:\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/'
    return re.sub(pattern, lambda m: m[0] if m[0].startswith('"') else
                  ''.join('\n' if c == '\n' else ' ' for c in m[0]), text, flags=re.S)


def _enum_value(expression, known):
    reference = re.fullmatch(r'(\w+)\.(\w+)', expression)
    if reference:
        return known[reference[1]][reference[2]]
    def value(node):
        if isinstance(node, ast.Constant) and isinstance(node.value, int):
            return node.value
        if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Name):
            return known[node.value.id][node.attr]
        if isinstance(node, ast.BinOp):
            left, right = value(node.left), value(node.right)
            if isinstance(node.op, ast.LShift):
                return left << right
            if isinstance(node.op, ast.BitOr):
                return left | right
        raise ValueError('不支持的源枚举表达式：' + expression)
    result = value(ast.parse(expression, mode='eval').body)
    return result - 2 ** 32 if result >= 2 ** 31 else result


def css_name(name):
    name = name.removeprefix('m_')
    aliases = {'TransitionRate': 'TransitionProgress', 'transitionRate': 'TransitionProgress',
               'EdgeShinyRate': 'EdgeShinyProgress', 'edgeShinyRate': 'EdgeShinyProgress'}
    name = aliases.get(name, name)
    name = re.sub(r'Tex(?=$|Scale|Offset|Speed)', 'Texture', name)
    name = re.sub(r'([a-z0-9])([A-Z])', r'\1-\2', name)
    return '--urui-fx-' + name.lower()


def _group(owner, name):
    if owner == 'UIEffectTweener':
        return 'C13', 'E07'
    if owner == 'UIEffectReplica':
        return 'C14', 'E07'
    key = name.removeprefix('m_').lower()
    for prefix, group in [('tone', ('C01', 'E03')), ('color', ('C02', 'E03')),
                          ('sampling', ('C03', 'E04')), ('transition', ('C04', 'E05')),
                          ('target', ('C05', 'E03')), ('shadow', ('C07', 'E06')),
                          ('gradation', ('C08', 'E06')), ('edge', ('C10', 'E05')),
                          ('detail', ('C11', 'E05')), ('pattern', ('C12', 'E05')),
                          ('flip', ('C12', 'E06'))]:
        if key.startswith(prefix):
            return group
    if 'blend' in key:
        return 'C06', 'E06'
    if 'preset' in key or 'gradientkeys' in key or key == 'replicas':
        return 'C14', 'E07'
    return 'C15', 'E02'


def _describe_target(entry):
    kind, name = entry['type'], entry['name']
    if kind == 'RectTransform':
        entry['css'] = None
        entry['attribute'] = 'data-urui-effect-root'
    elif kind in ('Vector2', 'MinMax01'):
        suffixes = ('x', 'y') if kind == 'Vector2' else ('min', 'max')
        entry['cssComponents'] = [entry['css'] + '-' + suffix for suffix in suffixes]
    elif kind in ('Texture', 'Texture2D'):
        entry['styleType'] = 'Image'
    if name == 'shadowGlow':
        entry['css'] = '--urui-fx-shadow-color-glow'
    if entry['owner'] == 'UIEffectTweener':
        entry['targetKind'] = 'playback'
        entry['css'] = None
    elif entry['owner'] == 'UIEffectBase' and name in ('effectMaterial', 'graphic', 'effectId', 'context'):
        entry['targetKind'] = 'rendering-adapter'
        entry['css'] = None
    else:
        entry['targetKind'] = 'parameter'


def read_inventory(source):
    source = Path(source)
    package = json.loads((source / 'package.json').read_text())
    result = {'schemaVersion': 1, 'version': package['version'], 'enums': {}, 'nestedEnums': {},
              'fields': [], 'properties': [], 'methods': [], 'sourceHashes': {}}
    known = {}
    text = _without_comments((source / 'Runtime/Enums.cs').read_text(encoding='utf-8-sig'))
    for match in re.finditer(r'public enum (\w+)\s*\{([^}]+)\}', text):
        name, body = match.groups()
        current, members = -1, []
        known[name] = {}
        for member in body.split(','):
            if not member.strip():
                continue
            parts = member.strip().split('=', 1)
            current = _enum_value(parts[1].strip(), known) if len(parts) == 2 else current + 1
            item = parts[0].strip()
            known[name][item] = current
            members.append({'name': item, 'value': current})
        result['enums'][name] = members
    for owner in SURFACES:
        path = source / 'Runtime' / (owner + '.cs')
        text = _without_comments(path.read_text(encoding='utf-8-sig'))
        for match in re.finditer(r'public enum (\w+)\s*\{([^}]+)\}', text):
            name, body = match.groups()
            current, members = -1, []
            for member in body.split(','):
                if not member.strip():
                    continue
                parts = member.strip().split('=', 1)
                current = _enum_value(parts[1].strip(), known) if len(parts) == 2 else current + 1
                members.append({'name': parts[0].strip(), 'value': current})
            result['nestedEnums'][owner + '.' + name] = members
        previous_end = 0
        field_pattern = r'\b(public|private|internal|protected)\s+([\w<>.]+)\s+(m_\w+)\s*(?:=\s*([^;]+))?;'
        for match in re.finditer(field_pattern, text):
            access, kind, name, default = match.groups()
            attributes = text[previous_end:match.start()]
            previous_end = match.end()
            if access != 'public' and '[SerializeField]' not in attributes:
                continue
            group, phase = _group(owner, name)
            result['fields'].append({'id': owner + '.' + name, 'owner': owner, 'name': name,
                'type': kind, 'default': (default or 'default').strip(), 'css': css_name(name),
                'constraints': re.findall(r'\[(?:Range|Min|ColorUsage|PowerRange)[^\]]*\]', attributes),
                'group': group, 'phase': phase, 'source': str(path.relative_to(source)),
                'line': text.count('\n', 0, match.start()) + 1})
        modifiers = r'(?:(?:override|virtual|static|abstract|sealed)\s+)*'
        for match in re.finditer(r'\bpublic\s+' + modifiers + r'([\w<>.]+)\s+(\w+)\s*(?=\{|=>)', text):
            kind, name = match.groups()
            if kind in ('class', 'enum', 'struct', 'interface'):
                continue
            group, phase = _group(owner, name)
            result['properties'].append({'id': owner + '.' + name, 'owner': owner,
                'name': name, 'type': kind, 'css': css_name(name), 'group': group, 'phase': phase,
                'aliasOf': 'shadowColorGlow' if name == 'shadowGlow' else None,
                'source': str(path.relative_to(source)), 'line': text.count('\n', 0, match.start()) + 1})
        for match in re.finditer(r'\bpublic\s+' + modifiers + r'([\w<>.]+)\s+(\w+)\s*\(([^)]*)\)', text):
            kind, name, parameters = match.groups()
            line = text.count('\n', 0, match.start()) + 1
            group, phase = _group(owner, name)
            result['methods'].append({'id': f'{owner}.{name}:{line}', 'owner': owner, 'name': name,
                'returns': kind, 'parameters': ' '.join(parameters.split()), 'group': group,
                'phase': phase, 'source': str(path.relative_to(source)), 'line': line})
    for path in sorted(source.rglob('*')):
        if path.is_file() and path.suffix in ('.cs', '.cginc', '.shader', '.json', '.md'):
            result['sourceHashes'][str(path.relative_to(source))] = hashlib.sha256(path.read_bytes()).hexdigest()
    for entry in result['fields'] + result['properties']:
        _describe_target(entry)
    return result


def new_coverage(inventory):
    features = {}
    for family, entries in inventory['enums'].items():
        group, phase = ENUM_GROUPS[family]
        for entry in entries:
            features[f'enum:{family}.{entry["name"]}'] = {'group': group, 'phase': phase}
    for family, entries in inventory.get('nestedEnums', {}).items():
        for entry in entries:
            features[f'enum:{family}.{entry["name"]}'] = {'group': 'C13', 'phase': 'E07'}
    for category in ('fields', 'properties', 'methods'):
        for entry in inventory[category]:
            features[category + ':' + entry['id']] = {'group': entry['group'], 'phase': entry['phase'],
                'css': entry.get('css'), 'cssComponents': entry.get('cssComponents'),
                'targetKind': entry.get('targetKind'), 'attribute': entry.get('attribute'),
                'source': entry['source'], 'line': entry['line']}
    for group, phase in [('C13', 'E07'), ('C14', 'E07'), ('C15', 'E10')]:
        features['contract:' + group] = {'group': group, 'phase': phase}
    for entry in features.values():
        entry.update(status='pending', implementation=None, evidence=[], tests=[])
    return {'schemaVersion': 1, 'sourceVersion': inventory['version'], 'features': features}


def audit_coverage(inventory, coverage, final=False):
    expected = new_coverage(inventory)['features']
    actual = coverage.get('features', {})
    errors = []
    if coverage.get('sourceVersion') != inventory['version']:
        errors.append('源版本不一致')
    for missing in sorted(expected.keys() - actual.keys()):
        errors.append('缺少能力：' + missing)
    for extra in sorted(actual.keys() - expected.keys()):
        errors.append('未知能力：' + extra)
    for key, entry in actual.items():
        if final and (entry.get('status') != 'passed' or not entry.get('implementation') or
                      not entry.get('tests') or not entry.get('evidence')):
            errors.append('未完成或缺少证据：' + key)
    return errors


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    data = read_inventory(args.source)
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / 'inventory.json').write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n')
    (args.output / 'coverage-initial.json').write_text(json.dumps(new_coverage(data), ensure_ascii=False, indent=2) + '\n')
    print(json.dumps({'version': data['version'], 'enumFamilies': len(data['enums']),
                      'fields': len(data['fields']), 'properties': len(data['properties']),
                      'methods': len(data['methods'])}))


if __name__ == '__main__':
    main()
