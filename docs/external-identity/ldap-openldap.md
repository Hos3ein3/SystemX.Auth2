# LDAP login with OpenLDAP (and other LDAP servers)

The user types a user name and password into the login page of the Server. The Server checks them in your directory. The user has **no local password**; the directory is asked at every login.

## How to use (for operators)

1. Create a **service account** in the directory that may search users and groups (read only). The Server uses it to find the user. Never use the directory admin account in production.
2. Make sure the directory offers **LDAPS** (port 636) or **StartTLS** (port 389). Plain LDAP is not offered.
3. Add the provider, and add `Ldap` to `Methods.AllowedLoginMethods` of the project.

```json
{
  "kind": "ldap",
  "name": "ldap-hq",
  "type": "Ldap",
  "displayName": "Company directory",
  "enabled": true,
  "host": "ldap.corp.com",
  "port": 636,
  "security": "Ldaps",
  "customCaCertificate": "-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----",
  "bindDn": "cn=auth2,ou=services,dc=corp,dc=com",
  "bindPassword": "...",
  "userSearchBase": "ou=people,dc=corp,dc=com",
  "userSearchFilter": "(&(objectClass=inetOrgPerson)(uid={0}))",
  "uniqueIdAttribute": "entryUUID",
  "emailAttribute": "mail",
  "groupSearchBase": "ou=groups,dc=corp,dc=com",
  "groupFilter": "(member={0})",
  "nestedGroups": false,
  "mode": "LdapOnly",
  "groupMappingMode": "Sync"
}
```

### Settings you must understand

| Setting | What it does |
|---|---|
| `userSearchFilter` | Must contain `{0}` exactly once. It is replaced by the user name **after escaping** (`*`, `(`, `)`, `\`, NUL), so input like `*)(uid=*` finds nobody. Exactly one entry must match; zero or more than one gives the same answer as a wrong password |
| `uniqueIdAttribute` | A value that never changes: `entryUUID` (OpenLDAP) or `objectGUID` (Active Directory). It is the **link key**. The DN is never used, because it changes when a user is moved |
| `groupFilter` | `{0}` is the escaped DN of the user (or of a group when nesting is on). Groups come back as both name (`finance`) and DN, so a mapping can use either |
| `nestedGroups` | Groups of groups, at most 5 levels and 500 groups |
| `mode` | `LdapOnly` (only the directory), `LocalThenLdap` (the local password first), `LdapThenLocal` (the directory first). A directory **outage** gives `ldap_unavailable`; the local password is not tried then |
| `customCaCertificate`, `tlsServerName` | Trust your own CA, and the name that must be in the certificate (default: `host`) |
| `validateServerCertificate` | Default true. Switching it off is for tests only |
| `timeoutSeconds` | Connect, bind and search time limit (default 5) |
| `revealDirectoryAccountState` | Off by default: a disabled account looks like a wrong password. Turn it on only if users should see `ldap_account_disabled` |

### What the Server does

- Empty passwords are never sent (some servers accept an empty bind as "anonymous success").
- Wrong passwords for a known directory user count toward the local lockout of the linked user (`Lockout.MaxFailedAttempts`).
- Sign-in attempts are rate limited per user and IP **before** the directory is asked (policy `ldap`, default 10 per minute).
- Passwords, bind passwords and the user name are never logged or written to the audit log. Audit events: `external.register`, `external.login.success`, `ldap.login.failed` (reason codes only).
- With `groupMappingMode: Sync` the groups are mapped at every login ([group-mapping.md](group-mapping.md)). There is no background sync: data is updated at login.
- The admin "test" routes (step 9) can check the connection and show what a lookup returns for one user, without any password.

## Common problems

See [troubleshooting.md](troubleshooting.md#ldap).

## How to implement or extend

- `LdapDirectory` (`Infrastructure/External/LdapDirectory.cs`) is the only code that talks to the directory (library: Novell.Directory.Ldap.NETStandard, see ADR-087). `LdapText` has the escape function. `LdapAwareLoginService` decides the order of local password and directory, and rate limits.
- **To read another attribute**: add it to the `wanted` list in `FindUserAsync`; it is then in `ExternalIdentity.Attributes`, usable by attribute mappings and `ClaimMappings`. To make it a named field (like email), add a setting and read it in `AuthenticateAsync`.
- Tests: `LdapSourceTests.cs` run against an OpenLDAP container with its own CA (`LdapFixture`).
