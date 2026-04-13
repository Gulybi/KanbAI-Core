# Context Handoff: Issue #28 — Clean Up Default Template and Set Up KanbAI Base API Structure

## Title & Business Value

**Title:** Clean Up Default Template and Set Up KanbAI Base API Structure
**Who:** The engineering team and any stakeholder who interacts with or evaluates the API (developers, QA, reviewers).
**Why:** The project still ships with the default .NET Web API boilerplate (`/weatherforecast` endpoint and `WeatherForecast` type). This placeholder code has no business value and creates a misleading impression of the API's capabilities. Removing it and establishing a proper health check endpoint ensures the API presents a clean, professional surface and provides a reliable operational liveness signal for development and future deployment. This is foundational hygiene — all subsequent API feature work (user endpoints, project endpoints, board endpoints) will build on the clean controller structure established here.

## Current State vs. Desired State

### Current State

- The project uses the **minimal API** pattern — there is no `Controllers` folder and no controller classes.
- The `WeatherForecast` type is defined as a **file-local record at the bottom of `KanbAI-Core/KanbAI-Core/Program.cs`**, not as a standalone file.
- The `/weatherforecast` endpoint is a **minimal API `MapGet` call in `Program.cs`**, not a controller action. There is no `WeatherForecastController.cs`.
- The project namespace convention is `KanbAI_Core` (underscore required by C# identifier rules due to the hyphen in the project name `KanbAI-Core`), as set in `KanbAI-Core/KanbAI-Core/KanbAI-Core.csproj` via `<RootNamespace>KanbAI_Core</RootNamespace>`.
- OpenAPI documentation and Scalar API reference UI are already configured (Issue #10) and currently display the `/weatherforecast` endpoint.
- The `ApiResponse` / `ApiResponse<T>` standard response wrapper is established in `KanbAI-Core/KanbAI-Core/DTOs/ApiResponse.cs` (Issue #8).
- Global exception handling via `GlobalExceptionHandler` is registered in `KanbAI-Core/KanbAI-Core/Middleware/GlobalExceptionHandler.cs` (Issue #9).
- CORS is configured via `AddCorsPolicy` extension method in `KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs` (Issue #10).
- DI extension methods are structured in `ServiceCollectionExtensions.cs` (Issue #11).
- **Multiple existing integration tests reference the `/weatherforecast` endpoint** for regression testing (e.g., `DependencyInjectionIntegrationTests`, `GlobalExceptionHandlerTests`, `CorsMiddlewareTests`). These will be affected by the removal.
- Seven domain entities and their EF Core configurations are in place (Issues #21–#26), but no domain-specific API endpoints exist yet.

### Desired State

- All `WeatherForecast` boilerplate code (type definition, endpoint mapping) is completely removed from `Program.cs` and any other location.
- The project supports **controller-based routing**, enabling future feature controllers (Users, Projects, Boards, etc.) to follow a consistent pattern.
- A health check endpoint exists at `GET /api/health` that returns an HTTP 200 OK response confirming the API is operational, using the project's established `ApiResponse` response format.
- The API documentation (Scalar/OpenAPI) displays only the health endpoint — no traces of WeatherForecast remain.
- All existing infrastructure (exception handling, CORS, authentication, DI) continues to function correctly.
- All automated tests pass — tests that depended on the removed `/weatherforecast` endpoint are updated to use the new health endpoint (or removed).

## Milestone Context

**Milestone:** AI Environment & Measurement Setup

| # | Issue | State | Relationship |
|---|-------|-------|--------------|
| **28** | **Clean up default template and set up KanbAI base API structure** | **Open (this issue)** | **Foundation** — removes boilerplate and establishes controller-based API structure |

Issue #28 is currently the only issue in the "AI Environment & Measurement Setup" milestone. It serves as a transitional step between the completed domain model setup (Milestone: "Database Models & EF Core Setup") and future API feature development.

**Note:** The issue body references deleting `WeatherForecast.cs` and `WeatherForecastController.cs` as standalone files, and creating `KanbAIController.cs`. In practice, the codebase differs from these assumptions — the weather forecast code exists as inline minimal API code in `Program.cs`, not as separate controller/model files. The issue also references creating a "KanbAIController.cs" whose exact role is unspecified. The engineering team should interpret this as establishing the appropriate controller infrastructure (e.g., a base controller or a general-purpose API controller) based on the project's conventions and needs. The critical business outcome is the removal of boilerplate and the presence of a working health endpoint.

## Acceptance Criteria

### Boilerplate Cleanup

- [ ] The `/weatherforecast` endpoint is removed and returns HTTP 404 Not Found when requested.
- [ ] The `WeatherForecast` type definition (currently a file-local record in `Program.cs`) is removed from the codebase entirely.
- [ ] No WeatherForecast-related routes or schemas appear in the API documentation (Scalar/OpenAPI).

### Health Check Endpoint

- [ ] A `GET /api/health` endpoint exists and returns HTTP 200 OK.
- [ ] The health endpoint response body contains a message confirming the API is operational (e.g., "KanbAI API is running smoothly.").
- [ ] The health endpoint response follows the project's established `ApiResponse` wrapper format.
- [ ] The health endpoint is visible and documented in the API reference UI (Scalar/OpenAPI).

### API Structure

- [ ] The project supports controller-based routing for the new health endpoint and future feature endpoints.
- [ ] All new source files follow the established `KanbAI_Core` namespace convention.
- [ ] The project compiles with zero errors and zero warnings after all changes.

### Regression Safety

- [ ] The application starts successfully and the health endpoint is reachable.
- [ ] Existing middleware (global exception handling, CORS, authentication) continues to function after the cleanup.
- [ ] Existing integration tests that referenced the removed `/weatherforecast` endpoint are updated to use the new `/api/health` endpoint (or an equivalent valid route) and pass.
- [ ] All automated tests pass after the changes are complete.

### AC Validation

| # | Criterion Summary | Testable | Specific | Independent | Impl-Free | Complete |
|---|-------------------|----------|----------|-------------|-----------|----------|
| 1 | /weatherforecast removed, returns 404 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 2 | WeatherForecast type removed from codebase | ✅ | ✅ | ✅ | ✅ | ✅ |
| 3 | No WeatherForecast in API docs | ✅ | ✅ | ✅ | ✅ | ✅ |
| 4 | GET /api/health returns 200 OK | ✅ | ✅ | ✅ | ✅ | ✅ |
| 5 | Health response confirms API operational | ✅ | ✅ | ✅ | ✅ | ✅ |
| 6 | Health response uses ApiResponse format | ✅ | ✅ | ✅ | ✅ | ✅ |
| 7 | Health endpoint visible in API docs | ✅ | ✅ | ✅ | ✅ | ✅ |
| 8 | Controller-based routing supported | ✅ | ✅ | ✅ | ✅ | ✅ |
| 9 | New files follow KanbAI_Core namespace | ✅ | ✅ | ✅ | ✅ | ✅ |
| 10 | Zero compilation errors/warnings | ✅ | ✅ | ✅ | ✅ | ✅ |
| 11 | Application starts, health endpoint reachable | ✅ | ✅ | ✅ | ✅ | ✅ |
| 12 | Existing middleware continues to function | ✅ | ✅ | ✅ | ✅ | ✅ |
| 13 | Tests updated for endpoint removal | ✅ | ✅ | ✅ | ✅ | ✅ |
| 14 | All automated tests pass | ✅ | ✅ | ✅ | ✅ | ✅ |
