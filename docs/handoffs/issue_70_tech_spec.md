# Technical Specification: Issue #70 - Fix 401 Unauthorized Error on Login and Audit API Authorization Requirements

**GitHub Issue:** [#70 - Fix 401 Unauthorized Error on Login and Audit API Authorization Requirements](https://github.com/Gulybi/KanbAI-Core/issues/70)  
**Context Document:** [issue_70_context.md](./issue_70_context.md)  
**Staff Engineer:** @agent_staff_engineer  
**Created:** 2026-05-02

---

## 1. Overview

This specification defines a critical fix for a production blocker where users cannot authenticate via the `/api/auth/login` and `/api/auth/register` endpoints due to conflicting authentication and authorization configurations. The root cause is a dual authentication scheme (JWT Bearer + Negotiate) and a global fallback authorization policy that requires all endpoints to be authorized by default, including public authentication endpoints.

**Scope:**
- Remove Negotiate authentication registration from `AddAuthServices()` method in `ServiceCollectionExtensions.cs`
- Remove the global fallback authorization policy (or set it to `null`)
- Add `[AllowAnonymous]` attribute to the `AuthController` class to explicitly mark authentication endpoints as public
- Verify all other controllers maintain proper authorization attributes (`[Authorize]` or `[AllowAnonymous]`)

**Out of Scope:**
- No database changes, entity changes, or EF Core configuration changes
- No DTO changes
- No service layer logic changes
- No new endpoints or business logic
- No changes to JWT Bearer authentication configuration (this is correct and should remain)

**Why This is Configuration-Only:**
This issue involves only authentication middleware configuration and controller attributes. The JWT Bearer authentication in `Program.cs` is correctly configured and should remain untouched. The bug is caused by conflicting Negotiate authentication and an overly restrictive global authorization policy.

**Design Note - Explicit Authorization Over Implicit:**
This specification follows the principle of explicit authorization attributes on controllers rather than relying on global fallback policies. This makes security requirements visible in the code and prevents accidental lockout of public endpoints.

---

## 2. Database/Domain Design

**N/A** - No database or domain changes required. This issue is purely configuration and attribute-based.

---

## 3. API Contracts

**N/A** - No API contract changes. All endpoints remain at their existing routes with the same request/response DTOs. The only change is authorization enforcement behavior:

- `/api/auth/register` (POST) - Will be accessible without authentication (currently blocked)
- `/api/auth/login` (POST) - Will be accessible without authentication (currently blocked)
- `/api/health` (GET) - Will remain accessible without authentication (currently working)
- `/api/project/*` - Will remain protected (currently working)
- `/api/task/*` - Will remain protected (currently working)
- `/api/column/*` - Will remain protected (currently working)

---

## 4. Application Layer Boundaries

**N/A** - No service layer changes. This issue affects only the infrastructure/middleware layer (authentication configuration) and the presentation layer (controller attributes).

---

## 5. Implementation Steps

### Step 1: Remove Negotiate Authentication and Fallback Policy from ServiceCollectionExtensions

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Extensions\ServiceCollectionExtensions.cs`

**Current Code (lines 37-49):**
```csharp
/// <summary>
/// Registers Negotiate authentication and authorization with a fallback policy.
/// </summary>
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
```

**New Code:**
```csharp
/// <summary>
/// Registers authorization services without a global fallback policy.
/// Controllers must explicitly declare authorization requirements using [Authorize] or [AllowAnonymous].
/// </summary>
public static IServiceCollection AddAuthServices(this IServiceCollection services)
{
    services.AddAuthorization();
    return services;
}
```

**Rationale:**
- Removing `services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate()` eliminates the Negotiate authentication scheme that conflicts with JWT Bearer (configured in `Program.cs` lines 23-42).
- Removing `options.FallbackPolicy = options.DefaultPolicy` eliminates the implicit "authorize all endpoints by default" behavior that was blocking public endpoints.
- The `AddAuthorization()` call without options registers the authorization middleware services, which are still required for the `[Authorize]` attribute to function on protected controllers.
- JWT Bearer authentication remains the sole authentication scheme (configured in `Program.cs` and not modified by this change).

**Note:** Remove the `using Microsoft.AspNetCore.Authentication.Negotiate;` directive at the top of the file (line 3) as it is no longer needed.

### Step 2: Add [AllowAnonymous] Attribute to AuthController

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Controllers\AuthController.cs`

**Current Code (line 11):**
```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController(
```

**New Code:**
```csharp
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController(
```

**Rationale:**
- Adding `[AllowAnonymous]` at the class level applies to all endpoints in the controller (both `/register` and `/login`).
- This makes the security requirement explicit and visible in the code.
- Class-level attribute is preferred over method-level attributes to reduce duplication (both endpoints need the same behavior).

**Note:** Ensure the `using Microsoft.AspNetCore.Authorization;` directive is present at the top of the file. (It may need to be added if not already present.)

### Step 3: Verify Authorization Attributes on All Controllers

**No code changes required for this step.** This is a verification/audit step to ensure no controllers accidentally lost authorization protection.

**Files to verify:**

| Controller | File Path | Expected Attribute | Current State | Action |
|------------|-----------|-------------------|---------------|--------|
| `AuthController` | `Controllers/AuthController.cs` | `[AllowAnonymous]` | Missing | **Add in Step 2** |
| `HealthController` | `Controllers/HealthController.cs` | `[AllowAnonymous]` | Present (line 9) | No change |
| `ProjectController` | `Controllers/ProjectController.cs` | `[Authorize]` | Present (line 11) | No change |
| `TaskController` | `Controllers/TaskController.cs` | `[Authorize]` | Present (line 11) | No change |
| `ColumnController` | `Controllers/ColumnController.cs` | `[Authorize]` | Present (line 11) | No change |

**Verification Checklist:**
- AuthController: `[AllowAnonymous]` added at class level
- HealthController: `[AllowAnonymous]` already present (no change)
- ProjectController: `[Authorize]` already present (no change)
- TaskController: `[Authorize]` already present (no change)
- ColumnController: `[Authorize]` already present (no change)

### Step 4: Update Integration Test Infrastructure (Optional Cleanup)

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\CustomWebApplicationFactory.cs` (if it exists)

**Note:** The context note mentions that the integration test infrastructure removes Negotiate authentication for TestServer compatibility. Once Negotiate is removed from production code, the test workarounds may no longer be strictly necessary, but they should remain for test isolation and to prevent regression if Negotiate is ever added back.

**Action:** NO CHANGES REQUIRED. Leave existing test infrastructure as-is. The test workarounds are defensive and harmless even after Negotiate is removed from production.

### Step 5: Build and Verify

Run the following command to verify compilation:

```bash
cd c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core
dotnet build --no-incremental
```

**Expected Output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## 6. QA Guidance

### 6.1 Test Files Structure

| Test File | Location | Type | Purpose |
|-----------|----------|------|---------|
| **AuthControllerTests.cs** (new or extend existing) | `KanbAI-Core.Tests/Controllers/` | Unit | Verify `[AllowAnonymous]` attribute is present on AuthController |
| **AuthApiIntegrationTests.cs** (new or extend existing) | `KanbAI-Core.Tests/Integration/` | Integration | Verify `/api/auth/register` and `/api/auth/login` accept unauthenticated requests and return proper responses |
| **AuthorizationAttributeAuditTests.cs** (new) | `KanbAI-Core.Tests/Security/` | Unit | Verify all controllers have explicit authorization attributes (fail if a controller is missing `[Authorize]` or `[AllowAnonymous]`) |
| **ProtectedEndpointsIntegrationTests.cs** (extend existing) | `KanbAI-Core.Tests/Integration/` | Integration | Verify protected endpoints still return 401 for unauthenticated requests |

### 6.2 Test Cases

#### AuthApiIntegrationTests.cs (Integration Tests)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 1 | `PostRegister_ValidRequest_Returns201WithToken` | Auth Success | Unauthenticated POST to `/api/auth/register` with valid data returns 201 Created with JWT token |
| 2 | `PostLogin_ValidCredentials_Returns200WithToken` | Auth Success | Unauthenticated POST to `/api/auth/login` with valid credentials returns 200 OK with JWT token |
| 3 | `PostLogin_InvalidCredentials_Returns401` | Auth Failure | Unauthenticated POST to `/api/auth/login` with invalid credentials returns 401 Unauthorized (from application logic, not middleware) |
| 4 | `PostRegister_DuplicateEmail_Returns400` | Validation | Unauthenticated POST to `/api/auth/register` with existing email returns 400 Bad Request |

#### ProtectedEndpointsIntegrationTests.cs (Integration Tests)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 5 | `GetProjects_Unauthenticated_Returns401` | Security | Unauthenticated GET to `/api/project` returns 401 Unauthorized |
| 6 | `PostTask_Unauthenticated_Returns401` | Security | Unauthenticated POST to `/api/task/column/{columnId}` returns 401 Unauthorized |
| 7 | `GetHealth_Unauthenticated_Returns200` | Public Endpoint | Unauthenticated GET to `/api/health` returns 200 OK (remains public) |
| 8 | `GetProjects_Authenticated_ReturnsProjects` | Auth Success | Authenticated GET to `/api/project` with valid JWT returns 200 OK (protected endpoints still work) |

#### AuthorizationAttributeAuditTests.cs (Unit Tests - Reflection-Based)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 9 | `AllControllers_HaveExplicitAuthorizationAttribute` | Security Audit | Scan all `*Controller` classes; assert each has either `[Authorize]` or `[AllowAnonymous]` at class level |
| 10 | `AuthController_HasAllowAnonymousAttribute` | Security Audit | Verify `AuthController` has `[AllowAnonymous]` attribute |
| 11 | `HealthController_HasAllowAnonymousAttribute` | Security Audit | Verify `HealthController` has `[AllowAnonymous]` attribute |
| 12 | `ProjectController_HasAuthorizeAttribute` | Security Audit | Verify `ProjectController` has `[Authorize]` attribute |
| 13 | `TaskController_HasAuthorizeAttribute` | Security Audit | Verify `TaskController` has `[Authorize]` attribute |
| 14 | `ColumnController_HasAuthorizeAttribute` | Security Audit | Verify `ColumnController` has `[Authorize]` attribute (if exists) |

### 6.3 Test Infrastructure Notes

**Integration Test Setup:**
- Use the pattern from `integration-testing.md` (remove Negotiate auth, register TestAuthHandler, set FallbackPolicy = null).
- After this fix, the production code will no longer register Negotiate, so the test setup's Negotiate removal becomes redundant but harmless.

**Reflection-Based Audit Test Pattern:**
```csharp
[Fact]
public void AllControllers_HaveExplicitAuthorizationAttribute()
{
    var assembly = typeof(Program).Assembly;
    var controllers = assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller"))
        .ToList();

    foreach (var controller in controllers)
    {
        var hasAuthorize = controller.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Any();
        var hasAllowAnonymous = controller.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).Any();

        Assert.True(hasAuthorize || hasAllowAnonymous,
            $"Controller {controller.Name} is missing [Authorize] or [AllowAnonymous] attribute.");
    }
}
```

**Manual Testing Checklist:**
1. Start the application (`dotnet run`)
2. Use Postman/curl to POST to `/api/auth/register` without Authorization header - should return 201
3. Use Postman/curl to POST to `/api/auth/login` without Authorization header - should return 200 or 401 (based on credentials)
4. Use Postman/curl to GET `/api/health` without Authorization header - should return 200
5. Use Postman/curl to GET `/api/project` without Authorization header - should return 401
6. Use Postman/curl to GET `/api/project` with valid JWT token - should return 200 with projects

---

## 7. Known Caveats

| Caveat | Impact | Mitigation |
|--------|--------|-----------|
| **Removal of fallback policy enables "fail-open" by default** | New controllers without explicit `[Authorize]` or `[AllowAnonymous]` will be unprotected (accessible without authentication). | **Addressed:** Security audit test (Test #9) will fail if a controller is missing an explicit authorization attribute. This shifts security enforcement from runtime (fallback policy) to compile-time/test-time (code review + automated test). |
| **Integration test workarounds for Negotiate may become redundant** | Test infrastructure removes Negotiate auth; after this fix, production code won't have Negotiate either. | **No action required:** Test workarounds are defensive and harmless. They prevent regression if Negotiate is ever re-introduced. |
| **JWT token validation still requires proper configuration** | If `JwtSettings` are misconfigured (wrong secret, issuer, audience), authentication will still fail. | **Out of scope:** JWT configuration in `Program.cs` is correct. This issue only fixes the authorization layer, not JWT validation. |
| **Expired or malformed tokens will return 401** | Expected behavior. JWT Bearer middleware handles token validation. | **No mitigation needed:** This is correct behavior per context note (Edge Cases section). |
| **No audit log for authentication configuration changes** | No runtime tracking of who changed authentication settings. | **Out of scope:** Configuration changes are tracked via git history and code review. |

---

## 8. Design Validation Self-Check

| Check | Question | Result |
|-------|----------|--------|
| **Namespaces** | Do all referenced namespaces match the project's `RootNamespace` and existing conventions? | ✅ Yes - `using Microsoft.AspNetCore.Authorization;` is standard; `using Microsoft.AspNetCore.Authentication.Negotiate;` will be removed |
| **Folder Paths** | Do all file paths reference real folders, or are "create new" steps included? | ✅ Yes - `Extensions/`, `Controllers/` exist; test folders may need creation (Step 6.1 specifies locations) |
| **Dependencies** | Are all required NuGet packages already in the `.csproj`, or are install steps included? | ✅ Yes - `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Authentication.Negotiate`, `Microsoft.AspNetCore.Authorization` are already in the project |
| **Naming Conflicts** | Do any new class/enum names conflict with existing types in the codebase? | ✅ N/A - No new types; only modifying existing code and adding attributes |
| **BaseEntity Compliance** | Do new entities inherit from `BaseEntity` and avoid re-declaring common properties? | ✅ N/A - No entity changes |
| **Code Standards** | Does the design comply with standards (file-scoped namespaces, no blocking async, constructor injection)? | ✅ Yes - No async code changes; only configuration and attributes |
| **Security** | Does the design comply with security practices (no hardcoded secrets, no PII in logs, secure DTOs)? | ✅ Yes - This fix IMPROVES security by preventing accidental lockout and requiring explicit authorization attributes. Shift from implicit (global policy) to explicit (per-controller attributes) improves auditability. |

**Result:** All checks pass. Design is ready for implementation.

---

## Summary of Design Decisions

1. **Remove Negotiate Authentication Entirely:** The dual authentication scheme (JWT Bearer + Negotiate) is the root cause. Negotiate is not needed for this application and conflicts with TestServer. Remove it from `AddAuthServices()`.

2. **Remove Global Fallback Authorization Policy:** The fallback policy (`options.FallbackPolicy = options.DefaultPolicy`) was causing all endpoints to require authorization by default, including public authentication endpoints. Removing it shifts the model from "deny all, allow some" (implicit) to "allow all, deny some" (explicit via attributes).

3. **Explicit Authorization Attributes:** Require all controllers to have either `[Authorize]` or `[AllowAnonymous]` at the class level. This makes security requirements visible in the code and prevents accidental exposure. A security audit test enforces this at test-time.

4. **Class-Level [AllowAnonymous] on AuthController:** Both `/register` and `/login` need to be public. Applying `[AllowAnonymous]` at the class level is cleaner than applying it to each method individually.

5. **No Changes to JWT Bearer Configuration:** The JWT Bearer authentication in `Program.cs` (lines 23-42) is correct and should remain untouched. This fix only removes the conflicting Negotiate scheme and the overly restrictive authorization policy.

6. **Test Infrastructure Remains Unchanged:** Integration test workarounds for Negotiate are defensive and should remain even after Negotiate is removed from production code. They prevent regression if Negotiate is ever re-introduced.

---

The technical specification is saved. You can now instruct the developer to read the tech spec and begin implementation.

---

## Development Status

### Files Created

No new files were created as part of this implementation.

### Files Modified

| File Path | Changes Made |
|-----------|--------------|
| `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs` | Removed `using Microsoft.AspNetCore.Authentication.Negotiate;` directive. Updated `AddAuthServices()` method to remove Negotiate authentication registration and global fallback authorization policy. Method now only calls `services.AddAuthorization();`. Updated XML doc summary to reflect new behavior. |
| `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/AuthController.cs` | Added `using Microsoft.AspNetCore.Authorization;` directive. Added `[AllowAnonymous]` attribute at class level to allow unauthenticated access to `/register` and `/login` endpoints. |
| `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core.Tests/Extensions/ServiceCollectionExtensionTests.cs` | Updated `AddAuthServices_RegistersAuthentication` test to `AddAuthServices_DoesNotRegisterAuthentication` to reflect the new behavior where authentication is not registered by `AddAuthServices()`. Test now asserts that `IAuthenticationSchemeProvider` is NOT registered, which aligns with the design decision to handle authentication separately in `Program.cs`. |

### Build & Test Results

**Build Status:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:08.02
```

**Test Results:**
```
Passed!  - Failed:     0, Passed:   390, Skipped:     2, Total:   392, Duration: 3 s
```

All tests pass. No new test failures introduced. The test `AddAuthServices_RegistersAuthentication` was updated to `AddAuthServices_DoesNotRegisterAuthentication` to align with the new implementation where `AddAuthServices()` no longer registers authentication services.

### Infrastructure Notes

**Test Regression Fix:**
- The pre-existing test `AddAuthServices_RegistersAuthentication` was checking that `IAuthenticationSchemeProvider` was registered by `AddAuthServices()`. 
- Per the tech spec, `AddAuthServices()` no longer registers authentication (Negotiate was removed).
- The test was updated to `AddAuthServices_DoesNotRegisterAuthentication` to verify that authentication is NOT registered by this method.
- **Justification:** This aligns with the design decision documented in the tech spec (Section 8, Summary of Design Decisions #5): "The JWT Bearer authentication in `Program.cs` is correct and should remain untouched. This fix only removes the conflicting Negotiate scheme and the overly restrictive authorization policy."

No other infrastructure changes or workarounds were required outside the tech spec's scope.

### Edge Cases for QA

1. **Public Endpoints (AuthController):**
   - Verify `/api/auth/register` (POST) accepts requests without an Authorization header and returns 201 Created with a JWT token for valid input.
   - Verify `/api/auth/login` (POST) accepts requests without an Authorization header and returns 200 OK with a JWT token for valid credentials, or 401 Unauthorized for invalid credentials (application-level validation, not middleware).

2. **Protected Endpoints:**
   - Verify `/api/project`, `/api/task/*`, and `/api/column/*` return 401 Unauthorized when accessed without a valid JWT token.
   - Verify the same endpoints return 200 OK (or appropriate status codes) when accessed with a valid JWT token in the Authorization header (`Bearer <token>`).

3. **Health Check Endpoint:**
   - Verify `/api/health` (GET) remains accessible without authentication and returns 200 OK.

4. **Invalid or Expired JWT Tokens:**
   - Verify that protected endpoints return 401 Unauthorized when accessed with an expired JWT token.
   - Verify that protected endpoints return 401 Unauthorized when accessed with a malformed JWT token.

5. **Authorization Attribute Audit:**
   - Verify that all controllers have either `[Authorize]` or `[AllowAnonymous]` at the class level.
   - Ensure no new controllers can be added without explicit authorization attributes (this should be enforced by the security audit test described in the QA Guidance section of the tech spec).

6. **Regression Prevention:**
   - Verify that the removal of the global fallback policy does not inadvertently expose any endpoints that should be protected.
   - Verify that JWT Bearer authentication still functions correctly (token validation, claims extraction, etc.).

---

**Implementation completed on:** 2026-05-02  
**Developer:** Senior .NET Developer (Claude Code Agent)  
**Status:** Ready for QA Review and Automated Test Implementation

---

## QA Status

**QA Testing completed on:** 2026-05-02  
**QA Engineer:** Senior QA Engineer (Claude Code Agent)  
**Status:** Implementation verified. All acceptance criteria met. All 16 new tests and the entire test suite pass (406/406 executable tests).

### Test Files Created

| Test File | Location | Tests Added | Purpose |
|-----------|----------|-------------|---------|
| `AuthApiIntegrationTests.cs` | `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Integration\` | 4 | Integration tests for authentication endpoints (`/api/auth/register` and `/api/auth/login`) to verify they accept unauthenticated requests and return proper responses |
| `ProtectedEndpointsIntegrationTests.cs` | `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Integration\` | 4 | Integration tests to verify protected endpoints still require authentication and public endpoints remain accessible after the authorization configuration fix |
| `AuthorizationAttributeAuditTests.cs` | `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Security\` | 8 | Reflection-based security audit tests to verify all controllers have explicit authorization attributes (`[Authorize]` or `[AllowAnonymous]`) at class level |

**Total new tests:** 16

### Test Results

**Overall Test Suite:**
- **Total tests:** 408 (increased from 392 before QA)
- **Passed:** 406
- **Failed:** 0
- **Skipped:** 2 (pre-existing `ScalarApiReferenceTests` — unrelated to this issue)
- **Test Duration:** ~4 seconds

**New Test Results by Category:**

| Test Category | Total | Passed | Failed | Notes |
|---------------|-------|--------|--------|-------|
| `AuthorizationAttributeAuditTests` | 8 | 8 | 0 | All security audit tests pass. Confirms all controllers have explicit authorization attributes. |
| `ProtectedEndpointsIntegrationTests` | 4 | 4 | 0 | Unauthenticated endpoints return 401, public health endpoint returns 200, authenticated GET /api/project returns 200 with wrapped `ApiResponse<List<ProjectResponseDto>>`. |
| `AuthApiIntegrationTests` | 4 | 4 | 0 | Register/login flows validated against an isolated in-memory EF Core database per test. |

### Bugs Found & Fixed

**No implementation bugs found.** The implementation correctly follows the technical specification:

1. `ServiceCollectionExtensions.AddAuthServices()` no longer registers Negotiate authentication
2. `ServiceCollectionExtensions.AddAuthServices()` no longer sets a fallback authorization policy
3. `AuthController` has `[AllowAnonymous]` attribute at class level
4. All other controllers (`ProjectController`, `TaskController`, `ColumnController`) have `[Authorize]` attribute
5. `HealthController` maintains `[AllowAnonymous]` attribute

### Outstanding Issues

**None.** All 16 new tests pass, and the full suite (406 executable tests) is green.

**Infrastructure Note — EF Core Provider Swap:**

The new integration tests that exercise persistence (`AuthApiIntegrationTests` register/login flows and `ProtectedEndpointsIntegrationTests.GetProjects_Authenticated_ReturnsProjects`) swap the production `UseSqlServer` registration for `UseInMemoryDatabase` at test time. Because the `InMemory` package is already referenced by the test project, the swap removes all existing `Microsoft.EntityFrameworkCore.*` service descriptors before re-registering the DbContext with a unique per-test database name. This avoids the "multiple providers registered" `InvalidOperationException` and keeps each test isolated.

Pure authorization tests (`AuthorizationAttributeAuditTests` and the unauthenticated branches of `ProtectedEndpointsIntegrationTests`) do not reach the database, so they do not need the provider swap.

**Acceptance Criteria Status:**

| Criterion | Status | Evidence |
|-----------|--------|----------|
| 1. Authentication Configuration Fixed | ✅ PASS | Code review confirms Negotiate authentication removed, fallback policy removed |
| 2. AuthController Public Access Restored | ✅ PASS | `AuthorizationAttributeAuditTests.AuthController_HasAllowAnonymousAttribute` passes; `AuthApiIntegrationTests.PostRegister_ValidRequest_Returns201WithToken` and `PostLogin_ValidCredentials_Returns200WithToken` pass against in-memory DB |
| 3. Protected Endpoints Remain Secure | ✅ PASS | `ProtectedEndpointsIntegrationTests.GetProjects_Unauthenticated_Returns401` and `PostTask_Unauthenticated_Returns401` pass |
| 4. Health Endpoint Remains Public | ✅ PASS | `ProtectedEndpointsIntegrationTests.GetHealth_Unauthenticated_Returns200` passes |
| 5. No Security Regressions | ✅ PASS | All `AuthorizationAttributeAuditTests` pass (8/8) |

**Manual Testing Recommendation:**

The tech spec (Section 6.3) provides a manual testing checklist. QA recommends running the manual tests to verify end-to-end functionality:

1. Start the application (`dotnet run`)
2. Use Postman/curl to POST to `/api/auth/register` without Authorization header - should return 201
3. Use Postman/curl to POST to `/api/auth/login` without Authorization header - should return 200 or 401 (based on credentials)
4. Use Postman/curl to GET `/api/health` without Authorization header - should return 200
5. Use Postman/curl to GET `/api/project` without Authorization header - should return 401
6. Use Postman/curl to GET `/api/project` with valid JWT token - should return 200 with projects

---

**QA Conclusion:** The implementation meets all acceptance criteria per Issue #70. The authorization configuration has been correctly fixed to allow unauthenticated access to authentication endpoints while maintaining security on protected endpoints. All 16 new tests pass, and the overall suite is green (406 passed, 0 failed, 2 pre-existing skips).
