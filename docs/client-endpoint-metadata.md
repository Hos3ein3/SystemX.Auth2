# Minimal API methods and attributes: when to use which

## What this is

The Client package gives you **two ways to write the same rule**:

| Style | Looks like | For |
|---|---|---|
| **Endpoint methods** (`Auth2EndpointExtensions`) | `app.MapGet("/orders", ...).WithPermission("orders.read")` | Minimal API endpoints and route groups |
| **Attributes** (`Metadata.cs`) | `[Auth2Permission("orders.read")]` | Controllers, actions, Razor Page models, Blazor components, lambdas, and the **constants** of your catalog |

They are not two engines. Every `With...` method just adds the matching attribute to the endpoint (`WithPermission(x)` is `RequireAuthorization(new Auth2PermissionAttribute(false, x))`). So:

- an endpoint method and an attribute with the same values behave **exactly** the same;
- both put a **policy** (`auth2:perm:any:orders.read`) and a **metadata object** on the endpoint;
- the policy makes ASP.NET Core ask the Auth2 handler; the metadata lets the **scanners** and your own code see what the endpoint needs.

## When to use it

| Situation | Use |
|---|---|
| A Minimal API endpoint or group | The `With...` methods |
| An MVC / API controller, a Razor Page, a Blazor component | The attribute on the class or the action |
| A rule for a whole controller | The attribute on the **class**, plus more on the actions |
| A lambda endpoint where you want the rule next to the parameters | An attribute on the lambda: `app.MapGet("/x", [Auth2Permission("a.b")] () => ...)` |
| You keep keys in constants and want the catalog to carry a display name and a description | The attribute **on the constant** (`[Auth2Permission(DisplayName = "...")] public const string ...`) |
| A rule that is none of these (time of day, owner of the record) | Your own policy, with `.WithAuth2Policy("name")` or `[Authorize(Policy = "name")]` |

If you mix styles in one application nothing breaks. Pick one per project for readability: endpoint methods for a Minimal API, attributes for MVC.

## The rules, side by side

| Minimal API | Attribute | Meaning | Policy name made |
|---|---|---|---|
| `.WithAuth2()` | `[Auth2Authorize]` | A valid token | (default policy) |
| `.WithAnonymous()` | `[Auth2Anonymous]` | Nobody is asked | |
| `.WithRole("A","B")` | `[Auth2Role("A","B")]` | **Any** of the roles | `auth2:role:any:A\|B` |
| `.WithAllRoles("A","B")` | `[Auth2Role(true, "A","B")]` | **All** of the roles | `auth2:role:all:A\|B` |
| `.WithPermission("a","b")` | `[Auth2Permission("a","b")]` | **Any** of the permissions | `auth2:perm:any:a\|b` |
| `.WithAllPermissions("a","b")` | `[Auth2Permission(true, "a","b")]` | **All** of the permissions | `auth2:perm:all:a\|b` |
| `.WithOnlyRolePermission()` | `[Auth2OnlyRolePermission]` | The permissions must come from a role (or a group's role) | (no policy: it changes how the permission rule reads) |
| `.WithOnlyUserPermission()` | `[Auth2OnlyUserPermission]` | The permissions must be direct grants | (same) |
| `.WithScope("s")` | `[Auth2Scope("s")]` | **Any** of the scopes of the token | `auth2:scope:any:s` |
| `.WithGroup("g")` | `[Auth2Group("g")]` | In **any** of the groups | `auth2:group:any:g` |
| `.WithMachineOnly()` | `[Auth2MachineOnly]` | A client-credentials token | `auth2:kind:machine:` |
| `.WithUserOnly()` | `[Auth2UserOnly]` | A person's token | `auth2:kind:user:` |
| `.WithAuth2Policy("p")` | `[Authorize(Policy = "p")]` | Your own policy | yours |
| `.WithAuth2Audit("act")` | `[Auth2Audit("act")]` | Raise an audit event | (metadata only) |

**Several values in one call = OR. Several calls or attributes = AND.**

```csharp
.WithPermission("orders.read", "orders.write")                 // read OR write
.WithPermission("orders.read").WithPermission("orders.write")  // read AND write
.WithAllPermissions("orders.read", "orders.write")             // read AND write, in one rule
.WithScope("a").WithScope("b")                                 // scopes have no "all" form: chain them
```

The same holds for attributes: two `[Auth2Permission]` on one action are two rules and both must pass. An attribute on the **class** and one on the **action** also both apply.

How each rule is checked (data, cost, case) is in [client-authorization.md](client-authorization.md#the-rules); what the words mean is in [access-concepts.md](access-concepts.md).

## Minimal API in practice

From `samples/SystemX.Auth2.Samples.MinimalApi/Program.cs`:

```csharp
app.MapGet("/api/orders", (ICurrentUser user) => Results.Ok(orders))
   .WithPermission("orders.read")
   .WithAuth2Audit("orders.read");

app.MapPost("/api/orders", ([FromBody] CreateMinimalOrderRequest request) => { /* ... */ })
   .WithPermission("orders.write")
   .WithAuth2Audit("orders.create");

app.MapGet("/api/orders/role-only", () => "...").WithPermission("orders.read").WithOnlyRolePermission();
app.MapGet("/api/reports", () => "...").WithScope("reports.read");
app.MapGet("/api/finance", () => "...").WithGroup("finance");
app.MapGet("/api/machine-only", () => "...").WithMachineOnly();
```

### Route groups: one rule for many endpoints

```csharp
var admin = app.MapGroup("/api/admin").WithRole("Admin").WithAuth2Audit("admin");

admin.MapGet("/users", ListUsers);                                        // needs Admin
admin.MapDelete("/users/{id}", DeleteUser).WithPermission("users.delete"); // needs Admin AND users.delete
admin.MapGet("/ping", () => "ok").WithAnonymous();                         // open: AllowAnonymous wins, as everywhere in ASP.NET Core
```

The group's rules are added to every endpoint inside, so they are **AND** with the endpoint's own rules. The scanners see them, because they read the metadata of each endpoint.

### Methods that do not authorize

`WithOnlyRolePermission()`, `WithOnlyUserPermission()` and `WithAuth2Audit()` only add metadata. `WithOnlyRolePermission()` does nothing without a permission rule on the same endpoint, and it applies to **every** permission rule of that endpoint. `WithAuth2Audit()` fires for any rule outcome on that endpoint, including a refusal ([client-authorization.md](client-authorization.md#audit-hook)).

## Attributes in practice

From `samples/SystemX.Auth2.Samples.Mvc`:

```csharp
[ApiController, Route("api/data")]
public class DataApiController(ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("public"), AllowAnonymous]
    public IActionResult GetPublicData() => Ok(...);

    [HttpGet("orders"), Auth2Permission("orders.read"), Auth2Audit("orders.read")]
    public IActionResult GetOrders() => Ok(...);

    [HttpGet("reports"), Auth2Scope("reports.read")]
    public IActionResult GetReports() => Ok(...);

    [HttpGet("admin-only"), Auth2Role("Admin")]
    public IActionResult GetAdminData() => Ok(...);
}
```

On a whole class (every action needs the rule):

```csharp
[Auth2Role("Admin")]
public class AdminController : Controller
{
    public IActionResult Index() => View();                                   // Admin
    [Auth2Permission("users.delete")] public IActionResult Delete() => ...;   // Admin AND users.delete
    [AllowAnonymous] public IActionResult Help() => View();                   // open
}
```

### Rules for attributes

- Where they go: `Class` and `Method` for every attribute. `Auth2Permission`, `Auth2Role`, `Auth2Scope` and `Auth2Group` also go on a **field** (a `const`): there they carry the texts of the catalog and do not protect anything.
- `Inherited = true`: a derived controller keeps the rules of its base class.
- **Pass a key on a method or class.** An `[Auth2Permission]` with no key on an action has no policy and means "a signed-in user", which is probably not what you meant. The no-key form is for constants.
- Names are written as you like (`"Orders.Read"`); the permission check trims and lower-cases them, role and group checks ignore case, scope checks are **exact** (case-sensitive).
- Standard ASP.NET attributes work next to these. `[AllowAnonymous]` and `[Auth2Anonymous]` both open an action. `[Authorize(Roles = "Admin")]` reads the `role` claims of the token; prefer `[Auth2Role]`, which also works when the token has no role claim.

### The texts on an attribute

An attribute can carry what the admin console shows:

```csharp
[Auth2Permission("orders.write", DisplayName = "Write orders", Description = "Create and change orders", Group = "Orders")]
[Auth2Role("Editor", DisplayName = "Editor", Description = "Edits orders", Permissions = ["orders.read", "orders.write"])]
[Auth2Scope("reports.read", DisplayName = "Read reports", Description = "May use the reports API")]
[Auth2Group("finance", DisplayName = "Finance team")]
```

The scanner copies these texts into the catalog **when the attribute names exactly one key** (the texts of an attribute with several keys would be ambiguous, so none is used). If the same key is found twice, the first one with texts wins over one without. The same texts work on a constant, read by `AddFromClass` ([permission-sync.md](permission-sync.md#catalog-sources-client-side)). Texts you set by hand in a list win over everything scanned.

## What the scanners read

The metadata is not only for authorization. The catalog sync ([permission-sync.md](permission-sync.md)) asks four scanners what your application **needs**, and pushes it to the Server so the admin console knows it.

| Scanner | Looks at | Finds |
|---|---|---|
| `IPermissionScanner` | `IAuth2PermissionMetadata` on every endpoint, and `Auth2PermissionAttribute` on the classes and methods of `AssembliesToScan` | permission keys |
| `IRoleScanner` | `IAuth2RoleMetadata`, `Auth2RoleAttribute` | role names (and `Permissions` of an attribute) |
| `IScopeScanner` | `IAuth2ScopeMetadata`, `Auth2ScopeAttribute` | custom scopes (`auth2.*` and the standard scopes are skipped silently) |
| `IGroupScanner` | `IAuth2GroupMetadata`, `Auth2GroupAttribute` | group names |

- **Endpoints** are always read (Minimal API, controller actions, Razor Pages: anything that is an endpoint). This is why `.WithPermission("orders.read")` makes `orders.read` appear in the catalog without a second list.
- **`AssembliesToScan`** is empty by default. List assemblies there to find attributes on code that is **not** an endpoint (service classes, Blazor components, shared libraries): `o.AssembliesToScan = [typeof(MyMarker).Assembly]`.
- `ScanningPolicy` decides what counts: `Manual` (only your lists), `AssemblyScan` (only what the scanners find), `Both` (the union; manual wins).
- Invalid keys are skipped with a warning that names the key. An invalid key in a rule still compiles, and the rule refuses everybody, so watch the log at start.

```csharp
builder.Services.AddAuth2Client(...)
    .EnablePermissions(o =>
    {
        o.SyncPolicy = PermissionSyncPolicy.Push;
        o.ScanningPolicy = ScanningPolicy.Both;
        o.AssembliesToScan = [typeof(Program).Assembly];
        o.AddFromClass(typeof(OrderPermissions));
    });
```

## Reading the metadata yourself

The interfaces are public so your own code can read a rule without parsing policy names:

| Interface | Gives |
|---|---|
| `IAuth2PermissionMetadata` | `Permissions`, `RequireAll` |
| `IAuth2RoleMetadata` | `Roles`, `RequireAll` |
| `IAuth2ScopeMetadata` | `Scopes` |
| `IAuth2GroupMetadata` | `Groups` |
| `IAuth2PermissionSourceMetadata` | `Source` (`Any`, `RoleOnly`, `UserOnly`) |
| `IAuth2TokenKindMetadata` | `MachineOnly` |
| `IAuth2AuditMetadata` | `Action` |

Example: a page that lists what every endpoint of your API needs (an API map for your own admins):

```csharp
app.MapGet("/api/_rules", (EndpointDataSource endpoints) => endpoints.Endpoints.OfType<RouteEndpoint>().Select(e => new
{
    Route = e.RoutePattern.RawText,
    Permissions = e.Metadata.OfType<IAuth2PermissionMetadata>().SelectMany(m => m.Permissions),
    Roles = e.Metadata.OfType<IAuth2RoleMetadata>().SelectMany(m => m.Roles),
    Scopes = e.Metadata.OfType<IAuth2ScopeMetadata>().SelectMany(m => m.Scopes),
})).WithRole("Admin");
```

Use `OfType<>()` and not `GetMetadata<>()` when a rule can appear more than once (a group plus the endpoint): `GetMetadata` returns only the last one.

## What happens when a rule fails

- No token, or a bad token: `401`.
- Valid token, rule not met: `403`.
- The server cannot be asked for permissions or groups and nothing is cached: `403`, fail closed, with a warning in the log. Rules that need only the token (scope, token kind, role claims) still work.
- Each endpoint with `WithAuth2Audit` / `[Auth2Audit]` raises an `Auth2AuthorizationEvent` to `IAuth2AuditSink` for both outcomes.

## Common problems

| Problem | Cause |
|---|---|
| An action with `[Auth2Permission]` is open to every signed-in user | The attribute has no key. Pass one |
| The catalog has the permission but no display name | The attribute names several keys (its texts are then not used). Name one key per attribute, or put the texts on a constant and use `AddFromClass` |
| The permission is in my code but not in the admin console | `SyncPolicy` is `None` or `Pull`, or the code is not an endpoint and the assembly is not in `AssembliesToScan` |
| `403` for a person who has the role | The role claim is a snapshot of the token: renew the token, or protect with a permission ([access-concepts.md](access-concepts.md#role-in-depth)) |
| A group rule does nothing for a machine token | A machine token is not a person: it has no groups |
| `.WithOnlyRolePermission()` always refuses | The Server is older and does not send the source lists, or there is no permission rule on the endpoint |

## How to implement or extend

- A new kind of rule: write an `IAuthorizationRequirement` and a handler like any ASP.NET Core policy, register it with `AddAuthorization`, and apply it with `.WithAuth2Policy("name")` or `[Authorize(Policy = "name")]`. It combines with the Auth2 rules by AND.
- Your own audit store: implement `IAuth2AuditSink` and register it as a singleton.
- Your own source of catalog items: `AddProvider<T>()` or `AddFactory(...)` ([permission-sync.md](permission-sync.md#catalog-sources-client-side)).

## Related pages

- [Scope, permission, role, group: which is which](access-concepts.md)
- [Current user and current client](client-current-user.md)
- [Authorization: permissions, roles and groups](authorization.md)
- [Concepts](concepts.md)
- [Enums reference](enums-reference.md)
- [Documentation index](index.md)
