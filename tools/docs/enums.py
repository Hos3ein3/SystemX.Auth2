#!/usr/bin/env python3
"""Builds docs/enums-reference.md. Values and their descriptions come from the XML comments of the code; the guidance comes from notes_enums.py.

Usage: python3 tools/docs/enums.py [path of SystemX.Auth2.Server.xml]      (build first: the XML files are written by `dotnet build`)
It stops with a list when an enum or a value has no note.
"""
import collections, os, re, sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
sys.path.insert(0, HERE)
from notes_enums import GROUPS, N   # noqa: E402

def text(e): return re.sub(r'\s+', ' ', ''.join(e.itertext())).strip() if e is not None else ''

def load(files):
    d = collections.defaultdict(dict); order = collections.defaultdict(list)
    for f in files:
        for m in ET.parse(f).getroot().iter('member'):
            n = m.get('name')
            if n.startswith('T:'):
                nm = n.split('.')[-1]; d[nm]['_t'] = text(m.find('summary')); d[nm]['_ns'] = n[2:]
            elif n.startswith('F:'):
                typ, _, mem = n[2:].rpartition('.'); nm = typ.split('.')[-1]
                d[nm][mem] = text(m.find('summary')); order[nm].append(mem)
    return d, order

def main():
    server_xml = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'src/SystemX.Auth2.Server/bin/Debug/net10.0/SystemX.Auth2.Server.xml')
    files = [os.path.join(ROOT, 'src/SystemX.Auth2.Contracts/bin/Debug/net10.0/SystemX.Auth2.Contracts.xml'),
             os.path.join(ROOT, 'src/SystemX.Auth2.Client/bin/Debug/net10.0/SystemX.Auth2.Client.xml'), server_xml]
    d, order = load(files)
    missing = []
    out = [open(os.path.join(HERE, 'enums_intro.md')).read().rstrip('\n'), '']
    for title, blurb, names in GROUPS:
        out += [f'## {title}', '', blurb, '']
        for name in names:
            n = N.get(name)
            if name not in d or not order[name]: missing.append(f'{name}: not found in the code'); continue
            if n is None: missing.append(f'{name}: no note'); continue
            miss = [v for v in order[name] if v not in n['values']]
            if miss: missing.append(f'{name}: no note for {", ".join(miss)}'); continue
            ns = d[name]['_ns'].rpartition('.')[0]
            out += [f'### {name}', '', f"`{ns}` · {d[name]['_t']}", '', n['where'], '', '| Value | Meaning | Choose it when |', '|---|---|---|']
            for v in order[name]:
                out.append(f"| `{v}` | {d[name][v]} | {n['values'][v]} |")
            out += ['', f"**Why it exists.** {n['why']}", '']
            if n.get('ex'): out += ['```' + n['ex'][0], n['ex'][1], '```', '']
    out.append(open(os.path.join(HERE, 'enums_outro.md')).read().rstrip('\n'))
    if missing:
        print('Problems:'); [print('  ' + m) for m in missing]; raise SystemExit(1)
    open(os.path.join(ROOT, 'docs', 'enums-reference.md'), 'w').write('\n'.join(out) + '\n')
    print('docs/enums-reference.md written')

if __name__ == '__main__': main()
