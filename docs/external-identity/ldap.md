# LDAP and Active Directory: enable and use

## What this is

The user types a **directory** user name and password into the normal sign-in form. The Server finds the user with a **service account**, checks the password by binding as that user, reads attributes and groups, and signs the user in with the same pipeline as every other sign-in. Data and groups are refreshed **at each sign-in**; there is no background sync.

## When to use it

Your users already live in Active Directory or OpenLDAP and must not get a second password.

## How to enable it

1. **Make a service account** in the directory with read rights (it needs to search users and read groups). Never use an admin account.
2. **Reach the directory with TLS.** Plain LDAP is not offered: use `Ldaps` (port 636) or `StartTls` (port 389). If your company CA is private, put its PEM in `customCaCertificate`. `tlsServerName` must be a name in the server certificate (a host name, not an IP address).
3. **Allow it in the project**: `Ldap` in `Methods.AllowedLoginMethods` (read the settings, add it, write them back with `If-Match`; see step 2 of [social-webintegration.md](social-webintegration.md#how-to-enable-it)).
4. **Add the provider.** Active Directory:

   ```http
   POST /api/auth2/admin/projects/{projectId}/external-providers
   { "kind": "ldap", "name": "ad", "type": "Ldap", "displayName": "Company directory", "enabled": true,
     "host": "dc.corp.com", "port": 636, "security": "Ldaps", "tlsServerName": "dc.corp.com",
     "bindDn": "CN=svc-auth2,OU=Service Accounts,DC=corp,DC=com", "bindPassword": "...",
     "userSearchBase": "DC=corp,DC=com", "userSearchFilter": "(&(objectClass=user)(objectCategory=person)(sAMAccountName={0}))",
     "userNameAttribute": "sAMAccountName", "uniqueIdAttribute": "objectGUID", "emailAttribute": "mail",
     "firstNameAttribute": "givenName", "lastNameAttribute": "sn", "memberOfAttribute": "memberOf", "nestedGroups": true,
     "mode": "LdapThenLocal" }
   ```

   `{0}` must appear exactly once in the filter. `mode`: `LdapOnly` (only the directory), `LocalThenLdap`, `LdapThenLocal` (try the other when the first does not know the user). Full field list: [external-identity.md](../external-identity.md#ldap-kind-ldap). OpenLDAP values: [ldap-openldap.md](ldap-openldap.md). Active Directory values and filters: [ldap-active-directory.md](ldap-active-directory.md).
5. **Check it** without a password:
   - `POST .../external-providers/ad/test`: TLS connect and service-account bind.
   - `POST .../external-providers/ad/ldap/test-user` with `{ "userName": "sara" }`: shows the DN, the unique id, the attributes and the groups the Server would read. No password is sent or shown.

## How to use it

- **The user** types the directory user name and password in the **normal login form** of the Server. There is no separate button (`loginFormRequired: true` in `GET /connect/external/providers`).
- **Self UI applications** can do the same through the token endpoint when the application has `AllowSelfUiLogin` on: `IAuth2WebService.LoginAsync(...)` sends the name and password and the Server decides ([client-setup.md](../client-setup.md#self-ui)). With the Mvc sample this is the Self UI form of `.UseSelfUi()`.
- **Groups to roles**: [group-mapping.md](group-mapping.md). `syncOnLogin` (default on) updates the person and the group mappings at every sign-in.
- **Disabled, expired and locked accounts** are refused. With `revealDirectoryAccountState` on, the user sees the real reason (`ldap_account_disabled`); by default the answer is the generic one.
- **Safety**: attempts are rate limited and count toward the project's lockout. Empty passwords are never sent. Filter input is escaped. The link key is the directory's unique id (`objectGUID` or `entryUUID`), never the DN.

## Common problems

[troubleshooting.md](troubleshooting.md#ldap): certificate chain, wrong `tlsServerName`, filter without `{0}`, service account without rights.

## How to implement or extend

`LdapDirectory` (search and bind), `LdapText` (escaping), `LdapAwareLoginService` (the order of local and directory checks). Tests: `LdapSourceTests.cs` run against a real OpenLDAP server in a container (`LdapFixture`). See [ldap-openldap.md](ldap-openldap.md#how-to-implement-or-extend).
