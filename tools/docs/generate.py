#!/usr/bin/env python3
"""Regenerates the API tables of docs/client-reference.md, docs/contracts-reference.md and docs/server-reference.md.

The tables come from the XML comments of the assemblies, filtered to the members that are really public (read with ListPublic.cs).
Usage: python3 tools/docs/generate.py            (build the solution first: the XML files are written by `dotnet build`)
Only the text between <!-- generated:start --> and <!-- generated:end --> is replaced; the rest of each page is written by hand.
"""
import os, re, subprocess, sys, glob
import xml.etree.ElementTree as ET
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from xmldoc import short, split_params, text, render   # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))

def shared(name):
    out = subprocess.check_output(['dotnet', '--list-runtimes']).decode().splitlines()
    line = [l for l in out if l.startswith(name + ' ')][-1]
    ver = line.split()[1]; base = line[line.index('[') + 1:line.index(']')]
    return os.path.join(base, ver)

def public_ids(dll, extra_dirs):
    args = ['dotnet', 'run', os.path.join(HERE, 'ListPublic.cs'), '--', dll, shared('Microsoft.AspNetCore.App'), shared('Microsoft.NETCore.App'), os.path.dirname(dll)] + extra_dirs
    return set(subprocess.check_output(args, cwd=ROOT).decode().split('\n'))

def load(xmlp, pub, namespaces):
    ext = {x[2:] for x in pub if x.startswith('X:')}
    types = defaultdict(lambda: {'summary': '', 'members': []})
    for m in ET.parse(xmlp).getroot().iter('member'):
        name = m.get('name')
        if name not in pub: continue
        kind, rest = name[0], name[2:]
        summ = text(m.find('summary'))
        if kind == 'T':
            ns = rest.rpartition('.')[0]
            if not namespaces or any(ns == n or ns.startswith(n + '.') for n in namespaces): types[rest]['summary'] = summ
            continue
        core = rest.split('(')[0]; typ, _, mem = core.rpartition('.')
        ns = typ.rpartition('.')[0]
        if namespaces and not any(ns == n or ns.startswith(n + '.') for n in namespaces): continue
        if mem == '#ctor': mem = '(constructor)'
        if kind == 'M':
            parts = [short(x) for x in split_params(rest[rest.index('(') + 1:-1])] if '(' in rest else []
            if rest in ext and parts: parts[0] = 'this ' + parts[0]
            ps = '(' + ', '.join(parts) + ')'
            sig = mem.split('``')[0] + ps
        else:
            sig = mem
        types[typ]['members'].append((kind, sig, summ, mem))
    return {k: v for k, v in types.items() if v['summary'] or v['members']}

def inject(page, body):
    p = os.path.join(ROOT, 'docs', page)
    s = open(p).read()
    a, b = '<!-- generated:start -->', '<!-- generated:end -->'
    i, j = s.index(a) + len(a), s.index(b)
    open(p, 'w').write(s[:i] + '\n' + body.strip('\n') + '\n' + s[j:])

def main():
    server_dll = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, 'src/SystemX.Auth2.Server/bin/Debug/net10.0/SystemX.Auth2.Server.dll')
    targets = [
        ('client-reference.md', 'src/SystemX.Auth2.Client/bin/Debug/net10.0/SystemX.Auth2.Client', None),
        ('contracts-reference.md', 'src/SystemX.Auth2.Contracts/bin/Debug/net10.0/SystemX.Auth2.Contracts', None),
    ]
    for page, stem, ns in targets:
        base = os.path.join(ROOT, stem)
        inject(page, render(load(base + '.xml', public_ids(base + '.dll', []), ns)))
    sbase = server_dll[:-4]
    inject('server-reference.md', render(load(sbase + '.xml', public_ids(server_dll, []),
        ['SystemX.Auth2.Server.Application', 'SystemX.Auth2.Server.Domain.Settings', 'SystemX.Auth2.Server.Infrastructure.DependencyInjection'])))

if __name__ == '__main__':
    main()
