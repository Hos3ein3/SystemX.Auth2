#!/usr/bin/env python3
"""Builds the settings pages of docs/ from the compiled settings classes (type, default, who may edit) and the hand-written notes in notes_*.py.

Usage: python3 tools/docs/settings.py [path of SystemX.Auth2.Server.dll]      (build the solution first)
It stops with a list when a setting has no note, so a new setting cannot be added without a description.
"""
import json, os, re, subprocess, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, HERE)

def shared(name):
    out = subprocess.check_output(['dotnet', '--list-runtimes']).decode().splitlines()
    line = [l for l in out if l.startswith(name + ' ')][-1]
    return os.path.join(line[line.index('[') + 1:line.index(']')], line.split()[1])

def read(dll, type_name):
    args = ['dotnet', 'run', os.path.join(HERE, 'ListSettings.cs'), '--', dll, type_name, shared('Microsoft.AspNetCore.App'), shared('Microsoft.NETCore.App'), os.path.dirname(dll)]
    out = subprocess.run(args, cwd=ROOT, capture_output=True, text=True).stdout.splitlines()
    rows, seen = [], set()
    for l in out:
        p = l.split('\t')
        if len(p) < 3 or p[0] in seen: continue
        seen.add(p[0]); rows.append(dict(path=p[0], type=p[1], default=p[2], editor=p[3] if len(p) > 3 else ''))
    return rows

def camel(s): return s[:1].lower() + s[1:]

def lit_json(v):
    if isinstance(v, str) and v.startswith('='): return v[1:]
    return json.dumps(v)

def lit_cs(v):
    if isinstance(v, str) and v.startswith('='): return v[1:]
    if isinstance(v, bool): return 'true' if v else 'false'
    if isinstance(v, (int, float)): return str(v)
    if isinstance(v, list): return '[' + ', '.join(lit_cs(x) for x in v) + ']'
    return json.dumps(v)

def nest(parts, value, style):
    inner = value
    for p in reversed(parts):
        inner = '{ ' + f'"{style(p)}": {inner}' + ' }'
    return inner

def example_json(path, value, style, wrap=None):
    parts = path.split('.')
    body = nest(parts, lit_json(value), style)
    if wrap: body = '{ ' + f'"{wrap}": {body}' + ' }'
    return body

def render_entry(row, note, example):
    t = row['type']; d = row['default']
    facts = f"`{t}` · default **`{d if len(d) < 90 else d[:87] + '...'}`**" + (f" · editor **{row['editor']}**" if row['editor'] else '')
    out = [f"#### `{row['path']}`", '', facts, '', f"**What it does.** {note['what']}", '', f"**When to change it.** {note['when']}", '', f"**Why.** {note['why']}"]
    if note.get('ex') is not None and example:
        out += ['', '```' + example[0], example[1](row, note['ex']), '```']
    out.append('')
    return '\n'.join(out)

def build(cfg, rows_by_class, notes):
    missing = []
    out = [cfg['intro'].rstrip('\n'), '']
    for title, blurb, cls, keys in cfg['groups']:
        rows = rows_by_class[cls]
        picked = [r for r in rows if keys(r['path'])]
        if not picked: continue
        out += [f'## {title}', '', blurb.strip(), '']
        for r in picked:
            n = notes.get(r['path'])
            if n is None: missing.append(f"{cls}.{r['path']}"); continue
            out.append(render_entry(r, n, cfg['example'](cls)))
    out.append(cfg['outro'].rstrip('\n'))
    return '\n'.join(out) + '\n', missing

def write(path, text):
    open(os.path.join(ROOT, 'docs', path), 'w').write(text)

def main():
    server_dll = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'src/SystemX.Auth2.Server/bin/Debug/net10.0/SystemX.Auth2.Server.dll')
    client_dll = os.path.join(ROOT, 'src/SystemX.Auth2.Client/bin/Debug/net10.0/SystemX.Auth2.Client.dll')
    import settings_docs
    problems = []
    for cfg in settings_docs.CONFIGS(server_dll, client_dll, read):
        text, missing = build(cfg, cfg['rows'], cfg['notes'])
        problems += missing
        if not missing: write(cfg['file'], text)
    if problems:
        print('Settings without a note (add them to tools/docs/notes_*.py):'); [print('  ' + p) for p in problems]; raise SystemExit(1)
    print('settings pages written')

if __name__ == '__main__':
    main()
