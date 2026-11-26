# A single-page app (Angular, React, Vue) with its own API

## What this is

How to sign people in to a single-page app (SPA) that has its own .NET API, **without writing a login, register or two-factor page in the SPA**. The pages are the Auth2 Server's hosted pages. The API project uses the Client package. The SPA only sends the browser to the API when the person is not signed in.

The examples use Angular and a movie tracker with an API called `MovieTracker.Api`. The same steps work for React or Vue: only the TypeScript part changes.

> **Status of this guide.** The API side uses the real methods of the Client package. The Angular side is standard Angular. The pieces were checked against the code of the package, but this guide has **not been run end to end with a real Angular app** in this repository. Run the checks in [Check that it works](#check-that-it-works) once, in your own setup.

## When to use it

Use it when:

- You have a SPA and a .NET API of your own, and the API can use the Client package.
- You want the hosted pages of the Server (login, register, forgot password, two-factor, social login, passkeys) and no forms in the SPA.

Do not use it when:

- You have a SPA **without** a backend. Every Auth2 application is a confidential client with a secret, and a browser cannot keep a secret ([ADR-030](decisions.md)). Put a small API in front of the SPA and follow this page.
- You want to build the forms in the SPA. That is the Self UI mode ([client-setup.md](client-setup.md#sign-in-for-browser-apps-ui-modes)). Social login, SAML and passkeys do not work inside your own form.

## How it works

The API is the application. The SPA is only its screen.

```
Browser (SPA)                      MovieTracker.Api                    Auth2 Server
     |  GET /api/me (cookie?)           |                                  |
     |--------------------------------->|                                  |
     |  401 (not signed in)             |                                  |
     |<---------------------------------|                                  |
     |  go to  /auth2/login?returnUrl   |                                  |
     |--------------------------------->|  redirect to the Server          |
     |-------------------------------------------------------------------->|  hosted login page
     |<--------------------------------------------------------------------|  code, back to /signin-oidc
     |--------------------------------->|  gets the tokens, sets the cookie|
     |  redirect to the SPA page        |                                  |
     |<---------------------------------|                                  |
     |  GET /api/me (cookie)  -> 200    |                                  |
```

- The **tokens stay in the API** (in the cookie, `SaveTokens`). The SPA never sees a token or a password.
- The SPA proves who the person is with the **cookie** of the API. That is why the cookie must reach the API: this decides which scenario below you choose.
- A request that looks like an API call (an `Accept` header for JSON and not for HTML, `X-Requested-With: XMLHttpRequest`, a bearer token, an `[ApiController]`, or `.AsAuth2Api()`) gets **`401`** or **`403`**, never a redirect. A browser navigation (`Accept: text/html`) is redirected to the login page. This is what lets a SPA use the same API as a normal site.

## Choose a scenario

| | Where the SPA and the API run | The cookie | Extra work |
|---|---|---|---|
| [A](#scenario-a-one-host-the-spa-is-in-wwwroot) | One host. The SPA files are in the `wwwroot` of the API | Same origin | None. The simplest |
| [B](#scenario-b-one-host-through-a-reverse-proxy) | One public host. A reverse proxy sends `/api` to the API and the rest to the SPA | Same origin | Proxy rules, forwarded headers |
| [C](#scenario-c-development-with-the-angular-proxy) | Development: `ng serve` | Same origin through the dev proxy | `proxy.conf.json`, `--ssl` |
| [D](#scenario-d-two-origins-of-the-same-site-no-proxy-no-wwwroot) | `app.example.com` and `api.example.com`, no proxy and no `wwwroot` | Same site, different origin | CORS, a login endpoint with an allow-list |
| [E](#scenario-e-two-different-sites) | `movies.com` and `api.other.com` | Cross-site | Not recommended |

Pick **A** when you can, **D** when the SPA lives on a static host or CDN and you do not want a proxy.

## The API side (all scenarios)

### 1. Register the application on the Server

Create an application for the API (admin console, or the admin API). Register:

| Field | Value |
|---|---|
| Redirect URI | `https://<public host of the API>/signin-oidc` |
| Post-logout redirect URI | `https://<public host of the API>/signout-callback-oidc` |
| Type | `Web` |

"Public host of the API" is the host the **browser** uses to reach the API. In scenarios A, B and C it is the host of the SPA. In scenario D it is `api.example.com`. The SPA's own address is **not** registered on the Server.

### 2. Program.cs

```csharp
using SystemX.Auth2.Client.CurrentUser;   // ICurrentUser
using SystemX.Auth2.Client.Options;        // the option classes
using SystemX.Auth2.Contracts.Enums;       // AccessLoadMode, PermissionSyncPolicy
// AddAuth2Client, UseHostedUi, UseAuth2 and WithPermission live in Microsoft.Extensions.DependencyInjection and Microsoft.AspNetCore.Builder (no using needed)

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuth2Client(builder.Configuration.GetSection("Auth2"))   // ServerUrl, ClientId, ClientSecret, ProjectKey
    .ServerWithMultiProject("movie-tracker")
    .UseHostedUi()                                               // authorization code + PKCE on the Server's pages
    .UseCookieAuthentication(o => o.AccessMode = AccessLoadMode.OnSignIn)
    .EnableCurrentUser()
    .EnablePermissions(o => o.SyncPolicy = PermissionSyncPolicy.Push);

var app = builder.Build();

app.UseAuth2();    // UseAuthentication + UseAuthorization + /auth2/login, /auth2/logout, /auth2/denied

// Who is signed in? The SPA calls this at start. 401 means "show the login".
app.MapGet("/api/me", async (ICurrentUser user, CancellationToken ct) =>
    !user.IsAuthenticated
        ? Results.Unauthorized()
        : Results.Ok(new
        {
            user.UserId, user.UserName, user.Email, user.FullName,
            roles = await user.GetRolesAsync(ct),
            permissions = await user.GetPermissionsAsync(ct),
        })).AllowAnonymous();

// A protected endpoint. Not signed in: 401. Signed in without the permission: 403.
app.MapGet("/api/movies", () => Results.Ok(new[] { "Alien", "Heat" })).WithPermission("movies.read");
app.MapPost("/api/movies", (MovieDto movie) => Results.Created("/api/movies/1", movie)).WithPermission("movies.write");

app.Run();

public sealed record MovieDto(string Title);
```

`appsettings.json` (the secret belongs in an environment variable, for example `Auth2__ClientSecret`):

```json
{ "Auth2": { "ServerUrl": "https://auth.example.com", "ApplicationName": "movie-tracker-api", "ClientId": "movie-tracker-api", "ClientSecret": "<secret>", "ProjectKey": "movie-tracker" } }
```

More on the builder: [client-builder.md](client-builder.md). More on `WithPermission`, roles, groups and two-factor rules: [client-endpoint-metadata.md](client-endpoint-metadata.md).

### 3. The SPA side (all scenarios)

A small environment file tells the SPA where the API is and where to send the browser to sign in. **Scenarios A, B and C** (one origin):

```ts
// src/environments/environment.ts
export const environment = {
  apiBase: '',                         // same origin
  loginUrl: '/auth2/login',            // provided by the Client package
  logoutUrl: '/auth2/logout',
  absoluteReturn: false,               // the Client package accepts a path on its own site only
};
```

**Scenario D** (two origins) is in [its section](#scenario-d-two-origins-of-the-same-site-no-proxy-no-wwwroot).

The service:

```ts
// src/app/auth.service.ts
import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { catchError, map, of, tap } from 'rxjs';
import { environment } from '../environments/environment';

export interface Me { userId: string; userName: string; email?: string; fullName?: string; roles: string[]; permissions: string[]; }

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  readonly me = signal<Me | null>(null);

  /** Asks the API who is signed in. 401 is an answer, not an error. */
  load() {
    return this.http.get<Me>(`${environment.apiBase}/api/me`).pipe(
      tap(m => this.me.set(m)),
      map(() => true),
      catchError(e => { if (e.status === 401) { this.me.set(null); return of(false); } throw e; }));
  }

  has(permission: string) { return this.me()?.permissions.includes(permission) ?? false; }

  /** Leaves the SPA: the hosted login page of the Server is shown, then the browser comes back. */
  login(back = location.pathname + location.search + location.hash) {
    const target = environment.absoluteReturn ? location.origin + back : back;
    location.href = `${environment.loginUrl}?returnUrl=${encodeURIComponent(target)}`;
  }

  logout() { location.href = `${environment.logoutUrl}?returnUrl=${encodeURIComponent(environment.absoluteReturn ? location.origin + '/' : '/')}`; }
}
```

The interceptor sends the cookie, marks the call as an API call (so it never gets a redirect, and a browser cannot forge it from another site without a CORS preflight), and starts the login on a `401`:

```ts
// src/app/auth.interceptor.ts
import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { environment } from '../environments/environment';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(`${environment.apiBase}/api`)) return next(req);
  const auth = inject(AuthService);
  const call = req.clone({ withCredentials: true, setHeaders: { 'X-Requested-With': 'XMLHttpRequest' } });
  return next(call).pipe(catchError(e => {
    if (e.status === 401 && !req.url.endsWith('/api/me')) auth.login();   // the session ended: sign in again
    return throwError(() => e);
  }));
};
```

The route guard and the registration:

```ts
// src/app/auth.guard.ts
import { CanActivateFn } from '@angular/router';
import { inject } from '@angular/core';
import { map, tap } from 'rxjs';
import { AuthService } from './auth.service';

export const signedIn: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.load().pipe(tap(ok => { if (!ok) auth.login(state.url); }), map(ok => ok));
};

// src/app/app.config.ts
export const appConfig: ApplicationConfig = {
  providers: [provideRouter(routes), provideHttpClient(withInterceptors([authInterceptor]))],
};

// src/app/app.routes.ts
export const routes: Routes = [
  { path: 'movies', canActivate: [signedIn], loadComponent: () => import('./movies.component').then(m => m.MoviesComponent) },
  { path: '', pathMatch: 'full', redirectTo: 'movies' },
];
```

Sign out is a button that calls `auth.logout()`. The guard only decides what the **screen** shows. The real check is always in the API (`WithPermission`).

## Scenario A: one host, the SPA is in wwwroot

The API serves the Angular files. Nothing crosses an origin, so there is no CORS and the default cookie works.

1. Build Angular into the `wwwroot` of the API. In `angular.json` (Angular 17 and later):

   ```json
   "outputPath": { "base": "../MovieTracker.Api/wwwroot", "browser": "" }
   ```

2. Serve the files and send every other path to `index.html` so Angular routes work after a reload:

   ```csharp
   app.UseDefaultFiles();
   app.UseStaticFiles();
   app.UseAuth2();
   // ... the /api endpoints ...
   app.MapFallbackToFile("index.html");     // after every other route
   ```

   `/auth2/login`, `/signin-oidc` and `/api/...` are real routes, so they win over the fallback. An unknown `/api/xyz` also returns `index.html`. If you do not want that, use a route pattern such as `"/{*path:regex(^(?!api/|auth2/).*$)}"`.

3. Use the environment values of the SPA section above.

Register `https://<host>/signin-oidc` and `https://<host>/signout-callback-oidc` on the Server.

## Scenario B: one host through a reverse proxy

The browser sees **one** public host. The proxy sends `/api`, `/auth2`, `/signin-oidc` and `/signout-callback-oidc` to the API and everything else to the SPA files (or to a SPA server). This is nginx:

```nginx
server {
  listen 443 ssl;
  server_name movies.example.com;

  location ~ ^/(api|auth2|signin-oidc|signout-callback-oidc) {
    proxy_pass http://127.0.0.1:5000;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
  }

  location / {
    root /var/www/movies;
    try_files $uri /index.html;
  }
}
```

The API must trust the proxy, or it builds the sign-in address with `http` and the wrong host, and the Server refuses the redirect URI:

```csharp
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownProxies.Add(IPAddress.Parse("10.0.0.5"));      // the address of the proxy
});
// ...
app.UseForwardedHeaders();     // before app.UseAuth2()
```

The same shape works with YARP, IIS, Azure Front Door or Traefik. The SPA uses the same-origin environment values. Register `https://movies.example.com/signin-oidc` and `.../signout-callback-oidc` on the Server.

## Scenario C: development with the Angular proxy

`ng serve` runs the SPA on one port and the API on another. To keep **one origin** in development, let the Angular dev server forward the API paths. Use `--ssl`: the cookie of the API is `Secure` and the sign-in address must have the same scheme as the page.

```json
// proxy.conf.json
{
  "/api":                   { "target": "https://localhost:5001", "secure": false },
  "/auth2":                 { "target": "https://localhost:5001", "secure": false },
  "/signin-oidc":           { "target": "https://localhost:5001", "secure": false },
  "/signout-callback-oidc": { "target": "https://localhost:5001", "secure": false }
}
```

```
ng serve --ssl --proxy-config proxy.conf.json
```

The browser is on `https://localhost:4200`, so register `https://localhost:4200/signin-oidc` and `https://localhost:4200/signout-callback-oidc` as extra redirect URIs of the application on the Server (a development one). The dev proxy keeps the `Host` header, so the API builds exactly this address. If the Server answers "invalid redirect_uri", compare the address in its message with the one you registered.

## Scenario D: two origins of the same site, no proxy, no wwwroot

The SPA is on `https://app.example.com` (a static host, a CDN, another server). The API is on `https://api.example.com`. The origins differ but the **site** is the same (`example.com`), so a `SameSite=Lax` cookie of the API is sent with `fetch` calls from the SPA, as long as the call has credentials and the API allows it with CORS. The same is true for two ports of one host in development (`https://localhost:4200` and `https://localhost:5001`; run **both** with `https`: a browser treats `http` and `https` as different sites for cookies).

There are two things to change.

### 1. The API: CORS with credentials, and a login endpoint that returns to the SPA

`/auth2/login?returnUrl=` accepts **a path on the API's own site only**, because it is an open redirect guard. The SPA is another origin, so the API gets its own login and logout endpoints. They return to the SPA only when its origin is in an allow-list.

```csharp
using Microsoft.AspNetCore.Authentication;
using SystemX.Auth2.Client.Authentication;     // Auth2AuthSchemes

var spaOrigins = builder.Configuration.GetSection("Spa:Origins").Get<string[]>() ?? [];   // ["https://app.example.com"]

builder.Services.AddCors(o => o.AddPolicy("spa", p => p
    .WithOrigins(spaOrigins)                  // exact origins. Never "*" with credentials
    .AllowCredentials()
    .WithHeaders("Content-Type", "X-Requested-With")
    .WithMethods("GET", "POST", "PUT", "DELETE")));

// ...
app.UseCors("spa");          // before app.UseAuth2()
app.UseAuth2();

// An address is good only when its origin is in the list. Anything else goes to the first SPA.
string Back(string? url) =>
    Uri.TryCreate(url, UriKind.Absolute, out var u) && spaOrigins.Contains(u.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase)
        ? u.ToString() : spaOrigins[0];

app.MapGet("/spa/login", (string? returnUrl) =>
    Results.Challenge(new AuthenticationProperties { RedirectUri = Back(returnUrl) }, [Auth2AuthSchemes.Oidc])).AllowAnonymous();

app.MapGet("/spa/logout", (string? returnUrl) =>
    Results.SignOut(new AuthenticationProperties { RedirectUri = Back(returnUrl) }, [Auth2AuthSchemes.Cookie, Auth2AuthSchemes.Oidc])).AllowAnonymous();
```

```json
{ "Spa": { "Origins": [ "https://app.example.com" ] } }
```

Leave the cookie options at their defaults (`SameSite=Lax`, `Secure`). Register `https://api.example.com/signin-oidc` and `https://api.example.com/signout-callback-oidc` on the Server. The SPA origin is **not** registered on the Server: the browser never calls the Server from the SPA, it only follows redirects. So the `AllowedCorsOrigins` of the Server are not needed either.

### 2. The SPA: absolute addresses

```ts
// src/environments/environment.ts
export const environment = {
  apiBase: 'https://api.example.com',
  loginUrl: 'https://api.example.com/spa/login',
  logoutUrl: 'https://api.example.com/spa/logout',
  absoluteReturn: true,       // the return address is the full address of the SPA page
};
```

The service, the interceptor and the guard of the section above work as they are. The interceptor already sets `withCredentials`, and `X-Requested-With` makes every call an API call. A custom header also forces a CORS preflight, so a page of another origin cannot post to the API with the cookie: with exact origins in the CORS policy, this is the protection against CSRF (cross-site request forgery) for this layout.

Check:

- The cookie is named `.SystemX.Auth2.Client` and belongs to the **API host**. It is not visible to the SPA code (it is `HttpOnly`).
- A preflight `OPTIONS` call to `/api/me` answers `Access-Control-Allow-Origin: https://app.example.com` and `Access-Control-Allow-Credentials: true`.

## Scenario E: two different sites

The SPA is on `movies.com` and the API on `api.other.com`. The cookie of the API is now a **third-party** cookie for the SPA. To send it you would need `o.SameSite = SameSiteMode.None` (with `Secure`) in `UseCookieAuthentication` and the CORS policy of scenario D. Safari and Firefox block such cookies by default, and Chrome limits them. It may work in one browser today and stop in the next.

**Do not build on it.** Choose one of these instead:

- Give the API a name under the SPA's site (`api.movies.com`): that is scenario D.
- Put a reverse proxy in front of both: scenario B.

## Two-factor for one action (step-up)

An endpoint with `.WithTotp()` (or `[Auth2Totp]`) needs a fresh authenticator code. The hosted UI cannot show the Server page inside an AJAX call, so for an **API call** the answer is `403` with the code `totp_required` (or `totp_setup_required` when the person has not turned two-factor on). The SPA must then send the **browser** to a route that asks for the code and comes back:

```csharp
// A browser navigation to this route is sent to the Server's /Account/VerifyTotp, and returns here when the code is good.
app.MapGet("/spa/step-up", (string? returnUrl) => Results.Redirect(Back(returnUrl))).WithTotp();   // Back(...) is in scenario D. In A to C use a path
```

```ts
// in the interceptor, next to the 401 branch
if (e.status === 403 && ['totp_required', 'totp_setup_required'].includes(e.error?.code)) {
  location.href = `${environment.apiBase}/spa/step-up?returnUrl=${encodeURIComponent(location.href)}`;
}
```

The person leaves the SPA, types the code on the Server page, and comes back to the page they were on. How long a code counts as fresh is the one setting `TwoFactor.StepUpMinutes` ([two-factor.md](two-factor.md)). For a `totp_setup_required` answer the Server page tells the person to turn two-factor on first. This route is described from the code of the package and the hosted step-up flow ([client-endpoint-metadata.md](client-endpoint-metadata.md)). Test it once in your setup.

## Security checklist

- Always `https`. The cookie is `Secure` by default. `SecurePolicy = SameAsRequest` is for development only.
- CORS: list exact origins. Never `AllowAnyOrigin` together with `AllowCredentials`.
- Keep the `X-Requested-With` header on every call of the SPA to the API.
- A login or logout route that returns to another origin must check an allow-list (scenario D does). Never copy `returnUrl` into a redirect as it is.
- The SPA must not keep a token or a password. There is none to keep.
- Check permissions in the API (`WithPermission`, `[Auth2Permission]`). Hide a button in the SPA only as a convenience.
- Behind a proxy, set forwarded headers and trust only the proxy ([security.md](security.md)).
- A cookie has a 4 KB limit. Keep `AccessMode` at `OnSignIn` or `Lazy` so roles are not copied into the cookie.

## Check that it works

1. Open the SPA while signed out. The guard calls `/api/me`, gets `401`, and the browser goes to the **Server's** login page.
2. Sign in. The browser returns to the SPA page you wanted, and `/api/me` answers `200` with your roles.
3. Reload the page. You stay signed in (the cookie).
4. Call a protected endpoint without the permission. The answer is `403`, and the browser does not move.
5. Press sign out. The cookie goes, and the next load of a guarded route sends you to the login page again.

## Common problems

| Symptom | Cause | Fix |
|---|---|---|
| The Server says `invalid redirect_uri` | The address the API sent is not registered. Behind a proxy it is usually `http` or the internal host | Register the exact public address of the API. Turn on forwarded headers (scenario B) |
| `/api/me` always `401` after a good login | The cookie does not reach the API: `withCredentials` is missing, the origin is not in CORS, or the cookie is `Secure` on an `http` page | Use the interceptor, list the origin, use `https` everywhere |
| Browser console: CORS error with credentials | `*` or a missing `AllowCredentials`, or the `X-Requested-With` header is not allowed | Exact origins, `AllowCredentials`, `WithHeaders("Content-Type", "X-Requested-With")` |
| A call returns the **HTML of the login page** with `200` | The call is not seen as an API call, so the cookie handler redirected it | Send `X-Requested-With: XMLHttpRequest` (the interceptor), or mark the endpoint with `.AsAuth2Api()` |
| Login works but the SPA lands on `/` of the API | `returnUrl` was not an accepted path or origin | Scenarios A to C: a path. Scenario D: an origin that is in `Spa:Origins` |
| It works in Chrome and not in Safari | A cross-site cookie (scenario E) | Use scenario D or B |
| Angular routes give 404 after reload | No fallback to `index.html` | `MapFallbackToFile("index.html")` or `try_files $uri /index.html` |

## How to implement or extend

- The routes `/auth2/login` and `/auth2/logout` are mapped by `UseAuth2()` ([client-setup.md](client-setup.md#sign-in-for-browser-apps-ui-modes)). Their `returnUrl` rule is `Auth2Http.SafeReturnUrl`: a path only.
- To change what the SPA can see about the person, change the `/api/me` endpoint. `ICurrentUser` has `HasPermissionAsync`, `HasRoleAsync`, `HasGroupAsync`, `IsEmailAddressConfirmed` and more ([client-current-user.md](client-current-user.md)).
- To call the Server's admin routes from the API (create a user, give a role), use `IAuth2WebClientService.Admin` ([client-api-service.md](client-api-service.md)).

## Related pages

- [Client setup](client-setup.md)
- [Client builder](client-builder.md)
- [Client endpoint metadata](client-endpoint-metadata.md)
- [Client authorization](client-authorization.md)
- [Two-factor](two-factor.md)
- [Security](security.md)
- [Decisions](decisions.md)
- [Documentation index](index.md)
