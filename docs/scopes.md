# Scopes and their resources

## What this is

A **scope** is a word that says **what a token may be used for**. An application asks for scopes when it signs a person in (or when a program asks for its own token), the Server grants the ones the application is **allowed** to have, and the scopes it grants are written into the token (the claim `scope`). Everything that receives the token (the Server's own API, your API) can then check them.

Three things to keep in mind:

- **A scope belongs to the application, not to the person.** Everybody who signs in through the same application gets the same scopes. What a *person* may do is decided by permissions, roles and groups ([access-concepts.md](access-concepts.md)).
- **A scope limits; it never gives rights to a person.** A token with `reports.read` is *allowed to be used* for the reports API; it does not make the person a reader of reports.
- **A scope can name the APIs it is for** (its **resources**). They become the audience (`aud`) of the token ([below](#resources-of-a-scope)).

```
application "shop"  --asks-->  scope=openid auth2.me reports.read
Server              --checks-> allowed for "shop"? registered on the server?
token               --carries-> "scope": "openid auth2.me reports.read"
                                "aud":   ["shop", "reports-api"]            (the resource of reports.read)
API                 --checks-> has the scope? is the audience me?
```

## When to use it

- You want an **application** (or a program) to be allowed to use one API and not another: `reports.read` for the reports API, `orders.write` for the orders API.
- You need a **program** (client credentials) to be limited to what it needs: it has scopes and no person at all.
- You want several APIs behind one application to **accept only the tokens meant for them** (resources).
- You want to know which `auth2.*` scope a call to the Server needs.

## The kinds of scope

| Kind | Examples | Made by | What it does |
|---|---|---|---|
| **Standard (OpenID Connect)** | `openid`, `profile`, `email`, `phone`, `roles`, `offline_access` | The Server (seeded) | Controls which **claims** the token has, and the refresh token |
| **SystemX.Auth2 (`auth2.*`)** | `auth2.me`, `auth2.register`, `auth2.client.handshake`, ... | The Server (seeded) | Lets a token call the Server's own API |
| **Custom** | `reports.read`, `orders.write` | You | What you decide: the rules of **your** APIs |

### Standard scopes

| Scope | What the token gets |
|---|---|
| `openid` | An identity token (the person's `sub`). Needed for every sign-in with a browser |
| `profile` | The claims of the name: `name`, `preferred_username`, `given_name`, `family_name`, `locale`. Without it `ICurrentUser.UserName` and `FullName` are `null` |
| `email` | `email` and `email_verified` |
| `phone` | `phone_number` |
| `roles` | The claim `role` (the person's effective roles). Without it the token has no role claim, and `[Auth2Role]` reads the roles from the access data instead |
| `offline_access` | A **refresh token**, so the application can renew the token without a new sign-in |

### The `auth2.*` scopes

A token that calls the Server's JSON API ([api-reference.md](api-reference.md)) must have the scope of that route. Most of them are for **programs** (client credentials); `auth2.me` is for a **person's** token.

| Scope | Lets the token | Typical holder |
|---|---|---|
| `auth2.me` | Read the current person (`/me`), change the password, user name, email address and phone number, and manage two-factor | A person's token. Ask for it at every sign-in |
| `auth2.register` | Register a person, finish joining a second application | An application's client token |
| `auth2.login` | Ask for a one-time code, forgot and reset a password, confirm an email or a phone | An application's client token |
| `auth2.client.handshake` | Make the handshake, read the external sign-in providers | Every application |
| `auth2.permissions.sync` | Push and pull permissions | An application with `EnablePermissions` |
| `auth2.roles.sync` | Push and pull roles | An application with `EnableRoles` |
| `auth2.groups.read` | Read groups | An application with `EnableGroups` |
| `auth2.groups.sync` | Push groups (only when the application allows group sync) | An application with `EnableGroups` |
| `auth2.scopes.sync` | Push and pull the custom scopes of the application | An application with `EnableScopes` |
| `auth2.admin` | Use the admin API for **one project** | An admin tool. Not allowed by default |
| `auth2.admin.global` | Use the admin API for **every project** | An admin tool. Not allowed by default |

A new application is allowed these scopes: `openid profile email phone roles offline_access auth2.me auth2.client.handshake auth2.register auth2.login auth2.permissions.sync auth2.roles.sync auth2.groups.read auth2.groups.sync auth2.scopes.sync`. **`auth2.admin` and `auth2.admin.global` are not in the list**: add them only to the clients of admin tools ([admin-api.md](admin-api.md)).

### Custom scopes

Your own words, for your own APIs. The usual shape is `{area}.{action}`: `reports.read`, `orders.write`. Rules:

- Lowercase letters, digits and `. _ : -`, 1 to 100 characters, starting and ending with a letter or digit (the rule of the sync). The console accepts any name without spaces up to 200 characters; keep to the sync rule so the scope can also be synced.
- **Not** `auth2.*` and not a standard scope: those names belong to the Server (the sync skips them).
- **Do not use the name of a permission.** A scope `reports.read` and a permission `reports.read` are different things that look the same in a log ([access-concepts.md](access-concepts.md)). Give the scope its own name (`api.reports.read`).

### Scope sync and what a scope really limits

**Read this before you rely on scopes for security.** Two project settings (global admin only) decide what a sync does:

| `ScopeSyncAllowed` | `AutoAssignScopeToApplication` | What happens to a pushed scope |
|---|---|---|
| on | **on** (default) | It is created if it is new **and added to the allowed scopes of the application that pushed it**, even when the scope already exists and an admin made it for another application. Only `auth2.*` and the standard scopes are refused. The application can use it at once |
| on | **off** | It is **only registered** (created if new, saved in the database, visible in the console). **No application is given it**, not even the one that pushed it. An admin allows it for the applications that need it |
| off | (any) | Nothing is accepted: the push answers `sync_not_allowed` |

With auto assign **on**, an application with a valid client secret can allow itself any custom scope, so a check like `[Auth2Scope("orders.write")]` fails only when the application does not **ask for** the scope at sign-in (or for its machine token). That is a decision of the application's developer, not a rule of the Server. With auto assign **off**, the Server did not allow the scope for the application: the token request fails with `invalid_scope` and `[Auth2Scope]` answers 403. The scope is a real limit.

For production, where scopes protect something:

1. Turn **off** `Clients.AutoAssignScopeToApplication` (or the whole `Clients.ScopeSyncAllowed`) in the project settings. The console has the checkbox *Give Synced Scopes To The Application*.
2. Give each application only the scopes it needs (**Applications -> the application -> Allowed Scopes**, or `ApplicationSpec.AllowedScopes`). A scope the clients registered is already in the list of scopes, so you only tick it.
3. Protect an API with the audience of the scope (`UseApiAuthentication(o => o.Audience = "orders-api")`, [resources of a scope](#resources-of-a-scope)), so it accepts only tokens made for it.

The sync response then lists in `addedKeys` the scopes **this call registered** (with auto assign on it lists the scopes the application was given). A scope is a limit on the **token** (what this client may do), not on the person: roles, permissions and groups limit the person.

## How a scope gets into a token

1. **The scope exists on the Server** (it is *registered*): the standard and `auth2.*` scopes are seeded; custom scopes you create ([below](#create-a-scope)).
2. **The application is allowed to use it** (its *allowed scopes*, [below](#allow-a-scope-for-an-application)).
3. **The application asks for it**: in the sign-in request, or in the token request of a program ([below](#ask-for-scopes)).
4. **The Server grants it.** A scope that is not allowed or not registered makes the request fail with `invalid_scope` (it is not dropped silently). When consent is on, the person sees the scopes and agrees ([authentication-flows.md](authentication-flows.md)).
5. **The token carries it** (claim `scope`, and the resources of the granted scopes in `aud`).
6. **Whoever receives the token checks it** ([below](#check-a-scope)).

For a **cookie application with the hosted UI**, the Client copies the scopes (and the application's client id, project and session ids) from the access token into the cookie when the person signs in, so `ICurrentClient.Scopes`, `[Auth2Scope]` and `WithScope` work on a cookie page.

## Create a scope

Only a **global admin** can create, change or delete a scope. A scope can be deleted only when **no application allows it** any more (`conflict`).

**In the admin console.** `/admin/p/{project}/scopes` → **Create**: name, display name, description and the resources. The display name and description are texts for the admin screens.

**In code** (seeding, your own admin tool), with the service behind the console:

```csharp
var (result, scope) = await scopes.CreateAsync(
    new CreateScopeRequest("api.reports.read", DisplayName: "Read reports", Description: "Use the reports API", Resources: ["reports-api"]),
    isGlobalAdmin: true, ct);
```

or with OpenIddict directly:

```csharp
var descriptor = new OpenIddictScopeDescriptor { Name = "api.reports.read", DisplayName = "Read reports" };
descriptor.Resources.Add("reports-api");
await scopeManager.CreateAsync(descriptor);
```

**From the client application (sync).** A client lists its scopes in `EnableScopes` (a list, `AddFromClass`, `AddFromEnum`, or found in `[Auth2Scope]` and `WithScope`), and the Client pushes them at start ([permission-sync.md](permission-sync.md#custom-scopes)). The Server registers a scope that is new (the first text stays) **and allows it for the calling application**. It never removes a scope and skips the reserved names. A scope made this way has no resources.

## Allow a scope for an application

Registering a scope does not give it to any application. Every application has its **allowed scopes**:

- **Console:** the application's page (`Applications` → the application → *Allowed Scopes*), space-separated.
- **Code:** `ApplicationSpec.AllowedScopes` (`IApplicationService.CreateAsync` and `UpdateAsync`). `UpdateAsync` with a list **replaces** the allowed scopes; add to the current list to keep the others.
- **Admin API:** `allowedScopes` when you create or update an application ([admin-api.md](admin-api.md)).
- **Sync:** with `Clients.AutoAssignScopeToApplication` on (default), allows the pushed scope for the **calling** application only. Another application, even of the same project, has to ask for it itself. With it off, a sync allows nothing: you allow the scope here.

Only the scopes an application needs. A browser application usually needs `openid profile email roles offline_access auth2.me` and its own custom scopes; a program needs only the `auth2.*` scopes of the routes it calls.

## Ask for scopes

| Who | How |
|---|---|
| **Cookie application, hosted UI** | `UseHostedUi(o => o.Scopes.AddRange(["reports.read"]))`. The default is `openid profile email offline_access auth2.me`; the list you give is added to it |
| **Your own login form (Self UI) or an API** | `new LoginRequest("sara", password, ["openid", "auth2.me", "reports.read"])`; without a list the Client asks for `openid offline_access auth2.me` |
| **A program (client credentials)** | `await machineTokens.GetTokenAsync(["auth2.permissions.sync"])`, or `AddAuth2MachineToken("reports.read")` on an `HttpClient`. One set of scopes per target API ([machine-to-machine.md](machine-to-machine.md)) |

## Check a scope

**In your API** (the token of the caller must have it):

```csharp
app.MapGet("/reports", Reports).WithScope("reports.read");              // Minimal API
[Auth2Scope("reports.read")] public IActionResult Reports() => ...;     // controller
app.MapGet("/x", X).WithScopeOrRole(["reports.read"], ["Admin"]);       // a scope OR a role
```

**In code:** `ICurrentClient.HasScope("reports.read")` and `ICurrentClient.Scopes` ([client-current-user.md](client-current-user.md)). The check is exact (case-sensitive) and needs no call to the Server.

**On the Server:** the routes of `auth2.*` check them for you, and answer `403 insufficient_scope` when the scope is missing.

A scope is **AND** with the other rules when you chain them: `[Auth2Scope("reports.read")] [Auth2Permission("reports.read")]` needs the application to be allowed to use the reports API **and** the person to be allowed to read reports ([client-endpoint-metadata.md](client-endpoint-metadata.md)).

## Resources of a scope

### What they are

Every scope has a list of **resources**: the names of the **APIs (resource servers)** the scope is meant for, for example `orders-api`. In the admin console (`/admin/p/{project}/scopes/detail?id=...`) the field is called **Associated Resources**.

A resource becomes an **audience** of the token: the claim `aud`, which tells an API "this token was made for you". When a token is made, the Server puts into `aud`:

1. the **client id of the application** that asked for the token (always first), then
2. the **resources of every scope that was granted**, without duplicates.

| Scope | Resources |
|---|---|
| `orders.api` | `orders-api` |
| `reports.api` | `reports-api`, `orders-api` |

A user signs in to the application `shop` and asks for `openid auth2.me orders.api reports.api`. The access token gets

```
"aud": ["shop", "orders-api", "reports-api"]
```

and the same token without those scopes gets `"aud": ["shop"]` only. An API that checks the audience then accepts only the tokens that were made for it, even when the signature of another token is valid: **a token is only good for the APIs that its scopes name.** It works for **user tokens** and for **machine tokens** the same way, and a **refresh** makes the audiences again from the scopes of the refresh, so a change of the resources reaches an existing session at its next refresh.

### When to use resources

- You have **several APIs** behind one application and each should accept only the tokens meant for it (`orders-api` must not accept a token that was made for `reports-api`).
- An API of **another party** wants to check `aud` with a fixed name that is not your client id.
- You want to see in the console which API a custom scope belongs to.

You do **not** need resources when you have one API per application: the audience is already the client id, and `UseApiAuthentication` checks exactly that ([client-authorization.md](client-authorization.md#api-authentication)).

### What a resource looks like

- Any text without spaces: a name (`orders-api`) or a URL (`https://orders.example.com`). The Server does not check that an API of that name exists, and there is no list of resources to choose from.
- A scope has zero, one or many. They are an **attribute of the scope**, not objects of their own: no table, no id and no page. They are stored as a JSON array in the column `resources` of the scope row (`openiddict_scopes`, [database-tables.md](database-tables.md)), so they need **no separate CRUD**: you edit them where you edit the scope.
- They are compared exactly (case-sensitive text). A resource that is the same as the client id, or is repeated, is added once. Empty entries are skipped.

### Manage them in the admin console

1. Open **Scopes** of the project. The column *Resources* shows the resources of each scope (`-` for none).
2. Choose a scope (or press **Create**). In **Associated Resources** type the resources separated by a **space, a comma or a new line**: `orders-api reports-api`.
3. Press **Save**. The page replaces the whole list with what you typed: delete a name to remove it, empty the field to remove all.

A change counts for **new** tokens at once. Tokens that already exist keep the audiences they were made with until they expire (the access token lifetime, 15 minutes by default); a **refresh** gives the new audiences.

### Manage them in code

`IAdminScopeService` (the service behind the console) takes the list as `Resources`; `isGlobalAdmin` must be `true`, otherwise the answer is `forbidden`.

```csharp
public sealed class ScopeSetup(IAdminScopeService scopes)
{
    public async Task CreateAsync(CancellationToken ct)
    {
        var (result, scope) = await scopes.CreateAsync(
            new CreateScopeRequest("reports.api", DisplayName: "Reports API", Description: "Read the reports", Resources: ["reports-api", "orders-api"]),
            isGlobalAdmin: true, ct);
        if (!result.Succeeded) throw new InvalidOperationException(result.ErrorCode);   // for example "conflict": the scope exists
    }

    public async Task ChangeAsync(Guid scopeId, CancellationToken ct)
    {
        // Resources: null = leave them as they are, [] = remove all, a list = replace the list
        await scopes.UpdateAsync(scopeId, new UpdateScopeRequest(Resources: ["reports-api", "billing-api"]), isGlobalAdmin: true, ct);
        await scopes.UpdateAsync(scopeId, new UpdateScopeRequest(DisplayName: "Reports"), isGlobalAdmin: true, ct);           // the resources stay
        await scopes.UpdateAsync(scopeId, new UpdateScopeRequest(Resources: []), isGlobalAdmin: true, ct);                    // none
    }

    public async Task<IReadOnlyList<string>?> ReadAsync(Guid scopeId, CancellationToken ct) => (await scopes.GetAsync(scopeId, ct))?.Resources;
}
```

| Call | Resources | Result |
|---|---|---|
| `CreateAsync(new CreateScopeRequest(name, ..., Resources: [...]), true)` | the list (or `null` for none) | `OperationResult` and the `AdminScopeDto` |
| `UpdateAsync(id, new UpdateScopeRequest(..., Resources: null), true)` | unchanged | |
| `UpdateAsync(id, new UpdateScopeRequest(..., Resources: []), true)` | removed | |
| `UpdateAsync(id, new UpdateScopeRequest(..., Resources: [...]), true)` | replaced | |
| `GetAsync(id)` / `ListAsync(query)` | `AdminScopeDto.Resources` | |

With OpenIddict directly: `descriptor.Resources.Add("orders-api")` before `CreateAsync`. Scopes registered by the client sync have **no** resources; add them in the console or with `UpdateAsync`. The scope still has to be **allowed on the application** that asks for it; the resources do not allow anything by themselves.

### Use them in an API

The API tells the Client package which audience it is. With `UseApiAuthentication` the audience defaults to the client id; set it to the resource name:

```csharp
builder.Services.AddAuth2Client("https://auth.example.com", o => { /* name, client id, secret */ })
    .ServerWithMultiProject("papertrade")
    .UseApiAuthentication(o => o.Audience = "orders-api");     // this API is the resource "orders-api"

app.MapGet("/orders", () => orders).WithAuth2();
```

Now the API accepts a token **only if `aud` contains `orders-api`**: the tokens whose scopes include `orders.api` (or any other scope that lists `orders-api`), and nothing else. The caller asks for the scope when it signs in:

```csharp
var token = await machineTokens.GetTokenAsync(["orders.api"]);                                             // a program
var login = await api.LoginAsync(new LoginRequest("sara", password, ["openid", "auth2.me", "orders.api"])); // a person
```

| Token | `aud` | `/orders` |
|---|---|---|
| asked for `orders.api` | `shop`, `orders-api` | `200` |
| asked for `auth2.me` only | `shop` | `401` (not for this API) |
| no token | | `401` |

Both rules apply on the endpoint: the audience says the token is **for this API**, and the usual rules (`WithScope`, `WithPermission`, `WithRole`, ...) say what the caller **may do** in it ([client-authorization.md](client-authorization.md)).

### Rules the Server keeps

- **The client id is always the first audience.** The Server finds the application of a call from its token (the claim `client_id` when it is one of the audiences, otherwise the first audience). A resource that happens to be the client id of **another** application never selects that application: a token with `aud = ["shop", "billing"]` is still a token of `shop`.
- **Only granted scopes count.** A scope that was not asked for, or not allowed on the application, adds nothing.
- **The identity token is not changed.** The resources go to the access token.

## Common problems

| Problem | Cause |
|---|---|
| `invalid_scope` when signing in or asking for a token | The application does not allow the scope, or the scope is not registered on the Server. Create it, then add it to the application's allowed scopes |
| A scope check never fails, whatever the application asks | Scope sync is on with `AutoAssignScopeToApplication`: the application allowed the scope for itself. Turn the setting off. See [scope sync and what a scope really limits](#scope-sync-and-what-a-scope-really-limits) |
| The scope is in the token of one application and not of another | Allowed scopes are per application. Sync allows a scope for the calling application only |
| `[Auth2Scope]` refuses everybody on a cookie page | The application did not ask for the scope (`UseHostedUi(o => o.Scopes...)`), or does not allow it |
| `403 insufficient_scope` from the Server | The token lacks the `auth2.*` scope of the route. Allow it on the application and ask for it |
| `UserName` is `null` | The `profile` scope was not asked for |
| No `role` claim in the token | The `roles` scope was not asked for ([access-concepts.md](access-concepts.md#role-in-depth)) |
| I cannot delete a scope | An application still allows it. Remove it from the allowed scopes first |
| The scope fields are read-only | Only a global admin can change a scope |
| I added a resource and nothing changed | The scope was not granted (not allowed, or not asked for), or you look at an old token: it keeps its audiences until it expires or is refreshed |
| My API answers `401` for every token after I set `Audience` to a resource name | The token does not have that audience. The caller must ask for a scope that lists the resource, and the scope must be allowed on its application |
| Two APIs accept each other's tokens | They list the same resource in their scopes, or both use the client id as `Audience`. Give each API its own resource name and its own scope |

## How to implement or extend

- The audiences are made in one place, `TokenIssuer.AudiencesAsync` (user tokens, machine tokens and refresh). To add more rules (for example an audience for every application of a project), change it there and keep the client id first.
- The helper `TokenAudience.ApplicationOf(principal)` is what the Server uses to find the application of a token; use it in your own server-side code instead of reading the first audience yourself.
- A scope for a new API: create it (with its resource), allow it for the applications that may call that API, check it with `WithScope`, and set `Audience` in that API.

## Related pages

- [Scope, permission, role, group: which is which](access-concepts.md#scope-in-depth)
- [The MVC sample: one action for every kind of rule](sample-mvc.md)
- [Seeding (first admin)](seeding.md)
- [Client setup](client-setup.md)
- [Getting started](getting-started.md)
- [Concepts](concepts.md)
- [Documentation index](index.md)
