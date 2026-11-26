# Contributing workflow

## What this is

The rules for every change in this repository.

## The rule: code, tests, pass, docs

For each small piece of work:

1. Write the code.
2. Write unit tests. Add integration tests if the piece uses the database, Redis or HTTP.
3. Run the tests. Fix them until all pass.
4. Write or update the page in `/docs`.
5. Only then start the next piece.

## Editing pages without a rebuild

In the **Development** environment the Server compiles the Razor pages (`.cshtml`) again when the file changes (`AddRazorRuntimeCompilation`, package `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation`): change a page, refresh the browser. It is on when `ASPNETCORE_ENVIRONMENT` is `Development`; the setting `Auth2:RazorRuntimeCompilation` overrides it (`true` forces it on, `false` turns it off even in development). It is **not** for production: it keeps the compiler and file watchers running, and the pages of the build are used otherwise. Notes:

- Only the `.cshtml` files are watched. A change of a `.cs` file (a page model) still needs a build (`dotnet watch` does that for you).
- The CSS is built from the classes found in the pages (Tailwind). A **new** Tailwind class in a page needs `npm run build:css` (in `src/SystemX.Auth2.Server`), or keep `npm run watch:css` running, before the class has a style.
- The tests run without it (the environment is `Testing`).

## Step by step

1. Create a branch.
2. Follow the rule above.
3. Update `CHANGELOG.md`.
4. Update `docs/decisions.md` if you made a design choice.
5. Open a pull request.

## PR checklist

- [ ] Build has zero warnings.
- [ ] Tests pass.
- [ ] Public API has XML comments.
- [ ] No secret is logged (passwords, OTP codes, tokens, client secrets, recovery codes, TOTP keys).
- [ ] Docs are updated, in simple English.
- [ ] `CHANGELOG.md` is updated.

## Coding style

- C# latest, nullable on, file-scoped namespaces.
- English for code, comments, logs and docs.
- Test names: `Method_Scenario_ExpectedResult`.
- No `TODO` without a note in [roadmap.md](roadmap.md).
- **Result types, by layer.** Inside the **Server** a method that can fail on a rule answers with `OperationResult` (`Succeeded`, `ErrorCode`, the Identity errors; `ApiResults.From` turns it into the HTTP answer). In the **Client package and the contracts** it is `Result` / `Result<T>` (`IsSuccess`, `Error` with the code and the message of the Server). Do not mix them in one layer: the Server does not return `Result`, the Client does not know `OperationResult`. Failures that are not about a rule (a missing project, a name that is taken in a service that creates something) are exceptions, as `ProjectNotFoundException` and `ProjectNameTakenException`.
- **Managers.** Ask for `UserManager<AppUser>` and `RoleManager<AppRole>` (the Identity types): the Server registers `Auth2UserManager` and `Auth2RoleManager` as their implementations ([identity-stores.md](identity-stores.md)).
- No UI work in the Server for now: a page is a PageModel plus an empty `.cshtml`.

## Docs style

Use simple English (B1–B2). Short sentences. Active voice. Explain each acronym once.
Each page has: What this is, When to use it, Step by step, Example, Common problems, Related pages.

## Related pages

- [testing.md](testing.md)
- [Decisions (ADR list)](decisions.md)
- [Architecture](architecture.md)
- [The key ring and its snapshot](key-ring.md)
- [Email and SMS](email-and-sms.md)
- [Database tables](database-tables.md)
- [External identity: social login, OIDC, brokering, SAML, LDAP and passkeys](external-identity.md)
- [Reference generator](../tools/docs/README.md)
- [Documentation index](index.md)
