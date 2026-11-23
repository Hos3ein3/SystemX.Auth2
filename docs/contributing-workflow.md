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
- No UI work in the Server for now: a page is a PageModel plus an empty `.cshtml`.

## Docs style

Use simple English (B1–B2). Short sentences. Active voice. Explain each acronym once.
Each page has: What this is, When to use it, Step by step, Example, Common problems, Related pages.

## Related pages

- [testing.md](testing.md)
