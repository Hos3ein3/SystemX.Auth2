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

### 2. Turn them on per application

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
| Per application | A passkey made in application A is **not found** in application B (the same person registers again there) |
| `maxPasskeysPerUser`, `passkeyNameMaxLength` | Limits. `passkey_limit_reached`, `validation_failed` |
| `passkeyUserVerification` | `required` (default): a passkey without fingerprint/PIN is refused at registration and sign-in |
| `requirePasskeyBackupMethod` | A person whose only other way to sign in is one external login cannot remove it while they have **one** passkey; they must add a second one first. Without a password, a login or a passkey, nothing can be removed (`cannot_remove_last_login_method`) |
| Blocked authenticators | `Auth2:Passkeys:BlockedAaguids` (server setting, global admin) lists authenticator models (AAGUID) that cannot be registered: `passkey_blocked_authenticator`. Existing passkeys of a blocked model stay listed so they can be removed. The AAGUID is **as the authenticator reports it** (attestation is off by default: `AttestationMode: None`) |
| Allow list | `AttestationMode: AaguidAllowList` plus `AllowedAaguids` accepts only the listed models |
| Replay | Every challenge is single use; a copied answer fails. The signature counter is stored and updated |
| Rate limit | Policy `passkey` (default 20 per minute) on `/account/passkey/*` |

### Recovery

A passkey is lost when the device is lost. Advise users to (1) register a passkey on **two** devices, or use one that syncs (iCloud Keychain, Google Password Manager, a password manager: the Server marks these `isBackedUp`), and (2) keep another way to sign in (a password or an external login). The management page suggests adding another passkey when none of the user's passkeys is backed up. An admin can remove a lost passkey (admin API, step 9).

## How the JSON contract works (for the UI)

See [ui-handover.md](../ui-handover.md#passkey-routes-json-for-the-ui-script): the Server gives the options, the script calls `navigator.credentials.create/get`, and sends the result back.

## How to implement or extend

- `PasskeyService` (`Infrastructure/External/PasskeyService.cs`) uses the passkey support of ASP.NET Core Identity (`SignInManager.MakePasskeyCreationOptionsAsync`, `PerformPasskeyAttestationAsync`, `MakePasskeyRequestOptionsAsync`, `PerformPasskeyAssertionAsync`). The challenge state lives in the short-lived, single-use cookie scheme `Identity.TwoFactorUserId` (cookie `.SystemX.Auth2.PasskeyState`).
- The Identity options are global, so the per-application rules are added to the options JSON and **checked again on the server** (user verification, limits).
- **How the per-application store works**: `AppUserPasskey` has `ApplicationId` in its key, and `user_passkeys` has a query filter by the current application. `Auth2UserStore` overrides the passkey methods so that every call needs a current application. Test: `PasskeyStoreTests`, `PasskeyFlowTests.APasskey_OfApplicationA_IsNotAcceptedByApplicationB`.
- The AAGUID is read from the attestation object (`authData`) with `System.Formats.Cbor`.
- Tests use `SoftwareAuthenticator` (ES256, attestation "none") that makes the same JSON a browser makes.
