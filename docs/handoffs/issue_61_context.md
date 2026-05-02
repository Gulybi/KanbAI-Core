# Issue #61: Install and Configure SignalR

## Business Value

**Who:** All KanbAI application users (project managers, team members, stakeholders)
**What:** Real-time bidirectional communication infrastructure between the backend and frontend clients
**Why:** Enable instant updates when kanban board state changes (task movements, new tasks, status changes) without requiring manual page refreshes or polling mechanisms

This is the foundational infrastructure for the "Real-time Updates & SignalR Integration" milestone. Without SignalR properly installed and configured, the application cannot support collaborative features where multiple users see live updates when others modify the kanban board.

## Current State vs. Desired State

### Current State (No Real-Time Capability)

**Package Dependencies:**
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/KanbAI-Core.csproj` (lines 10-25) contains NO SignalR package references
- The application uses standard REST API patterns (Controllers in `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/`)
- No Hubs folder or SignalR infrastructure exists in the project structure

**CORS Configuration:**
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs` (lines 50-64) defines the `AddCorsPolicy` method
- The existing CORS policy `"AllowAngularFrontend"` (line 9) allows HTTP methods GET, POST, PUT, DELETE, PATCH (line 59)
- The policy allows headers "Content-Type" and "Authorization" (line 60)
- Allowed origins are configured via `appsettings.Development.json` (line 6): `["http://localhost:4200"]`
- **MISSING:** The CORS policy does NOT include `.AllowCredentials()`, which is required for SignalR's persistent WebSocket connections
- **MISSING:** The CORS policy does NOT use `.WithExposedHeaders()` to expose any SignalR negotiation headers

**Authentication Setup:**
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Program.cs` (lines 22-42) configures JWT Bearer authentication
- Authentication is applied via middleware (line 66): `app.UseAuthentication()`
- Authorization middleware is present (line 67): `app.UseAuthorization()`
- SignalR hubs will need to integrate with this existing JWT authentication to ensure only authenticated users can connect

**Application Startup:**
- `Program.cs` (line 69) maps controllers using `app.MapControllers()`
- No SignalR hub endpoints are currently mapped
- The middleware pipeline (lines 63-67) follows the correct order: exception handler, HTTPS redirection, CORS, authentication, authorization

**Folder Structure Conventions:**
- Services are organized by feature in `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/` (Auth, Columns, Projects, Tasks)
- Extension methods are in `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Extensions/`
- Controllers follow REST conventions in `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/`

### Desired State (SignalR Infrastructure Ready)

**Package Dependencies:**
- The `Microsoft.AspNetCore.SignalR.Core` package (or equivalent) is installed and referenced in the `.csproj` file
- SignalR is available for use in services and hub implementations

**CORS Configuration:**
- The `AddCorsPolicy` method in `ServiceCollectionExtensions.cs` is updated to support SignalR requirements
- The CORS policy includes `.AllowCredentials()` to permit WebSocket credential passing
- The CORS policy properly exposes any necessary SignalR headers for negotiation (if required by the frontend)
- The frontend origin `http://localhost:4200` can establish WebSocket connections for SignalR

**SignalR Service Registration:**
- SignalR services are registered in the DI container during application startup
- The registration is placed in an appropriate extension method in `ServiceCollectionExtensions.cs` to follow existing patterns (e.g., `AddSignalRInfrastructure()`)
- The SignalR configuration uses default settings (no custom serialization or hub options required at this stage)

**SignalR Middleware & Endpoint Mapping:**
- SignalR endpoints are mapped in `Program.cs` after controller mapping
- The middleware pipeline order remains correct (CORS before SignalR, authentication/authorization before SignalR)

**Security Integration:**
- SignalR hub endpoints are secured using the existing JWT Bearer authentication
- Unauthenticated users cannot establish SignalR connections (will be enforced when hubs are created in issue #62)

**Configuration Readiness:**
- No new configuration settings are required in `appsettings.json` or `appsettings.Development.json` at this stage
- The existing CORS allowed origins configuration (`Cors:AllowedOrigins`) continues to control which frontends can connect

## Milestone Context

This issue is part of Milestone #6: "Real-time Updates & SignalR Integration". The milestone contains the following issues in implementation order:

1. **Issue #61 (This Issue): Install and Configure SignalR** - Prerequisite for all other real-time features
2. **Issue #62: Create KanbanHub and Connection Management** - Depends on #61
3. **Issue #63: Refactor Services to Broadcast Events** - Depends on #62
4. **Issue #64: Document AI SignalR Implementation** - Final documentation step

Without completing issue #61, the subsequent issues cannot be implemented because the SignalR infrastructure will not be available. This is a pure infrastructure setup task with no business logic.

## Acceptance Criteria

### 1. SignalR Package Installed
- The project file (`KanbAI-Core.csproj`) references a SignalR NuGet package compatible with .NET 10
- The application builds successfully with the new package reference
- No package version conflicts or dependency warnings are present in the build output

### 2. SignalR Services Registered in DI Container
- An extension method exists in `ServiceCollectionExtensions.cs` (or an equivalent location) that registers SignalR services
- The extension method is called in `Program.cs` during application startup before `builder.Build()` is invoked
- The SignalR service registration follows the existing code organization patterns (similar to `AddPersistence`, `AddApiInfrastructure`, etc.)

### 3. CORS Policy Supports WebSocket Connections
- The `AddCorsPolicy` method in `ServiceCollectionExtensions.cs` includes `.AllowCredentials()` in the policy builder
- The CORS policy allows the frontend origin (`http://localhost:4200` in development) to establish WebSocket connections
- The CORS policy does NOT break existing REST API functionality (all current controllers continue to work)

### 4. Middleware Pipeline Configured for SignalR
- The `Program.cs` middleware pipeline includes SignalR endpoint mapping (e.g., `app.MapHub<T>()` or prepared for hub registration)
- The middleware pipeline order is correct:
  - Exception handler BEFORE SignalR
  - CORS BEFORE SignalR
  - Authentication BEFORE SignalR
  - Authorization BEFORE SignalR
- The application starts successfully without runtime errors related to SignalR configuration

### 5. No Breaking Changes to Existing Functionality
- All existing REST API endpoints (AuthController, ProjectController, TaskController, ColumnController, HealthController) continue to function correctly
- The existing JWT authentication flow is unaffected
- Integration tests (if any) continue to pass or are appropriately updated to handle SignalR infrastructure

### 6. Ready for Hub Implementation
- The SignalR infrastructure is ready for hub classes to be created (issue #62)
- No additional package installation or service registration will be required for hub implementation
- The authentication pipeline is ready to secure SignalR hub connections

## Relevant Files

### Package Configuration
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/KanbAI-Core.csproj` (lines 10-25) - Add SignalR package reference

### Service Registration & Configuration
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Program.cs` (lines 1-73) - Register SignalR services and configure middleware pipeline
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs` (lines 1-65) - Add SignalR extension method and update CORS policy

### Configuration Files (Reference Only - May Not Require Changes)
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/appsettings.json` (lines 1-9) - Base configuration
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/appsettings.Development.json` (lines 1-20) - Development environment settings (CORS origins)

### Existing Authentication Infrastructure (Reference Only - Must Not Break)
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Program.cs` (lines 22-42) - JWT Bearer authentication configuration
- `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Auth/` - Token service and authentication logic

## Edge Cases & Considerations

### Edge Case: CORS Preflight Requests for SignalR Negotiation
- SignalR performs a negotiation handshake before establishing WebSocket connections
- The CORS policy must permit OPTIONS requests for the negotiation endpoint
- If the frontend receives CORS errors during SignalR connection attempts, the CORS policy configuration must be reviewed
- **Acceptance Test:** Browser DevTools Network tab shows successful OPTIONS and POST requests to the SignalR negotiation endpoint without CORS errors

### Edge Case: Mixed HTTP/WebSocket Traffic
- SignalR can fall back to long polling if WebSocket connections fail
- The CORS policy must support both HTTP and WebSocket protocols
- The `.AllowCredentials()` setting is mandatory for both protocols when using authentication
- **Acceptance Test:** SignalR successfully establishes connections even if WebSockets are blocked (uses fallback transport)

### Edge Case: Authentication Token Passing in WebSocket Handshake
- JWT tokens for SignalR are typically passed via query string parameters (`?access_token=...`) during WebSocket handshake (NOT in headers, since WebSocket API doesn't support custom headers)
- The authentication middleware must be configured to accept tokens from query strings for SignalR connections
- **Consideration:** This configuration will be handled in the JWT Bearer options in `Program.cs`

### Consideration: Development vs. Production CORS Origins
- The current `AllowedOrigins` configuration is environment-specific (`http://localhost:4200` for development)
- Production environments will require different origins to be configured in production `appsettings.json`
- **Not in Scope:** Production CORS configuration (will be handled during deployment setup)

### Consideration: SignalR Hub Creation (Out of Scope for This Issue)
- This issue focuses ONLY on infrastructure setup (package installation, service registration, CORS configuration)
- No hub classes, connection management logic, or broadcast methods are created in this issue
- Hub implementation is deferred to issue #62
- **Validation:** After this issue is complete, the application should build and run successfully, but no SignalR hubs will be available yet

### Consideration: Integration Test Compatibility
- The project has custom integration test infrastructure (referencing issue #70's context note)
- SignalR infrastructure should not break existing `WebApplicationFactory<Program>` tests
- SignalR hubs will require special test setup (likely involving `TestServer` limitations with WebSockets)
- **Out of Scope:** SignalR hub testing infrastructure (will be addressed when hubs are implemented in issue #62)

### Consideration: Logging SignalR Connection Events
- SignalR logs connection lifecycle events (connect, disconnect, errors) using the existing `ILogger` infrastructure
- Default logging level configuration in `appsettings.json` should capture SignalR informational messages
- **Acceptance Test:** When a SignalR connection is established (after issue #62), connection events appear in application logs
