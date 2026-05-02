# Technical Specification: Issue #62 - Create KanbanHub and Connection Management

**GitHub Issue:** [#62 - Create KanbanHub and Connection Management](https://github.com/Gulybi/KanbAI-Core/issues/62)  
**Context Document:** [issue_62_context.md](./issue_62_context.md)  
**Staff Engineer:** @agent_staff_engineer  
**Created:** 2026-05-02

---

## 1. Overview

This specification defines the implementation of the SignalR Hub infrastructure that enables real-time, bidirectional communication between the server and connected clients. The implementation introduces a new `KanbanHub` class that manages client connections, project-specific SignalR groups, and integrates with the existing JWT authentication scheme configured in `Program.cs`.

**Scope:**
- Create `KanbanHub` class inheriting from `Microsoft.AspNetCore.SignalR.Hub` in a new `Hubs/` folder
- Implement `JoinProjectGroup(string projectId)` and `LeaveProjectGroup(string projectId)` hub methods for client-invocable group management
- Register SignalR services in `Program.cs` using `builder.Services.AddSignalR()`
- Map the hub endpoint `/hubs/kanban` in `Program.cs` using `app.MapHub<KanbanHub>("/hubs/kanban")`
- Configure SignalR to use the existing JWT Bearer authentication scheme for WebSocket connection authorization
- Establish a consistent group naming convention: `project_{projectId}` (lowercase Guid)
- Implement structured logging for connection lifecycle and group operations

**Out of Scope (per context note):**
- No `IHubContext<KanbanHub>` injection into services (deferred to Issue #63)
- No authorization checks within the hub methods to verify project membership (can be added in Issue #63)
- No client-to-client messaging, presence indicators, typing indicators, or custom event types
- No database changes, entity changes, or EF Core configuration changes
- No broadcasting of business events (Issue #63 will add this capability)
- No frontend client implementation (separate Angular integration)

**Why No Database Changes:**
The hub infrastructure operates on existing domain entities (`Project`, `BoardColumn`, `ProjectMember`) and does not require new database tables or schema modifications. Group membership is managed entirely in-memory by SignalR's built-in `Groups` API.

**Design Note - JWT Authentication Integration:**
SignalR in ASP.NET Core 3.0+ automatically inherits the configured authentication schemes from the application's middleware pipeline. Because JWT Bearer is already configured as the default authentication scheme in `Program.cs` (lines 23-42), SignalR connections will automatically require a valid JWT token during the WebSocket handshake. No additional SignalR-specific authentication configuration is required.

---

## 2. Database/Domain Design

**N/A** - No database or domain changes required. This issue is purely infrastructure and application-layer code.

The hub will reference existing entities conceptually (Project.Id for group naming) but does not directly interact with the database. Issue #63 will introduce `ApplicationDbContext` usage in services that broadcast through `IHubContext<KanbanHub>`.

---

## 3. API Contracts

### 3.1 SignalR Hub Endpoint

| Endpoint | Protocol | Auth | Description |
|----------|----------|------|-------------|
| `/hubs/kanban` | WebSocket (with HTTP upgrade handshake) | JWT Bearer Required | SignalR hub endpoint for real-time Kanban board updates. Clients must provide a valid JWT token during connection (via query string `?access_token=<jwt>` or Authorization header). |

### 3.2 Client-Invocable Hub Methods

SignalR hubs expose methods that clients can invoke via the SignalR client library. The following methods are public and client-callable:

#### JoinProjectGroup Method

```csharp
public async Task JoinProjectGroup(string projectId)
```

**Purpose:** Adds the calling connection to a project-specific SignalR group so the client receives broadcast messages scoped to that project.

**Parameters:**
- `projectId` (string): The project's Guid identifier as a string (e.g., `"c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o"`).

**Behavior:**
1. Validates that `projectId` is not null, empty, or whitespace.
2. Validates that `projectId` is a valid Guid format using `Guid.TryParse`.
3. Constructs the group name: `project_{projectId}` (Guid converted to lowercase).
4. Adds the connection to the group using `Groups.AddToGroupAsync(Context.ConnectionId, groupName)`.
5. Logs the operation with structured logging: `User {UserId} joined project group {GroupName} (connection {ConnectionId})`.
6. Returns `Task` (no return value; void equivalent in async context).

**Error Handling:**
- If `projectId` is null, empty, or whitespace → throws `HubException` with message `"Project ID is required."`
- If `projectId` is not a valid Guid → throws `HubException` with message `"Invalid project ID format."`
- SignalR automatically translates `HubException` into a client-side error event with the provided message.

#### LeaveProjectGroup Method

```csharp
public async Task LeaveProjectGroup(string projectId)
```

**Purpose:** Removes the calling connection from a project-specific SignalR group.

**Parameters:**
- `projectId` (string): The project's Guid identifier as a string.

**Behavior:**
1. Validates that `projectId` is not null, empty, or whitespace.
2. Validates that `projectId` is a valid Guid format using `Guid.TryParse`.
3. Constructs the group name: `project_{projectId}` (lowercase).
4. Removes the connection from the group using `Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName)`.
5. Logs the operation: `User {UserId} left project group {GroupName} (connection {ConnectionId})`.
6. Returns `Task` (no return value).

**Error Handling:**
- Same validation as `JoinProjectGroup`.
- Attempting to leave a group the client never joined does NOT throw an exception (SignalR handles this gracefully as a no-op).

### 3.3 Hub Context Properties (Server-Side)

The following `Hub` base class properties are accessible within hub methods:

| Property | Type | Description |
|----------|------|-------------|
| `Context.ConnectionId` | `string` | Unique identifier for the current SignalR connection. |
| `Context.User` | `ClaimsPrincipal?` | Authenticated user's claims principal (populated by JWT Bearer authentication). |
| `Context.UserIdentifier` | `string?` | User ID extracted from JWT claims (usually `ClaimTypes.NameIdentifier`). |
| `Groups` | `IGroupManager` | API for adding/removing connections to/from named groups. |

### 3.4 Group Naming Convention

**Format:** `project_{projectId}`

**Examples:**
- Project ID `c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o` → group name `project_c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o`
- Project ID `12345678-1234-1234-1234-123456789abc` → group name `project_12345678-1234-1234-1234-123456789abc`

**Rationale:**
- **Lowercase normalization:** Guid.ToString() returns lowercase by default in .NET; explicitly use `.ToLowerInvariant()` for consistency.
- **Prefix `project_`:** Scopes the group name namespace, allowing future expansion (e.g., `column_`, `task_`, `board_`).
- **No URL encoding or escaping required:** SignalR group names are internal identifiers (not exposed in URLs); Guids contain only alphanumeric and hyphen characters.

**Case Sensitivity:** SignalR group names are **case-sensitive** by default. Normalize all project IDs to lowercase before constructing group names to prevent `project_ABC123` and `project_abc123` from being treated as different groups.

### 3.5 Example Client Usage (TypeScript/JavaScript)

```typescript
import * as signalR from "@microsoft/signalr";

// Establish connection with JWT token
const connection = new signalR.HubConnectionBuilder()
  .withUrl("https://localhost:5001/hubs/kanban", {
    accessTokenFactory: () => localStorage.getItem("jwtToken")!
  })
  .withAutomaticReconnect()
  .build();

// Start connection
await connection.start();
console.log("Connected to KanbanHub");

// Join a project group
await connection.invoke("JoinProjectGroup", "c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o");
console.log("Joined project group");

// Leave a project group
await connection.invoke("LeaveProjectGroup", "c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o");
console.log("Left project group");

// Listen for server-to-client events (Issue #63 will define these)
connection.on("TaskCreated", (data) => {
  console.log("Task created:", data);
});
```

**Note:** The client-side implementation is out of scope for this issue. The above example is provided for context only.

---

## 4. Application Layer Boundaries

### 4.1 KanbanHub Class

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Hubs\KanbanHub.cs`

**Namespace:** `KanbAI_Core.Hubs`

**Base Class:** `Microsoft.AspNetCore.SignalR.Hub`

**Dependencies (Constructor Injection):**
- `ILogger<KanbanHub> _logger`

**Public Methods:**

```csharp
namespace KanbAI_Core.Hubs;

using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

public sealed class KanbanHub : Hub
{
    private readonly ILogger<KanbanHub> _logger;

    public KanbanHub(ILogger<KanbanHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Adds the calling connection to a project-specific SignalR group.
    /// Clients in the same group receive identical broadcast messages sent to that group.
    /// </summary>
    /// <param name="projectId">The project's Guid identifier as a string.</param>
    /// <exception cref="HubException">
    /// Thrown when projectId is null, empty, whitespace, or not a valid Guid format.
    /// </exception>
    public async Task JoinProjectGroup(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            _logger.LogWarning(
                "User {UserId} attempted to join a project group with null/empty projectId (connection {ConnectionId})",
                GetUserId(), Context.ConnectionId);
            throw new HubException("Project ID is required.");
        }

        if (!Guid.TryParse(projectId, out var projectGuid))
        {
            _logger.LogWarning(
                "User {UserId} attempted to join a project group with invalid projectId format: {ProjectId} (connection {ConnectionId})",
                GetUserId(), projectId, Context.ConnectionId);
            throw new HubException("Invalid project ID format.");
        }

        var groupName = $"project_{projectGuid.ToString().ToLowerInvariant()}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "User {UserId} joined project group {GroupName} (connection {ConnectionId})",
            GetUserId(), groupName, Context.ConnectionId);
    }

    /// <summary>
    /// Removes the calling connection from a project-specific SignalR group.
    /// Clients navigating away from a project board should invoke this method to stop receiving updates.
    /// </summary>
    /// <param name="projectId">The project's Guid identifier as a string.</param>
    /// <exception cref="HubException">
    /// Thrown when projectId is null, empty, whitespace, or not a valid Guid format.
    /// </exception>
    public async Task LeaveProjectGroup(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            _logger.LogWarning(
                "User {UserId} attempted to leave a project group with null/empty projectId (connection {ConnectionId})",
                GetUserId(), Context.ConnectionId);
            throw new HubException("Project ID is required.");
        }

        if (!Guid.TryParse(projectId, out var projectGuid))
        {
            _logger.LogWarning(
                "User {UserId} attempted to leave a project group with invalid projectId format: {ProjectId} (connection {ConnectionId})",
                GetUserId(), projectId, Context.ConnectionId);
            throw new HubException("Invalid project ID format.");
        }

        var groupName = $"project_{projectGuid.ToString().ToLowerInvariant()}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        _logger.LogInformation(
            "User {UserId} left project group {GroupName} (connection {ConnectionId})",
            GetUserId(), groupName, Context.ConnectionId);
    }

    /// <summary>
    /// Invoked when a client connects to the hub.
    /// Logs connection events for observability.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation(
            "User {UserId} connected to KanbanHub (connection {ConnectionId})",
            GetUserId(), Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Invoked when a client disconnects from the hub.
    /// Logs disconnection events for observability.
    /// SignalR automatically cleans up group memberships on disconnect.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning(
                exception,
                "User {UserId} disconnected from KanbanHub with exception (connection {ConnectionId})",
                GetUserId(), Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation(
                "User {UserId} disconnected from KanbanHub (connection {ConnectionId})",
                GetUserId(), Context.ConnectionId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Extracts the user ID from the authenticated user's JWT claims.
    /// Returns "anonymous" if the user is not authenticated or the NameIdentifier claim is missing.
    /// </summary>
    private string GetUserId()
    {
        return Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
    }
}
```

**Key Design Decisions:**

1. **Sealed Class:** Prevents inheritance to avoid complexity; SignalR hubs are typically leaf types.
2. **HubException for Validation Errors:** SignalR's standard exception type for client-facing errors; automatically propagated to clients as error events.
3. **Structured Logging:** All log messages use parameterized templates (never string interpolation) per `@rule_testing_observability`.
4. **GetUserId() Helper:** Centralizes the pattern of extracting user ID from claims; returns `"anonymous"` for unauthenticated connections (although authentication is enforced, this is a defensive fallback for logging).
5. **Lowercase Normalization:** `projectGuid.ToString().ToLowerInvariant()` ensures consistent group names regardless of how the client submits the Guid string.
6. **Connection Lifecycle Logging:** `OnConnectedAsync` and `OnDisconnectedAsync` provide observability without requiring external monitoring (logs are ingested by Serilog/Elasticsearch in production).

### 4.2 SignalR Configuration in Program.cs

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Program.cs`

**Changes Required:**

1. **Add using directive** (top of file, with other usings):
   ```csharp
   using KanbAI_Core.Hubs;
   ```

2. **Register SignalR services** (after line 20, before authentication configuration):
   ```csharp
   builder.Services.AddSignalR();
   ```

3. **Map hub endpoint** (after line 69, after `app.MapControllers()`):
   ```csharp
   app.MapHub<KanbanHub>("/hubs/kanban");
   ```

**Rationale for Placement:**

- **Service registration before authentication:** SignalR services must be registered in the DI container before `AddAuthentication` is called, although SignalR will automatically use the authentication schemes configured later in the pipeline.
- **Hub mapping after UseAuthorization:** The `app.MapHub` call must occur after `app.UseAuthentication()` and `app.UseAuthorization()` in the middleware pipeline (lines 66-67) to ensure authentication/authorization middleware processes the WebSocket upgrade request before SignalR handles the connection.
- **Hub mapping after MapControllers:** Consistent with the application's convention of registering endpoint mappings at the end of `Program.cs` (controllers on line 69, hub on line 70).

**JWT Authentication Inheritance:**

SignalR automatically uses the default authentication scheme configured in `Program.cs` (JWT Bearer, lines 23-42). When a client attempts to connect to `/hubs/kanban`, the authentication middleware intercepts the WebSocket upgrade request and validates the JWT token. If the token is invalid or missing, the middleware returns HTTP 401 Unauthorized and the WebSocket connection is never established.

**No Additional SignalR Authentication Configuration Required:** Unlike some frameworks, ASP.NET Core SignalR does NOT require explicit `.RequireAuthorization()` calls or authentication options in `AddSignalR()`. The hub inherits the application's authentication pipeline automatically.

---

## 5. Implementation Steps

### Step 1: Create the `Hubs/` Folder

**Location:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Hubs\`

The `Hubs/` folder does not yet exist in the project. It will be created implicitly when the first file is written into it. No explicit `mkdir` command is required (the file system will create the directory on file write).

### Step 2: Create the `KanbanHub` Class

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Hubs\KanbanHub.cs`

Use the exact code from Section 4.1 above. Copy the entire class definition verbatim, including:
- File-scoped namespace declaration: `namespace KanbAI_Core.Hubs;`
- All XML documentation comments
- The sealed modifier on the class
- All three public methods: `JoinProjectGroup`, `LeaveProjectGroup`, `OnConnectedAsync`, `OnDisconnectedAsync`
- The private `GetUserId()` helper method

### Step 3: Register SignalR Services in `Program.cs`

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Program.cs`

**Location:** After line 20 (after `builder.Services.AddScoped<ITaskService, TaskService>();`)

**Add:**
```csharp
builder.Services.AddSignalR();
```

**Line placement rationale:** Register SignalR services in the DI container during the service registration phase, before building the `WebApplication`. This placement is after all other service registrations (auth, columns, tasks) and before the authentication/authorization configuration block (lines 22-42).

### Step 4: Add `using` Directive for Hubs

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Program.cs`

**Location:** Top of file, after existing `using` statements (after line 9)

**Add:**
```csharp
using KanbAI_Core.Hubs;
```

This allows the `MapHub<KanbanHub>` call in Step 5 to resolve the `KanbanHub` type.

### Step 5: Map the Hub Endpoint in `Program.cs`

**File:** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Program.cs`

**Location:** After line 69 (after `app.MapControllers();`)

**Add:**
```csharp
app.MapHub<KanbanHub>("/hubs/kanban");
```

**Middleware pipeline ordering verification:**

The hub endpoint mapping MUST occur after the following middleware calls (lines 63-67):
```csharp
app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();
```

Endpoint mapping happens at the end of the pipeline, after all middleware is registered. The current code structure is correct: middleware setup (lines 63-67) → endpoint mapping (line 69 controllers, then line 70 hub).

### Step 6: Build & Verify

**Command:**
```bash
cd c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core
dotnet build --no-incremental
```

**Expected Output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

If the build fails, check:
- The `Hubs/` folder was created correctly.
- The `using KanbAI_Core.Hubs;` directive is present in `Program.cs`.
- No syntax errors in `KanbanHub.cs` (verify all braces and semicolons are balanced).
- The `Microsoft.AspNetCore.SignalR` namespace is available (it is part of the ASP.NET Core framework, not a separate NuGet package, so no additional package reference is needed).

### Step 7: Smoke Test (Optional)

Run the application and verify the hub endpoint is registered:

**Command:**
```bash
dotnet run
```

**Verification Steps:**
1. Open the Scalar UI at `https://localhost:5001/scalar/v1` (Development environment only).
2. Check the application logs (console output) for the following line during startup:
   ```
   info: Microsoft.AspNetCore.Routing.EndpointMiddleware[1]
         Executing endpoint 'KanbAI_Core.Hubs.KanbanHub'
   ```
   This log entry will appear when a client attempts to connect to `/hubs/kanban`, confirming the endpoint is mapped correctly.

3. Use a SignalR client tool (e.g., Postman with WebSocket support, or the SignalR .NET client) to attempt a connection:
   ```
   wss://localhost:5001/hubs/kanban?access_token=<valid-jwt-token>
   ```
   - With a valid JWT token → connection succeeds, logs show `"User {UserId} connected to KanbanHub"`
   - Without a JWT token → connection fails with HTTP 401 Unauthorized during WebSocket handshake
   - With an invalid JWT token → connection fails with HTTP 401 Unauthorized

This smoke test is not a substitute for automated tests (QA will write comprehensive integration tests). It is a quick sanity check to confirm the hub is reachable.

---

## 6. QA Guidance

### 6.1 Test File Locations

| Test File | Location | Type | Purpose |
|-----------|----------|------|---------|
| `KanbanHubTests.cs` | `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Hubs\` | Unit | Hub method logic, validation, group operations (using mocked `IHubCallerClients` and `IGroupManager`) |
| `KanbanHubIntegrationTests.cs` | `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Integration\` | Integration | WebSocket connection with JWT auth, hub invocation over real SignalR transport (using `WebApplicationFactory<Program>` and `HubConnectionBuilder`) |

### 6.2 Test Case Tables

#### KanbanHubTests.cs (Unit - Mocked SignalR Context)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 1 | `JoinProjectGroup_ValidProjectId_AddsConnectionToGroup` | Happy path | Valid Guid → `Groups.AddToGroupAsync` called with correct group name |
| 2 | `JoinProjectGroup_ValidProjectId_LogsInformationEvent` | Logging | Verify structured log message with UserId, GroupName, ConnectionId |
| 3 | `JoinProjectGroup_NullProjectId_ThrowsHubException` | Validation | Null `projectId` → `HubException("Project ID is required.")` |
| 4 | `JoinProjectGroup_EmptyProjectId_ThrowsHubException` | Validation | Empty string `projectId` → `HubException("Project ID is required.")` |
| 5 | `JoinProjectGroup_WhitespaceProjectId_ThrowsHubException` | Validation | `"   "` projectId → `HubException("Project ID is required.")` |
| 6 | `JoinProjectGroup_InvalidGuidFormat_ThrowsHubException` | Validation | `"not-a-guid"` → `HubException("Invalid project ID format.")` |
| 7 | `JoinProjectGroup_ValidGuid_NormalizesToLowercase` | Consistency | Uppercase/mixed-case Guid input → group name uses lowercase |
| 8 | `LeaveProjectGroup_ValidProjectId_RemovesConnectionFromGroup` | Happy path | Valid Guid → `Groups.RemoveFromGroupAsync` called with correct group name |
| 9 | `LeaveProjectGroup_ValidProjectId_LogsInformationEvent` | Logging | Verify structured log message |
| 10 | `LeaveProjectGroup_NullProjectId_ThrowsHubException` | Validation | Same validation as JoinProjectGroup |
| 11 | `LeaveProjectGroup_EmptyProjectId_ThrowsHubException` | Validation | Same validation as JoinProjectGroup |
| 12 | `LeaveProjectGroup_InvalidGuidFormat_ThrowsHubException` | Validation | Same validation as JoinProjectGroup |
| 13 | `LeaveProjectGroup_GroupNotJoined_DoesNotThrow` | Edge case | Leaving a group the connection never joined does not throw (SignalR no-op behavior) |
| 14 | `OnConnectedAsync_LogsConnectionEvent` | Lifecycle | Connection ID and UserId logged |
| 15 | `OnDisconnectedAsync_WithoutException_LogsInformationEvent` | Lifecycle | Clean disconnect logged at Information level |
| 16 | `OnDisconnectedAsync_WithException_LogsWarningEvent` | Lifecycle | Disconnect with exception logged at Warning level with exception details |
| 17 | `GetUserId_AuthenticatedUser_ReturnsNameIdentifierClaim` | Security | Context.User with NameIdentifier claim → returns claim value |
| 18 | `GetUserId_UnauthenticatedUser_ReturnsAnonymous` | Security | Context.User is null → returns "anonymous" |
| 19 | `GetUserId_MissingNameIdentifierClaim_ReturnsAnonymous` | Security | Context.User exists but no NameIdentifier → returns "anonymous" |

#### KanbanHubIntegrationTests.cs (Integration - Real WebSocket)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 20 | `Connect_WithValidJwtToken_EstablishesConnection` | Auth | Valid token → connection succeeds, `OnConnectedAsync` invoked |
| 21 | `Connect_WithoutJwtToken_Returns401Unauthorized` | Auth | No token → WebSocket upgrade fails with 401 |
| 22 | `Connect_WithInvalidJwtToken_Returns401Unauthorized` | Auth | Malformed or expired token → 401 |
| 23 | `JoinProjectGroup_ValidProjectId_SuccessfullyJoins` | Happy path | Invoke `JoinProjectGroup` with valid Guid → no error, logs show success |
| 24 | `JoinProjectGroup_InvalidProjectId_ReceivesHubException` | Validation | Invoke with `"invalid"` → client receives error event with message `"Invalid project ID format."` |
| 25 | `LeaveProjectGroup_ValidProjectId_SuccessfullyLeaves` | Happy path | Invoke `LeaveProjectGroup` with valid Guid → no error |
| 26 | `JoinMultipleGroups_SameConnection_AllowsMultipleGroupMemberships` | Edge case | Single connection can join multiple project groups (e.g., user viewing multiple boards in tabs) |
| 27 | `Disconnect_AutomaticallyRemovesFromAllGroups` | Lifecycle | Connection closes → SignalR automatically cleans up group memberships (verified by checking no orphaned group entries) |

### 6.3 Test Infrastructure

#### Unit Tests (KanbanHubTests.cs)

**Mocking Strategy:**

SignalR hubs depend on several contextual properties (`Context`, `Groups`, `Clients`) that are difficult to construct directly. Use the following mocking pattern:

```csharp
using Moq;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

public class KanbanHubTests
{
    private readonly Mock<IGroupManager> _mockGroups;
    private readonly Mock<HubCallerContext> _mockContext;
    private readonly Mock<ILogger<KanbanHub>> _mockLogger;
    private readonly KanbanHub _hub;

    public KanbanHubTests()
    {
        _mockGroups = new Mock<IGroupManager>();
        _mockContext = new Mock<HubCallerContext>();
        _mockLogger = new Mock<ILogger<KanbanHub>>();

        _hub = new KanbanHub(_mockLogger.Object)
        {
            Groups = _mockGroups.Object,
            Context = _mockContext.Object
        };
    }

    [Fact]
    public async Task JoinProjectGroup_ValidProjectId_AddsConnectionToGroup()
    {
        // Arrange
        var projectId = Guid.NewGuid().ToString();
        var connectionId = "conn-12345";
        var userId = Guid.NewGuid().ToString();

        _mockContext.Setup(c => c.ConnectionId).Returns(connectionId);
        _mockContext.Setup(c => c.User).Returns(CreateClaimsPrincipal(userId));

        // Act
        await _hub.JoinProjectGroup(projectId);

        // Assert
        var expectedGroupName = $"project_{projectId.ToLowerInvariant()}";
        _mockGroups.Verify(
            g => g.AddToGroupAsync(connectionId, expectedGroupName, default),
            Times.Once);
    }

    private static ClaimsPrincipal CreateClaimsPrincipal(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }
}
```

**Verification Points:**
- Verify `Groups.AddToGroupAsync` or `Groups.RemoveFromGroupAsync` is called with the correct parameters.
- Verify structured log messages using `Mock<ILogger>` verification (check log level, message template, and parameter values).
- Verify `HubException` is thrown with the exact expected message.

#### Integration Tests (KanbanHubIntegrationTests.cs)

**Testing Strategy:**

Use `WebApplicationFactory<Program>` to host the application in-memory, then use the official SignalR .NET client (`Microsoft.AspNetCore.SignalR.Client`) to establish a WebSocket connection and invoke hub methods.

**Required NuGet Package (Test Project):**
```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.5" />
```

**Example Test Setup:**

```csharp
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;

public class KanbanHubIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public KanbanHubIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Connect_WithValidJwtToken_EstablishesConnection()
    {
        // Arrange
        var token = GenerateValidJwtToken(); // Helper method to generate JWT
        var hubUrl = $"{_factory.Server.BaseAddress}hubs/kanban";

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult(token)!;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        // Act
        await connection.StartAsync();

        // Assert
        connection.State.Should().Be(HubConnectionState.Connected);

        // Cleanup
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Connect_WithoutJwtToken_Returns401Unauthorized()
    {
        // Arrange
        var hubUrl = $"{_factory.Server.BaseAddress}hubs/kanban";

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .Build();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await connection.StartAsync());

        exception.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private string GenerateValidJwtToken()
    {
        // Use the same JWT generation logic from the application's TokenService
        // Or inject ITokenService from the test factory and call GenerateToken
        // Placeholder implementation:
        var jwtSettings = new JwtSettings
        {
            SecretKey = "test-secret-key-must-be-at-least-32-characters-long",
            Issuer = "KanbAI-Core",
            Audience = "KanbAI-Core",
            ExpirationMinutes = 60
        };
        // ... (use System.IdentityModel.Tokens.Jwt to generate a token)
    }
}
```

**Key Testing Concerns:**

1. **Authentication Enforcement:** Verify that connections without a valid JWT token are rejected during the WebSocket upgrade handshake (HTTP 401 response before WebSocket protocol negotiation).
2. **Hub Method Invocation:** Verify that `connection.InvokeAsync("JoinProjectGroup", projectId)` succeeds for valid input and throws for invalid input.
3. **Error Propagation:** Verify that `HubException` messages are received by the client as error events (SignalR client libraries expose these as exceptions on the `InvokeAsync` call).
4. **Connection Lifecycle:** Verify that `OnConnectedAsync` and `OnDisconnectedAsync` are invoked by checking application logs (or by mocking `ILogger` in the test factory).

**Note on TestAuthHandler:**

Unlike REST API integration tests (which use `TestAuthHandler` per `@rule_integration_testing`), SignalR integration tests should use real JWT tokens because SignalR's WebSocket transport does not support the same authentication middleware interception points as HTTP requests. Attempting to use `TestAuthHandler` with SignalR will fail during WebSocket negotiation.

### 6.4 Naming Conventions

All test methods MUST follow the pattern: `MethodName_StateUnderTest_ExpectedBehavior` per `@rule_testing_observability`.

Examples:
- `JoinProjectGroup_ValidProjectId_AddsConnectionToGroup`
- `Connect_WithoutJwtToken_Returns401Unauthorized`

### 6.5 AAA Structure

Every test must use Arrange-Act-Assert structure with blank lines separating the three sections:

```csharp
[Fact]
public async Task Example_Test()
{
    // Arrange
    var projectId = Guid.NewGuid().ToString();

    // Act
    await _hub.JoinProjectGroup(projectId);

    // Assert
    _mockGroups.Verify(g => g.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Once);
}
```

---

## 7. Known Caveats

| # | Caveat | Impact | Mitigation |
|---|--------|--------|-----------|
| 1 | **No project membership authorization in hub methods** | Any authenticated user can join any project group, even if they are not a member of that project. | Accepted per context note ("Out of Scope"). Issue #63 will add authorization checks by injecting `ApplicationDbContext` and verifying `ProjectMember` rows before allowing group joins. |
| 2 | **Group name case sensitivity** | SignalR group names are case-sensitive. If a client submits `"ABC-123"` and another submits `"abc-123"`, they will join different groups. | Mitigated by explicitly normalizing all project IDs to lowercase using `.ToLowerInvariant()` before constructing group names. Document this convention prominently for Issue #63. |
| 3 | **No rate limiting on hub method invocations** | A malicious client could spam `JoinProjectGroup` calls with random Guids, causing excessive logging and potential memory pressure. | Accepted for this issue. Future enhancement can add rate limiting middleware or hub filters (SignalR supports `IHubFilter` for cross-cutting concerns). |
| 4 | **WebSocket connection limits** | The default ASP.NET Core Kestrel server has a connection limit that may be insufficient for high-concurrency scenarios (e.g., 1000+ simultaneous users). | Not a concern for initial deployment. If connection limits are reached in production, configure `KestrelServerOptions.Limits.MaxConcurrentConnections` in `Program.cs`. |
| 5 | **SignalR automatic reconnection on client** | The client example in §3.5 uses `.withAutomaticReconnect()`, which means clients will attempt to reconnect after network interruptions. If a user was in a project group before disconnecting, they must manually re-invoke `JoinProjectGroup` after reconnecting (SignalR does not persist group memberships across reconnects). | This is expected SignalR behavior. Client applications must track which groups they were in and re-join after reconnection. Document this in the frontend integration guide. |
| 6 | **JWT token expiration during long-lived connections** | A WebSocket connection established with a valid JWT token will remain open even after the token expires. SignalR does not re-validate the token after the initial handshake. | Accepted per SignalR design. If token revocation is required (e.g., user logs out), the server can forcibly disconnect the client by calling `IHubContext<KanbanHub>.Clients.User(userId).SendAsync("ForceDisconnect")` (to be implemented in Issue #63 if needed). |
| 7 | **No graceful degradation to long polling** | The hub endpoint uses WebSocket transport exclusively. Clients behind restrictive firewalls or proxies that block WebSocket connections will fail to connect. | SignalR supports automatic fallback to Server-Sent Events (SSE) and Long Polling if WebSocket fails, but this requires additional client configuration (`.WithUrl(url, options => options.Transports = HttpTransportType.All)`). Document this in the frontend integration guide. |
| 8 | **TestServer limitations for SignalR integration tests** | Unlike REST API tests, SignalR WebSocket tests cannot use `TestAuthHandler` (see §6.3 Note). Integration tests must use real JWT tokens. | Mitigated by providing a `GenerateValidJwtToken()` helper method in the test class that reuses the application's `TokenService` logic or constructs tokens directly using `System.IdentityModel.Tokens.Jwt`. |

---

## 8. Design Validation (Self-Check)

| Check | Question | Status |
|-------|----------|--------|
| **Namespaces** | Do all referenced namespaces match `RootNamespace = KanbAI_Core` and existing conventions? | ✅ Pass — `KanbAI_Core.Hubs` follows the same pattern as `KanbAI_Core.Controllers`, `KanbAI_Core.Services.Tasks` |
| **Folder Paths** | Do all file paths reference real folders, or are "create new" steps included? | ✅ Pass — Step 1 explicitly documents that `Hubs/` folder will be created; no other new folders required |
| **Dependencies** | Are all required NuGet packages already in the `.csproj`, or are install steps included? | ✅ Pass — `Microsoft.AspNetCore.SignalR` is part of the ASP.NET Core framework (no separate package required for .NET 10.0). Test project needs `Microsoft.AspNetCore.SignalR.Client` (documented in §6.3). |
| **Naming Conflicts** | Do any new class/enum names conflict with existing types? | ✅ Pass — `KanbanHub` is unique; no conflicts with `Hub` base class (fully qualified as `Microsoft.AspNetCore.SignalR.Hub`) |
| **BaseEntity Compliance** | Do new entities inherit from `BaseEntity`? | ✅ N/A — No new entities; hub does not persist data to the database |
| **Code Standards** | File-scoped namespaces, no blocking async, constructor injection, structured logging? | ✅ Pass — `namespace KanbAI_Core.Hubs;` (file-scoped), all methods return `Task` (no `.Result`/`.Wait()`), logger injected via constructor, all log messages use parameterized templates |
| **Security** | No hardcoded secrets; no PII in logs; authentication enforced; input validation? | ✅ Pass — No secrets in code; only GUIDs and connection IDs in logs (no email/name/password); JWT authentication enforced via middleware; `projectId` validated as Guid format before use |

**Result:** All checks pass. Design is ready for implementation.

---

## Development Status

### Files Created

| File Path | Purpose |
|-----------|---------|
| `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Hubs\KanbanHub.cs` | SignalR Hub class for real-time Kanban board communication. Provides client-invocable methods `JoinProjectGroup` and `LeaveProjectGroup` for group management. Implements connection lifecycle logging via `OnConnectedAsync` and `OnDisconnectedAsync`. |

### Files Modified

| File Path | Changes |
|-----------|---------|
| `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Program.cs` | **Line 8:** Added `using KanbAI_Core.Hubs;` directive to enable hub type resolution. **Line 22:** Added `builder.Services.AddSignalR();` to register SignalR services in DI container. **Line 72:** Added `app.MapHub<KanbanHub>("/hubs/kanban");` to map hub endpoint after `UseAuthentication()` and `UseAuthorization()` middleware. |

### Build & Test Results

**Build Status:** ✅ Success
- **Warnings:** 0
- **Errors:** 0
- **Build Time:** 3.08 seconds

**Test Results:** ✅ All Passed
- **Total Tests:** 408
- **Passed:** 406
- **Failed:** 0
- **Skipped:** 2 (pre-existing)
- **Duration:** 5 seconds

**Regression Analysis:** No new test failures introduced. All pre-existing tests continue to pass.

### Infrastructure Notes

**SignalR Package Dependency:**
- No additional NuGet package installation required. `Microsoft.AspNetCore.SignalR` is built into the ASP.NET Core 10.0 framework and is available implicitly. The project's existing `.csproj` file requires no modifications.

**JWT Authentication Integration:**
- SignalR hub automatically inherits JWT Bearer authentication configured in `Program.cs` (lines 24-44). The hub endpoint `/hubs/kanban` will reject WebSocket connections that do not provide a valid JWT token during the handshake.
- No additional SignalR-specific authentication configuration was added, per the tech spec design note (Section 1).

**Group Naming Convention:**
- Implemented consistent group naming pattern: `project_{projectId}` with lowercase normalization via `.ToLowerInvariant()` to prevent case-sensitivity issues.
- The pattern is reusable by services in Issue #63 for broadcasting events to project-scoped groups.

### Edge Cases for QA

The QA Tester should focus testing on the following areas:

| Category | Test Focus |
|----------|-----------|
| **Authentication Enforcement** | Verify that clients without a valid JWT token receive HTTP 401 Unauthorized during WebSocket handshake. Verify that expired or malformed tokens are rejected. |
| **Input Validation** | Test `JoinProjectGroup` and `LeaveProjectGroup` with null, empty string, whitespace, and invalid Guid formats (e.g., `"not-a-guid"`, `"abc-123"`). Verify `HubException` is thrown with correct error messages. |
| **Case Normalization** | Submit mixed-case and uppercase Guid strings to `JoinProjectGroup`. Verify that group names are consistently lowercase (e.g., `"ABC-123"` → `project_abc-123`). |
| **Multiple Group Membership** | Verify a single connection can join multiple project groups simultaneously (e.g., user viewing multiple boards in different tabs). |
| **Connection Lifecycle** | Verify `OnConnectedAsync` and `OnDisconnectedAsync` log messages appear correctly. Verify SignalR automatically cleans up group memberships when a connection is closed. |
| **Graceful Leave Behavior** | Invoke `LeaveProjectGroup` for a group the connection never joined. Verify no exception is thrown (SignalR should handle as a no-op). |
| **Logging Quality** | Verify all log messages use parameterized templates (no string interpolation). Verify UserId, GroupName, and ConnectionId are captured correctly in structured logs. |

**Known Limitations (Deferred to Issue #63):**
- No project membership authorization checks: Any authenticated user can join any project group, even if they are not a member. This is expected per the tech spec (Section 7, Caveat #1).
- No broadcasting of business events: The hub infrastructure is in place but services do not yet use `IHubContext<KanbanHub>` to send events. This will be implemented in Issue #63.

---

**Document Status:** Implementation Complete  
**Last Updated:** 2026-05-02  
**Developer:** @agent_developer  
**Next Steps:** QA Tester should review the implementation and write automated tests per Section 6 (QA Guidance).

---

## QA Status

**QA Engineer:** @agent_tester_qa  
**QA Completion Date:** 2026-05-03  
**Status:** ✅ Complete — all tests pass, one bug found and fixed.

### Test Files Created

| File Path | Type | Tests | Coverage |
|-----------|------|-------|----------|
| `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Hubs\KanbanHubTests.cs` | Unit | 20 | Hub method logic (`JoinProjectGroup`, `LeaveProjectGroup`), validation branches, lowercase normalization, connection lifecycle (`OnConnectedAsync`, `OnDisconnectedAsync`), and `GetUserId` behavior — exercised via mocked `IGroupManager`, `HubCallerContext`, and `ILogger<KanbanHub>`. |
| `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Integration\KanbanHubIntegrationTests.cs` | Integration | 11 | End-to-end SignalR connection through `WebApplicationFactory<Program>` using the `Microsoft.AspNetCore.SignalR.Client` library over `LongPolling` transport (TestServer does not support WebSocket). Covers authentication enforcement (HTTP negotiate), connection establishment, hub method invocation, validation-error propagation back to the client, and multi-group membership on a single connection. |

### Test Project Dependency Added

- `Microsoft.AspNetCore.SignalR.Client` `10.0.5` added to `KanbAI-Core.Tests.csproj` to host a real SignalR client inside the test process.

### Test Results

- **Total tests (solution):** 439 (was 408 before QA)
- **Passed:** 437
- **Failed:** 0
- **Skipped:** 2 (pre-existing Scalar UI tests; unrelated)
- **New tests added:** 31 (20 unit + 11 integration)
- **Duration:** ~5 seconds
- **Regressions:** None. All 406 pre-existing pass-count tests still pass.

### Bugs Found & Fixed

**Bug: `/hubs/kanban` accepted unauthenticated WebSocket/HTTP connections.**

- **Expected** (per context note acceptance criterion #4): *"Clients attempting to connect to `/hubs/kanban` without a valid JWT token receive a connection rejection (e.g., HTTP 401 Unauthorized during WebSocket handshake)."*
- **Actual** (observed by `Negotiate_Unauthenticated_Returns401` integration test): The negotiate endpoint returned HTTP 200 and issued a `connectionId` to anonymous callers, and `HubConnection.StartAsync()` with no authenticated identity completed successfully. An anonymous client could then invoke `JoinProjectGroup` and `LeaveProjectGroup` freely.
- **Root cause:** The tech spec (Section 4.2) asserted that *"SignalR automatically uses the default authentication scheme configured in `Program.cs` ... No Additional SignalR Authentication Configuration Required."* This is only half true — SignalR *authenticates* (populates `Context.User`) but does not *authorize*. Because the application has no `FallbackPolicy`, every endpoint is anonymous-by-default unless it opts in. The REST controllers in this codebase opt in via `[Authorize]` on each controller class; the hub was missing the equivalent.
- **Fix applied:** Added `[Authorize]` attribute to `KanbanHub` in [KanbAI-Core/Hubs/KanbanHub.cs](../../KanbAI-Core/KanbAI-Core/Hubs/KanbanHub.cs), matching the pattern used by `ProjectController`, `ColumnController`, and `TaskController`. This enforces authorization at the hub negotiate request, rejecting anonymous connections with HTTP 401 before the SignalR connection is established.
- **Verification:** `Negotiate_Unauthenticated_Returns401` and `Connect_Unauthenticated_FailsToStart` now both pass, while `Negotiate_Authenticated_ReturnsSuccess` and `Connect_WithValidAuthentication_EstablishesConnection` continue to succeed.

### Outstanding Issues

None. Acceptance criteria coverage:

| Acceptance Criterion Group | Covered By |
|----------------------------|------------|
| SignalR hub infrastructure is established and accessible to authenticated clients | `Connect_WithValidAuthentication_EstablishesConnection`, `Negotiate_Authenticated_ReturnsSuccess` |
| Clients can join project-specific groups | `JoinProjectGroup_ValidProjectId_AddsConnectionToGroup`, `JoinProjectGroup_ValidProjectId_SuccessfullyJoins`, `JoinMultipleGroups_SameConnection_AllInvocationsSucceed` |
| Clients can leave project-specific groups | `LeaveProjectGroup_ValidProjectId_RemovesConnectionFromGroup`, `LeaveProjectGroup_ValidProjectId_SuccessfullyLeaves`, `LeaveProjectGroup_NeverJoined_DoesNotThrow` |
| JWT authentication is enforced for SignalR connections | `Negotiate_Unauthenticated_Returns401`, `Connect_Unauthenticated_FailsToStart` (after the bug fix above) |
| Group naming convention consistency | `JoinProjectGroup_ValidGuid_NormalizesToLowercase`, plus the `$"project_{projectGuid.ToString().ToLowerInvariant()}"` assertion in the other `AddsConnectionToGroup`/`RemovesConnectionFromGroup` tests |
| Edge cases handled gracefully | `JoinProjectGroup_NullProjectId_ThrowsHubException`, `_EmptyProjectId_`, `_WhitespaceProjectId_`, `_InvalidGuidFormat_`, and the `LeaveProjectGroup_*` validation mirrors |
| Connection lifecycle logging | `OnConnectedAsync_LogsConnectionEvent`, `OnDisconnectedAsync_WithoutException_LogsInformationEvent`, `OnDisconnectedAsync_WithException_LogsWarningEvent` |
| User identity extraction from JWT claims | `OnConnectedAsync_AuthenticatedUser_LogsNameIdentifierClaim`, `OnConnectedAsync_UnauthenticatedUser_LogsAnonymous`, `OnConnectedAsync_MissingNameIdentifierClaim_LogsAnonymous` |

### Notes for Issue #63

- The integration test infrastructure (`CreateFactory`, `BuildHubConnection`, `TestAuthHandler`) is designed to be reusable. When Issue #63 adds service-driven broadcasts via `IHubContext<KanbanHub>`, these tests can be extended by wiring a second `HubConnection` and asserting that broadcasts arrive via `connection.On<...>(...)` handlers.
- The `[Authorize]` added to fix the bug above gives authenticated-only access. Issue #63's project-membership check will layer on top (e.g., querying `ApplicationDbContext` for `ProjectMember` rows inside `JoinProjectGroup` before calling `Groups.AddToGroupAsync`).
- TestServer's lack of WebSocket support forced the integration tests onto `LongPolling`. Production deployments will use WebSocket as the default transport; the hub code path is identical either way, so `LongPolling` is a valid proxy for the end-to-end contract.
