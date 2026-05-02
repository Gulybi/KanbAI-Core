# Technical Specification: Issue #61 - Install and Configure SignalR

**GitHub Issue:** [#61 - Install and Configure SignalR](https://github.com/Gulybi/KanbAI-Core/issues/61)  
**Context Document:** [issue_61_context.md](./issue_61_context.md)  
**Staff Engineer:** @agent_staff_engineer  
**Created:** 2026-05-02

---

## 1. Overview

This specification defines the installation and configuration of ASP.NET Core SignalR infrastructure to enable real-time bidirectional communication between the KanbAI backend and Angular frontend clients. This is a pure infrastructure setup task that establishes the foundation for WebSocket-based real-time updates when kanban board state changes (task movements, new tasks, status changes).

**Scope:**
- Install the SignalR NuGet package (if required for .NET 10)
- Update CORS policy to support WebSocket connections (add `.AllowCredentials()` and `.WithExposedHeaders()`)
- Create an extension method to register SignalR services in the DI container
- Configure SignalR middleware pipeline ordering in `Program.cs`
- Configure JWT Bearer authentication to accept tokens from query string parameters (required for WebSocket handshake)
- Prepare endpoint mapping infrastructure (no actual hubs created in this issue)

**Out of Scope:**
- No SignalR hub classes created (deferred to Issue #62)
- No connection management logic or broadcast methods (deferred to Issue #62)
- No service refactoring to broadcast events (deferred to Issue #63)
- No database schema changes, entity changes, or EF Core configuration changes
- No changes to existing REST API controller logic
- No production CORS origins configuration (development-only scope)

**Why This is Infrastructure-Only:**
This issue focuses exclusively on package installation, service registration, middleware configuration, and authentication setup. No business logic, hub implementation, or real-time messaging functionality is included. The application should build and run successfully after this issue is complete, but no SignalR hubs will be available until Issue #62.

**Design Note - SignalR in .NET 10:**
ASP.NET Core SignalR is included in the ASP.NET Core shared framework starting from .NET Core 3.0+. For .NET 10, no additional NuGet package is required unless custom protocol implementations or client libraries are needed. The SignalR server components are available via the `Microsoft.AspNetCore.SignalR` namespace automatically. However, for explicit versioning and future compatibility, referencing `Microsoft.AspNetCore.SignalR.Core` is a safe practice.

**Design Note - JWT Token Passing for WebSockets:**
Unlike HTTP requests where JWT tokens are passed via the `Authorization: Bearer <token>` header, the WebSocket API does not support custom headers during the initial handshake. SignalR clients must pass the JWT token as a query string parameter (`?access_token=<token>`) during the WebSocket upgrade request. The JWT Bearer authentication options must be configured to read tokens from the query string for SignalR endpoints specifically.

---

## 2. Database/Domain Design

**N/A** - No database or domain changes required. This issue is purely infrastructure configuration (package installation, service registration, middleware configuration, CORS policy updates, authentication configuration).

---

## 3. API Contracts

**N/A** - No API endpoints are created or modified in this issue. SignalR hub endpoints will be created in Issue #62. This issue only prepares the infrastructure and endpoint mapping mechanism (`app.MapHub<T>()` capability) without registering any actual hubs.

**Preparatory Work:**
The `Program.cs` middleware pipeline will be structured to support future hub registration. When Issue #62 adds the first hub (e.g., `KanbanHub`), the registration will be:

```csharp
// Future hub mapping (NOT in this issue - for reference only)
// app.MapHub<KanbanHub>("/hubs/kanban");
```

This spec ensures the infrastructure is ready for this call to succeed.

---

## 4. Application Layer Boundaries

### 4.1 SignalR Service Registration Extension Method

**File:** `KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs`

**New Method Signature:**

```csharp
/// <summary>
/// Registers SignalR services for real-time bidirectional communication.
/// Uses default JSON serialization and hub options.
/// </summary>
public static IServiceCollection AddSignalRInfrastructure(this IServiceCollection services)
{
    services.AddSignalR();
    return services;
}
```

**Rationale:**
- Follows the existing extension method pattern (`AddPersistence`, `AddApiInfrastructure`, `AddAuthServices`, `AddCorsPolicy`).
- Uses default SignalR configuration (no custom hub options, no custom serialization).
- Returns `IServiceCollection` for method chaining consistency.
- Simple implementation - advanced configuration (e.g., Azure SignalR Service, Redis backplane) deferred to production deployment issues.

**Advanced Configuration (Not in Scope):**
Future issues may extend this method to configure:
- `HubOptions` (e.g., `MaximumReceiveMessageSize`, `ClientTimeoutInterval`, `KeepAliveInterval`)
- Custom JSON serialization options
- Message backplane for multi-server scenarios (Redis, Azure SignalR Service)

For this issue, the default configuration is sufficient.

### 4.2 CORS Policy Update

**File:** `KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs`

**Current Code (lines 50-64):**
```csharp
public static IServiceCollection AddCorsPolicy(
    this IServiceCollection services, IConfiguration configuration)
{
    var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    services.AddCors(options =>
    {
        options.AddPolicy(CorsPolicyName, policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                  .WithHeaders("Content-Type", "Authorization");
        });
    });
    return services;
}
```

**Updated Code:**
```csharp
public static IServiceCollection AddCorsPolicy(
    this IServiceCollection services, IConfiguration configuration)
{
    var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    services.AddCors(options =>
    {
        options.AddPolicy(CorsPolicyName, policy =>
        {
            policy.WithOrigins(allowedOrigins)
                  .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
                  .WithHeaders("Content-Type", "Authorization")
                  .AllowCredentials();
        });
    });
    return services;
}
```

**Changes:**
1. Added `.AllowCredentials()` - **CRITICAL** for SignalR WebSocket connections with authentication.

**Why `.AllowCredentials()` is Required:**
WebSocket connections (used by SignalR) require credentials to be sent with the initial upgrade request when authentication is enabled. Without `.AllowCredentials()`, the browser will block the WebSocket handshake with a CORS error: `"Credential is not supported if the CORS header 'Access-Control-Allow-Origin' is '*'"`.

**Why `.WithExposedHeaders()` is NOT Required (Yet):**
The context note mentioned exposing SignalR negotiation headers, but ASP.NET Core SignalR's negotiation endpoint (`/hubs/<hubname>/negotiate`) does NOT require custom response headers to be exposed to JavaScript. The standard `Content-Type` and `Content-Length` headers are always exposed by default. If future issues require custom headers (e.g., custom negotiation metadata), `.WithExposedHeaders("X-Custom-Header")` can be added then.

**Security Note:**
`.AllowCredentials()` requires explicit origins (not wildcard `*`). The existing code uses `configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()`, which reads from `appsettings.Development.json` (`["http://localhost:4200"]`). This is correct and secure.

### 4.3 JWT Bearer Authentication Configuration for SignalR

**File:** `KanbAI-Core/KanbAI-Core/Program.cs`

**Current Code (lines 22-42):**
```csharp
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.Zero
        };
    });
```

**Updated Code:**
```csharp
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        // Allow SignalR to read JWT tokens from query string (WebSocket handshake requirement)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                // Only apply query string token extraction for SignalR hub paths
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
```

**Changes:**
1. Added `options.Events = new JwtBearerEvents { OnMessageReceived = ... }` to extract JWT tokens from the query string for SignalR WebSocket connections.

**Rationale:**
- WebSocket API does not support custom headers during the initial handshake. SignalR clients must pass the JWT token as `?access_token=<token>`.
- The `OnMessageReceived` event is invoked before token validation, allowing the middleware to extract the token from the query string and assign it to `context.Token`.
- Path filtering (`path.StartsWithSegments("/hubs")`) ensures query string token extraction only applies to SignalR hub endpoints, not REST API endpoints (which should continue using the `Authorization: Bearer <token>` header).
- This pattern is documented in the official ASP.NET Core SignalR authentication documentation: https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz

**Security Note:**
Query string parameters are logged by default in most web servers and proxies. For production deployments, consider:
- Disabling query string logging for `/hubs/*` paths.
- Using secure WebSocket connections (`wss://`) exclusively (already enforced by HTTPS redirection).
- Short JWT token expiration times (currently 60 minutes per `appsettings.Development.json`).

---

## 5. Implementation Steps

### Step 1: Verify SignalR Availability in .NET 10 (No Package Install Required)

**Action:** Verify that `Microsoft.AspNetCore.SignalR` is available in .NET 10 without additional package installation.

**Verification Command:**
```bash
cd c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core
dotnet add package Microsoft.AspNetCore.SignalR.Core --version 10.* --dry-run
```

**Expected Result:**
If the command suggests no changes or states the package is already available, no installation is needed. SignalR is part of the ASP.NET Core shared framework in .NET 10.

**If Package Installation is Required:**
If the dry-run indicates the package is not available, install it:
```bash
dotnet add package Microsoft.AspNetCore.SignalR.Core
```

**Note:** Based on the context note and .NET 10 documentation, SignalR should be available without additional packages. This step is a safety check.

### Step 2: Update CORS Policy to Support WebSocket Connections

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Extensions\ServiceCollectionExtensions.cs`

**Modify the `AddCorsPolicy` method:**
- Add `.AllowCredentials()` after `.WithHeaders("Content-Type", "Authorization")` (see Section 4.2 for exact code).

**Rationale:**
SignalR WebSocket connections require credentials (authentication cookies or tokens) to be sent with the upgrade request. Without `.AllowCredentials()`, the browser will block the connection with a CORS error.

### Step 3: Add JWT Query String Token Extraction for SignalR

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Program.cs`

**Modify the `.AddJwtBearer(options => { ... })` configuration:**
- Add the `options.Events = new JwtBearerEvents { OnMessageReceived = ... }` block as shown in Section 4.3.

**Rationale:**
WebSocket API does not support custom headers. SignalR clients must pass JWT tokens via query string (`?access_token=<token>`). The `OnMessageReceived` event extracts the token for validation.

### Step 4: Create SignalR Service Registration Extension Method

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Extensions\ServiceCollectionExtensions.cs`

**Add the `AddSignalRInfrastructure` method:**
- Place it immediately after the `AddCorsPolicy` method (around line 65).
- Use the exact code from Section 4.1.

**XML Doc Comment:**
Include the XML doc comment from Section 4.1 to maintain consistency with existing extension methods.

### Step 5: Register SignalR Services in Program.cs

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Program.cs`

**Modify the service registration chain (lines 44-49):**

**Current Code:**
```csharp
builder.Services
    .AddAuthorization()
    .AddPersistence(builder.Configuration)
    .AddApiInfrastructure()
    .AddAuthServices()
    .AddCorsPolicy(builder.Configuration);
```

**Updated Code:**
```csharp
builder.Services
    .AddAuthorization()
    .AddPersistence(builder.Configuration)
    .AddApiInfrastructure()
    .AddAuthServices()
    .AddCorsPolicy(builder.Configuration)
    .AddSignalRInfrastructure();
```

**Change:**
- Added `.AddSignalRInfrastructure()` at the end of the service registration chain.

**Rationale:**
- Follows the existing method chaining pattern.
- SignalR services must be registered before `builder.Build()` is called.
- Order within the chain does not matter for service registration (only middleware pipeline order matters).

### Step 6: Verify Middleware Pipeline Order (No Changes Required)

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Program.cs`

**Current Middleware Pipeline (lines 63-69):**
```csharp
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
```

**Verification:**
The existing middleware pipeline order is correct for SignalR:
1. `UseExceptionHandler()` - catches exceptions from all downstream middleware (including SignalR hubs when added)
2. `UseHttpsRedirection()` - redirects HTTP to HTTPS (WebSocket upgrade requests benefit from this)
3. `UseCors()` - applies CORS policy **before** authentication (REQUIRED for WebSocket preflight requests)
4. `UseAuthentication()` - validates JWT tokens (including query string tokens for SignalR)
5. `UseAuthorization()` - enforces authorization policies (SignalR hubs will inherit this)
6. `MapControllers()` - maps REST API endpoints

**No changes required.** When Issue #62 adds hub endpoints, they will be mapped after `MapControllers()`:

```csharp
app.MapControllers();
// Future hub mapping (NOT in this issue):
// app.MapHub<KanbanHub>("/hubs/kanban");
```

**Why CORS Before Authentication:**
WebSocket upgrade requests (used by SignalR) perform a CORS preflight check (OPTIONS request) **before** authentication. If `UseCors()` were placed after `UseAuthentication()`, the preflight request would fail with 401 Unauthorized because it does not include authentication credentials.

### Step 7: Build and Verify

**Build Command:**
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

**Run Command (Optional Smoke Test):**
```bash
dotnet run
```

**Expected Behavior:**
- Application starts without errors.
- Scalar UI is accessible at `http://localhost:<port>/scalar/v1` (Development environment only).
- All existing REST API endpoints continue to function correctly.
- No SignalR hub endpoints are available yet (Issue #62 will add them).

**Verification Checklist:**
- Application builds successfully with no warnings.
- Application starts without runtime exceptions.
- Existing REST API endpoints (`/api/auth/login`, `/api/project`, etc.) continue to work.
- CORS policy allows requests from `http://localhost:4200`.
- JWT Bearer authentication continues to work for REST API endpoints.

### Step 8: Test Infrastructure Compatibility (Awareness Only - No Code Changes)

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\*IntegrationTests.cs`

**Action:** Be aware that SignalR infrastructure registration should not break existing integration tests.

**Rationale:**
- Existing integration tests use `WebApplicationFactory<Program>` and follow the patterns in `.claude/rules/integration-testing.md`.
- SignalR service registration (`AddSignalR()`) does not interfere with the test infrastructure's `TestAuthHandler` pattern or `FallbackPolicy = null` configuration.
- SignalR hub testing (WebSocket connections, message broadcasting) will be addressed in Issue #62 when hubs are created.

**No changes required in this issue.** Integration tests should continue to pass without modification.

---

## 6. QA Guidance

### 6.1 Test Files Structure

| Test File | Location | Type | Purpose |
|-----------|----------|------|---------|
| **SignalRInfrastructureTests.cs** (new) | `KanbAI-Core.Tests/Infrastructure/` | Unit | Verify SignalR services are registered in DI container |
| **CorsWebSocketTests.cs** (new) | `KanbAI-Core.Tests/Integration/` | Integration | Verify CORS policy allows WebSocket upgrade requests from allowed origins |
| **JwtQueryStringAuthenticationTests.cs** (new) | `KanbAI-Core.Tests/Integration/` | Integration | Verify JWT tokens can be passed via query string for `/hubs/*` paths |
| **ExistingRestApiTests** (extend existing) | `KanbAI-Core.Tests/Integration/*IntegrationTests.cs` | Regression | Verify existing REST API endpoints continue to work after SignalR infrastructure is added |

### 6.2 Test Cases

#### SignalRInfrastructureTests.cs (Unit Tests)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 1 | `AddSignalRInfrastructure_RegistersSignalRServices` | Service Registration | Verify `IServiceCollection.AddSignalR()` is called; assert `IHubContext<T>` can be resolved from DI (using a dummy hub type for testing) |
| 2 | `AddCorsPolicy_IncludesAllowCredentials` | CORS Configuration | Verify the CORS policy includes `.AllowCredentials()` by inspecting the `CorsPolicy` object from `IOptions<CorsOptions>` |
| 3 | `AddJwtBearer_IncludesQueryStringTokenExtraction` | Authentication Configuration | Verify `JwtBearerOptions.Events.OnMessageReceived` is not null (requires reflection or a test-specific configuration validator) |

#### CorsWebSocketTests.cs (Integration Tests - Using `WebApplicationFactory<Program>`)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 4 | `OptionsRequest_ToFutureHubPath_ReturnsAllowCredentialsHeader` | CORS Preflight | OPTIONS request to `/hubs/test` from `http://localhost:4200` returns `Access-Control-Allow-Credentials: true` header |
| 5 | `OptionsRequest_ToFutureHubPath_FromDisallowedOrigin_Returns403OrNoCredentials` | CORS Security | OPTIONS request from `http://evil.com` does not return `Allow-Credentials` header or returns 403 |

**Note:** These tests validate the CORS configuration without requiring actual SignalR hubs to exist. The OPTIONS preflight request path (`/hubs/test`) will return 404 after the CORS middleware processes it, but the CORS headers should be present.

#### JwtQueryStringAuthenticationTests.cs (Integration Tests - Using `WebApplicationFactory<Program>`)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 6 | `GetRequest_ToFutureHubPath_WithValidQueryStringToken_Authenticates` | Authentication | GET request to `/hubs/test?access_token=<valid-jwt>` returns 404 (hub not found) but does NOT return 401 (token was validated) |
| 7 | `GetRequest_ToFutureHubPath_WithInvalidQueryStringToken_Returns401` | Authentication | GET request to `/hubs/test?access_token=invalid` returns 401 Unauthorized |
| 8 | `GetRequest_ToFutureHubPath_WithoutToken_Returns401` | Authentication | GET request to `/hubs/test` (no token) returns 401 Unauthorized (if hub endpoints require authentication) |
| 9 | `GetRequest_ToRestApiEndpoint_WithQueryStringToken_IgnoresToken` | Security Isolation | GET request to `/api/project?access_token=<valid-jwt>` returns 401 (query string token extraction only applies to `/hubs/*` paths, REST API endpoints require `Authorization` header) |

**Note:** These tests validate the JWT query string extraction logic without requiring actual SignalR hubs to exist. The test requests will return 404 (hub not found) or 401 (unauthorized), but the presence/absence of authentication errors confirms the query string token extraction is working.

#### Regression Tests (Extend Existing Integration Tests)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 10 | `PostLogin_AfterSignalRInfrastructure_Returns200WithToken` | Regression | Verify `/api/auth/login` continues to work after SignalR infrastructure is added |
| 11 | `GetProjects_Authenticated_AfterSignalRInfrastructure_ReturnsProjects` | Regression | Verify authenticated requests to `/api/project` continue to work |
| 12 | `GetHealth_Unauthenticated_AfterSignalRInfrastructure_Returns200` | Regression | Verify unauthenticated requests to `/api/health` continue to work |

### 6.3 Test Infrastructure Notes

**Integration Test Setup (WebApplicationFactory Pattern):**
- Use the standard `CreateClient()` pattern from `.claude/rules/integration-testing.md`.
- Remove Negotiate authentication descriptors and register `TestAuthHandler` as documented.
- Set `FallbackPolicy = null` to disable global authorization for test endpoints.

**SignalR Hub Testing Caveat:**
- TestServer (used by `WebApplicationFactory`) does **NOT** support actual WebSocket connections.
- Testing actual SignalR message broadcasting and hub method invocation will require:
  - Microsoft's `Microsoft.AspNetCore.SignalR.Client` package for test clients.
  - In-memory SignalR backplane or mock `IHubContext<T>` for unit testing hub methods.
- **Out of Scope for This Issue:** Full SignalR hub testing will be addressed in Issue #62 when hubs are created.

**CORS Testing Approach:**
- Use `HttpClient.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/hubs/test") { Headers = { { "Origin", "http://localhost:4200" } } })`.
- Assert response headers include `Access-Control-Allow-Credentials: true`.
- No actual WebSocket upgrade is required to test CORS preflight behavior.

**JWT Query String Testing Approach:**
- Create a valid JWT token using the test project's `TokenService` or a test helper.
- Send GET requests to `/hubs/test?access_token=<token>` (hub endpoint path, even though no hub exists yet).
- Assert the response is 404 (hub not found) rather than 401 (authentication succeeded, hub not found).
- Assert invalid tokens return 401 (authentication failed).

**Manual Testing (Optional):**
Use a WebSocket testing tool (e.g., `websocat`, Postman WebSocket, browser DevTools) to verify:
1. WebSocket upgrade request to `ws://localhost:<port>/hubs/test` from origin `http://localhost:4200` completes the CORS preflight successfully (even if the connection is refused due to no hub existing).
2. WebSocket upgrade request with `?access_token=<valid-jwt>` passes authentication (even if the connection is refused due to no hub existing).

---

## 7. Known Caveats

| # | Caveat | Impact | Mitigation |
|---|--------|--------|-----------|
| 1 | **No SignalR hubs created in this issue** | SignalR infrastructure is registered, but no actual real-time communication is possible until Issue #62 adds hub classes. The application will build and run successfully, but no WebSocket endpoints will be available. | Expected behavior per issue scope. Document clearly in acceptance criteria that this issue is infrastructure-only. Issue #62 will add the first hub (`KanbanHub`). |
| 2 | **Query string token logging security concern** | JWT tokens passed via `?access_token=<token>` may be logged by web servers, proxies, and application logs (query strings are logged by default). | For development: acceptable risk. For production: (a) disable query string logging for `/hubs/*` paths, (b) use short token expiration times (currently 60 minutes), (c) enforce HTTPS exclusively via `UseHttpsRedirection()` (already configured). Document in production deployment guide. |
| 3 | **CORS `.AllowCredentials()` requires explicit origins** | `.AllowCredentials()` cannot be used with wildcard origin (`*`). The existing configuration uses explicit origins from `appsettings.Development.json`, so this is not an issue for development. | Production deployment must configure `Cors:AllowedOrigins` in production `appsettings.json` with the actual frontend URL. Do NOT use `*` as an origin when `.AllowCredentials()` is enabled (the browser will reject it). |
| 4 | **TestServer does not support WebSocket connections** | Integration tests using `WebApplicationFactory<Program>` cannot establish actual WebSocket connections to test SignalR message broadcasting. | For this issue: test CORS preflight and JWT query string extraction without actual WebSocket connections (use HTTP GET requests to future hub paths). For Issue #62: use `Microsoft.AspNetCore.SignalR.Client` with a real test server (not TestServer) or mock `IHubContext<T>` for unit testing hub methods. |
| 5 | **SignalR negotiation fallback to long polling** | If WebSocket connections fail (e.g., corporate proxy blocks WebSockets), SignalR falls back to Server-Sent Events (SSE) or long polling. CORS policy must support both protocols. | `.AllowCredentials()` supports all SignalR transports (WebSocket, SSE, long polling). No additional configuration required. |
| 6 | **Middleware pipeline order is critical** | CORS middleware must be placed **before** authentication middleware to allow unauthenticated CORS preflight requests (OPTIONS). Authentication middleware must be placed **before** SignalR hub endpoints to enforce authorization. | Verified in Step 6: existing middleware pipeline order is correct. Do NOT reorder middleware without reviewing SignalR requirements. |
| 7 | **No hub endpoint mapping in this issue** | `Program.cs` will not call `app.MapHub<T>()` in this issue. The infrastructure is ready, but no hub endpoints are registered. | Expected behavior per issue scope. Issue #62 will add the first hub mapping: `app.MapHub<KanbanHub>("/hubs/kanban")`. |
| 8 | **Integration tests may require InMemory database swap** | If integration tests seed data and test authenticated SignalR hub connections (in Issue #62), they will need the EF Core provider swap pattern documented in issue #70's tech spec (remove `UseSqlServer`, register `UseInMemoryDatabase`). | Not applicable to this issue (no hub endpoints created). Document in Issue #62 QA guidance. |

---

## 8. Design Validation Self-Check

| Check | Question | Status |
|-------|----------|--------|
| **Namespaces** | Do all referenced namespaces match the project's `RootNamespace` and existing conventions? | ✅ Pass - `Microsoft.AspNetCore.SignalR`, `Microsoft.AspNetCore.Authentication.JwtBearer` are standard ASP.NET Core namespaces; `KanbAI_Core.Extensions` follows existing conventions |
| **Folder Paths** | Do all file paths reference real folders, or are "create new" steps included? | ✅ Pass - `Extensions/`, `Controllers/` exist; test folders (`KanbAI-Core.Tests/Infrastructure/`, `KanbAI-Core.Tests/Integration/`) may need creation (specified in QA guidance) |
| **Dependencies** | Are all required NuGet packages already in the `.csproj`, or are install steps included? | ✅ Pass - SignalR is part of the ASP.NET Core shared framework in .NET 10; `Microsoft.AspNetCore.Authentication.JwtBearer` already referenced; no new packages required (Step 1 includes verification) |
| **Naming Conflicts** | Do any new class/enum names conflict with existing types in the codebase? | ✅ Pass - `AddSignalRInfrastructure` is a new extension method; no new types introduced; no conflicts |
| **BaseEntity Compliance** | Do new entities inherit from `BaseEntity` and avoid re-declaring common properties? | ✅ N/A - No entity changes in this issue |
| **Code Standards** | Does the design comply with standards (file-scoped namespaces, no blocking async, constructor injection)? | ✅ Pass - Extension methods follow existing patterns; JWT event handler uses `Task.CompletedTask` (no blocking); no new async code |
| **Security** | Does the design comply with security practices (no hardcoded secrets, no PII in logs, secure DTOs)? | ✅ Pass - No hardcoded secrets (JWT settings from configuration); query string token extraction only applies to `/hubs/*` paths (REST API endpoints unaffected); `.AllowCredentials()` requires explicit origins (already configured); HTTPS enforced via `UseHttpsRedirection()` |

**Result:** All checks pass. Design is ready for implementation.

---

## Summary of Design Decisions

1. **No NuGet Package Installation Required:** SignalR is part of the ASP.NET Core shared framework in .NET 10. Step 1 includes a verification command to confirm availability, but no package installation is expected.

2. **CORS Policy Update - Add `.AllowCredentials()`:** SignalR WebSocket connections require credentials (JWT tokens) to be sent with the upgrade request. `.AllowCredentials()` is mandatory and already compatible with explicit origins from `appsettings.Development.json`.

3. **JWT Query String Token Extraction:** WebSocket API does not support custom headers. The `OnMessageReceived` event extracts JWT tokens from `?access_token=<token>` for SignalR hub paths (`/hubs/*`), while REST API endpoints continue using the `Authorization: Bearer <token>` header.

4. **SignalR Service Registration Extension Method:** Follows existing patterns (`AddPersistence`, `AddApiInfrastructure`, `AddAuthServices`, `AddCorsPolicy`). Uses default SignalR configuration (advanced options deferred to production deployment issues).

5. **Middleware Pipeline Order Verified:** Existing pipeline order is correct for SignalR. No changes required. CORS before authentication allows preflight requests; authentication before hub mapping enforces authorization.

6. **No Hub Endpoints in This Issue:** This is a pure infrastructure setup task. No hub classes, no connection management logic, no message broadcasting. Issue #62 will add the first hub (`KanbanHub`).

7. **Test Strategy:** Unit tests verify service registration and configuration. Integration tests validate CORS preflight and JWT query string extraction without requiring actual WebSocket connections (TestServer limitation). Full SignalR hub testing deferred to Issue #62.

---

The technical specification is saved. You can now instruct the developer to read the tech spec and begin implementation.

---

## 9. Development Status

**Developer:** @agent_developer
**Implementation Date:** 2026-05-02
**Branch:** `61-install-and-configure-signalr`

### 9.1 Files Created

None. This issue is infrastructure-only and modifies existing files exclusively.

### 9.2 Files Modified

| File | Change Summary |
|------|----------------|
| [KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs](../../KanbAI-Core/KanbAI-Core/Extensions/ServiceCollectionExtensions.cs) | Added `.AllowCredentials()` to the `AddCorsPolicy` CORS policy builder (required for SignalR WebSocket credential passing). Added new extension method `AddSignalRInfrastructure()` that calls `services.AddSignalR()`. |
| [KanbAI-Core/KanbAI-Core/Program.cs](../../KanbAI-Core/KanbAI-Core/Program.cs) | Added `JwtBearerEvents.OnMessageReceived` handler that extracts `?access_token=...` from the query string for requests with paths starting with `/hubs`. Appended `.AddSignalRInfrastructure()` to the service registration chain. |

### 9.3 Build & Test Results

**Build:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Test Suite:**
```
Passed!  - Failed:     0, Passed:   406, Skipped:     2, Total:   408, Duration: 5 s
```

- No build errors, no warnings.
- All 406 existing tests continue to pass.
- 2 skipped tests are pre-existing (unrelated to this change).
- No regressions introduced.

### 9.4 Infrastructure Notes

**SignalR Package:** As predicted by the tech spec (Section 1, "Design Note - SignalR in .NET 10"), no NuGet package installation was required. `services.AddSignalR()` and the `Microsoft.AspNetCore.SignalR` namespace are available via the ASP.NET Core shared framework in .NET 10. The `KanbAI-Core.csproj` file was NOT modified. Step 1 of the tech spec's implementation steps (package verification) resolved to "no package needed."

**Middleware Pipeline:** As stated in Step 6 of the tech spec, the existing pipeline order (`UseExceptionHandler` → `UseHttpsRedirection` → `UseCors` → `UseAuthentication` → `UseAuthorization` → `MapControllers`) is already correct for SignalR. No changes were made to the middleware pipeline.

**No Hub Mapping:** Per the issue scope, no `app.MapHub<T>()` calls are present. Issue #62 will add the first hub mapping.

**No Workarounds Required:** No environmental or tooling workarounds were needed. The implementation matched the tech spec exactly.

### 9.5 Edge Cases for QA

1. **Path filter precision (`/hubs` vs `/hubs/...`)** — `path.StartsWithSegments("/hubs")` matches both `/hubs` and any `/hubs/<hub-name>` path. QA should confirm that:
   - A valid `?access_token=...` on `/hubs/test` causes the token to be validated.
   - A valid `?access_token=...` on `/api/project` is IGNORED (query-string token extraction must NOT bleed into REST API endpoints).
   - A path like `/hubsadmin` (no slash, sibling name) does NOT match `/hubs` prefix — `StartsWithSegments` is segment-aware and will not match.

2. **CORS preflight without credentials** — The CORS policy now includes `.AllowCredentials()`. QA should verify that an OPTIONS preflight request to `/hubs/test` from the allowed origin `http://localhost:4200` returns `Access-Control-Allow-Credentials: true`. A preflight from a disallowed origin (e.g. `http://evil.com`) must NOT return that header (it should be absent, or the preflight should be rejected by the browser).

3. **Regression on existing REST API authentication** — The `OnMessageReceived` handler should NOT alter behavior for REST API endpoints. QA should confirm:
   - `GET /api/project` with a valid `Authorization: Bearer <token>` header still returns 200.
   - `GET /api/project` with NO header and NO query-string token still returns 401.
   - `GET /api/project?access_token=<valid-jwt>` still returns 401 (query-string tokens are only accepted for `/hubs/*` paths).

4. **Hub endpoints not yet mapped** — Per the issue scope, no `app.MapHub<T>()` call exists. A GET request to `/hubs/test` will return 404, not 401 (authentication middleware runs first and succeeds if the token is valid, but no endpoint matches). QA test cases 6-9 in Section 6.2 of the tech spec capture this behavior.

5. **`JwtBearerEvents` instantiation** — The handler is created once at configuration time and reused per-request. The lambda closes over no mutable state, so it is thread-safe. No further QA investigation needed here, but worth noting for reviewers.

6. **TestServer behavior** — The existing integration test infrastructure (with `TestAuthHandler` overriding the JWT scheme) is not affected by the `OnMessageReceived` handler, because tests use a different authentication scheme (`Test`) that bypasses JWT entirely. All 406 existing tests pass, confirming this.

---

Development is complete and files are saved. You can now instruct the QA tester to review the implementation and write automated tests.

---

## 10. QA Status

**QA Tester:** @agent_tester_qa
**Review Date:** 2026-05-02
**Branch:** `61-install-and-configure-signalr`

### 10.1 Test Files Created

| Test File | Type | Coverage |
|-----------|------|----------|
| [KanbAI-Core.Tests/Extensions/SignalRInfrastructureTests.cs](../../KanbAI-Core/KanbAI-Core.Tests/Extensions/SignalRInfrastructureTests.cs) | Unit | `AddSignalRInfrastructure` DI wiring + `AddCorsPolicy` `SupportsCredentials`/explicit-origins invariants |
| [KanbAI-Core.Tests/Integration/SignalRCorsPreflightIntegrationTests.cs](../../KanbAI-Core/KanbAI-Core.Tests/Integration/SignalRCorsPreflightIntegrationTests.cs) | Integration | CORS preflight on `/hubs/*` and `/api/*` paths from allowed and disallowed origins |
| [KanbAI-Core.Tests/Integration/SignalRJwtQueryStringIntegrationTests.cs](../../KanbAI-Core/KanbAI-Core.Tests/Integration/SignalRJwtQueryStringIntegrationTests.cs) | Integration | Real-JWT-middleware behavior of the `OnMessageReceived` handler — extraction, validation, REST-API isolation, sibling-name guard |

### 10.2 Test Coverage Summary

**Unit tests (6):**
- `AddSignalRInfrastructure_ReturnsSameServiceCollection_ForFluentChaining` — fluent-chaining contract.
- `AddSignalRInfrastructure_RegistersHubLifetimeManager` — `HubLifetimeManager<T>` resolvable from DI (core SignalR service).
- `AddSignalRInfrastructure_RegistersHubContext` — `IHubContext<T>` resolvable (issue #63 broadcast entry point).
- `AddSignalRInfrastructure_RegistersSignalRMarkerServices` — descriptor-level check of open-generic `HubLifetimeManager<>`.
- `AddCorsPolicy_PolicyHasSupportsCredentialsTrue` — `SupportsCredentials == true` on the resolved `CorsPolicy`.
- `AddCorsPolicy_PolicyHasExplicitOriginsNotWildcard` — regression guard against a future `AllowAnyOrigin()` swap (incompatible with `AllowCredentials`).

**Integration tests — CORS preflight (3):**
- Allowed origin → `Access-Control-Allow-Credentials: true` returned on `/hubs/*`.
- Disallowed origin → no CORS headers returned on `/hubs/*`.
- REST path `/api/project` preflight → still returns `Allow-Credentials: true` (shared policy, no regression).

**Integration tests — JWT query string (7):**
- `/hubs/probe?access_token=<valid>` → 200 (token extracted and validated).
- `/hubs/probe?access_token=<invalid>` → 401 (extracted token fails signature validation).
- `/hubs/probe` (no token) → 401.
- `/hubs/probe/deeper?access_token=<valid>` → 200 (nested path, `StartsWithSegments` matches).
- `/api/probe?access_token=<valid>` → 401 (query-string token MUST NOT leak into REST endpoints — key security test).
- `/api/probe` with `Authorization: Bearer <valid>` → 200 (header auth still works, no regression).
- `/hubsadmin?access_token=<valid>` → not 200 (segment-aware path guard pins `StartsWithSegments` behavior).

The JWT integration tests deliberately use the REAL JWT middleware (no `TestAuthHandler`) and sign tokens with the application's real `JwtSettings`, because the behavior under test IS the JWT middleware configuration. A minimal probe endpoint is injected via an `IStartupFilter` so authentication can be observed without requiring issue #62's hub classes.

### 10.3 Test Results

**Build:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

**Full Test Suite:**
```
Passed!  - Failed:     0, Passed:   422, Skipped:     2, Total:   424, Duration: 4 s
```

- 16 new tests added — all pass.
- 406 pre-existing tests continue to pass (no regressions).
- 2 skipped tests are pre-existing (Scalar UI tests — unrelated).

### 10.4 Bugs Found & Fixed

None. The implementation matches the tech spec and passes all coverage described in Section 6 (QA Guidance).

### 10.5 Outstanding Issues

None. All acceptance criteria from the context note are covered:

| Acceptance Criterion | Validated By |
|---|---|
| 1. SignalR package available / builds | Build succeeded; `AddSignalRInfrastructure_RegistersHubLifetimeManager` |
| 2. SignalR services registered in DI | `AddSignalRInfrastructure_RegistersHubContext`, `AddSignalRInfrastructure_RegistersSignalRMarkerServices` |
| 3. CORS supports credentialed WebSockets | `AddCorsPolicy_PolicyHasSupportsCredentialsTrue`, `Preflight_ToFutureHubPath_FromAllowedOrigin_IncludesAllowCredentialsHeader` |
| 4. Middleware pipeline order unchanged and correct | Every integration test boots the full production pipeline; `Preflight_ToRestApiPath_FromAllowedOrigin_AlsoIncludesAllowCredentialsHeader` confirms CORS still precedes auth |
| 5. No breaking changes to existing functionality | Full-suite regression — 406 pre-existing tests still pass |
| 6. Ready for hub implementation | `IHubContext<T>` resolvable; JWT query-string extraction works on arbitrary `/hubs/*` paths |

QA testing is complete. All tests pass and the implementation meets the acceptance criteria.
