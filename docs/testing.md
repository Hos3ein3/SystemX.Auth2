# Testing

## What this is

How to run the tests.

## Step by step

```bash
dotnet test                                   # everything
dotnet test tests/SystemX.Auth2.Contracts.Tests   # one project
dotnet test --collect:"XPlat Code Coverage"   # with coverage (coverlet)
```

## Test projects

| Project | Kind | Needs Docker |
|---|---|---|
| Contracts.Tests | Unit | No |
| Server.Tests | Unit | No |
| Server.IntegrationTests | Integration (PostgreSQL, Redis, `WebApplicationFactory`) | Yes |
| Client.Tests | Unit | No |
| Client.IntegrationTests | Client against an in-memory Server | Yes |

## Rules

- Unit tests use no network and no database.
- Tests are deterministic: inject a clock, never use `Thread.Sleep`.
- Target: 80% line coverage for Application, Client and Contracts. Do not write empty tests to reach it.
- Stack: xUnit, Shouldly, NSubstitute, Testcontainers.

## Common problems

- *Integration tests cannot start:* start Docker first.

## Related pages

- [contributing-workflow.md](contributing-workflow.md)
