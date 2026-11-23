# Decisions (ADR list)

Short records of design choices. Newest at the bottom.

## ADR-001: Central package management
All package versions live in `Directory.Packages.props`.

## ADR-002: xUnit v2 for tests
The spec names xUnit with `xunit.runner.visualstudio`. We use xUnit 2.9.3 with runner 3.x, which is stable with `Microsoft.NET.Test.Sdk`.

## ADR-003: Test package for ASP.NET
`Microsoft.AspNetCore.Mvc.Testing` is pinned to the 10.0.x line to match net10.0 (the 11.x line targets a newer framework).

## ADR-004: Enums serialize as strings
Every Contracts enum has `[JsonConverter(typeof(JsonStringEnumConverter<T>))]`, so JSON is readable and stable.

## ADR-005: Result type
`Result` / `Result<T>` live in Contracts for server-side use. The Client will choose between `Result<T>` and exceptions in phase 11 and record it here.

## ADR-006: Gateway project is a class library
The Gateway is an empty class library with one internal marker type so it can be packed.

## ADR-007: Contracts additions
The spec lists DTO file names but not all fields. Fields for `LoginRequest`, `LoginResponse`, `RegisterRequest`, `RegisterResponse`, `TwoFactorRequest`, `ForgotPasswordRequest`, `ResetPasswordRequest`, `ChangePasswordRequest`, `ApplicationDto`, `CreateApplicationRequest`, `UpdateApplicationRequest`, `UserDto` were chosen by us. We added a few error codes (`application_disabled`, `invalid_token`, and others). Names from the spec were not changed.
