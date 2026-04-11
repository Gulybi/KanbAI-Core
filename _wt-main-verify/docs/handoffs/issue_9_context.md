# Context Handoff: Issue #9 - Implement Global Exception Handling Middleware

## Title & Business Value
**Title:** Implement Global Exception Handling Middleware
**Who:** API consumers (front-end applications, integrations) and the engineering team maintaining KanbAI-Core.
**Why:** Without a centralized exception handling layer, any unhandled exception in the API will surface raw framework error pages or, worse, leak internal stack traces, class names, and connection details to the client. This is both a security risk and a poor developer experience for API consumers. A global exception handler ensures that every failure—whether an unexpected null reference, a database timeout, or a validation gap—is caught in one place, logged for the engineering team, and returned to the caller as a clean, predictable response. This is a foundational reliability and security concern that must be in place before any domain-specific endpoints are built.

## Current State vs. Desired State

**Current State:**
- The `Program.cs` pipeline contains only `app.UseHttpsRedirection()`. There is no `UseExceptionHandler`, `UseDeveloperExceptionPage`, `UseStatusCodePages`, or any custom `UseMiddleware<T>()` registration.
- No middleware classes exist anywhere in the project (no `Middleware/` directory, no `IMiddleware` or `IExceptionHandler` implementations).
- The project already has a standardised `ApiResponse` / `ApiResponse<T>` wrapper in `DTOs/ApiResponse.cs` with `Fail(string)` and `Fail(IEnumerable<string>)` factory methods. This wrapper is the established contract for communicating errors to callers.
- The project's coding standards (`.github/.cursor/rules/rule/code_standards.mdc`) already instruct developers to rely on global exception middleware and to avoid broad `try/catch` blocks in controllers or minimal API handlers—but the middleware those standards reference does not yet exist.
- If an unhandled exception occurs today, ASP.NET Core's default behaviour will return an HTML error page (development) or an empty 500 response (production), neither of which matches the project's `ApiResponse` contract.

**Desired State:**
- A global exception handling mechanism intercepts every unhandled exception before ASP.NET Core's default error handling takes over.
- Caught exceptions are logged with sufficient detail (exception type, message, stack trace, request path) for the engineering team to diagnose issues from server-side logs.
- The client receives a standardised error response using the project's existing `ApiResponse` format (i.e., `Success = false`, an appropriate error message, and no leaked internal details such as stack traces, class names, or connection strings).
- In the Development environment, additional diagnostic detail (e.g., exception type or message) may optionally be included in the response to aid local debugging, but stack traces must never be sent to the client.
- The HTTP status code returned for unhandled exceptions is `500 Internal Server Error` (or a more specific code if the exception type warrants it).
- The API never returns raw HTML error pages or framework-default error bodies.

## Acceptance Criteria
- [ ] A global exception handling mechanism is implemented (custom middleware and/or `IExceptionHandler`) that catches all unhandled exceptions across the API.
- [ ] The handler returns responses using the project's existing `ApiResponse` format (`Success = false`, human-readable message, no internal details).
- [ ] The HTTP response status code for unhandled exceptions is `500` (Internal Server Error).
- [ ] The response `Content-Type` is `application/json`.
- [ ] Internal implementation details (stack traces, exception types, connection strings) are never exposed to the client in non-Development environments.
- [ ] Exceptions are logged server-side with sufficient detail for diagnosis (exception type, message, stack trace, request path).
- [ ] The middleware is registered in the request pipeline (`Program.cs`) in the correct order (before routing/endpoints).
- [ ] The existing `/weatherforecast` endpoint and all current tests continue to pass without modification.
- [ ] The solution compiles successfully.
