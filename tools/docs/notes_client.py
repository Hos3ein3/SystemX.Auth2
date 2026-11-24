# Hand-written explanations of the Client package options. Keys are "<options class>.<member>". Examples are C# lines inside the builder method of that class.
N = {}
def n(key, what, when, why, ex=None): N[key] = dict(what=what, when=when, why=why, ex=ex)

C = 'Auth2ClientOptions.'
n(C+'ServerUrl', 'The address of the Auth2 Server (absolute `http` or `https`). **Required.** Every call and the discovery address are built from it.', 'Per environment (development, test, production).', 'The client has no other way to find the Server.', 'https://auth.example.com')
n(C+'ApplicationName', 'The name of this application **inside its project** on the Server. The handshake compares it with the Server\'s name and answers `Rejected` when they differ. **Required.**', 'Once, when you register the application.', 'It catches a wrong client id or a copied configuration before any user is affected.', 'papertrade-api')
n(C+'ClientId', 'The OAuth client id of the application. **Required.**', 'Once.', 'It identifies the application to the Server and is the audience of the tokens it checks.', 'papertrade-api')
n(C+'ClientSecret', 'The OAuth client secret. **Required.** Never logged. Keep it in a secret store.', 'When you rotate the secret.', 'It proves that the application is the real one (handshake, sync, machine tokens, code flow).', '=builder.Configuration["Auth2:Secret"]!')
n(C+'ProjectKey', 'The project of this application on a **multi-project** server; set it with `ServerWithMultiProject`. Lowercase letters, digits, `-`, `_`; 3 to 64 characters; not `_global`. Not a secret.', 'Multi-project servers only.', 'The Server checks that your client belongs to that project, which catches configuration mix-ups.', 'papertrade')
n(C+'OnServerUnavailable', '`Continue` (start anyway and retry in the background) or `Throw` (stop the application) when the Server cannot be reached at start. See [enums-reference.md](enums-reference.md#startupfailurebehavior).', 'Use `Throw` when the application is useless without the Server.', 'A worker that cannot work should fail fast; a website should stay up.', '=StartupFailureBehavior.Throw')
n(C+'OnProjectNotFound', '`Throw` or `Continue` when the Server answers `NotFound` or `Rejected` (wrong secret, name, key or mode). Never retried.', 'Use `Throw` in production, `Continue` in development.', 'It is a configuration error: failing at start is the clearest signal.', '=StartupFailureBehavior.Throw')
n(C+'HandshakeOnStartup', 'Make the handshake when the host starts.', 'Set `false` to choose the moment yourself and call `IAuth2ApiClientService.HandshakeAsync()`.', 'The sync waits for a good handshake, so this also decides when sync starts.', False)
n(C+'HandshakeTimeout', 'Time limit of one handshake attempt.', 'Slow networks.', 'A hung Server must not block start-up for long.', '=TimeSpan.FromSeconds(10)')
n(C+'HandshakeMaxRetries', 'Background retries after an unreachable Server (only with `OnServerUnavailable = Continue`).', 'Raise it when the Server is often slower to start than your application.', 'Lets the application connect when the Server comes up later.', 10)
n(C+'HandshakeInitialDelay', 'Wait before the first retry. It doubles each time (2, 4, 8 ... seconds).', 'Rarely.', 'Backoff avoids hammering a Server that is starting.', '=TimeSpan.FromSeconds(1)')
n(C+'HandshakeMaxDelay', 'The longest wait between two retries (not shorter than the initial delay).', 'Rarely.', 'Caps the backoff.', '=TimeSpan.FromSeconds(30)')
n(C+'ClientVersion', 'Text sent in the handshake. Default: the version of the package.', 'To send your own application version.', 'Shows in Server logs which client version connected.', '1.4.2')
n(C+'RequestTimeout', 'Time limit of one call to the Server that is not the handshake.', 'Slow links.', 'Your request must not hang because the Server does.', '=TimeSpan.FromSeconds(15)')
n(C+'CatalogRefreshInterval', 'How often permissions, roles, scopes and groups are read again when their policy is `Pull` or `Both`.', 'Shorter if admins change the lists often.', 'The cached lists are used between reads.', '=TimeSpan.FromMinutes(2)')
n(C+'SyncMaxRetries', 'How many times a failed sync is tried again (waits grow like the handshake retries).', 'Rarely.', 'A short network problem should not lose the push.', 8)
n(C+'FailOnSyncError', 'When `true`, a sync that still fails after the retries stops the application (`Auth2StartupException`).', 'In a deployment check where the Server must always have your list.', 'Off by default: a sync problem is logged and the application keeps serving.', True)

K = 'Auth2CookieOptions.'
n(K+'CookieName', 'The name of the sign-in cookie of your application.', 'Two applications on one host need different names.', 'Same-named cookies overwrite each other.', 'papertrade.auth')
n(K+'ExpireTimeSpan', 'How long the cookie lasts. `null` (default) takes the Server\'s `Sessions.CookieLifetimeMinutes` read at the handshake, and 8 hours when the Server has none.', 'When this application must differ from the Server\'s value.', 'The Server is the usual owner of the session length.', '=TimeSpan.FromHours(2)')
n(K+'SlidingExpiration', 'Renews the cookie while the person is active.', 'Off for a fixed session length.', 'Stops a session from ending in the middle of work.', False)
n(K+'SameSite', 'The `SameSite` mode. It must be `Lax` or `None` for the sign-in redirect to work.', 'Keep `Lax`.', '`Strict` drops the cookie on the redirect back from the Server.', '=SameSiteMode.Lax')
n(K+'SecurePolicy', 'When the cookie is sent: `Always` (https only), `SameAsRequest`, `None`.', '`SameAsRequest` only in local development over http.', 'A cookie sent over http can be stolen.', '=CookieSecurePolicy.SameAsRequest')
n(K+'LoginPath', 'Where browsers go to sign in. The default `/auth2/login` starts the hosted sign-in; with the Self UI set your own page.', 'Self UI.', 'The cookie handler redirects here when someone is not signed in.', '/Account/Login')
n(K+'AccessDeniedPath', 'Where a signed-in person goes when they may not do something.', 'To show your own "no access" page.', 'The default `/auth2/denied` is minimal.', '/Account/AccessDenied')
n(K+'SaveTokens', 'Keeps the access, refresh and identity tokens in the cookie. Needed so that the Client can call the Server for the person\'s access data later.', 'Turn off only if you never read roles or permissions from the Server.', 'Without tokens `ICurrentUser` cannot ask `/me`.', False)
n(K+'AccessMode', 'When the person\'s roles, permissions and groups are loaded: `Lazy`, `OnSignIn`, `RolesInCookie`, `None`. See [enums-reference.md](enums-reference.md#accessloadmode).', '`OnSignIn` for long sessions, `RolesInCookie` for small role sets.', 'The access token in the cookie is not renewed, so a late read can fail.', '=AccessLoadMode.OnSignIn')

H = 'Auth2HostedUiOptions.'
n(H+'Scopes', 'The scopes the hosted sign-in asks for. Default `openid profile email offline_access auth2.me`.', 'Add the scopes of your APIs.', '`profile` and `email` fill the name and email; `offline_access` gives a refresh token; `auth2.me` lets the Client read `/me`.', ['openid', 'profile', 'email', 'offline_access', 'auth2.me', 'reports.read'])
n(H+'CallbackPath', 'Where the Server sends the browser after sign-in. **Register this address as a redirect URI of the application on the Server.**', 'Only if `/signin-oidc` is taken.', 'The Server accepts only registered redirect URIs.', '/signin-oidc')
n(H+'SignedOutCallbackPath', 'Where the Server sends the browser after sign-out. Register it as a post-logout redirect URI.', 'Rarely.', 'Same reason.', '/signout-callback-oidc')
n(H+'RequireHttpsMetadata', 'Insists on https for the discovery address. `null` means true, except when `ServerUrl` is http.', 'Leave `null`.', 'Discovery over http can be tampered with.', True)
n(H+'BackchannelHandler', 'The `HttpMessageHandler` for the calls to the Server (discovery, token). Mostly for tests.', 'Tests or special networks.', 'Lets you plug in a fake handler.', None)

A = 'Auth2ApiOptions.'
n(A+'Authority', 'The address used to find the Server\'s signing keys (default `ServerUrl`). It must be the issuer the Server writes in its tokens.', 'When the Server is reached by another address than its public issuer (inside a container network).', 'A token is only valid for its issuer.', 'https://auth.example.com')
n(A+'Audience', 'The audience a token must have (default `ClientId`).', 'When your API has a different audience.', 'Tokens of other applications are refused.', 'papertrade-api')
n(A+'RequireHttpsMetadata', 'Insists on https for the key address. `null` means true, except when the Server address is http.', 'Leave `null`.', 'Keys over http can be replaced.', True)
n(A+'ClockSkew', 'Time difference allowed when checking the lifetime of a token (default 30 s).', 'Servers with bad clocks.', 'A small skew avoids false expiry; a large one accepts expired tokens.', '=TimeSpan.FromSeconds(60)')
n(A+'KeyRefreshInterval', 'The least time between two reads of the key set after an unknown key id (default 30 s).', 'Rarely.', 'After a key rotation the first token with an unknown key makes the Client read the keys again; this limits how often.', '=TimeSpan.FromSeconds(10)')
n(A+'BackchannelHandler', 'The `HttpMessageHandler` for discovery and key calls. Mostly for tests.', 'Tests.', 'Lets you plug in a fake handler.', None)

U = 'Auth2CurrentUserOptions.'
n(U+'Source', '`TokenAndServer` (roles, permissions and groups from `/me`, cached) or `TokenOnly` (roles from the token, no calls). See [enums-reference.md](enums-reference.md#currentusersource).', '`TokenOnly` for a small API that only needs roles.', 'Fewer calls versus complete data.', '=CurrentUserSource.TokenOnly')
n(U+'CacheTtl', 'How long access data is kept before it is read again (default 10 minutes). A newer `pv` claim also forces a read.', 'Shorter if role changes must show sooner.', 'Trade freshness against calls ([client-current-user.md](client-current-user.md)).', '=TimeSpan.FromMinutes(2)')

M = 'Auth2MachineOptions.'
n(M+'Scopes', 'The scopes used for a machine token when a call names none.', 'A worker that always needs the same scopes.', 'Saves repeating the scopes at every call ([machine-to-machine.md](machine-to-machine.md)).', ['reports.read'])

P = 'Auth2PermissionOptions.'
n(P+'DuplicatePolicy', '`Fail` (default: the application does not start when two sources define the same key differently) or `FirstWins`. See [enums-reference.md](enums-reference.md#duplicatepolicy).', '`FirstWins` when several sources overlap on purpose.', 'A silent clash would hide a wrong definition.', '=DuplicatePolicy.FirstWins')
n(P+'HasLateSources', '`true` when a provider or factory was added (they are called when the host starts, not when you configure). Read only.', 'Never set it.', 'Tells the start-up check to resolve the late sources.', None)
n(P+'SyncPolicy', 'How the list moves between the application and the Server: `None`, `Push`, `Pull`, `Both`. The same members exist on the role, scope and group options. See [enums-reference.md](enums-reference.md#permissionsyncpolicy).', '`Push` for the normal case.', 'Your code is usually the source of truth.', '=PermissionSyncPolicy.Push')
n(P+'ScanningPolicy', 'Where entries come from: `Manual`, `AssemblyScan`, `Both`. See [enums-reference.md](enums-reference.md#scanningpolicy).', '`Both` to scan attributes and also list by hand.', 'Attributes keep the list next to the code that needs it.', '=ScanningPolicy.Both')
n(P+'PermissionList', 'The entries you list by hand (`RoleList`, `ScopeList`, `GroupList` on the other options). The same as `Add(...)` for each.', 'To give names and descriptions that scanning cannot know.', 'Manual entries win over scanned ones.', '=[new("orders.read", "Read Orders", "Allows viewing orders", "Orders")]')
n(P+'AssembliesToScan', 'Assemblies searched for the attributes on types and methods (Blazor components, services). Endpoints are scanned without it.', 'When attributes sit outside endpoints.', 'Scanning every assembly would be slow and surprising.', '=[typeof(Program).Assembly]')
