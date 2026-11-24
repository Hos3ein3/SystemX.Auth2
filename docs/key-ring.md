# The key ring and its snapshot

## What this is

The **key ring** is the part of the Server that owns the keys that sign and encrypt tokens. When you do not give the Server certificate files, it makes the keys itself, keeps them in the database (table `key_ring`), shares them between all instances, and replaces them on request without a restart.

Two words you will meet:

- **Key pair.** One signing key and one encryption key, both RSA 2048, made together. A pair is created at the first start and at every rotation.
- **Snapshot.** The copy of the current keys that every Server instance keeps **in memory**. Token checks use the snapshot, never the database, so a request does not wait for a query. The snapshot is replaced as one piece when the keys change.

## When to use it

| You want | Use |
|---|---|
| The simplest setup, several instances, rotation with one command | The **key ring** (the default when no certificate files are set) |
| Keys that you make, store and rotate yourself (HSM, a company PKI, a policy that says so) | **Certificate files**: `Auth2:Keys:SigningCertificates` and `EncryptionCertificates`. The key ring then stays idle |

The Server in `Production` refuses to start without an `https` issuer **and** keys; with the key ring you only need the issuer, because the keys come from the database. See [server-setup.md](server-setup.md#keys-issuer-and-the-production-guard). In `Development` and `Testing` the key ring works the same way as in production.

## What a key is (properties)

One row of `key_ring` is one key (so a pair is two rows).

| Property | Column | Meaning |
|---|---|---|
| `Id` | `id` | A time-ordered GUID. The list of ids is also what the snapshot compares (see below) |
| `Purpose` | `purpose` | `Signing` (signs access and identity tokens) or `Encryption` (encrypts authorization codes and refresh tokens) |
| `ProtectedPfx` | `protected_pfx` | The certificate **with its private key**, as a PFX, base64, and then encrypted with ASP.NET Core Data Protection (purpose string `SystemX.Auth2.KeyRing`). It is never stored or logged in clear |
| `Thumbprint` | `thumbprint` | The certificate thumbprint. Public: shown in the admin pages and in the `rotate-keys` output |
| `CreatedAtUtc` | `created_at_utc` | When the key was made |
| `NotAfterUtc` | `not_after_utc` | End of validity. A **later** date makes OpenIddict prefer the key when it picks one to sign with, so a new key is always made at least one second later than the newest existing one |
| `RetiredAtUtc` | `retired_at_utc` | `null` while the key is current. Set when a newer pair replaced it or an admin retired it. A retired key no longer signs |
| `RemoveAtUtc` | `remove_at_utc` | When the retired key is deleted. `null` while current |

The certificate is self-signed (`CN=SystemX.Auth2 Signing 20261124…`), valid from five minutes in the past (the clocks of instances differ) to `NotAfterUtc`. Signing keys have the key usage *digital signature*, encryption keys *key encipherment*.

### Lifecycle of a key

```
created ──(current: signs / encrypts, and checks / decrypts)──► retired ──(still checks and decrypts)──► removed
 day 0                                                         rotation or early retire              RemoveAtUtc
```

A **retired** key keeps validating signatures and decrypting, so tokens issued before the rotation keep working. Its public part stays in the discovery key set (`/.well-known/jwks`), so APIs that validate tokens locally accept old tokens until the key is removed.

### Settings (`Auth2:Keys`)

| Key | Default | Meaning |
|---|---|---|
| `KeyLifetimeDays` | `730` | Validity of a new key (the certificate lifetime) |
| `RetiredKeyDays` | `35` | How long a retired key stays before it is deleted. Keep it longer than the refresh token lifetime (30 days by default): tokens signed or encrypted with it must be able to die first |
| `RefreshSeconds` | `30` | How often every instance looks for keys changed by another instance |
| `SigningCertificates`, `EncryptionCertificates` | none | Certificate files. When at least one of each is set, the key ring is **not used** |

## How the snapshot works

`KeyRing` is a singleton. Inside it, `_snapshot` is an immutable record: a **fingerprint** and the list of keys (the key information plus the loaded certificate).

1. **Fingerprint.** It is the sorted list of the ids of all rows that are not yet past `RemoveAtUtc`. Two instances with the same database have the same fingerprint.
2. **Refresh** (`RefreshAsync`). Reads the rows, computes the fingerprint, and **stops** when it equals the one in memory (the usual case, so the 30-second check costs one small query). Otherwise it decrypts every `ProtectedPfx`, loads the certificates (in memory only: `EphemeralKeySet`; macOS cannot do that, so it uses the default key set there), builds a new snapshot and swaps it in under a lock.
3. **Change token.** After the swap the key ring cancels a `CancellationChangeToken`. OpenIddict (the server part and the validation part) listens to it and **builds its options again with the new keys**. That is how a rotation reaches token signing without a restart.
4. **Who refreshes.** The instance that rotates refreshes at once. All the others run `KeyRingRefreshService`, a background service that calls `RefreshAsync` every `RefreshSeconds`. If a key cannot be decrypted (most often because the Data Protection keys changed) it is skipped, an error is logged (`A key of the key ring could not be read`) and the other keys keep working.
5. **Order.** Credentials are given to OpenIddict newest first (by `NotAfterUtc`); the first one signs, all of them check.
6. **When the database is not ready** (not migrated yet, or down at start) the Server uses **temporary** keys so that it can still answer, logs a warning, and reads the real keys as soon as they exist.

Why a snapshot: reading and decrypting a PFX per request would be slow and would make every token check depend on the database. With the snapshot the cost is one memory read, and a failure of the database does not stop token checks.

## How to use it

**Look at the keys** (global admin): admin page `/admin/keys` or `GET /api/auth2/admin/keys`. Each key shows purpose, thumbprint, validity, and whether it is current or retired (and when it will be removed).

**Rotate** (global admin), regularly (every 6 to 12 months) and at once if you think a key leaked:

```bash
dotnet run --project src/SystemX.Auth2.Server -- rotate-keys      # the command
# or: POST /api/auth2/admin/keys/rotate                           # the admin API, or the Rotate button on /admin/keys
```

What happens: under a database lock a new pair is made; the current pair gets `RetiredAtUtc = now` and `RemoveAtUtc = now + RetiredKeyDays`; this instance refreshes; the others follow within `RefreshSeconds`. An audit event `keys.rotated` is written.

**Retire one key early** (the *Retire* button on `/admin/keys`): sets `RetiredAtUtc` and `RemoveAtUtc` for that key only. Use it for a key you want to stop using before the next rotation. Make sure another current key of the same purpose exists.

**Removal** is automatic: the maintenance job `key-removal` deletes the rows whose `RemoveAtUtc` has passed ([security.md](security.md#background-jobs)).

**Start** needs nothing: the first instance that starts with an empty table creates the first pair (`EnsureAsync`). Several instances starting together are safe: creation runs in a transaction holding the PostgreSQL advisory lock `auth2.keyring`, so only one pair is made. Rotation uses the same lock.

## Where it is used

| Used for | Notes |
|---|---|
| Signing access and identity tokens | The newest signing key signs. The public keys are published at `/.well-known/jwks` |
| Encrypting authorization codes and refresh tokens | The newest encryption key encrypts. Access tokens are **not** encrypted ([decisions.md](decisions.md)) |
| Validating the Server's own APIs | The validation part of OpenIddict (`/connect/userinfo`, the admin API) is rebuilt with the same keys through the same change token |
| Your applications | They never see a private key. The Client package reads the public keys from discovery, caches them, and reads them again when it meets an unknown key id after a rotation ([client-authorization.md](client-authorization.md)) |

It is **not** used for: the sign-in cookie and other Data Protection payloads (those use the Data Protection keys), the encryption of provider secrets (also Data Protection), or the SAML service provider certificate (made and stored per SAML provider, see [saml-certificate-rotation.md](external-identity/saml-certificate-rotation.md)).

## Benefits

- **Zero-touch start.** No certificate files to make before the first run.
- **Rotation without a restart or downtime**, and without breaking tokens that are already issued.
- **Many instances share one set of keys** through the database; a rotation on one is picked up by all.
- **Safe at rest.** Private keys are encrypted with Data Protection and never leave the database in clear.
- **Fast and resilient checks.** The in-memory snapshot makes token checks independent of the database; a bad key row cannot stop the good ones.
- **Audited.** Every rotation writes `keys.rotated`.

## Limits and things to know

- The key ring is only as safe as the **Data Protection keys** (table `data_protection_keys`). If you lose them, the `protected_pfx` values cannot be read and no key can be loaded. Back up the database. For stronger protection protect the Data Protection keys with a certificate in your own startup code ([security.md](security.md#keys-and-rotation)).
- All instances must share the Data Protection keys and the application name `SystemX.Auth2` (they do by default: the keys are in the database).
- Retiring or removing the **only** key of a purpose leaves nothing to sign or encrypt with. Rotate (which makes a new pair) instead.
- APIs that validate the JWT elsewhere keep using the public keys they cached until they read discovery again.

## Common problems

| Symptom | Cause and fix |
|---|---|
| Log: `A key of the key ring could not be read` | The Data Protection keys changed (a restored database without them, a different application name). Restore `data_protection_keys`; the key is read again at the next refresh |
| Log: `The key ring is not readable yet` | The database is not migrated or not reachable. Run `migrate`; temporary keys are used meanwhile, so tokens made in that time stop working after the real keys appear |
| After a rotation one instance still signs with the old key | It has not refreshed yet (up to `RefreshSeconds`) or cannot reach the database. Check `/health/ready` |
| An API says `invalid signature` just after a rotation | It cached the old key set. The Client package reads discovery again on an unknown key id; a custom validator must do the same |

## How to implement or extend

- Interface: `IKeyRing` (`ListAsync`, `EnsureAsync`, `RotateAsync`, `RemoveExpiredAsync`, `RetireAsync`, `RefreshAsync`) in `Application/OperationsAbstractions.cs`. Implementation: `Infrastructure/Operations/KeyRing.cs`; the entity is `KeyRingEntry` (`Domain/Entities/KeyRing.cs`).
- The wiring to OpenIddict: `KeyRingOptionsConfigurator` (server options), `KeyRingValidationConfigurator` and `KeyRingValidationChangeSource` (validation options), `KeyRingRefreshService` (the 30-second refresh). All are skipped when `Auth2KeyOptions.HasCertificates` is true.
- To use a different key store (a vault) replace these configurators and implement the same change-token idea: build the credentials, add them to `OpenIddictServerOptions`, and cancel a change token when they change.
- Tests: `KeyRingTests` in `tests/SystemX.Auth2.Server.IntegrationTests/OperationsTests.cs` (first start, rotation with old tokens still valid, removal, the command and the admin API) and `ScaleOutTests` (a second instance picks up a rotation).

## Related pages

- [security.md](security.md)
- [deployment-and-scaling.md](deployment-and-scaling.md)
- [database-tables.md](database-tables.md)
- [authentication-flows.md](authentication-flows.md)
- [decisions.md](decisions.md)
- [Authorization: permissions, roles and groups](authorization.md)
- [Architecture](architecture.md)
- [Server settings](server-settings.md)
- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](external-identity.md)
- [Observability](observability.md)
- [Admin JSON API](admin-api.md)
- [API reference](api-reference.md)
- [Email and SMS](email-and-sms.md)
- [UI handover](ui-handover.md)
- [Contributing workflow](contributing-workflow.md)
- [Documentation index](index.md)
