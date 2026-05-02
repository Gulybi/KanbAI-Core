# Issue #62: Create KanbanHub and Connection Management

**GitHub Issue:** [#62 - Create KanbanHub and Connection Management](https://github.com/Gulybi/KanbAI-Core/issues/62)  
**Milestone:** Real-time Updates & SignalR Integration (Issue #62 of 3)  
**Assignee:** @Gulybi  
**Created:** 2026-05-02

---

## Business Value & Context

### What Are We Building?

A SignalR Hub infrastructure that enables real-time, bidirectional communication between the server and connected clients. This includes:
- **KanbanHub class** - The central SignalR hub that manages client connections and dispatches real-time updates
- **Connection grouping by project/board** - Automatic assignment of clients to SignalR groups based on the board they are viewing
- **Group membership management** - Methods for clients to join and leave project-specific groups when navigating between boards

### Who Is This For?

**Project Members** who need to:
- See changes made by other team members appear instantly on their Kanban board without manual page refresh
- Collaborate in real-time with confidence that all users are viewing the current state of the board
- Experience a responsive, modern application that updates seamlessly as work progresses

**Development Team** benefits by:
- Establishing a reusable SignalR infrastructure pattern for future real-time features
- Separating connection management concerns from business logic
- Creating a foundation that other services can use to broadcast events (prepared for Issue #63)

### Why Is This Valuable?

Real-time collaboration is a core expectation in modern project management tools. Without it:
- Users must manually refresh their browser to see updates made by teammates, creating confusion and data staleness
- Concurrent editing scenarios lead to conflicts and lost work (e.g., two users moving the same task simultaneously without knowing)
- The user experience feels outdated compared to industry-standard tools like Trello, Jira, or Asana
- Teams lose trust in the system as a single source of truth when different users see different board states

SignalR provides the technical foundation for features like:
- Live task updates (creation, movement, deletion)
- Real-time assignment notifications
- Collaborative editing indicators (seeing who else is viewing or editing the same board)
- Instant feedback when changes are made by any team member

This issue establishes the hub and connection infrastructure. Issue #63 will integrate it with existing services to broadcast business events.

---

## Milestone Context

**Milestone:** Real-time Updates & SignalR Integration  
**Total Issues:** 3

| Issue | Title | Status | Dependency |
|-------|-------|--------|------------|
| **#62** | **Create KanbanHub and Connection Management** | **OPEN** | **Foundation (YOU ARE HERE)** |
| #63 | Refactor Services to Broadcast Events | OPEN | Requires #62 (Hub must exist before services can broadcast through it) |
| #64 | Document AI SignalR Implementation (AI_LOGS.md) | OPEN | Requires #62 and #63 (documents the completed implementation) |

**Relationship to Other Issues:**
- **Foundation for #63**: The TaskService, ProjectService, and ColumnService will be refactored to inject `IHubContext<KanbanHub>` and broadcast events when tasks are created, moved, deleted, or when project/column changes occur. This cannot happen until KanbanHub exists.
- **Foundation for #64**: Documentation of the SignalR implementation requires the hub and event broadcasting to be complete before patterns can be documented.

**Prior Work:**
The application currently has:
- JWT-based authentication configured in `Program.cs` (lines 22-42)
- RESTful API controllers for Projects, Columns, and Tasks
- Service layer architecture with dependency injection
- Authorization pattern that verifies project membership

SignalR has not been integrated yet. The `.csproj` file shows no `Microsoft.AspNetCore.SignalR` package reference, and no Hub classes exist in the codebase.

---

## Current State vs. Desired State

### Current State

**Infrastructure:**
- `Program.cs` (c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Program.cs) contains ASP.NET Core setup with:
  - JWT authentication (lines 22-42)
  - Authorization (line 45)
  - CORS configuration (line 49)
  - Controllers mapped (line 69)
  - **No SignalR registration or hub endpoint mapping**

**Package Dependencies:**
- `KanbAI-Core.csproj` (c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/KanbAI-Core.csproj) includes:
  - `Microsoft.AspNetCore.Authentication.JwtBearer` (line 12)
  - `Microsoft.AspNetCore.Authentication.Negotiate` (line 13)
  - `Microsoft.EntityFrameworkCore.SqlServer` (line 15)
  - **No SignalR package** (SignalR is built into ASP.NET Core 3.0+, so no explicit package reference needed)

**Services and Broadcasting:**
- `TaskService` (c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs) handles task creation and movement
  - Contains business logic for task operations (CreateTaskAsync, MoveTaskAsync per ITaskService interface)
  - **No real-time broadcasting** - changes are only visible to the client that made the request
- `ProjectService` (c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Projects/ProjectService.cs) handles project CRUD and member management
  - Contains business logic for projects, members, and role-based operations
  - **No real-time broadcasting** - project changes are not pushed to other clients
- `ColumnService` (c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Columns/ColumnService.cs) handles column operations
  - Contains business logic for column creation, retrieval, and deletion
  - **No real-time broadcasting** - column changes are not pushed to other clients

**Folder Structure:**
The project follows these conventions:
- Controllers → `KanbAI-Core/Controllers/`
- Services → `KanbAI-Core/Services/{Feature}/` (e.g., `Services/Tasks/`, `Services/Projects/`)
- Entities → `KanbAI-Core/Models/Entities/`
- DTOs → `KanbAI-Core/DTOs/`
- **No `Hubs/` folder exists**

**Domain Model:**
- `Project` entity (c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Entities/Project.cs) has:
  - `Id` (Guid, PK from BaseEntity)
  - `Name` (string)
  - `Description` (string, nullable)
  - Navigation: `Members` (ICollection of ProjectMember), `Columns` (ICollection of BoardColumn)
- `BoardColumn` entity (c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Entities/BoardColumn.cs) has:
  - `Id` (Guid, PK from BaseEntity)
  - `Name` (string)
  - `ProjectId` (Guid, FK)
  - Navigation: `Project`, `Tasks` (ICollection of KanbanTask)

These entities form the basis for group naming. A client viewing a project's Kanban board should join a SignalR group scoped to that `ProjectId`.

### Desired State

**SignalR Hub Class:**
- New `KanbanHub` class inheriting from `Hub` (SignalR base class)
- File location: `KanbAI-Core/Hubs/KanbanHub.cs`
- Provides methods for clients to call:
  - `JoinProjectGroup(string projectId)` - Adds the calling connection to a project-specific group
  - `LeaveProjectGroup(string projectId)` - Removes the calling connection from a project-specific group
- Implements connection lifecycle methods (optional, for cleanup on disconnect)
- Uses SignalR's built-in `Groups` API to manage group membership

**Program.cs Integration:**
- SignalR services registered in the DI container (e.g., `builder.Services.AddSignalR();`)
- Hub endpoint mapped in the middleware pipeline (e.g., `app.MapHub<KanbanHub>("/hubs/kanban");`)
- SignalR added **after** CORS configuration but **before** `app.Run()`
- JWT authentication integrated with SignalR so only authenticated users can connect to the hub

**Authentication with SignalR:**
- SignalR configured to use the existing JWT authentication scheme
- Clients must send the JWT token when establishing the WebSocket connection (e.g., via query string or Authorization header)
- Unauthorized clients receive a connection rejection

**Group Naming Convention:**
A consistent, predictable pattern for group names that:
- Uses project ID as the primary scope (e.g., `"project_{projectId}"`)
- Is easily constructed by both server-side services and client-side code
- Supports future expansion (e.g., column-specific groups: `"project_{projectId}_column_{columnId}"`)

**Error Handling:**
- Invalid projectId formats handled gracefully (e.g., non-Guid strings)
- Authorization checks ensure users can only join groups for projects they are members of (optional for this issue; can be deferred to Issue #63)
- Connection failures logged without exposing sensitive information

---

## Acceptance Criteria

### SignalR hub infrastructure is established and accessible to authenticated clients.

- [ ] A `KanbanHub` class exists in the `KanbAI-Core/Hubs/` folder
- [ ] The hub class inherits from `Microsoft.AspNetCore.SignalR.Hub`
- [ ] SignalR services are registered in `Program.cs` using `builder.Services.AddSignalR()`
- [ ] The KanbanHub endpoint is mapped in `Program.cs` using `app.MapHub<KanbanHub>("/hubs/kanban")`
- [ ] The hub endpoint is placed in the middleware pipeline after `UseAuthentication()` and `UseAuthorization()`
- [ ] Clients can establish a WebSocket connection to `/hubs/kanban` when providing a valid JWT token

### Clients can join project-specific groups to receive scoped updates.

- [ ] The KanbanHub exposes a public method `JoinProjectGroup(string projectId)` that clients can invoke via SignalR
- [ ] When `JoinProjectGroup` is called, the client connection is added to a SignalR group using the format `"project_{projectId}"`
- [ ] The method validates that `projectId` is a valid Guid format before adding the connection to the group
- [ ] If the projectId is invalid (not a Guid), the method returns an error or throws a controlled exception (implementation detail left to Staff Engineer)
- [ ] Multiple clients viewing the same project can join the same group and will receive identical broadcast messages sent to that group

### Clients can leave project-specific groups when navigating away from a board.

- [ ] The KanbanHub exposes a public method `LeaveProjectGroup(string projectId)` that clients can invoke via SignalR
- [ ] When `LeaveProjectGroup` is called, the client connection is removed from the SignalR group `"project_{projectId}"`
- [ ] The method validates that `projectId` is a valid Guid format before attempting to remove the connection from the group
- [ ] If a client disconnects (closes the browser, network failure), the connection is automatically cleaned up by SignalR without manual intervention required

### JWT authentication is enforced for SignalR connections.

- [ ] Clients attempting to connect to `/hubs/kanban` without a valid JWT token receive a connection rejection (e.g., HTTP 401 Unauthorized during WebSocket handshake)
- [ ] The SignalR hub configuration reuses the existing JWT authentication scheme configured in `Program.cs` (lines 22-42)
- [ ] Authenticated clients can access the hub and invoke `JoinProjectGroup` and `LeaveProjectGroup` methods
- [ ] The user's identity (UserId from JWT claims) is accessible within the hub via `Context.User` or `Context.UserIdentifier`

### The implementation follows existing architectural patterns.

- [ ] The `KanbanHub` class uses file-scoped namespace declarations (C# 10+ convention)
- [ ] The hub registration in `Program.cs` follows the same DI and middleware ordering conventions as other services
- [ ] No hardcoded connection strings, secrets, or sensitive data are introduced in the hub or configuration
- [ ] Logging is implemented for connection events (optional: OnConnectedAsync, OnDisconnectedAsync) using `ILogger<KanbanHub>`

### Group naming convention is documented and consistent.

- [ ] The group name format `"project_{projectId}"` is used consistently in both `JoinProjectGroup` and `LeaveProjectGroup` methods
- [ ] The group name construction is case-insensitive-safe (e.g., use `.ToLowerInvariant()` on projectId if necessary, or enforce uppercase consistently)
- [ ] The pattern is reusable by services in Issue #63 for broadcasting messages to project groups

### Edge cases are handled gracefully.

- [ ] Attempting to join a group with a null or empty `projectId` returns a clear error (does not crash the hub)
- [ ] Attempting to join a group with a non-Guid string (e.g., `"abc123"`) returns a clear error or validation failure
- [ ] Attempting to leave a group the client never joined does not throw an exception (SignalR handles this gracefully)
- [ ] Rapid connect/disconnect cycles by the same client do not cause memory leaks or orphaned group memberships
- [ ] Clients can join multiple project groups simultaneously if viewing multiple boards in different tabs (SignalR supports this inherently)

---

## Out of Scope (Future Enhancements)

These capabilities are explicitly **not** part of this issue and should be deferred:

- **Broadcasting events from services** - No `IHubContext<KanbanHub>` injection into TaskService, ProjectService, or ColumnService (deferred to Issue #63)
- **Authorization checks within the hub** - Verifying that a user is a member of the project before allowing them to join its group (can be added in Issue #63 or a future security hardening issue)
- **Client-to-client messaging** - No peer-to-peer chat or direct message features
- **Presence indicators** - No "who is online" or "who is viewing this board" features
- **Typing indicators or cursors** - No collaborative editing UI affordances
- **Connection retry logic** - Clients are responsible for reconnection strategy (handled by SignalR client library)
- **Custom event types** - Issue #63 will define specific message types (e.g., `TaskCreated`, `TaskMoved`, `ProjectUpdated`)
- **Message history or persistence** - SignalR is ephemeral; no database storage of broadcasted messages
- **Rate limiting or throttling** - No protection against clients spamming `JoinProjectGroup` calls (can be added later if needed)
- **Frontend client implementation** - This issue only covers the backend hub; the Angular frontend client integration is separate

---

## Reference Materials

**Related Issues:**
- [#63 - Refactor Services to Broadcast Events](https://github.com/Gulybi/KanbAI-Core/issues/63) - Will inject `IHubContext<KanbanHub>` into services to send real-time updates
- [#64 - Document AI SignalR Implementation](https://github.com/Gulybi/KanbAI-Core/issues/64) - Documents the completed SignalR integration patterns

**Relevant Files:**
- `KanbAI-Core/Program.cs` - Application startup and middleware configuration; SignalR registration will be added here
- `KanbAI-Core/Services/Tasks/TaskService.cs` - Will broadcast task events in Issue #63
- `KanbAI-Core/Services/Projects/ProjectService.cs` - Will broadcast project events in Issue #63
- `KanbAI-Core/Services/Columns/ColumnService.cs` - Will broadcast column events in Issue #63
- `KanbAI-Core/Models/Entities/Project.cs` - Project entity with Id used for group naming

**Coding Standards:**
- `.claude/rules/code-standards.md` - Modern C# conventions (file-scoped namespaces, async/await, DI patterns)
- `.claude/rules/security-safety.md` - JWT authentication enforcement, no hardcoded secrets, input validation
- `.claude/rules/testing-observability.md` - Logging for connection events, structured logging for group operations
- `.claude/rules/integration-testing.md` - Patterns for testing SignalR hubs with WebApplicationFactory (if integration tests are added)

**External Documentation:**
- [Microsoft Docs: SignalR with ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction)
- [Microsoft Docs: SignalR Hubs](https://learn.microsoft.com/en-us/aspnet/core/signalr/hubs)
- [Microsoft Docs: SignalR Groups](https://learn.microsoft.com/en-us/aspnet/core/signalr/groups)
- [Microsoft Docs: SignalR Authentication](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz)

---

## Success Metrics

This feature is considered complete when:
1. A client with a valid JWT token can successfully connect to `/hubs/kanban`
2. A client can invoke `JoinProjectGroup("project_<guid>")` and be added to the corresponding SignalR group
3. A client can invoke `LeaveProjectGroup("project_<guid>")` and be removed from the corresponding SignalR group
4. Clients without a valid JWT token are rejected when attempting to connect to the hub
5. Invalid projectId values (non-Guid strings, null, empty) are handled without crashing the hub
6. The SignalR hub is registered correctly in `Program.cs` and follows the project's DI and middleware conventions
7. The implementation passes code review with zero security vulnerabilities related to authentication bypass or connection hijacking

---

**Prepared by:** Product Manager Agent  
**Date:** 2026-05-02
