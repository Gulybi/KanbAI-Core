# Issue #77: Fix SignalR CORS Policy Error (x-requested-with header blocked)

## Business Value

**Who:** All KanbAI application users who need real-time updates on kanban board changes
**What:** Fix the CORS policy to allow SignalR connections from the Angular frontend
**Why:** The Angular frontend (localhost:4200) cannot establish SignalR connections to the backend Hub (localhost:5257) due to a CORS preflight failure. The browser blocks the connection with the error: "Request header field x-requested-with is not allowed by Access-Control-Allow-Headers in preflight response." This prevents users from receiving real-time updates when kanban board state changes, forcing them to manually refresh the page to see changes made by other users.

Without fixing this CORS policy, the SignalR infrastructure (installed in issue #61 and implemented in issue #62) is non-functional from the frontend's perspective. The real-time collaborative features that are the primary value proposition of the "Real-time Updates & SignalR Integration" milestone are completely blocked.

## Current State vs. Desired State

### Current State (SignalR Connections Blocked)

**CORS Policy Configuration:**
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs` (lines 52-67) defines the `AddCorsPolicy` method
- The CORS policy named `"AllowAngularFrontend"` (line 11) is configured with:
  - Allowed origin: `http://localhost:4200` (from `appsettings.Development.json` line 6)
  - Allowed methods: GET, POST, PUT, DELETE, PATCH (line 61)
  - Allowed headers: ONLY "Content-Type" and "Authorization" (line 62)
  - Credentials: Enabled via `.AllowCredentials()` (line 63)

**The Problem:**
- SignalR's negotiation process sends HTTP requests with additional headers beyond "Content-Type" and "Authorization"
- The browser is blocking the `x-requested-with` header during the SignalR preflight OPTIONS request
- The backend's CORS policy (line 62) uses `.WithHeaders("Content-Type", "Authorization")`, which creates a restrictive allow-list that rejects any other headers
- This causes the SignalR negotiation to fail with "TypeError: Failed to fetch" before the WebSocket connection is even attempted

**SignalR Infrastructure:**
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Hubs/KanbanHub.cs` (lines 1-129) exists and is functional
- The hub is mapped at `/hubs/kanban` in `Program.cs` (line 92)
- SignalR is properly registered in DI (line 24 in Program.cs, lines 73-77 in ServiceCollectionExtensions.cs)
- JWT authentication is configured to accept tokens from query strings for SignalR (Program.cs lines 47-61)

**Browser Behavior:**
- When the Angular frontend attempts to connect to SignalR, the browser sends an OPTIONS preflight request
- The preflight request includes `x-requested-with` header (and potentially other SignalR-specific headers)
- The backend responds with `Access-Control-Allow-Headers: Content-Type, Authorization` (missing `x-requested-with`)
- The browser rejects the preflight response and blocks the actual SignalR negotiation request
- The frontend receives a CORS error in the browser console and cannot establish the connection

### Desired State (SignalR Connections Successful)

**CORS Policy Configuration:**
- The `AddCorsPolicy` method in `ServiceCollectionExtensions.cs` is updated to allow all headers required by SignalR
- The CORS policy uses `.AllowAnyHeader()` instead of `.WithHeaders("Content-Type", "Authorization")` to permit SignalR's dynamic header requirements
- Alternatively, if explicit header control is required, the policy explicitly includes common SignalR headers: "Content-Type", "Authorization", "x-requested-with", "x-signalr-user-agent"
- The change is backward-compatible and does not break existing REST API functionality

**SignalR Connection Success:**
- The Angular frontend successfully completes the SignalR negotiation handshake
- The browser does not block any headers during the OPTIONS preflight request
- The SignalR connection is established using WebSockets (or falls back to long polling if WebSockets are unavailable)
- Users receive real-time updates when kanban board state changes

**Security Maintained:**
- The CORS policy continues to restrict allowed origins to `http://localhost:4200` in development
- Credentials are still required (`.AllowCredentials()` remains in place)
- Allowed HTTP methods remain unchanged (GET, POST, PUT, DELETE, PATCH)
- JWT authentication continues to secure the SignalR hub connections

## Milestone Context

This issue is NOT part of a formal milestone but is a blocker for the functionality delivered in Milestone #6: "Real-time Updates & SignalR Integration."

**Related Issues:**
- **Issue #61 (Install and Configure SignalR):** Installed SignalR infrastructure and updated CORS to include `.AllowCredentials()`, but did not anticipate the `x-requested-with` header requirement
- **Issue #62 (Create KanbanHub and Connection Management):** Implemented the `KanbanHub` with `JoinProjectGroup` and `LeaveProjectGroup` methods, which are now inaccessible from the frontend due to this CORS issue
- **Issue #63 (Refactor Services to Broadcast Events):** Services were refactored to broadcast SignalR messages, but these broadcasts cannot reach the frontend due to the blocked connections

**This issue is a regression/bug fix** that unblocks the entire real-time updates feature set. It was discovered during frontend integration testing when attempting to connect to the `KanbanHub` from the Angular application.

## Acceptance Criteria

### 1. CORS Preflight Succeeds for SignalR Negotiation
- The browser's OPTIONS preflight request to the SignalR negotiation endpoint (`/hubs/kanban/negotiate`) receives a successful response (HTTP 204 or 200)
- The `Access-Control-Allow-Headers` response header includes `x-requested-with` (and any other headers required by SignalR)
- No CORS-related errors appear in the browser console during SignalR connection attempts

### 2. SignalR Connection Establishes Successfully
- The Angular frontend (localhost:4200) can successfully establish a SignalR connection to the backend hub (localhost:5257)
- The connection uses the WebSocket transport (or falls back to long polling if WebSockets are blocked)
- The browser's Network tab shows successful negotiation requests (POST to `/hubs/kanban/negotiate`) without CORS errors

### 3. JWT Authentication Continues to Work
- SignalR connections from unauthenticated clients are rejected (HTTP 401)
- SignalR connections with valid JWT tokens (passed via `?access_token=...` query string) are accepted
- The existing JWT authentication flow for REST API endpoints remains unaffected

### 4. REST API Endpoints Remain Functional
- All existing REST API endpoints (AuthController, ProjectController, TaskController, ColumnController, AttachmentController, HealthController) continue to function correctly
- The CORS policy change does not break existing frontend-to-backend HTTP requests
- API endpoints that use the "Authorization" header continue to authenticate properly

### 5. No Breaking Changes to Existing Tests
- All existing integration tests continue to pass (including those in `KanbAI-Core.Tests`)
- Any tests that validate CORS behavior remain valid or are updated to reflect the new header policy
- The application builds successfully with no new warnings or errors

### 6. Configuration Remains Environment-Aware
- The CORS allowed origins continue to be read from `appsettings.Development.json` (or environment-specific configuration)
- The development environment allows `http://localhost:4200` only
- The CORS policy is ready for production origins to be configured via environment-specific settings

## Edge Cases & Considerations

### Edge Case: SignalR Header Variations
- Different SignalR client libraries (JavaScript, .NET, etc.) may send different headers during negotiation
- Using `.AllowAnyHeader()` ensures forward compatibility with SignalR client library updates
- Explicit header lists (e.g., `.WithHeaders("Content-Type", "Authorization", "x-requested-with")`) may need updates if new SignalR versions introduce additional headers
- **Recommended Approach:** Use `.AllowAnyHeader()` for maximum compatibility unless there are specific security requirements that mandate explicit header control

### Edge Case: Preflight Caching
- Browsers cache CORS preflight responses based on `Access-Control-Max-Age` header
- After deploying the CORS policy fix, developers may need to clear browser cache or wait for preflight cache expiration
- **Testing Note:** Use browser DevTools "Disable cache" option during testing to avoid stale preflight responses

### Edge Case: Multiple Allowed Origins (Future)
- The current configuration allows a single origin (`http://localhost:4200`)
- Future production deployments may require multiple allowed origins (e.g., staging, production frontend URLs)
- The existing `Cors:AllowedOrigins` configuration pattern supports arrays of origins
- **Not in Scope:** This issue only fixes the header policy; multiple origin support is already implemented

### Consideration: Security Impact of `.AllowAnyHeader()`
- `.AllowAnyHeader()` permits any HTTP header in CORS preflight requests
- This does NOT grant access to all clients—origin restrictions remain in place
- This does NOT bypass authentication—JWT tokens are still required for hub connections
- This does NOT expose the backend to new attack vectors beyond standard CORS risks
- The backend's authentication and authorization logic determines what actions authenticated clients can perform, regardless of headers

### Consideration: Long-Term CORS Strategy
- The current CORS policy is optimized for development (`http://localhost:4200`)
- Production CORS configuration should follow principle of least privilege while supporting SignalR
- If explicit header control is required for compliance/security policies, the allowed headers list must include all SignalR requirements
- **Not in Scope:** Production CORS configuration will be addressed during deployment planning
