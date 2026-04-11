# Context Handoff: Issue #10 - Configure Core Middleware (Swagger & CORS)

## Title & Business Value
**Title:** Configure Core Middleware (Swagger & CORS)
**Who:** Developers working on KanbAI-Core and the Angular frontend application that will consume this API.
**Why:** Two foundational developer-experience and integration concerns must be resolved before domain-specific endpoints are built:

1. **Swagger / OpenAPI UI:** The API currently exposes raw OpenAPI metadata (`app.MapOpenApi()`), but there is no interactive Swagger UI for developers to browse, test, and debug endpoints from a browser. Without a visual testing harness, developers must rely on external tools (Postman, curl) for every request, slowing down the feedback loop during local development.

2. **CORS (Cross-Origin Resource Sharing):** The Angular frontend will run on `http://localhost:4200` during development. Browsers enforce the same-origin policy and will block any XHR/Fetch request from the frontend to this backend unless the backend explicitly allows the frontend's origin. Without a CORS policy, the frontend-backend integration cannot function at all—requests will fail before they even reach the API pipeline.

Both of these are zero-feature-value prerequisites: they do not deliver user-facing functionality, but they unblock every future feature that involves an API endpoint.

## Current State vs. Desired State

**Current State:**
- `Program.cs` registers `builder.Services.AddOpenApi()` and conditionally maps it in Development via `app.MapOpenApi()`. This exposes the raw OpenAPI JSON document (typically at `/openapi/v1.json`), but provides **no interactive UI** for exploring or testing endpoints.
- There is **no Swagger UI** middleware registered (no `UseSwaggerUI`, no Swashbuckle, no NSwag UI).
- There is **no CORS configuration** anywhere in the project—no `AddCors()` service registration, no `UseCors()` middleware call, and no named or default CORS policies.
- The request pipeline order is: `UseExceptionHandler()` → `UseHttpsRedirection()` → endpoint mappings.
- Authentication is configured using Windows/Negotiate auth with a fallback authorization policy requiring all requests to be authorized.

**Desired State:**
- An interactive Swagger UI is available in the Development environment, allowing developers to browse all registered endpoints, inspect request/response schemas, and execute test requests directly from the browser.
- A named CORS policy is defined that permits the Angular frontend origin (`http://localhost:4200`) to make cross-origin requests to this API. The policy should allow the HTTP methods and headers required for a typical SPA-to-API integration (e.g., GET, POST, PUT, DELETE, PATCH; standard + JSON content-type headers).
- The CORS middleware is registered in the correct position in the request pipeline so that preflight (OPTIONS) requests are handled before authentication or authorization rejects them.
- Both Swagger UI and CORS are restricted to the Development environment (or at minimum, the CORS origin list is environment-aware) so that production deployments do not inadvertently expose a wide-open CORS policy or a public Swagger page.

## Acceptance Criteria
- [ ] Swagger UI is accessible at a well-known URL (e.g., `/swagger`) when running in the Development environment.
- [ ] Swagger UI displays all registered API endpoints with their request/response schemas.
- [ ] Swagger UI is **not** exposed in non-Development environments (Production, Staging).
- [ ] A CORS policy is defined that allows the Angular frontend origin `http://localhost:4200`.
- [ ] The CORS policy allows at minimum the HTTP methods: GET, POST, PUT, DELETE, PATCH.
- [ ] The CORS policy allows the headers necessary for JSON API communication (e.g., `Content-Type`, `Authorization`).
- [ ] The CORS middleware is placed in the pipeline so that preflight OPTIONS requests are handled correctly (i.e., before `UseAuthorization`).
- [ ] The existing `/weatherforecast` endpoint and all current tests continue to pass without modification.
- [ ] The solution compiles successfully.
