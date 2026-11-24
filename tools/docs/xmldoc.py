import re, sys, xml.etree.ElementTree as ET
from collections import defaultdict

def short(t):
    # decode a doc-id type string into a readable C# type
    t=t.strip()
    def conv(s):
        s=s.replace('@','&')
        out='';i=0
        # split top-level on '{' '}' ','
        def parse(s,i):
            name='';
            while i<len(s) and s[i] not in '{},':
                name+=s[i]; i+=1
            args=[]
            if i<len(s) and s[i]=='{':
                i+=1
                while True:
                    a,i=parse(s,i); args.append(a)
                    if s[i]==',': i+=1; continue
                    if s[i]=='}': i+=1; break
            m=re.match(r'^(.*?)`+\d+$',name)
            base=name
            if base.startswith('``'): base='T'+base[2:]
            elif base.startswith('`'): base='T'+base[1:]
            else:
                base=base.split('.')[-1]
                base=re.sub(r'`+\d+$','',base)
            alias={'String':'string','Int32':'int','Boolean':'bool','Int64':'long','Object':'object','Void':'void'}
            suffix=''
            while base.endswith('[]'): base=base[:-2]; suffix+='[]'
            if re.fullmatch(r'T\d+',base): base='T'
            base=alias.get(base,base)+suffix
            if args: base+='<'+', '.join(args)+'>'
            return base,i
        r,i=parse(s,0)
        return r
    return conv(t)

def split_params(p):
    out=[];depth=0;cur=''
    for ch in p:
        if ch=='{': depth+=1
        if ch=='}': depth-=1
        if ch==',' and depth==0: out.append(cur); cur=''
        else: cur+=ch
    if cur: out.append(cur)
    return out

def text(el):
    if el is None: return ''
    parts=[]
    def walk(e):
        if e.text: parts.append(e.text)
        for c in e:
            if c.tag=='see':
                cr=c.get('cref') or ''
                parts.append('`'+short(cr.split(':',1)[-1].split('(')[0].split('.')[-1])+'`')
            elif c.tag=='paramref': parts.append('`'+c.get('name','')+'`')
            elif c.tag=='c': parts.append('`'+''.join(c.itertext())+'`')
            elif c.tag=='b': parts.append('**'+''.join(c.itertext())+'**')
            else: walk(c)
            if c.tail: parts.append(c.tail)
    walk(el)
    return re.sub(r'\s+',' ',''.join(parts)).strip().replace('|','\\|')

def load(path, namespaces):
    tree=ET.parse(path)
    types=defaultdict(lambda: {'summary':'','members':[]})
    for m in tree.getroot().iter('member'):
        name=m.get('name'); kind,rest=name[0],name[2:]
        summ=text(m.find('summary'))
        if kind=='T':
            ns,_,t=rest.rpartition('.')
            if any(ns==n or ns.startswith(n+'.') for n in namespaces):
                types[rest]['summary']=summ
        elif kind in 'MPFE':
            core=rest.split('(')[0]
            typ,_,mem=core.rpartition('.')
            if mem=='#ctor': mem='(constructor)'
            if not any(typ.rpartition('.')[0]==n or typ.rpartition('.')[0].startswith(n+'.') for n in namespaces): continue
            params=''
            if '(' in rest and kind=='M':
                params='('+', '.join(short(x) for x in split_params(rest[rest.index('(')+1:-1]))+')'
            elif kind=='M': params='()'
            if kind in 'MPFE':
                gen=''
                mm=re.search(r'``(\d+)',core)
                types[typ]['members'].append((kind,mem.split('``')[0]+params if kind=='M' else mem,summ,mem))
    return types

def render(types, order_by_ns=True, heading_level=3):
    byns=defaultdict(list)
    for full,d in types.items():
        ns,_,t=full.rpartition('.')
        byns[ns].append((t,full,d))
    out=[]
    for ns in sorted(byns):
        out.append(f'\n## `{ns}`\n')
        for t,full,d in sorted(byns[ns]):
            t=re.sub(r'`\d+$','<T>',t)
            out.append(f'\n### {t}\n')
            if d['summary']: out.append(d['summary']+'\n')
            mem=d['members']
            if mem:
                out.append('| Member | What it does |\n|---|---|')
                seen=set()
                for kind,sig,summ,name in mem:
                    label={'P':'','M':'','F':'','E':''}[kind]
                    key=(sig)
                    if key in seen: continue
                    seen.add(key)
                    out.append(f'| `{sig}` | {summ or ""} |')
                out.append('')
    return '\n'.join(out)
