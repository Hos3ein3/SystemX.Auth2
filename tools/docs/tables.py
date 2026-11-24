#!/usr/bin/env python3
"""Regenerates the column lists of docs/database-tables.md from the EF model snapshot.

Usage: python3 tools/docs/tables.py
Only the text between <!-- columns:TABLE:start --> and <!-- columns:TABLE:end --> is replaced; the rest of the page is written by hand.
"""
import os, re

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
SNAP = os.path.join(ROOT, 'src/SystemX.Auth2.Server/Persistence/Migrations/Auth2DbContextModelSnapshot.cs')
PAGE = os.path.join(ROOT, 'docs/database-tables.md')

def parse():
    s = open(SNAP).read()
    blocks = re.findall(r'modelBuilder\.Entity\("([^"]+)", b =>\n\s*\{\n(.*?)\n                \}\);', s, flags=re.S)
    tables = {}
    by_entity = {}
    for ent, body in blocks:
        t = re.search(r'b\.ToTable\("([^"]+)"', body)
        if not t: continue
        cols = []
        for m in re.finditer(r'b\.Property<([^>]+)>\("([^"]+)"\)(.*?);', body, flags=re.S):
            ty, pn, chain = m.groups()
            cn = re.search(r'HasColumnName\("([^"]+)"', chain)
            ct = re.search(r'HasColumnType\("([^"]+)"', chain)
            nullable = ty.endswith('?') or (ty in ('string', 'byte[]') and 'IsRequired()' not in chain)
            cols.append((cn.group(1) if cn else pn, ct.group(1) if ct else ty, nullable))
        pk = re.search(r'b\.HasKey\(([^)]*)\)', body)
        seen = set(); unique = []
        for c in cols:   # a column can be matched twice when a later line repeats the property (identity columns)
            if c[0].lower() in seen: continue
            seen.add(c[0].lower()); unique.append(c)
        tables[t.group(1)] = {'cols': unique, 'pk': re.findall(r'"([^"]+)"', pk.group(1)) if pk else []}
        by_entity[ent] = t.group(1)
    for ent, body in blocks:   # owned types that EF stores as one jsonb column
        if ent in by_entity and 'b.ToTable(' in body: continue
        for m in re.finditer(r'\.ToJson\("([^"]+)"\)', body):
            if ent in by_entity: tables[by_entity[ent]]['cols'].append((m.group(1), 'jsonb', False))
    return tables

def snake(n): return re.sub(r'(?<!^)(?=[A-Z])', '_', n).lower()

def render(t):
    out = ['<details><summary>All columns</summary>', '', '| Column | Type | Null |', '|---|---|---|']
    for name, ty, null in t['cols']:
        out.append(f"| `{name}` | `{ty}` | {'yes' if null else 'no'} |")
    out.append('')
    out.append('Primary key: ' + ', '.join(f"`{snake(c)}`" for c in t['pk']) + '.' if t['pk'] else '')
    out.append('')
    out.append('</details>')
    return '\n'.join(out)

def main():
    tables = parse()
    s = open(PAGE).read()
    def sub(m):
        name = m.group(1)
        if name not in tables: raise SystemExit(f'unknown table {name}')
        return f"<!-- columns:{name}:start -->\n{render(tables[name])}\n<!-- columns:{name}:end -->"
    s = re.sub(r'<!-- columns:(\w+):start -->.*?<!-- columns:\1:end -->', sub, s, flags=re.S)
    open(PAGE, 'w').write(s)
    print(f'{len(tables)} tables in the snapshot')

if __name__ == '__main__':
    main()
