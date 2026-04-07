# Technical Specification: Issue #10 - Configure Core Middleware (Swagger & CORS)

## Overview

Configure two foundational middleware components in `Program.cs`:

1. **Interactive API Reference UI** — Replace the raw OpenAPI JSON endpoint with an interactive developer UI for browsing and testing endpoints during local development.
2. **CORS Policy** — Define a Cross-Origin Resource Sharing policy that permits the Angular frontend (`http://localhost:4200`) to communicate with this API during development.

No new domain entities, database changes, or migrations are introduced in this issue.

---

## Architecture & Design Decisions

### Decision 1: Scalar.AspNetCore over Swashbuckle.AspNetCore

| Option | Pros | Cons |
|---|---|---|
| **Scalar.AspNetCore (chosen)** | Microsoft-recommended replacement for Swagger UI in .NET 9+; works natively with `Microsoft.AspNetCore.OpenApi` (already in the project); actively maintained; superior UX with modern design, search, code snippets, and dark mode; single package addition. | Interactive UI is served at `/scalar/v1` instead of the traditional `/swagger` path. |
| Swashbuckle.AspNetCore | Traditional Swagger UI at `/swagger`; widely known. | Maintainer has stepped away; not included in .NET 9/10 templates; may have compatibility issues with .NET 10; requires separate document generator (project already uses `Microsoft.AspNetCore.OpenApi`). |

**Decision:** Use `Scalar.AspNetCore` because the project targets .NET 10 and already uses `Microsoft.AspNetCore.OpenApi` for document generation. Scalar is the framework-endorsed interactive UI and requires zero additional document generation configuration. The acceptance criteria for "interactive UI at a well-known URL" is satisfied at `/scalar/v1`.

### Decision 2: Configuration-Driven CORS Origins

| Option | Pros | Cons |
|---|---|---|
| **Configuration-driven (chosen)** | Origins read from `appsettings.{Environment}.json`; environment-aware by default; extensible for future environments (staging, production); CORS middleware is always in the pipeline ensuring correct ordering. | Slightly more indirection than inline code. |
| Hardcoded with `IsDevelopment()` guard | Simple; easy to understand. | Not extensible; requires code changes to add origins for staging/production; middleware may be absent from the pipeline in non-dev environments, complicating ordering guarantees. |

**Decision:** Read allowed origins from configuration (`Cors:AllowedOrigins`). Define the origins only in `appsettings.Development.json`. The base `appsettings.json` has no origins, so production defaults to an empty list (no cross-origin requests allowed). The CORS middleware is always registered in the pipeline to guarantee correct ordering relative to authentication/authorization.

### Decision 3: CORS Policy Scope

The named policy `AllowAngularFrontend` allows:
- **Origins:** Loaded from configuration (Development: `http://localhost:4200`)
- **Methods:** `GET`, `POST`, `PUT`, `DELETE`, `PATCH`
- **Headers:** `Content-Type`, `Authorization`

This covers standard SPA-to-API communication. `AllowCredentials()` is deliberately **not** included — the Angular app will use token-based authentication (bearer tokens in the `Authorization` header), not cookie-based auth.

---

## Pipeline Order

The middleware pipeline after this change must follow the ASP.NET Core recommended order:

```
1. app.UseExceptionHandler()        ← existing (catches all unhandled exceptions)
2. app.UseHttpsRedirection()        ← existing
3. app.UseCors("AllowAngularFrontend")  ← NEW (must be before implicit UseAuthorization)
4. [implicit UseAuthentication]     ← auto-added by WebApplication when AddAuthentication is registered
5. [implicit UseAuthorization]      ← auto-added by WebApplication when AddAuthorization is registered
6. Endpoint mappings               ← MapOpenApi, MapScalarApiReference (dev-only), MapGet, etc.
```

**Why `UseCors()` before authorization:** CORS preflight requests (HTTP `OPTIONS`) do not carry authentication credentials. If `UseAuthorization()` runs first, the `FallbackPolicy` (which requires all requests to be authorized) will reject preflight requests with `401` before the CORS middleware can respond with the appropriate `Access-Control-*` headers. This breaks the browser's CORS handshake entirely.

---

## API Contract

No new API endpoints are introduced. The following infrastructure endpoints become available in Development:

| Endpoint | Method | Description | Environment |
|---|---|---|---|
| `/openapi/v1.json` | GET | Raw OpenAPI 3.x JSON document | Development only |
| `/scalar/v1` | GET | Interactive API reference UI | Development only |

---

## Configuration Changes

### `appsettings.Development.json` — Add CORS origins

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=KanbAI;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:4200"]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

The base `appsettings.json` is **not modified** — the absence of a `Cors:AllowedOrigins` section means production defaults to zero allowed origins (safe by design).

---

## Implementation Steps for @agent_developer

Follow these steps in order, referencing `@rule_code_standards`:

### Step 1 — Add the Scalar.AspNetCore NuGet Package

1. Add the `Scalar.AspNetCore` package to `KanbAI-Core/KanbAI-Core/KanbAI-Core.csproj`:
   ```shell
   dotnet add KanbAI-Core/KanbAI-Core/KanbAI-Core.csproj package Scalar.AspNetCore
   ```
2. Verify the package reference appears in the `.csproj` file.

### Step 2 — Update `appsettings.Development.json`

1. Add the `Cors` configuration section with the Angular frontend origin:
   ```json
   "Cors": {
     "AllowedOrigins": ["http://localhost:4200"]
   }
   ```

### Step 3 — Update `Program.cs` Service Registrations

Add the following **before** `builder.Build()`, after the existing `AddProblemDetails()` call:

1. Read the allowed origins from configuration:
   ```csharp
   var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
   ```

2. Register the CORS service with a named policy:
   ```csharp
   builder.Services.AddCors(options =>
   {
       options.AddPolicy("AllowAngularFrontend", policy =>
       {
           policy.WithOrigins(allowedOrigins)
                 .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                 .WithHeaders("Content-Type", "Authorization");
       });
   });
   ```

### Step 4 — Update `Program.cs` Middleware Pipeline

1. **Inside the `IsDevelopment()` block**, add the Scalar API reference endpoint after `MapOpenApi()`:
   ```csharp
   if (app.Environment.IsDevelopment())
   {
       app.MapOpenApi();
       app.MapScalarApiReference();
   }
   ```

2. **After `UseHttpsRedirection()` and before endpoint mappings**, add the CORS middleware:
   ```csharp
   app.UseExceptionHandler();
   app.UseHttpsRedirection();
   app.UseCors("AllowAngularFrontend");
   ```

3. Add the required `using` directive at the top of `Program.cs` (only if Scalar does not auto-register via global usings — verify after package install):
   ```csharp
   using Scalar.AspNetCore;
   ```

### Step 5 — Verify the Final Pipeline Order

The complete `Program.cs` pipeline section should read (in order):

```csharp
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors("AllowAngularFrontend");

// ... endpoint mappings (MapGet, etc.)
```

### Step 6 — Verify Build and Existing Tests

1. Run `dotnet build` on the solution to confirm zero errors and zero new warnings.
2. Run `dotnet test` to confirm all 69 existing tests still pass without modification.

---

## Test Infrastructure Setup for Integration Tests

### `CreateClient` Helper Method

The CORS and Scalar integration tests must bypass the same Negotiate auth issue documented in `@rule_integration_testing`. Use this exact `CreateClient` implementation:

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

### `TestAuthHandler` (Nested Private Class)

Same no-op handler as `GlobalExceptionHandlerTests`:

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
| **Negotiate auth in TestServer** | `NegotiateHandler` throws `NotSupportedException` on every request. | Auth is stripped and replaced with `TestAuthHandler` in `CreateClient` (see §`@rule_integration_testing` §1). |
| **CORS requires `Origin` header** | `HttpClient` from `CreateClient()` does not automatically set an `Origin` header; CORS middleware will not produce response headers without it. | Tests must explicitly set the `Origin` request header (and preflight headers for OPTIONS tests). |
| **CORS in Production test** | When environment is `Production`, `appsettings.Development.json` is not loaded, so `Cors:AllowedOrigins` is empty. The CORS middleware is registered but allows no origins. | This is correct behavior — tests should verify that CORS headers are absent for production environment. |
| **Scalar UI serves HTML** | The `/scalar/v1` endpoint returns an HTML page (Content-Type: `text/html`), not JSON. | Tests for Scalar should assert on status code and content type, not deserialize as JSON. |

---

## Edge Cases for @agent_tester_qa

The QA agent should create integration tests using `WebApplicationFactory<Program>` in two new test classes.

### Test Class 1: `ScalarApiReferenceTests`

**File:** `KanbAI-Core/KanbAI-Core.Tests/Middleware/ScalarApiReferenceTests.cs`
**Namespace:** `KanbAI_Core.Tests.Middleware`

| # | Test Name | Assertion |
|---|---|---|
| 1 | `ScalarUi_InDevelopment_ReturnsSuccessStatusCode` | `GET /scalar/v1` returns `200 OK` when environment is `Development`. |
| 2 | `ScalarUi_InDevelopment_ReturnsHtmlContent` | `GET /scalar/v1` returns `Content-Type` starting with `text/html`. |
| 3 | `ScalarUi_InProduction_ReturnsNotFound` | `GET /scalar/v1` returns `404 Not Found` when environment is `Production`. |
| 4 | `OpenApiDocument_InDevelopment_ReturnsSuccessStatusCode` | `GET /openapi/v1.json` returns `200 OK` when environment is `Development`. |
| 5 | `OpenApiDocument_InProduction_ReturnsNotFound` | `GET /openapi/v1.json` returns `404 Not Found` when environment is `Production`. |

### Test Class 2: `CorsMiddlewareTests`

**File:** `KanbAI-Core/KanbAI-Core.Tests/Middleware/CorsMiddlewareTests.cs`
**Namespace:** `KanbAI_Core.Tests.Middleware`

| # | Test Name | Assertion |
|---|---|---|
| 1 | `Preflight_WithAllowedOrigin_ReturnsCorsPolicyHeaders` | Send `OPTIONS /weatherforecast` with headers `Origin: http://localhost:4200`, `Access-Control-Request-Method: GET`, `Access-Control-Request-Headers: Content-Type`. Verify response contains `Access-Control-Allow-Origin: http://localhost:4200`. |
| 2 | `Preflight_WithAllowedOrigin_AllowsRequiredMethods` | Same preflight request as above. Verify `Access-Control-Allow-Methods` header contains `GET`, `POST`, `PUT`, `DELETE`, `PATCH`. |
| 3 | `Preflight_WithAllowedOrigin_AllowsRequiredHeaders` | Same preflight request. Verify `Access-Control-Allow-Headers` contains `Content-Type` and `Authorization`. |
| 4 | `ActualRequest_WithAllowedOrigin_IncludesAccessControlAllowOriginHeader` | Send `GET /weatherforecast` with `Origin: http://localhost:4200`. Verify response includes `Access-Control-Allow-Origin: http://localhost:4200`. |
| 5 | `ActualRequest_WithDisallowedOrigin_DoesNotIncludeAccessControlAllowOriginHeader` | Send `GET /weatherforecast` with `Origin: http://evil.example.com`. Verify response does **not** contain `Access-Control-Allow-Origin` header. |
| 6 | `Preflight_InProduction_DoesNotReturnCorsHeaders` | Set environment to `Production`. Send preflight `OPTIONS /weatherforecast` with `Origin: http://localhost:4200`. Verify response does **not** contain `Access-Control-Allow-Origin` header (no origins configured in production). |
| 7 | `ExistingWeatherEndpoint_ContinuesToWork` | `GET /weatherforecast` (no `Origin` header) returns `200` with a valid `ApiResponse` body (`Success == true`). |

---

## Files Changed Summary

### New Package
- `Scalar.AspNetCore` — Added to `KanbAI-Core/KanbAI-Core/KanbAI-Core.csproj`

### Modified Files
- `KanbAI-Core/KanbAI-Core/Program.cs` — CORS service registration, `UseCors()` middleware, Scalar API reference endpoint
- `KanbAI-Core/KanbAI-Core/appsettings.Development.json` — `Cors:AllowedOrigins` configuration section

### New Test Files (for @agent_tester_qa)
- `KanbAI-Core/KanbAI-Core.Tests/Middleware/ScalarApiReferenceTests.cs`
- `KanbAI-Core/KanbAI-Core.Tests/Middleware/CorsMiddlewareTests.cs`

---

## Development Status

**Completed by:** @agent_developer
**Date:** 2026-04-07

### Changes Made

1. **Added `Scalar.AspNetCore` v2.13.20** to `KanbAI-Core/KanbAI-Core/KanbAI-Core.csproj`.
2. **Updated `appsettings.Development.json`** — Added `Cors:AllowedOrigins` section with `http://localhost:4200`.
3. **Updated `Program.cs`** — Added `using Scalar.AspNetCore;`, CORS service registration with named policy `AllowAngularFrontend` (config-driven origins, explicit methods and headers), `app.MapScalarApiReference()` inside the `IsDevelopment()` block, and `app.UseCors("AllowAngularFrontend")` after `UseHttpsRedirection()`.

### Pipeline Order (verified)

1. `UseExceptionHandler()` → 2. `UseHttpsRedirection()` → 3. `UseCors("AllowAngularFrontend")` → 4. [implicit auth] → 5. Endpoint mappings (`MapOpenApi`, `MapScalarApiReference`, `MapGet`)

### Build & Test Verification

- `dotnet build`: 0 errors, 0 warnings
- `dotnet test`: 69/69 tests passed (all existing tests unaffected)

### QA Notes

- Scalar UI is served at `/scalar/v1` (not `/swagger`) — this is per the tech spec decision.
- CORS preflight tests must set `Origin` and `Access-Control-Request-Method` headers explicitly (the test `HttpClient` does not add them automatically).
- Production environment should have no `Cors:AllowedOrigins` — CORS middleware is registered but allows zero origins.
- Base `appsettings.json` was intentionally not modified (safe-by-default: no origins in production).

---

## Testing Status

**Completed by:** @agent_tester_qa
**Date:** 2026-04-07

### Test Class 1: `ScalarApiReferenceTests`

**File:** `KanbAI-Core/KanbAI-Core.Tests/Middleware/ScalarApiReferenceTests.cs`

| # | Test Name | Status |
|---|---|---|
| 1 | `ScalarUi_InDevelopment_ReturnsSuccessStatusCode` | PASS |
| 2 | `ScalarUi_InDevelopment_ReturnsHtmlContent` | PASS |
| 3 | `ScalarUi_InProduction_ReturnsNotFound` | PASS |
| 4 | `OpenApiDocument_InDevelopment_ReturnsSuccessStatusCode` | PASS |
| 5 | `OpenApiDocument_InProduction_ReturnsNotFound` | PASS |

### Test Class 2: `CorsMiddlewareTests`

**File:** `KanbAI-Core/KanbAI-Core.Tests/Middleware/CorsMiddlewareTests.cs`

| # | Test Name | Status |
|---|---|---|
| 1 | `Preflight_WithAllowedOrigin_ReturnsCorsPolicyHeaders` | PASS |
| 2 | `Preflight_WithAllowedOrigin_AllowsRequiredMethods` | PASS |
| 3 | `Preflight_WithAllowedOrigin_AllowsRequiredHeaders` | PASS |
| 4 | `ActualRequest_WithAllowedOrigin_IncludesAccessControlAllowOriginHeader` | PASS |
| 5 | `ActualRequest_WithDisallowedOrigin_DoesNotIncludeAccessControlAllowOriginHeader` | PASS |
| 6 | `Preflight_InProduction_DoesNotReturnCorsHeaders` | PASS |
| 7 | `ExistingWeatherEndpoint_ContinuesToWork` | PASS |

### Summary

- **Total tests in suite:** 81 (69 existing + 12 new)
- **Passed:** 81
- **Failed:** 0
- **Coverage gaps:** None identified. All acceptance criteria edge cases covered.
