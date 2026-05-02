# Issue #70: Fix 401 Unauthorized Error on Login and Audit API Authorization Requirements

## Business Value

**Who:** Frontend client users attempting to authenticate
**What:** Users receive a 401 Unauthorized error when attempting to log in via POST /api/auth/login
**Why:** The login endpoint is being incorrectly protected by authorization middleware, preventing legitimate authentication attempts and blocking all user access to the application

This is a critical production blocker that prevents any user from logging into the system.

## Current State vs. Desired State

### Current State (Broken)

**Authentication Configuration Conflict:**
- `Program.cs` (lines 23-42) configures JWT Bearer authentication with proper token validation
- `ServiceCollectionExtensions.cs` `AddAuthServices()` method (lines 40-49) ALSO registers Negotiate authentication and sets a global fallback authorization policy
- The `AddAuthServices()` extension is called in `Program.cs` (line 48), which overrides the default authentication scheme from JwtBearer to Negotiate
- The fallback policy (line 46) requires ALL endpoints to be authorized by default, including public authentication endpoints

**Controller Authorization State:**
- `AuthController.cs`: Has NO `[AllowAnonymous]` attribute on the class or endpoints (Register line 18, Login line 54)
- `HealthController.cs`: Correctly marked with `[AllowAnonymous]` (line 9)
- `ProjectController.cs`: Correctly protected with `[Authorize]` (line 11)
- `TaskController.cs`: Correctly protected with `[Authorize]` (line 11)
- `ColumnController.cs`: Correctly protected with `[Authorize]` (line 11)

**Root Cause:**
The global fallback authorization policy (set in `AddAuthServices()`) applies to all endpoints by default. Since `AuthController` lacks `[AllowAnonymous]` attributes, the `/api/auth/login` and `/api/auth/register` endpoints require authorization before users can authenticate, creating a logical impossibility.

Additionally, the `AddAuthServices()` method registers Negotiate (Windows) authentication, which conflicts with the JWT Bearer authentication configured in `Program.cs`. This dual authentication scheme registration is causing authentication middleware confusion.

### Desired State (Fixed)

**Authentication Configuration:**
- JWT Bearer authentication should be the sole authentication scheme
- Remove Negotiate authentication registration from `AddAuthServices()`
- Remove the global fallback authorization policy (or set it to null) to prevent accidental lockout of public endpoints
- Controllers should explicitly declare their authorization requirements using attributes

**Controller Authorization:**
- `AuthController`: Both `/register` and `/login` endpoints must have `[AllowAnonymous]` to permit unauthenticated access
- All other controllers (Project, Task, Column) should remain protected with `[Authorize]`
- `HealthController`: Should remain public with `[AllowAnonymous]`

**Security Principle:**
Use explicit authorization attributes on controllers/endpoints rather than implicit global policies. This makes security requirements visible and reviewable in the code.

## Acceptance Criteria

### 1. Authentication Configuration Fixed
- The `AddAuthServices()` method in `ServiceCollectionExtensions.cs` no longer registers Negotiate authentication
- The `AddAuthServices()` method no longer sets a fallback authorization policy (or sets it to null)
- JWT Bearer remains the sole authentication scheme configured in `Program.cs`

### 2. AuthController Public Access Restored
- The `AuthController` class or its individual endpoints (`/register` and `/login`) are marked with `[AllowAnonymous]`
- An unauthenticated POST request to `/api/auth/register` with valid registration data returns 201 Created with a JWT token
- An unauthenticated POST request to `/api/auth/login` with valid credentials returns 200 OK with a JWT token
- An unauthenticated POST request to `/api/auth/login` with invalid credentials returns 401 Unauthorized (from application logic, not authorization middleware)

### 3. Protected Endpoints Remain Secure
- An unauthenticated GET request to `/api/project` returns 401 Unauthorized
- An unauthenticated POST request to `/api/task/column/{columnId}` returns 401 Unauthorized
- An authenticated request with a valid JWT token to `/api/project` returns 200 OK with project data (or empty array if no projects exist)
- An authenticated request with a valid JWT token to protected endpoints succeeds based on business logic authorization rules

### 4. Health Endpoint Remains Public
- An unauthenticated GET request to `/api/health` returns 200 OK

### 5. No Security Regressions
- Previously protected endpoints (Project, Task, Column controllers) are still decorated with `[Authorize]` at the class level
- No controller has accidentally had authorization removed

## Relevant Files

### Authentication & Authorization Configuration
- `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Program.cs` (lines 23-48, 66-67)
- `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs` (lines 40-49)

### Controllers Requiring Audit
- `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/AuthController.cs` (missing `[AllowAnonymous]`)
- `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/HealthController.cs` (correctly public)
- `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/ProjectController.cs` (correctly protected)
- `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs` (correctly protected)
- `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/ColumnController.cs` (correctly protected)

## Edge Cases & Considerations

### Edge Case: Expired or Malformed JWT Tokens
- A request with an expired JWT token to a protected endpoint should return 401 Unauthorized
- A request with a malformed JWT token to a protected endpoint should return 401 Unauthorized
- These scenarios should be handled by the JWT Bearer authentication middleware, not application logic

### Edge Case: Missing Authorization Header
- A request to a protected endpoint without an `Authorization` header should return 401 Unauthorized
- This is the expected behavior and should remain unchanged

### Consideration: Future Endpoints
- Developers adding new controllers must explicitly choose `[Authorize]` or `[AllowAnonymous]`
- Without the fallback policy, forgetting to add `[Authorize]` would leave endpoints unprotected (fail-open), which is a security risk
- This should be addressed through code review processes and documented coding standards, not through automatic global policies that can lock out public endpoints

### Consideration: Integration Test Compatibility
- The project has integration test infrastructure (`CustomWebApplicationFactory.cs`) that removes Negotiate authentication for TestServer compatibility
- Once Negotiate is removed from production code, the test workarounds may no longer be necessary (but should remain for test isolation)
