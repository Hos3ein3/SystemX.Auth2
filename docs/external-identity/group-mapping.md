# Group mapping

A **mapping** turns something that the external source says (a group, a claim or an attribute) into a **role, group or permission of the same project**. Mappings are used by OIDC, SAML and LDAP.

## How to use (for operators)

1. Set `GroupMappingMode` to `Sync` on the provider.
2. Create mappings (admin API, step 9). A mapping has:

| Field | Meaning |
|---|---|
| `providerName` | One provider, or empty for all providers of the project |
| `sourceType` | `Group` (groups from the source), `Claim` (OIDC claim) or `Attribute` (SAML or LDAP attribute) |
| `sourceName` | The claim or attribute name. Empty for groups |
| `matchType` | `Exact` or `Prefix`. Case does not matter. **No regular expressions** (they can be slow and unsafe) |
| `sourceValue` | The text to match |
| `targetType`, `targetId` | `Role`, `Group` or `Permission` and its id |

Example: members of the group `Finance-EMEA` get the role `accountant`:
`sourceType=Group`, `matchType=Exact`, `sourceValue=Finance-EMEA`, `targetType=Role`, `targetId=<id of accountant>`.

### What happens at every sign-in

- Every target that matches is **added** (if the user does not have it).
- A target is **removed** only if some mapping of this provider mentions it and nothing matches any more. Roles, groups and permissions that no mapping mentions are never touched, so manual assignments stay.
- Changes raise the access version (tokens and caches refresh) and write one audit event `external.mapping.applied` with the number of changes (not the group names).

### Rules that cannot be changed

- A mapping can only give things of **the same project**. A target of another project is ignored.
- `GlobalAdmin` and system roles can never be given by a mapping.
- Mappings do nothing when `GroupMappingMode` is `None`.

## How to implement or extend

`ExternalMappingService` (`Infrastructure/External/ExternalLoginService.cs`) computes the wanted set and the mentioned set and uses `IAccessService` and `IGroupService`, so every change has the normal audit events and version bumps. To support a new kind of source value, return it as `ExternalIdentity.Groups` or `ExternalIdentity.Attributes`. Tests: `ExternalMappingTests`.
