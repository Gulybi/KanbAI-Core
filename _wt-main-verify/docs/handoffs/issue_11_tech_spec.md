# Technical Specification: Issue #11 - Setup Dependency Injection (DI) Extension Methods

## Overview

Refactor `Program.cs` by extracting all `IServiceCollection` service registrations into a dedicated static extension class. This produces a clean, scannable application entry point and organises registrations by concern, improving maintainability as the project grows.

No new domain entities, database changes, API endpoints, or NuGet packages are introduced.

---

## Architecture & Design Decisions

### Decision 1: Single Extension Class with Multiple Methods (vs. One Method or Multiple Classes)

| Option | Pros | Cons |
|---|---|---|
| **Single class, multiple methods (chosen)** | Groups all DI registrations in one discoverable location; each method addresses a single concern; methods are independently testable; `Program.cs` reads like a table of contents. | A single file grows over time — but easy to split later. |
| Single method (`AddApplicationServices`) | Simplest change. | Becomes a monolith as services grow; harder to test individual concerns. |
| Multiple classes (one per concern) | Maximum separation. | Over-engineered for the current size of the project; more files to navigate. |

**Decision:** Create `ServiceCollectionExtensions` with four focused methods (`AddPersistence`, `AddApiInfrastructure`, `AddAuthServices`, `AddCorsPolicy`). Each method groups related registrations and returns `IServiceCollection` to support fluent chaining.

### Decision 2: Extension Method Parameter — `IConfiguration` vs `WebApplicationBuilder`

| Option | Pros | Cons |
|---|---|---|
| **`IConfiguration` parameter (chosen)** | Standard .NET convention; extension targets `IServiceCollection` which is framework-agnostic and easy to unit test with a bare `ServiceCollection`. | Caller passes `builder.Configuration` explicitly. |
| `WebApplicationBuilder` parameter | Gives access to `Services` and `Configuration` in one object. | Non-standard; tightly couples extensions to the host builder; harder to test without a real builder. |

**Decision:** Extension methods target `IServiceCollection`. Methods that need configuration values accept `IConfiguration` as a second parameter. This is the idiomatic .NET pattern (e.g., `AddDbContext`, `AddAuthentication`).

### Decision 3: CORS Policy Name Constant

The string `"AllowAngularFrontend"` currently appears in both the service registration (`AddCors`) and the middleware call (`UseCors`). After refactoring, these live in different files.

**Decision:** Expose a `public const string CorsPolicyName` on `ServiceCollectionExtensions` so both the extension method and `Program.cs` pipeline reference the same constant. This eliminates a magic string and prevents silent drift.

### Decision 4: Scope — Service Registrations Only (No Middleware Pipeline Extraction)

The acceptance criteria focus on moving **registration logic** out of `Program.cs`. The middleware pipeline (`Use*` calls) is kept in `Program.cs` because:
- The pipeline order is a critical application concern that benefits from being visible in the entry point.
- Hiding it behind an extension method can obscure ordering bugs.
- The current pipeline is short (3 calls) and does not need extraction.

A `WebApplicationExtensions` class for middleware can be introduced in a future issue if the pipeline grows.

---

## Detailed Design

### New File: `Extensions/ServiceCollectionExtensions.cs`

**Namespace:** `KanbAI_Core.Extensions`

```csharp
namespace KanbAI_Core.Extensions;

public static class ServiceCollectionExtensions
{
    public const string CorsPolicyName = "AllowAngularFrontend";

    /// Registers ApplicationDbContext with SQL Server using the "DefaultConnection" connection string.
    public static IServiceCollection AddPersistence(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));
        return services;
    }

    /// Registers OpenAPI document generation, global exception handler, and ProblemDetails.
    public static IServiceCollection AddApiInfrastructure(this IServiceCollection services)
    {
        services.AddOpenApi();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        return services;
    }

    /// Registers Negotiate authentication and authorization with a fallback policy.
    public static IServiceCollection AddAuthServices(this IServiceCollection services)
    {
        services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
            .AddNegotiate();
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = options.DefaultPolicy;
        });
        return services;
    }

    /// Registers CORS with the "AllowAngularFrontend" policy using origins from configuration.
    public static IServiceCollection AddCorsPolicy(
        this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                policy.WithOrigins(allowedOrigins)
                      .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                      .WithHeaders("Content-Type", "Authorization");
            });
        });
        return services;
    }
}
```

**Required `using` directives for the extension class:**

```csharp
using KanbAI_Core.Data;
using KanbAI_Core.Middleware;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.EntityFrameworkCore;
```

### Modified File: `Program.cs`

After refactoring, `Program.cs` should read:

```csharp
using Scalar.AspNetCore;
using KanbAI_Core.DTOs;
using KanbAI_Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddPersistence(builder.Configuration)
    .AddApiInfrastructure()
    .AddAuthServices()
    .AddCorsPolicy(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(ServiceCollectionExtensions.CorsPolicyName);

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
    {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
            .ToArray();
        return ApiResponse<WeatherForecast[]>.Ok(forecast);
    })
    .WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public partial class Program { }
```

**Key changes in `Program.cs`:**
- Removed `using` directives that are now only needed inside the extension class (`Microsoft.AspNetCore.Authentication.Negotiate`, `Microsoft.EntityFrameworkCore`, `KanbAI_Core.Data`, `KanbAI_Core.Middleware`).
- Added `using KanbAI_Core.Extensions`.
- Replaced all inline `builder.Services.*` registrations with four chained extension method calls.
- Replaced the magic string `"AllowAngularFrontend"` in `UseCors()` with `ServiceCollectionExtensions.CorsPolicyName`.

---

## Implementation Steps for @agent_developer

Follow these steps in order, referencing `@rule_code_standards`:

### Step 1 — Create the Extensions Directory and Extension Class

1. Create the directory `KanbAI-Core/KanbAI-Core/KanbAI-Core/Extensions/`.
2. Create the file `KanbAI-Core/KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs` with the contents defined in the **Detailed Design** section above.
3. Ensure the namespace is `KanbAI_Core.Extensions` (file-scoped namespace).
4. Include the four extension methods: `AddPersistence`, `AddApiInfrastructure`, `AddAuthServices`, `AddCorsPolicy`.
5. Include the `public const string CorsPolicyName = "AllowAngularFrontend"` constant.

### Step 2 — Update `Program.cs`

1. Replace the inline service registrations with chained calls to the new extension methods.
2. Replace the `"AllowAngularFrontend"` string literal in `UseCors()` with `ServiceCollectionExtensions.CorsPolicyName`.
3. Update the `using` directives: remove those no longer directly needed in `Program.cs`, add `using KanbAI_Core.Extensions`.
4. **Do NOT** change the middleware pipeline order or endpoint mappings.
5. **Do NOT** remove `public partial class Program { }` — it is required for integration test support.

### Step 3 — Verify Build and Existing Tests

1. Run `dotnet build` on the solution — expect 0 errors, 0 warnings.
2. Run `dotnet test` — all 81 existing tests must pass without modification.

---

## Test Infrastructure Setup for Integration Tests

### `CreateClient` Helper Method

Tests that verify the extension methods via `WebApplicationFactory<Program>` should use the standard `CreateClient` pattern from `@rule_integration_testing`:

```csharp
private HttpClient CreateClient(string environment = "Development")
{
    return _factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment(environment);
        builder.ConfigureTestServices(services =>
        {
            var authConfigs = services
                .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                .ToList();
            foreach (var descriptor in authConfigs)
                services.Remove(descriptor);

            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
            services.AddAuthorization(options => options.FallbackPolicy = null);
        });
    }).CreateClient();
}
```

### `TestAuthHandler`

Same no-op handler as established in previous test classes:

```csharp
private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
```

### Known Middleware/Pipeline Caveats

| Caveat | Impact | Mitigation |
|---|---|---|
| **Negotiate auth in TestServer** | `NegotiateHandler` throws `NotSupportedException` on every request. | Auth is stripped and replaced with `TestAuthHandler` in `CreateClient` (per `@rule_integration_testing` §1). |
| **Extension methods are additive** | Calling an extension method twice (e.g., `AddPersistence` in both the extension and a test override) can result in duplicate registrations. | Unit tests should create a fresh `ServiceCollection` per test. Integration tests should rely on `ConfigureTestServices` to override, not re-register. |
| **`IConfiguration` dependency** | `AddPersistence` and `AddCorsPolicy` require `IConfiguration`. Unit tests must build a test configuration (e.g., `ConfigurationBuilder` with in-memory collection). | See test examples below. |

---

## Edge Cases for @agent_tester_qa

The QA agent should create unit tests for the extension class and one integration regression test to verify the full pipeline still works after refactoring.

### Test Class 1: `ServiceCollectionExtensionTests` (Unit Tests)

**File:** `KanbAI-Core/KanbAI-Core.Tests/Extensions/ServiceCollectionExtensionTests.cs`
**Namespace:** `KanbAI_Core.Tests.Extensions`

These are **unit tests** that create a bare `ServiceCollection`, call a single extension method, build the provider, and assert on registrations. Use `ConfigurationBuilder` with in-memory dictionaries for methods that require `IConfiguration`.

| # | Test Name | Assertion |
|---|---|---|
| 1 | `AddPersistence_RegistersApplicationDbContext` | After calling `AddPersistence`, `ServiceProvider.GetService<ApplicationDbContext>()` is not null (use in-memory DB provider in the test config to avoid SQL Server dependency). |
| 2 | `AddPersistence_RegistersDbContextOptions` | After calling `AddPersistence`, `ServiceProvider.GetService<DbContextOptions<ApplicationDbContext>>()` is not null. |
| 3 | `AddApiInfrastructure_RegistersExceptionHandler` | After calling `AddApiInfrastructure`, the service collection contains a descriptor with `ServiceType == typeof(IExceptionHandler)` and `ImplementationType == typeof(GlobalExceptionHandler)`. |
| 4 | `AddApiInfrastructure_RegistersProblemDetails` | After calling `AddApiInfrastructure`, the service collection contains a registration that includes `IProblemDetailsService`. |
| 5 | `AddAuthServices_RegistersAuthentication` | After calling `AddAuthServices`, `ServiceProvider.GetService<IAuthenticationSchemeProvider>()` is not null. |
| 6 | `AddAuthServices_RegistersAuthorization` | After calling `AddAuthServices`, `ServiceProvider.GetService<IAuthorizationPolicyProvider>()` is not null. |
| 7 | `AddCorsPolicy_RegistersCorsOptions` | After calling `AddCorsPolicy`, `ServiceProvider.GetRequiredService<IOptions<CorsOptions>>()` is not null and the policy named `"AllowAngularFrontend"` can be retrieved. |
| 8 | `AddCorsPolicy_WithNoConfiguredOrigins_RegistersEmptyPolicy` | Call `AddCorsPolicy` with a configuration that has no `Cors:AllowedOrigins` key. Verify the policy is created (no exception) and returns a policy object. |
| 9 | `AllExtensionMethods_ReturnServiceCollection_ForFluentChaining` | Verify that each method returns the same `IServiceCollection` instance that was passed in. |
| 10 | `CorsPolicyName_HasExpectedValue` | `ServiceCollectionExtensions.CorsPolicyName` equals `"AllowAngularFrontend"`. |

### Test Class 2: `DependencyInjectionIntegrationTests` (Integration Test)

**File:** `KanbAI-Core/KanbAI-Core.Tests/Extensions/DependencyInjectionIntegrationTests.cs`
**Namespace:** `KanbAI_Core.Tests.Extensions`

One integration test using `WebApplicationFactory<Program>` to verify the refactored `Program.cs` still resolves all services and the pipeline works end-to-end.

| # | Test Name | Assertion |
|---|---|---|
| 1 | `Application_WithRefactoredDI_StartsSuccessfully` | `CreateClient()` does not throw; a `GET /weatherforecast` request returns `200 OK`. |
| 2 | `Application_WithRefactoredDI_ResolvesApplicationDbContext` | Using the test server's `Services`, resolve `ApplicationDbContext` within a scope — it should not be null. |

---

## Files Changed Summary

### New Files
- `KanbAI-Core/KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs`

### Modified Files
- `KanbAI-Core/KanbAI-Core/KanbAI-Core/Program.cs` — replaced inline registrations with extension method calls

### New Test Files (for @agent_tester_qa)
- `KanbAI-Core/KanbAI-Core.Tests/Extensions/ServiceCollectionExtensionTests.cs`
- `KanbAI-Core/KanbAI-Core.Tests/Extensions/DependencyInjectionIntegrationTests.cs`

---

## Development Status

**Completed by:** @agent_developer
**Date:** 2026-04-07

### Files Created
- `KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs` — Static extension class with four methods (`AddPersistence`, `AddApiInfrastructure`, `AddAuthServices`, `AddCorsPolicy`) and the `CorsPolicyName` constant.

### Files Modified
- `KanbAI-Core/KanbAI-Core/Program.cs` — Replaced all inline `builder.Services.*` registrations with fluent-chained extension method calls. Removed unused `using` directives (`Microsoft.AspNetCore.Authentication.Negotiate`, `Microsoft.EntityFrameworkCore`, `KanbAI_Core.Data`, `KanbAI_Core.Middleware`). Added `using KanbAI_Core.Extensions`. Replaced `"AllowAngularFrontend"` magic string with `ServiceCollectionExtensions.CorsPolicyName`.

### Build Verification
- `dotnet build` — **0 errors, 0 warnings**.
- `dotnet test` — Test runner is blocked by a Windows Application Control (WDAC) policy on this machine (`0x800711C7`). This is a pre-existing environment restriction, not related to code changes. QA should verify tests pass in a clean environment or CI pipeline.

### Edge Cases for QA
- The middleware pipeline order in `Program.cs` was **not** changed — only service registrations were moved.
- `public partial class Program { }` is preserved for integration test support.
- Each extension method returns `IServiceCollection` to support fluent chaining — QA should verify this in unit tests.
- `AddPersistence` and `AddCorsPolicy` depend on `IConfiguration` — unit tests should use `ConfigurationBuilder` with in-memory dictionaries.

---

## Testing Status

**Completed by:** @agent_tester_qa
**Date:** 2026-04-07

### Test Class 1: `ServiceCollectionExtensionTests` (Unit Tests)

**File:** `KanbAI-Core/KanbAI-Core.Tests/Extensions/ServiceCollectionExtensionTests.cs`

| # | Test Name | Status |
|---|---|---|
| 1 | `AddPersistence_RegistersApplicationDbContext` | ✅ Pass |
| 2 | `AddPersistence_RegistersDbContextOptions` | ✅ Pass |
| 3 | `AddApiInfrastructure_RegistersExceptionHandler` | ✅ Pass |
| 4 | `AddApiInfrastructure_RegistersProblemDetails` | ✅ Pass |
| 5 | `AddAuthServices_RegistersAuthentication` | ✅ Pass |
| 6 | `AddAuthServices_RegistersAuthorization` | ✅ Pass |
| 7 | `AddCorsPolicy_RegistersCorsOptions` | ✅ Pass |
| 8 | `AddCorsPolicy_WithNoConfiguredOrigins_RegistersEmptyPolicy` | ✅ Pass |
| 9 | `AllExtensionMethods_ReturnServiceCollection_ForFluentChaining` | ✅ Pass |
| 10 | `CorsPolicyName_HasExpectedValue` | ✅ Pass |

### Test Class 2: `DependencyInjectionIntegrationTests` (Integration Tests)

**File:** `KanbAI-Core/KanbAI-Core.Tests/Extensions/DependencyInjectionIntegrationTests.cs`

| # | Test Name | Status |
|---|---|---|
| 1 | `Application_WithRefactoredDI_StartsSuccessfully` | ✅ Pass |
| 2 | `Application_WithRefactoredDI_ResolvesApplicationDbContext` | ✅ Pass |

### Summary

- **Total tests in solution:** 93 (81 existing + 12 new)
- **Passed:** 93
- **Failed:** 0
- **Coverage gaps:** None identified. All acceptance criteria from the tech spec are covered.
