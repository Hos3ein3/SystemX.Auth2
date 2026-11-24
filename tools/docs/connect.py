#!/usr/bin/env python3
"""Adds the missing links of the "Related pages" sections of docs/*.md from one curated map (RELATED).

Usage: python3 tools/docs/connect.py
It only adds links that are not there yet and creates a "Related pages" section when a page has none (before the generated tables, if any).
A test (DocsTests) fails when a page is not listed in docs/index.md or has no "Related pages" section.
"""
import os, re, glob

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
DOCS = os.path.join(ROOT, 'docs')

# page -> pages it should point to (paths relative to docs/). The relation is made two-way by symmetric().
FAMILIES = [
    # start here
    ['getting-started.md', 'concepts.md', 'server-setup.md', 'client-setup.md', 'seeding.md', 'troubleshooting.md'],
    ['concepts.md', 'architecture.md', 'database-tables.md', 'credential-scope.md', 'decisions.md', 'comparison-and-limits.md'],
    ['identity-stores.md', 'database-tables.md', 'credential-scope.md', 'authentication-flows.md', 'authorization.md', 'server-settings.md', 'architecture.md', 'client-current-user.md'],
    ['password-history.md', 'server-settings.md', 'database-tables.md', 'identity-stores.md', 'authentication-flows.md', 'error-codes.md', 'two-factor.md', 'decisions.md'],
    ['redis.md', 'deployment-and-scaling.md', 'server-setup.md', 'server-settings.md', 'security.md', 'client-setup.md', 'client-builder.md', 'observability.md', 'troubleshooting.md'],
    ['two-factor.md', 'authentication-flows.md', 'identity-stores.md', 'credential-scope.md', 'server-settings.md', 'admin-api.md', 'client-setup.md', 'error-codes.md', 'security.md', 'external-identity/passkeys.md'],
    ['enums-reference.md', 'server-settings.md', 'client-builder.md', 'contracts-reference.md', 'database-tables.md', 'external-identity.md', 'two-factor.md'],
    # server
    ['server-setup.md', 'server-settings.md', 'seeding.md', 'deployment-and-scaling.md', 'security.md', 'observability.md', 'email-and-sms.md', 'server-reference.md'],
    ['server-settings.md', 'server-reference.md', 'credential-scope.md', 'email-and-sms.md', 'admin-api.md', 'localization.md', 'external-identity.md'],
    ['database-tables.md', 'key-ring.md', 'security.md', 'authorization.md', 'architecture.md', 'server-settings.md', 'external-identity.md', 'deployment-and-scaling.md'],
    ['key-ring.md', 'security.md', 'deployment-and-scaling.md', 'server-setup.md', 'observability.md', 'client-authorization.md'],
    ['security.md', 'deployment-and-scaling.md', 'observability.md', 'seeding.md', 'email-and-sms.md', 'external-identity.md'],
    ['deployment-and-scaling.md', 'observability.md', 'gateway.md', 'server-setup.md'],
    ['authentication-flows.md', 'api-reference.md', 'error-codes.md', 'authorization.md', 'credential-scope.md', 'external-identity.md', 'ui-handover.md', 'server-settings.md'],
    ['authorization.md', 'permission-sync.md', 'client-authorization.md', 'client-current-user.md', 'admin-api.md', 'concepts.md'],
    ['permission-sync.md', 'client-setup.md', 'client-reference.md', 'authorization.md', 'admin-api.md'],
    ['admin-api.md', 'api-reference.md', 'external-identity.md', 'email-and-sms.md', 'key-ring.md', 'ui-handover.md', 'server-settings.md'],
    ['api-reference.md', 'error-codes.md', 'contracts-reference.md', 'client-reference.md', 'machine-to-machine.md', 'authentication-flows.md'],
    ['error-codes.md', 'troubleshooting.md', 'contracts-reference.md', 'authentication-flows.md'],
    ['ui-handover.md', 'external-identity.md', 'external-identity/passkeys.md', 'localization.md', 'server-reference.md', 'authentication-flows.md'],
    ['localization.md', 'email-and-sms.md', 'server-settings.md'],
    ['email-and-sms.md', 'authentication-flows.md', 'security.md', 'database-tables.md', 'server-reference.md'],
    ['credential-scope.md', 'identity-stores.md', 'database-tables.md', 'authentication-flows.md', 'server-settings.md'],
    ['seeding.md', 'database-tables.md', 'authorization.md'],
    ['handshake-and-startup.md', 'client-setup.md', 'concepts.md', 'server-setup.md', 'permission-sync.md'],
    # client
    ['client-setup.md', 'client-builder.md', 'client-reference.md', 'client-authorization.md', 'client-current-user.md', 'machine-to-machine.md', 'handshake-and-startup.md', 'getting-started.md', 'contracts-reference.md'],
    ['client-reference.md', 'client-builder.md', 'client-authorization.md', 'client-current-user.md', 'machine-to-machine.md', 'contracts-reference.md'],
    ['machine-to-machine.md', 'authentication-flows.md', 'client-reference.md'],
    ['client-authorization.md', 'client-current-user.md', 'client-reference.md'],
    ['contracts-reference.md', 'client-reference.md', 'server-reference.md', 'api-reference.md'],
    ['client-builder.md', 'client-setup.md', 'client-reference.md', 'handshake-and-startup.md', 'permission-sync.md', 'client-current-user.md', 'machine-to-machine.md', 'getting-started.md', 'server-settings.md'],
    # project
    ['roadmap.md', 'feature-status.md', 'comparison-and-limits.md', 'decisions.md', 'gateway.md'],
    ['decisions.md', 'architecture.md', 'contributing-workflow.md', 'key-ring.md', 'email-and-sms.md', 'database-tables.md', 'external-identity.md'],
    ['contributing-workflow.md', 'testing.md', 'architecture.md', 'decisions.md'],
    ['testing.md', 'architecture.md', 'getting-started.md', 'observability.md'],
    ['troubleshooting.md', 'error-codes.md', 'getting-started.md', 'observability.md', 'external-identity/troubleshooting.md'],
    ['feature-status.md', 'roadmap.md', 'comparison-and-limits.md'],
    # external identity
    ['external-identity.md', 'external-identity/social-webintegration.md', 'external-identity/saml.md', 'external-identity/ldap.md', 'external-identity/passkeys.md',
     'external-identity/brokering.md', 'external-identity/group-mapping.md', 'external-identity/troubleshooting.md', 'ui-handover.md', 'admin-api.md'],
    ['external-identity/social-webintegration.md', 'external-identity/google.md', 'external-identity/microsoft.md', 'external-identity/github.md', 'external-identity/apple.md',
     'external-identity/facebook.md', 'external-identity/generic-oidc.md', 'external-identity/troubleshooting.md'],
    ['external-identity/generic-oidc.md', 'external-identity/social-webintegration.md', 'external-identity/google.md', 'external-identity/microsoft.md', 'external-identity/github.md', 'external-identity/apple.md', 'external-identity/facebook.md'],
    ['external-identity/saml.md', 'external-identity/saml-generic.md', 'external-identity/saml-entra-id.md', 'external-identity/saml-okta.md', 'external-identity/saml-adfs.md',
     'external-identity/saml-keycloak.md', 'external-identity/saml-certificate-rotation.md', 'external-identity/group-mapping.md', 'external-identity/troubleshooting.md'],
    ['external-identity/ldap.md', 'external-identity/ldap-openldap.md', 'external-identity/ldap-active-directory.md', 'external-identity/group-mapping.md', 'external-identity/troubleshooting.md'],
    ['external-identity/brokering.md', 'external-identity/group-mapping.md', 'external-identity/saml.md', 'external-identity/ldap.md', 'external-identity/social-webintegration.md', 'authentication-flows.md'],
    ['external-identity/passkeys.md', 'authentication-flows.md', 'server-settings.md', 'admin-api.md', 'external-identity/troubleshooting.md'],
]
EXTRA = {   # one-way links (page -> targets) that are not symmetric
    'index.md': [],
}
OUTSIDE = {   # links to files outside docs/ (path relative to the repository root)
    'contributing-workflow.md': [('tools/docs/README.md', 'Reference generator')],
    'roadmap.md': [('CHANGELOG.md', 'Changelog')],
    'feature-status.md': [('CHANGELOG.md', 'Changelog')],
    'client-setup.md': [('src/SystemX.Auth2.Client/README.md', 'Client package README')],
    'server-setup.md': [('src/SystemX.Auth2.Server/README.md', 'Server README')],
    'getting-started.md': [('README.md', 'Repository README')],
}

def title(path):
    for l in open(path):
        if l.startswith('# '): return l[2:].strip()
    return os.path.basename(path)

def related():
    rel = {}
    for fam in FAMILIES:
        for a in fam:
            for b in fam:
                if a != b: rel.setdefault(a, [])
        # the first page of a family points to all the others; every other page points to the first and to its neighbours
        head = fam[0]
        for b in fam[1:]:
            rel.setdefault(head, []).append(b)
            rel.setdefault(b, []).append(head)
        for a in fam[1:]:
            for b in fam[1:]:
                if a != b: rel.setdefault(a, []).append(b)
    for p, ts in EXTRA.items(): rel.setdefault(p, []).extend(ts)
    return rel

def link(frm, to_docs_rel, text):
    target = os.path.join(DOCS, to_docs_rel)
    return os.path.relpath(target, os.path.dirname(os.path.join(DOCS, frm))).replace(os.sep, '/')

def apply(page, targets, outside):
    path = os.path.join(DOCS, page)
    s = open(path).read()
    existing = set(os.path.normpath(os.path.join(os.path.dirname(path), m.split('#')[0])) for m in re.findall(r'\]\(([^)\s]+)\)', s) if not m.startswith('http'))
    lines = []
    seen = set()
    for t in targets:
        if t in seen or t == page: continue
        seen.add(t)
        tp = os.path.normpath(os.path.join(DOCS, t))
        if not os.path.exists(tp): raise SystemExit(f'{page}: missing target {t}')
        rl = link(page, t, '')
        if tp not in existing: lines.append(f'- [{title(tp)}]({rl})')
    for rp, label in outside:
        tp = os.path.join(ROOT, rp)
        rl = os.path.relpath(tp, os.path.dirname(path)).replace(os.sep, '/')
        if os.path.normpath(tp) not in existing: lines.append(f'- [{label}]({rl})')
    idx = os.path.relpath(os.path.join(DOCS, 'index.md'), os.path.dirname(path)).replace(os.sep, '/')
    if page != 'index.md' and os.path.normpath(os.path.join(DOCS, 'index.md')) not in existing:
        lines.append(f'- [Documentation index]({idx})')
    if not lines: return 0
    block = '\n'.join(lines)
    m = re.search(r'^## Related pages\s*\n', s, flags=re.M)
    if m:
        # append to the end of the section (up to the next heading or the end)
        nxt = re.search(r'^## ', s[m.end():], flags=re.M)
        end = m.end() + nxt.start() if nxt else len(s)
        body = s[m.end():end].rstrip('\n')
        s = s[:m.end()] + body + '\n' + block + '\n' + ('\n' + s[end:] if nxt else '')
    else:
        marker = re.search(r'^## .*\(generated\)', s, flags=re.M)
        sec = '## Related pages\n\n' + block + '\n'
        if marker: s = s[:marker.start()] + sec + '\n' + s[marker.start():]
        else: s = s.rstrip('\n') + '\n\n' + sec
    open(path, 'w').write(s)
    return len(lines)

def main():
    rel = related()
    pages = sorted(os.path.relpath(p, DOCS).replace(os.sep, '/') for p in glob.glob(DOCS + '/**/*.md', recursive=True))
    total = 0
    for page in pages:
        if page == 'index.md': continue
        total += apply(page, rel.get(page, []), OUTSIDE.get(page, []))
    print(f'{total} links added')

if __name__ == '__main__':
    main()
