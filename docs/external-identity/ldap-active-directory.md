# LDAP login with Active Directory

Same feature as [OpenLDAP](ldap-openldap.md), with the settings that Active Directory needs.

## How to use (for operators)

1. **Service account**: make a normal user (for example `svc-auth2`), set *Password never expires*, give no special rights (Domain Users can read the directory).
2. **LDAPS**: the domain controller needs a certificate for LDAPS (port 636). Export your company root CA as PEM and put it in `customCaCertificate`. `tlsServerName` must be a name that is in the controller certificate (use the host name of the controller, not an IP address). Use a DNS name that points to several controllers for failover.
3. Choose the filter by what users type:

| Users type | `userSearchFilter` |
|---|---|
| Short name (`sara`) | `(&(objectClass=user)(objectCategory=person)(sAMAccountName={0}))` |
| Email-like name (`sara@corp.com`) | `(&(objectClass=user)(objectCategory=person)(userPrincipalName={0}))` |
| Either | `(&(objectClass=user)(objectCategory=person)(\|(sAMAccountName={0})(userPrincipalName={0})))` |

4. Settings:

```json
{
  "kind": "ldap",
  "name": "ad",
  "type": "Ldap",
  "enabled": true,
  "host": "dc.corp.com",
  "port": 636,
  "security": "Ldaps",
  "customCaCertificate": "-----BEGIN CERTIFICATE-----\n...\n-----END CERTIFICATE-----",
  "bindDn": "CN=svc-auth2,OU=Service Accounts,DC=corp,DC=com",
  "bindPassword": "...",
  "userSearchBase": "DC=corp,DC=com",
  "userSearchFilter": "(&(objectClass=user)(objectCategory=person)(sAMAccountName={0}))",
  "userNameAttribute": "sAMAccountName",
  "uniqueIdAttribute": "objectGUID",
  "emailAttribute": "mail",
  "firstNameAttribute": "givenName",
  "lastNameAttribute": "sn",
  "phoneAttribute": "telephoneNumber",
  "memberOfAttribute": "memberOf",
  "groupSearchBase": "DC=corp,DC=com",
  "groupFilter": "(&(objectClass=group)(member={0}))",
  "nestedGroups": true
}
```

- `objectGUID` is binary; the Server turns it into a GUID text. It is the link key.
- `memberOf` lists the direct groups of the user. With `groupFilter` and `nestedGroups` the Server also finds groups of groups (AD also has the special rule `member:1.2.840.113556.1.4.1941:=` for this, which works in `groupFilter` too).
- Disabled accounts: the Server reads `userAccountControl`; a disabled account is refused. With `revealDirectoryAccountState` on, the user sees `ldap_account_disabled`; the AD bind error `data 533` is understood too.
- A user who must change the password (`data 773`) or whose password expired (`data 532`) cannot sign in: they must change it in Windows first.

## Common problems

See [troubleshooting.md](troubleshooting.md#ldap).

## How to implement or extend

See [ldap-openldap.md](ldap-openldap.md#how-to-implement-or-extend).

## Related pages

- [LDAP and Active Directory: enable and use](ldap.md)
- [Group mapping](group-mapping.md)
- [Documentation index](../index.md)
