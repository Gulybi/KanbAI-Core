# Technical Specification: Issue #28 — Clean Up Default Template and Set Up KanbAI Base API Structure

## Overview

This issue removes the default .NET Web API boilerplate (`WeatherForecast` type and `/weatherforecast` minimal API endpoint, both inline in `Program.cs`) and establishes the controller-based API infrastructure that all future feature controllers will build upon. A health check endpoint at `GET /api/health` replaces the removed boilerplate, providing a liveness signal using the project's established `ApiResponse` wrapper format. Authentication and authorization middleware are added to the pipeline to enable `[Authorize]` / `[AllowAnonymous]` attributes on controllers.

**In scope:**
- Remove `WeatherForecast` record and `/weatherforecast` `MapGet` from `Program.cs`.
- Register MVC controller services (`AddControllers`) and map controller routes (`MapControllers`).
- Add `UseAuthentication()` and `UseAuthorization()` middleware to the pipeline.
- Create `HealthController` with `GET /api/health` returning `ApiResponse.Ok(...)`.
- Update 3 integration test files that reference the removed `/weatherforecast` endpoint.

**Out of scope:**
- Database/domain changes (none required).
- New NuGet packages (controller support is built into `Microsoft.NET.Sdk.Web`).
- Feature controllers (Users, Projects, etc.) — those are future issues.

---

## Database / Domain Design

N/A — no entity, enum, configuration, or migration changes for this issue.

---

## API Contracts

### Endpoint: Health Check

| Method | Route | Auth | Request Body | Response DTO | Success Status | Error Status |
|--------|-------|------|--------------|--------------|----------------|--------------|
| `GET` | `/api/health` | `[AllowAnonymous]` | N/A | `ApiResponse` | `200 OK` | N/A |

### Response Body (200 OK)

```json
{
  "success": true,
  "message": "KanbAI API is running smoothly.",
  "errors": []
}
```

The response uses the existing `ApiResponse.Ok(string?)` factory method from `KanbAI_Core.DTOs.ApiResponse` — no new DTOs required.

### Removed Endpoint

| Method | Route | Previous Behavior | New Behavior |
|--------|-------|-------------------|--------------|
| `GET` | `/weatherforecast` | `200 OK` with `ApiResponse<WeatherForecast[]>` | `404 Not Found` |

---

## Application Layer Boundaries

N/A — the health endpoint returns a static message directly from the controller action. No service layer, repository, or MediatR handler is required.

---

## Implementation Steps for @agent_developer

Follow these steps strictly in order, referencing `@rule_code_standards`.

### Step 1 — Register Controller Services

**File:** `KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs`

1. Add `services.AddControllers();` as the **first line** inside the `AddApiInfrastructure` method body (before `AddOpenApi`).

**Target state of `AddApiInfrastructure`:**

```csharp
public static IServiceCollection AddApiInfrastructure(this IServiceCollection services)
{
    services.AddControllers();
    services.AddOpenApi();
    services.AddExceptionHandler<GlobalExceptionHandler>();
    services.AddProblemDetails();
    return services;
}
```

**Rationale:** Placing `AddControllers()` inside `AddApiInfrastructure` keeps all API-related service registrations together. No new NuGet packages are needed — `AddControllers()` is part of the `Microsoft.NET.Sdk.Web` SDK.

---

### Step 2 — Clean Up `Program.cs`

**File:** `KanbAI-Core/KanbAI-Core/Program.cs`

1. **Remove** the `using KanbAI_Core.DTOs;` import (no longer needed after weather endpoint removal).
2. **Remove** the `summaries` string array (lines 30–33 in current file).
3. **Remove** the entire `app.MapGet("/weatherforecast", ...)` block including `.WithName("GetWeatherForecast")` (lines 35–47).
4. **Remove** the `record WeatherForecast(...)` definition at the bottom of the file (lines 51–54).
5. **Add** `app.UseAuthentication();` after `app.UseCors(...)`.
6. **Add** `app.UseAuthorization();` after `app.UseAuthentication()`.
7. **Add** `app.MapControllers();` after `app.UseAuthorization()` and before `app.Run()`.
8. **Keep** `public partial class Program { }` at the end of the file.

**Target state of `Program.cs`:**

```csharp
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

    if (!app.Configuration.GetValue<bool>("Testing:SkipScalar"))
    {
        app.MapScalarUi();
    }
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
```

**Pipeline order rationale:** The standard ASP.NET Core middleware ordering is `ExceptionHandler → HTTPS → CORS → Authentication → Authorization → Endpoints`. `MapControllers()` replaces the removed `MapGet("/weatherforecast", ...)` as the endpoint source.

**Note on `UseAuthentication` / `UseAuthorization`:** These middleware calls are currently missing from the pipeline. While the auth services are registered in `AddAuthServices()`, the middleware was never added — meaning the `FallbackPolicy` (which requires authentication on all endpoints) was not enforced for minimal API endpoints. Adding the middleware now enables `[Authorize]` and `[AllowAnonymous]` attributes on controllers. Existing integration tests are unaffected because they set `FallbackPolicy = null` and use a no-op `TestAuthHandler`.

---

### Step 3 — Create the Controllers Folder and HealthController

1. **Create directory:** `KanbAI-Core/KanbAI-Core/Controllers/`

2. **Create file:** `KanbAI-Core/KanbAI-Core/Controllers/HealthController.cs`

**Controller contract:**

```csharp
namespace KanbAI_Core.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
        => Ok(ApiResponse.Ok("KanbAI API is running smoothly."));
}
```

**Required usings:**
- `KanbAI_Core.DTOs`
- `Microsoft.AspNetCore.Authorization`
- `Microsoft.AspNetCore.Mvc`

**Design notes:**
- `[ApiController]` enables automatic model validation, `[FromBody]` inference, and `ProblemDetails` responses for 400 errors — consistent with modern ASP.NET Core controller patterns.
- `[Route("api/[controller]")]` resolves to `/api/health` (lowercase convention from `[controller]` token).
- `[AllowAnonymous]` ensures the health endpoint is accessible without authentication, overriding the `FallbackPolicy` set in `AddAuthServices()`. This is standard for liveness/readiness probes.
- The controller returns `ApiResponse.Ok("KanbAI API is running smoothly.")` wrapped in `OkObjectResult` (200 OK), matching the existing response wrapper pattern.

---

### Step 4 — Update Integration Tests

Three test files reference the removed `/weatherforecast` endpoint and must be updated to use `/api/health`.

#### 4a. `GlobalExceptionHandlerTests.cs`

**File:** `KanbAI-Core/KanbAI-Core.Tests/Middleware/GlobalExceptionHandlerTests.cs`

1. In the `#region Regression — Existing Endpoints` section, rename test method `ExistingWeatherEndpoint_ContinuesToWork` → `HealthEndpoint_Returns200OK`.
2. Change the request URL from `"/weatherforecast"` to `"/api/health"`.
3. Keep all assertions unchanged (`StatusCode == OK`, `Success == true`).

#### 4b. `CorsMiddlewareTests.cs`

**File:** `KanbAI-Core/KanbAI-Core.Tests/Middleware/CorsMiddlewareTests.cs`

1. Replace **all** occurrences of `"/weatherforecast"` with `"/api/health"` in test methods:
   - `Preflight_WithAllowedOrigin_ReturnsCorsPolicyHeaders`
   - `Preflight_WithAllowedOrigin_AllowsRequiredMethods`
   - `Preflight_WithAllowedOrigin_AllowsRequiredHeaders`
   - `ActualRequest_WithAllowedOrigin_IncludesAccessControlAllowOriginHeader`
   - `ActualRequest_WithDisallowedOrigin_DoesNotIncludeAccessControlAllowOriginHeader`
   - `Preflight_InProduction_DoesNotReturnCorsHeaders`
2. In the `#region Regression — Existing Endpoints` section, rename test method `ExistingWeatherEndpoint_ContinuesToWork` → `HealthEndpoint_Returns200OK`.
3. Change the regression test URL from `"/weatherforecast"` to `"/api/health"`.

#### 4c. `DependencyInjectionIntegrationTests.cs`

**File:** `KanbAI-Core/KanbAI-Core.Tests/Extensions/DependencyInjectionIntegrationTests.cs`

1. In `Application_WithRefactoredDI_StartsSuccessfully`, change the request URL from `"/weatherforecast"` to `"/api/health"`.
2. Update the assertion message to match (e.g., "the refactored DI pipeline should start successfully and serve requests").

---

### Step 5 — Verify Build and Tests

1. Run `dotnet build` — expect **0 errors, 0 warnings**.
2. Run `dotnet test` — expect **all tests pass** (existing tests updated, no regression).
3. Verify that `GET /api/health` returns `200 OK` with `{"success":true,"message":"KanbAI API is running smoothly.","errors":[]}`.
4. Verify that `GET /weatherforecast` returns `404 Not Found`.
5. Verify that the Scalar API reference (in Development) shows the health endpoint and does **not** show any WeatherForecast schemas or routes.

---

## QA Guidance for @agent_tester_qa

### Test File Locations

| File | Scope |
|------|-------|
| `KanbAI-Core.Tests/Controllers/HealthControllerTests.cs` | **New** — Health endpoint integration tests |
| `KanbAI-Core.Tests/Middleware/GlobalExceptionHandlerTests.cs` | **Updated** — Regression test URL changed |
| `KanbAI-Core.Tests/Middleware/CorsMiddlewareTests.cs` | **Updated** — All endpoint URLs changed |
| `KanbAI-Core.Tests/Extensions/DependencyInjectionIntegrationTests.cs` | **Updated** — Startup test URL changed |
| `KanbAI-Core.Tests/Extensions/ServiceCollectionExtensionTests.cs` | **Extended** — Verify `AddControllers` registration |

### New Tests Required

**File:** `KanbAI-Core.Tests/Controllers/HealthControllerTests.cs`

| Test Name | Category | Description |
|-----------|----------|-------------|
| `HealthEndpoint_Returns200OK` | Happy Path | `GET /api/health` returns HTTP 200 |
| `HealthEndpoint_ReturnsApiResponseFormat` | Happy Path | Response body deserializes to `ApiResponse` with `Success = true` |
| `HealthEndpoint_ReturnsExpectedMessage` | Happy Path | `Message` equals `"KanbAI API is running smoothly."` |
| `HealthEndpoint_ReturnsJsonContentType` | Happy Path | `Content-Type` is `application/json` |
| `WeatherForecastEndpoint_Returns404` | Regression | `GET /weatherforecast` returns HTTP 404 |
| `HealthEndpoint_IsAccessibleWithoutAuthentication` | Security | Endpoint returns 200 even when `FallbackPolicy` requires auth (tests without `FallbackPolicy = null` override) |

**File:** `KanbAI-Core.Tests/Extensions/ServiceCollectionExtensionTests.cs` (extend existing)

| Test Name | Category | Description |
|-----------|----------|-------------|
| `AddApiInfrastructure_RegistersControllerServices` | Happy Path | Verify that `AddApiInfrastructure` registers MVC controller services (e.g., `IActionDescriptorCollectionProvider` is resolvable) |

### Test Infrastructure Notes

- Use `WebApplicationFactory<Program>` via `CustomWebApplicationFactory` (already established).
- Use the existing `CreateClient` pattern with `TestAuthHandler` auth bypass for most tests.
- For the `HealthEndpoint_IsAccessibleWithoutAuthentication` test, create a client **without** setting `FallbackPolicy = null` — this verifies `[AllowAnonymous]` actually works against the production auth policy.
- Follow existing `MethodName_StateUnderTest_ExpectedBehavior` naming convention.
- Use AAA pattern with blank-line separation.

---

## Known Caveats

| Caveat | Impact | Mitigation |
|--------|--------|------------|
| **Negotiate auth + TestServer** | `TestServer` does not support `IConnectionItemsFeature` required by Negotiate authentication. All integration tests must replace Negotiate with a no-op `TestAuthHandler`. | Existing `CreateClient` helper already handles this. No change needed. |
| **`UseAuthentication` / `UseAuthorization` added** | These middleware calls were previously missing. Adding them enforces the `FallbackPolicy` (require authentication on all endpoints). Future endpoints without `[AllowAnonymous]` will require Negotiate auth. | This is correct behavior. The health endpoint uses `[AllowAnonymous]`. Future feature controllers will need auth — which is the intended design. |
| **CORS preflight with controller endpoints** | CORS preflight (`OPTIONS`) requests must reach the CORS middleware before auth. The pipeline order (`UseCors` → `UseAuthentication` → `UseAuthorization` → `MapControllers`) ensures this. | Pipeline order is correct. Existing CORS tests will verify this. |
| **OpenAPI controller discovery** | `Microsoft.AspNetCore.OpenApi` (v10.0.5) with `AddOpenApi()` + `MapOpenApi()` automatically discovers controller endpoints mapped via `MapControllers()`. No additional configuration is needed for the health endpoint to appear in the OpenAPI document. | Verified by the `ScalarApiReferenceTests` existing test suite and manual verification in Step 5. |
| **No `WeatherForecast` type in test project** | The `WeatherForecast` type was a file-local record in `Program.cs`. No test files directly reference the `WeatherForecast` type — they only reference the `/weatherforecast` URL. Removal is safe. | URL references are updated in Step 4. |

---

## Development Status

**Completed by @agent_developer on 2026-04-13.**

### Files Created

| File | Purpose |
|------|---------|
| `KanbAI-Core/KanbAI-Core/Controllers/HealthController.cs` | Health check endpoint (`GET /api/health`) returning `ApiResponse.Ok("KanbAI API is running smoothly.")` with `[AllowAnonymous]` |

### Files Modified

| File | Change |
|------|--------|
| `KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs` | Added `services.AddControllers();` as first line in `AddApiInfrastructure()` |
| `KanbAI-Core/KanbAI-Core/Program.cs` | Removed `WeatherForecast` record, `/weatherforecast` `MapGet`, and `summaries` array. Removed `using KanbAI_Core.DTOs;`. Added `app.UseAuthentication()`, `app.UseAuthorization()`, and `app.MapControllers()`. |
| `KanbAI-Core/KanbAI-Core.Tests/Middleware/GlobalExceptionHandlerTests.cs` | Renamed `ExistingWeatherEndpoint_ContinuesToWork` → `HealthEndpoint_Returns200OK`, changed URL from `/weatherforecast` to `/api/health` |
| `KanbAI-Core/KanbAI-Core.Tests/Middleware/CorsMiddlewareTests.cs` | Replaced all 7 occurrences of `/weatherforecast` with `/api/health`, renamed `ExistingWeatherEndpoint_ContinuesToWork` → `HealthEndpoint_Returns200OK` |
| `KanbAI-Core/KanbAI-Core.Tests/Extensions/DependencyInjectionIntegrationTests.cs` | Changed request URL from `/weatherforecast` to `/api/health` |

### Build & Test Results

| Metric | Result |
|--------|--------|
| Build | **SUCCESS** — 0 errors, 0 warnings |
| Tests Total | 216 |
| Tests Passed | 214 |
| Tests Failed | 0 |
| Tests Skipped | 2 (pre-existing) |

### Infrastructure Notes

- No workarounds or infrastructure created outside the tech spec's scope.
- `UseAuthentication()` and `UseAuthorization()` middleware were added to the pipeline as specified — these were previously missing despite auth services being registered.

### Edge Cases for QA

- Verify `GET /api/health` returns `200 OK` with `{"success":true,"message":"KanbAI API is running smoothly.","errors":[]}`.
- Verify `GET /weatherforecast` returns `404 Not Found` (endpoint removed).
- Verify `[AllowAnonymous]` on `HealthController` correctly overrides the `FallbackPolicy` — the health endpoint should be accessible without authentication even when `FallbackPolicy` requires auth.
- Verify the Scalar API reference (Development environment) shows only the health endpoint and no WeatherForecast schemas or routes.
- Verify CORS preflight requests to `/api/health` work correctly (pipeline order: `UseCors` → `UseAuthentication` → `UseAuthorization` → `MapControllers`).

## Testing Status

**Completed by @agent_tester_qa on 2026-04-13.**

### New Test File

**File:** `KanbAI-Core/KanbAI-Core.Tests/Controllers/HealthControllerTests.cs`

| # | Test Name | Category | Status |
|---|-----------|----------|--------|
| 1 | `HealthEndpoint_Returns200OK` | Happy Path | PASS |
| 2 | `HealthEndpoint_ReturnsApiResponseFormat` | Happy Path | PASS |
| 3 | `HealthEndpoint_ReturnsExpectedMessage` | Happy Path | PASS |
| 4 | `HealthEndpoint_ReturnsJsonContentType` | Happy Path | PASS |
| 5 | `WeatherForecastEndpoint_Returns404` | Regression | PASS |
| 6 | `HealthEndpoint_IsAccessibleWithoutAuthentication` | Security | PASS |

### Extended Test File

**File:** `KanbAI-Core/KanbAI-Core.Tests/Extensions/ServiceCollectionExtensionTests.cs`

| # | Test Name | Category | Status |
|---|-----------|----------|--------|
| 1 | `AddApiInfrastructure_RegistersControllerServices` | Happy Path | PASS |

### Full Suite Summary

| Metric | Result |
|--------|--------|
| Total | 223 |
| Passed | 221 |
| Failed | 0 |
| Skipped | 2 (pre-existing) |

### Coverage Notes

- All 6 tests from the QA guidance section are implemented and passing.
- The `HealthEndpoint_IsAccessibleWithoutAuthentication` test validates `[AllowAnonymous]` against the production `FallbackPolicy` by deliberately NOT setting `FallbackPolicy = null`.
- The `WeatherForecastEndpoint_Returns404` test confirms the boilerplate endpoint was fully removed.
- The `AddApiInfrastructure_RegistersControllerServices` test verifies MVC controller services registration via `IActionDescriptorCollectionProvider` resolution.
- No coverage gaps identified for this issue's scope.
