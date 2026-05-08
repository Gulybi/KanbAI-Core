# Issue #85: POST /api/project/{projectId}/members Returns 401 on Invite Failures Instead of Documented 400/403

## Business Value

**Who:** Project owners using the member invitation feature to add collaborators to their projects.

**What:** Fix the POST /api/project/{projectId}/members endpoint so that business-logic failures (email not registered, user already a member, non-owner attempting invite) return the correct HTTP status codes (400/403) instead of 401 Unauthorized.

**Why:** This issue creates three critical problems:

1. **User Experience Breakdown:** Users see a generic "Your session has expired. Please sign in again." error when they enter an unregistered email address or attempt other invalid operations, instead of seeing the specific reason their invitation failed (e.g., "No user found with email address: user@example.com").

2. **Dead Frontend Code:** The frontend already implements error-handling logic in `members-api.service.ts` with a `mapMemberErrorToUserMessage` function that branches on documented error shapes (400 for user not found, 403 for non-owner, etc.). These branches are unreachable because the backend returns 401 for these cases.

3. **Operational Blind Spot:** When this bug is fixed system-wide, monitoring dashboards tracking 401 counts will correctly reflect authentication failures (expired/missing JWT) rather than being polluted with false positives from business-logic validation failures. This improves observability and incident response.

**API Contract:** The `.claude/backend_api_map.md` documentation (lines 63-74) explicitly defines the error contract for this endpoint. Currently, the implementation violates that contract, creating a trust gap between documentation and actual behavior.

**Root Cause (Likely):** Based on the issue description, the most likely causes are:
- The `ResolveUserIdAsync` method in ProjectService.cs throws an exception (e.g., database query failure, null reference) when an email is not found, and this exception is caught by middleware that converts it to 401 because the `[Authorize]` attribute is still active.
- Model binding for `AddMemberDto` fails validation (e.g., malformed email, missing required fields), and ASP.NET Core returns 401 instead of 400.
- The GlobalExceptionHandler is inadvertently catching service-layer business exceptions and returning 401 instead of letting the controller return the appropriate status code.

**Impact:** This is a **high-priority bug** because:
- It affects every project owner who attempts to invite a user that doesn't exist (a common scenario during onboarding).
- It degrades user trust in the application (users see "session expired" when their session is valid).
- It blocks the frontend from providing clear, actionable error messages.
- It is a regression risk for other authenticated endpoints if the same pattern is present elsewhere.

## Current State vs. Desired State

### Current State (401 Returned for Business-Logic Failures)

**POST /api/project/{projectId}/members Endpoint:**
- **Controller Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/ProjectController.cs (lines 95-123)
- **Service Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Projects/ProjectService.cs (lines 355-401)

**Current Behavior:**
The endpoint is documented to return:
- `403` — Caller is not project owner
- `400` — Email not registered, user already a member, missing/invalid input
- `404` — Project not found
- `201` — Success

**However, users report receiving `401 Unauthorized` when submitting a valid owner JWT with an unregistered email address, instead of the documented `400` with message "No user found with email address: {email}".**

**Controller Error Handling (Lines 105-120):**
```csharp
if (member == null)
{
    if (errorMessage == "Only the project owner can add members.")
    {
        return StatusCode(403, ApiResponse.Fail(errorMessage));
    }
    if (errorMessage == "User not found." ||
        errorMessage == "User is already a member of this project." ||
        errorMessage!.StartsWith("No user found with email address:") ||
        errorMessage == "Provide either UserId or Email, not both." ||
        errorMessage == "Either UserId or Email is required.")
    {
        return BadRequest(ApiResponse.Fail(errorMessage));
    }
    return NotFound(ApiResponse.Fail(errorMessage!));
}
```

The controller correctly maps error messages to HTTP status codes. This means the issue is NOT in the controller's error-handling logic.

**Service Layer (AddMemberAsync Overload with AddMemberDto):**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Projects/ProjectService.cs (lines 355-367)
- **Flow:**
  1. Controller extracts `requestingUserId` from JWT via `GetCurrentUserId()` (line 98)
  2. Controller calls `_projectService.AddMemberAsync(projectId, dto, requestingUserId)` (line 100-103)
  3. Service calls `ResolveUserIdAsync(dto.UserId, dto.Email)` (line 360)
  4. If `ResolveUserIdAsync` returns `(null, errorMessage)`, service returns `(null, errorMessage)` (line 362-363)
  5. Otherwise, service delegates to the core `AddMemberAsync(projectId, userId, requestingUserId)` overload (line 366)

**ResolveUserIdAsync Method (Lines 369-401):**
```csharp
private async Task<(Guid? userId, string? errorMessage)> ResolveUserIdAsync(
    Guid? userIdFromDto,
    string? emailFromDto)
{
    if (userIdFromDto.HasValue && !string.IsNullOrWhiteSpace(emailFromDto))
    {
        return (null, "Provide either UserId or Email, not both.");
    }

    if (!userIdFromDto.HasValue && string.IsNullOrWhiteSpace(emailFromDto))
    {
        return (null, "Either UserId or Email is required.");
    }

    if (userIdFromDto.HasValue)
    {
        return (userIdFromDto.Value, null);
    }

    var trimmedEmail = emailFromDto!.Trim();
    var user = await _context.Users
        .AsNoTracking()
        .FirstOrDefaultAsync(u => u.Email.ToLower() == trimmedEmail.ToLower());

    if (user == null)
    {
        _logger.LogWarning("No user found with email address: {Email}", trimmedEmail);
        return (null, $"No user found with email address: {trimmedEmail}");
    }

    _logger.LogInformation("Resolved email {Email} to user {UserId}", trimmedEmail, user.Id);
    return (user.Id, null);
}
```

**Analysis:** The method returns a tuple with `(null, "No user found with email address: {email}")` when the email is not found. It does NOT throw an exception. This means the service layer is correctly returning an error message to the controller, and the controller should map it to 400 via the `BadRequest` branch (line 111).

**Possible Root Causes (To Verify During Investigation):**

1. **Model Binding Failure:** If the `AddMemberDto` model binding fails (e.g., invalid JSON, missing Content-Type header), ASP.NET Core returns 400 by default, but if the `[Authorize]` middleware rejects the request before model binding, it returns 401. This could happen if:
   - The JWT token is missing the required claims (e.g., `NameIdentifier`).
   - The `GetCurrentUserId()` method throws `UnauthorizedAccessException` (line 173) when the claim is invalid, which might be caught and converted to 401.

2. **GetCurrentUserId() Throws Exception:** The controller's `GetCurrentUserId()` method (lines 166-177) throws `UnauthorizedAccessException` if the `NameIdentifier` claim is missing or invalid. This exception might be caught by ASP.NET Core's authentication middleware and converted to 401 instead of propagating to the GlobalExceptionHandler.

3. **Database Connection Failure in ResolveUserIdAsync:** If the database query at line 389 throws an exception (e.g., connection timeout, EF Core error), the exception propagates up to the controller. If the GlobalExceptionHandler catches it and the environment is Production, it returns 500. If the environment is Development, it might return 500 with the exception details. However, the issue description explicitly states "401 Unauthorized" is returned, not 500.

4. **JWT Validation Failure (Short Lifetime/Clock Skew):** The JWT authentication middleware is configured with `ClockSkew = TimeSpan.Zero` (c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Program.cs line 44), meaning tokens expire exactly at their `exp` claim with no grace period. If the token is expired or has invalid `iss`/`aud`, the request is rejected with 401 BEFORE the controller action runs. The issue description mentions this as a possibility: "If this turns out to be the cause, the fix is on the JWT validation side — treat this issue as a triage spike and split out a follow-up."

**Most Likely Root Cause (Based on Evidence):**
Given that:
- The service layer returns error messages correctly (no exceptions in the "user not found" path).
- The controller maps error messages to status codes correctly.
- Users report 401 for "email not registered" cases.

**The most likely cause is #4: JWT validation is rejecting the request before the controller action runs.** This could happen if:
- The token's lifetime is too short (e.g., expires during the user's form-filling).
- Clock skew between client and server causes premature expiration.
- The token's `iss` or `aud` claims don't match the server configuration.

**However, the issue description states "a valid owner JWT + an unregistered email comes back as 401", which implies the JWT IS valid (otherwise, the 401 would be expected). This suggests the root cause is NOT #4.**

**Second Most Likely Cause:** #2: `GetCurrentUserId()` throws `UnauthorizedAccessException` for some reason, and this exception is caught by middleware and converted to 401. This could happen if:
- The `NameIdentifier` claim is missing from the JWT (JWT validation passes, but the claim is not present).
- The `NameIdentifier` claim is not a valid GUID.

**To Verify:** Add detailed logging to `GetCurrentUserId()` to capture the exact state when the exception is thrown (JWT claims, parsed user ID, etc.).

**AddMemberDto Structure:**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/DTOs/AddMemberDto.cs
- **Properties:**
  - `Guid? UserId` (optional)
  - `string? Email` (optional, validated with `[EmailAddress]` attribute)
- **Validation:** The `[EmailAddress]` attribute ensures the email format is valid if provided. If the email is malformed, ASP.NET Core returns 400 with a validation error before the controller action runs. This is correct behavior and not the bug.

**Authentication Configuration:**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Program.cs (lines 26-62)
- **Default Scheme:** JWT Bearer (line 29-31)
- **Token Validation:**
  - `ValidateIssuer = true` (line 37)
  - `ValidateAudience = true` (line 39)
  - `ValidateLifetime = true` (line 41)
  - `ClockSkew = TimeSpan.Zero` (line 44) — No grace period for expired tokens
- **JWT Settings Source:** `appsettings.json` bound to `JwtSettings` class (line 15-16)
  - `SecretKey`, `Issuer`, `Audience`, `ExpirationMinutes`

**Global Exception Handling:**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Middleware/GlobalExceptionHandler.cs
- **Behavior:** Catches all unhandled exceptions and returns 500 with `ApiResponse.Fail(message)`. In Development, the message includes the exception type and message. In Production, it returns a generic message.
- **Important:** The GlobalExceptionHandler does NOT convert exceptions to 401. It only returns 500.

**Integration Test Evidence:**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core.Tests/Integration/ProjectMemberManagementIntegrationTests.cs
- **Test Pattern:** Tests use a custom `TestAuthHandler` that injects a `NameIdentifier` claim with the test user's GUID (lines 150-174).
- **Unauthenticated Test:** The `AddMember_UnauthenticatedRequest_Returns401` test (lines 53-70) registers the test auth handler WITHOUT a user ID, which causes `AuthenticateResult.NoResult()` to be returned, and ASP.NET Core responds with 401. This confirms that missing authentication returns 401 as expected.

**Conclusion:** The code review suggests the service layer and controller are implemented correctly. The root cause is likely one of:
1. `GetCurrentUserId()` throws `UnauthorizedAccessException` due to missing/invalid `NameIdentifier` claim.
2. JWT validation rejects the token for some reason (expired, wrong issuer/audience) before the controller action runs.
3. A middleware between authentication and the controller is converting legitimate errors to 401.

**This issue is a triage spike.** The first step is to add detailed logging and reproduce the issue to identify the exact point where 401 is returned.

### Desired State (Correct HTTP Status Codes Returned for Business-Logic Failures)

**Expected Behavior Per `.claude/backend_api_map.md` (Lines 63-74):**

| Failure Scenario | Status Code | Response Body |
|---|---|---|
| Caller not owner | 403 | `ApiResponse.Fail("Only the project owner can add members.")` |
| Email not registered | 400 | `ApiResponse.Fail("No user found with email address: {email}")` or `ApiResponse.Fail("User not found.")` |
| User already a member | 400 | `ApiResponse.Fail("User is already a member of this project.")` |
| Missing UserId and Email | 400 | `ApiResponse.Fail("Either UserId or Email is required.")` |
| Both UserId and Email provided | 400 | `ApiResponse.Fail("Provide either UserId or Email, not both.")` |
| Project not found | 404 | `ApiResponse.Fail("Project not found.")` |
| Valid JWT, unhandled error | 500 | `ApiResponse.Fail("An unexpected error occurred. Please try again later.")` (Production) or `ApiResponse.Fail("{ExceptionType}: {Message}")` (Development) |
| Missing / invalid / expired JWT | 401 | (JWT middleware default response — no ApiResponse envelope) |

**Critical Requirement:** 401 Unauthorized MUST be returned ONLY when:
- The `Authorization` header is missing.
- The JWT token is malformed, tampered, or cannot be parsed.
- The JWT token has expired (`exp` claim is in the past).
- The JWT token has invalid `iss` (issuer) or `aud` (audience) claims.

**401 MUST NOT be returned when:**
- The JWT is valid but the user is not a project owner (return 403).
- The JWT is valid but the email address is not registered (return 400).
- The JWT is valid but the user is already a member (return 400).
- The JWT is valid but the input is missing or invalid (return 400).
- The JWT is valid but the project does not exist (return 404).

**Frontend Impact (After Fix):**
Once the backend returns the correct status codes, the frontend's `mapMemberErrorToUserMessage` function in `members-api.service.ts` will correctly display:
- "This user is not registered. Please ask them to sign up first." (for 400 with "No user found with email address:")
- "Only the project owner can add members." (for 403)
- "This user is already a member of this project." (for 400 with "User is already a member")

**Observability Impact (After Fix):**
Monitoring dashboards can reliably use 401 counts as a signal for authentication issues (expired tokens, missing tokens) rather than being polluted with false positives from business-logic validation failures.

**Non-Goals:**
- Do NOT change the frontend error-copy strings. The frontend is already correct.
- Do NOT change JWT lifetime or issuer/audience configuration in this issue (unless investigation confirms they are misconfigured). Treat this as a separate hardening task.
- Do NOT implement silent token refresh. This is out of scope.

## Milestone Context

This issue is **not part of a formal milestone**. However, it is a **bug fix** that affects a core feature (member management) and must be addressed to restore the API contract documented in `.claude/backend_api_map.md`.

### Related Context

| File | Location | Relationship |
|---|---|---|
| `.claude/backend_api_map.md` | Lines 63-74 | **API Contract** — Documents the expected error status codes for POST /api/project/{projectId}/members |
| `members-api.service.ts` (frontend) | N/A (frontend repo) | **Frontend Dependency** — Already implements error-handling logic that expects 400/403, branches are dead until backend catches up |
| PR #74 | N/A | **Frontend Fix** — Tightened frontend so 401 with JWT present no longer logs user out, but still shows generic "session expired" message |

### Implementation Context

**This is a triage and bug-fix issue.** The first step is to reproduce the issue and add detailed logging to identify the root cause. Once the root cause is identified, the fix may be:
- Adding more defensive null checks in `GetCurrentUserId()` to avoid throwing `UnauthorizedAccessException`.
- Adjusting JWT validation configuration (if lifetime/clock skew is too strict).
- Ensuring the GlobalExceptionHandler does not inadvertently convert business exceptions to 401.

If the root cause is JWT validation (expired tokens, wrong issuer/audience), this issue should be treated as a spike, and a follow-up issue should be created to address JWT configuration.

## Acceptance Criteria

### 1. Email Not Registered Returns 400
- [ ] POST /api/project/{projectId}/members with a valid owner JWT and `{ "email": "unregistered@example.com" }` (where the email does NOT exist in the Users table) returns HTTP 400.
- [ ] Response body is `ApiResponse.Fail("No user found with email address: unregistered@example.com")` (exact prefix match on "No user found with email address:").
- [ ] Response does NOT return HTTP 401.

### 2. User Already a Member Returns 400
- [ ] POST /api/project/{projectId}/members with a valid owner JWT and `{ "email": "existing-member@example.com" }` (where the user IS already a member) returns HTTP 400.
- [ ] Response body is `ApiResponse.Fail("User is already a member of this project.")`.

### 3. Non-Owner Caller Returns 403
- [ ] POST /api/project/{projectId}/members with a valid non-owner JWT (authenticated user is a project member but NOT owner) and `{ "email": "any@example.com" }` returns HTTP 403.
- [ ] Response body is `ApiResponse.Fail("Only the project owner can add members.")`.

### 4. Project Not Found Returns 404
- [ ] POST /api/project/{non-existent-guid}/members with a valid owner JWT returns HTTP 404.
- [ ] Response body is `ApiResponse.Fail("Project not found.")`.

### 5. Missing or Invalid JWT Returns 401 (Regression Guard)
- [ ] POST /api/project/{projectId}/members with NO `Authorization` header returns HTTP 401.
- [ ] POST /api/project/{projectId}/members with an expired JWT returns HTTP 401.
- [ ] POST /api/project/{projectId}/members with a tampered JWT (invalid signature) returns HTTP 401.

### 6. All Non-Auth Responses Use ApiResponse Envelope
- [ ] Every 400, 403, 404, and 500 response includes an `ApiResponse` or `ApiResponse<T>` JSON body with `success: false` and a `message` field.
- [ ] 401 responses from JWT middleware do NOT include the ApiResponse envelope (this is ASP.NET Core default behavior and is acceptable).

### 7. Documentation Matches Behavior
- [ ] After the fix, `.claude/backend_api_map.md` (lines 63-74) accurately reflects the actual HTTP status codes returned by the endpoint.
- [ ] If any discrepancies are found during implementation, update either the code or the documentation to match.

### 8. Integration Tests Assert Status and Body
- [ ] Backend integration test: Valid owner JWT + unregistered email → 400 with body containing "No user found with email address:"
- [ ] Backend integration test: Valid owner JWT + already-a-member email → 400 with body containing "User is already a member"
- [ ] Backend integration test: Valid non-owner JWT → 403
- [ ] Backend integration test: Valid owner JWT + non-existent project → 404
- [ ] Backend integration test: No Authorization header → 401
- [ ] Existing integration test `AddMember_UnauthenticatedRequest_Returns401` (lines 53-70 in ProjectMemberManagementIntegrationTests.cs) continues to pass (regression guard).

### 9. Logging Distinguishes 400/403 from 401
- [ ] Server logs at Warning level when a 403 is returned (non-owner attempts to add member): "User {UserId} attempted to add member to project {ProjectId} without Owner role"
- [ ] Server logs at Warning level when a 400 is returned (user not found): "No user found with email address: {Email}"
- [ ] Server logs do NOT log these cases as authentication failures (e.g., "Invalid JWT" or "Missing Authorization header").
- [ ] JWT authentication failures (missing/expired/invalid token) are logged separately by the JWT middleware (not by application code).

### 10. Root Cause Documented in Commit Message
- [ ] The commit message includes a brief explanation of the root cause (e.g., "GetCurrentUserId() was throwing UnauthorizedAccessException when NameIdentifier claim was missing, which was caught by auth middleware and converted to 401").
- [ ] If the root cause is JWT validation (expired token, clock skew), the commit message states "Root cause is JWT configuration, not business logic. Follow-up issue created."

### 11. No Breaking Changes
- [ ] The fix does NOT change the behavior of successful requests (201 with MemberResponseDto).
- [ ] The fix does NOT change the behavior of other endpoints (GET /api/project/{projectId}/members, DELETE /api/project/{projectId}/members/{userId}, etc.).

### 12. Frontend Error Messages Display Correctly (Post-Deployment)
- [ ] After deployment, when a project owner enters an unregistered email in the frontend invite form, the UI displays "This user is not registered. Please ask them to sign up first." (or the exact copy from `mapMemberErrorToUserMessage`) instead of "Your session has expired. Please sign in again."
- [ ] This is a manual acceptance test performed after backend deployment (not an automated test in this issue).

## Acceptance Criteria Quality Gate Validation

| Criterion | Testable | Specific | Independent | Implementation-Free | Complete |
|-----------|----------|----------|-------------|---------------------|----------|
| AC1 (Email not registered → 400) | Yes — HTTP status code is 400, body contains specific prefix | Yes — exact status code and body substring | Yes — single verifiable outcome | Yes — describes HTTP response, not code | Yes — covers primary bug scenario |
| AC2 (Already a member → 400) | Yes — HTTP status code is 400, body contains specific message | Yes — exact status code and message | Yes — single verifiable outcome | Yes — describes HTTP response, not code | Yes — covers edge case |
| AC3 (Non-owner → 403) | Yes — HTTP status code is 403, body contains specific message | Yes — exact status code and message | Yes — single verifiable outcome | Yes — describes HTTP response, not code | Yes — covers authorization case |
| AC4 (Project not found → 404) | Yes — HTTP status code is 404 | Yes — exact status code | Yes — single verifiable outcome | Yes — describes HTTP response, not code | Yes — covers not-found case |
| AC5 (Missing/invalid JWT → 401) | Yes — HTTP status code is 401 | Yes — exact status code | Yes — single verifiable outcome | Yes — describes HTTP response, not auth middleware | Yes — regression guard |
| AC6 (ApiResponse envelope) | Yes — response body includes `success` and `message` fields | Yes — JSON schema validation | Yes — single verifiable outcome | Yes — describes response format, not serialization code | Yes — covers contract |
| AC7 (Docs match behavior) | Yes — manual review of `.claude/backend_api_map.md` | Yes — specific file and line range | Yes — single verifiable outcome | Yes — describes documentation update, not code | Yes — ensures contract is accurate |
| AC8 (Integration tests) | Yes — tests assert status codes and body substrings | Yes — exact test scenarios listed | Yes — each test scenario is independent | Yes — describes test assertions, not test implementation | Yes — covers all error branches |
| AC9 (Logging distinguishes errors) | Yes — log messages contain specific text and log level | Yes — exact log messages and levels | Yes — single verifiable outcome | Yes — describes log content, not logging API | Yes — covers observability |
| AC10 (Root cause in commit) | Yes — commit message contains explanation | Yes — commit message must include "root cause" explanation | Yes — single verifiable outcome | Yes — describes commit message content, not code | Yes — ensures traceability |
| AC11 (No breaking changes) | Yes — existing tests pass, 201 response unchanged | Yes — regression tests verify no behavior change | Yes — single verifiable outcome | Yes — describes system behavior, not code | Yes — guards against regression |
| AC12 (Frontend displays correct error) | Yes — manual test after deployment | Yes — exact UI copy to display | Yes — single verifiable outcome | Yes — describes user-facing behavior, not frontend code | Yes — end-to-end validation |
