# Passkeys

A **passkey** is a login key stored in the user's phone, laptop, password manager or security key. The user unlocks it with a fingerprint, face or device PIN. There is no password to steal or to phish: the key only works for the real website (the domain is part of the check).
Passkeys here are a **primary** way to sign in. They do not replace the authenticator-app code (TOTP) as a second factor; a passkey that was unlocked by the user (user verification) already counts as several factors, so the local second factor is skipped for it.

## How to use (for operators)

### 1. Choose the server domain

Passkeys are bound to a domain, the **relying party id**. Set it once:

```
Auth2:Passkeys:ServerDomain = auth.example.com
```

- **Required in Production.** Without it the Server refuses passkeys (`passkey_not_allowed`).
- Use the domain of the Server (no `https://`, no port). The browser address must be that domain or a sub-domain of it.
- **Do not change it later.** Every passkey made for the old domain stops working.
- Passkeys need **HTTPS**. Origins are checked: only the address of the Server (the `Auth2:Issuer`, or the request address in Development) is accepted.

### 2. Turn them on per project

```json
{
  "passkeys": {
    "allowPasskeys": true,
    "passkeyUserVerification": "required",
    "passkeyResidentKey": "preferred",
    "maxPasskeysPerUser": 10,
    "passkeyNameMaxLength": 50,
    "requirePasskeyBackupMethod": true
  },
  "methods": { "allowedLoginMethods": ["EmailPassword", "Passkey"] }
}
```

All fields are for developers (global admin) only. `Passkey` must be in `AllowedLoginMethods`, otherwise sign-in is refused with `login_method_not_allowed`.

### 3. Rules the Server enforces

| Rule | Meaning |
|---|---|
| Per project | A passkey made in project A is **not found** in project B (the same person registers again there) |
| `maxPasskeysPerUser`, `passkeyNameMaxLength` | Limits. `passkey_limit_reached`, `validation_failed` |
| `passkeyUserVerification` | `required` (default): a passkey without fingerprint/PIN is refused at registration and sign-in |
| `requirePasskeyBackupMethod` | A person whose only other way to sign in is one external login cannot remove it while they have **one** passkey; they must add a second one first. Without a password, a login or a passkey, nothing can be removed (`cannot_remove_last_login_method`) |
| Blocked authenticators | `Auth2:Passkeys:BlockedAaguids` (server setting, global admin) lists authenticator models (AAGUID) that cannot be registered: `passkey_blocked_authenticator`. Existing passkeys of a blocked model stay listed so they can be removed. The AAGUID is **as the authenticator reports it** (attestation is off by default: `AttestationMode: None`) |
| Allow list | `AttestationMode: AaguidAllowList` plus `AllowedAaguids` accepts only the listed models |
| Replay | Every challenge is single use; a copied answer fails. The signature counter is stored and updated |
| Rate limit | Policy `passkey` (default 20 per minute) on `/account/passkey/*` |

### Recovery

A passkey is lost when the device is lost. Advise users to (1) register a passkey on **two** devices, or use one that syncs (iCloud Keychain, Google Password Manager, a password manager: the Server marks these `isBackedUp`), and (2) keep another way to sign in (a password or an external login). The management page suggests adding another passkey when none of the user's passkeys is backed up. An admin can remove a lost passkey with the admin API (see [admin-api.md](../admin-api.md#external-identity-routes)).

## How to use passkeys (for users and applications)

1. **Add a passkey.** A signed-in user opens `/Manage/Passkeys` on the Server, picks a name and confirms with the device (fingerprint, face, PIN or security key). The page script asks `POST /account/passkey/creation-options` for the options, calls `navigator.credentials.create`, and sends the result to `POST /account/passkey/register`; the Server stores the public key for **this project**.
2. **Sign in with it.** On the sign-in page the user chooses the passkey button. The script asks `POST /account/passkey/request-options` for a challenge, calls `navigator.credentials.get`, and sends the answer to `POST /account/passkey/signin`. The Server checks it and signs the user in with the same pipeline as a password (membership, lockout, consent). A passkey unlocked by the user already counts as several factors, so the local second factor is skipped for it.
3. **Manage them.** `/Manage/Passkeys` lists the passkeys (name, created, last used, `isBackedUp`), renames and deletes them. An admin removes a lost one with `DELETE /api/auth2/admin/projects/{projectId}/users/{userId}/passkeys/{passkeyId}`.
4. **Your application** changes nothing: tokens from a passkey sign-in carry `amr` (use `ICurrentClient.Amr`, see [client-current-user.md](../client-current-user.md)). Applications with the **Self UI** cannot offer passkeys, because the browser binds a passkey to the Server's domain: send those users to the hosted sign-in. The Mvc sample (`.UseHostedUi()`) signs in on the Server, so it gets passkey sign-in once the project allows it. The Server's sign-in page does not draw the passkey button yet (see [ui-handover.md](../ui-handover.md)); until it does, call the routes above from your own script.

The exact JSON of every passkey route is in [ui-handover.md](../ui-handover.md#passkey-routes-json-for-the-ui-script). Tests: `PasskeyFlowTests` with `SoftwareAuthenticator` (it makes the same JSON a browser makes).

## How the JSON contract works (for the UI)

See [ui-handover.md](../ui-handover.md#passkey-routes-json-for-the-ui-script): the Server gives the options, the script calls `navigator.credentials.create/get`, and sends the result back.

## How to implement or extend

- `PasskeyService` (`Infrastructure/External/PasskeyService.cs`) uses the passkey support of ASP.NET Core Identity (`SignInManager.MakePasskeyCreationOptionsAsync`, `PerformPasskeyAttestationAsync`, `MakePasskeyRequestOptionsAsync`, `PerformPasskeyAssertionAsync`). The challenge state lives in the short-lived, single-use cookie scheme `Identity.TwoFactorUserId` (cookie `.SystemX.Auth2.PasskeyState`).
- The Identity options are global, so the per-project rules are added to the options JSON and **checked again on the server** (user verification, limits).
- **How the per-project store works**: `AppUserPasskey` has `ApplicationId` in its key, and `user_passkeys` has a query filter by the current project. `Auth2UserStore` overrides the passkey methods so that every call needs a current project. Test: `PasskeyStoreTests`, `PasskeyFlowTests.APasskey_OfApplicationA_IsNotAcceptedByApplicationB`.
- The AAGUID is read from the attestation object (`authData`) with `System.Formats.Cbor`.
- Tests use `SoftwareAuthenticator` (ES256, attestation "none") that makes the same JSON a browser makes.

## Related pages

- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](../external-identity.md)
- [Localization](../localization.md)
- [Server reference](../server-reference.md)
- [Authentication flows](../authentication-flows.md)
- [Social login with OpenIddict.Client.WebIntegration](social-webintegration.md)
- [SAML 2.0: enable and use](saml.md)
- [LDAP and Active Directory: enable and use](ldap.md)
- [Brokering: your app trusts only the Server](brokering.md)
- [Group mapping](group-mapping.md)
- [Troubleshooting external sign-in](troubleshooting.md)
- [Server settings](../server-settings.md)
- [Documentation index](../index.md)
- [Two-factor authentication and `TwoFactorService`](../two-factor.md)
- [Identity stores and managers (`SystemX.Auth2.Server.Persistence.Identity`)](../identity-stores.md)
- [Credential scope](../credential-scope.md)
- [Client setup](../client-setup.md)
- [Error codes](../error-codes.md)
- [Security](../security.md)
