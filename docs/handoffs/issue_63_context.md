# Issue #63: Refactor Services to Broadcast Events

## Business Value

**Who:** All KanbAI application users (project managers, team members, stakeholders) working collaboratively on Kanban boards

**What:** Integrate real-time event broadcasting into existing service layer operations (TaskService, ColumnService, ProjectService) so that changes made by one user are instantly visible to all other users viewing the same project board

**Why:** Enable true collaborative project management where multiple team members can work on the same board simultaneously and see each other's changes in real-time. Without this integration:
- Users see stale data until they manually refresh their browser
- Concurrent editing leads to confusion and conflicts (e.g., two users moving the same task without knowing)
- The application feels outdated compared to modern collaborative tools like Trello, Jira, or Asana
- Teams cannot trust the board as a single source of truth when different users see different states

This feature bridges the gap between the SignalR infrastructure (established in issues #61 and #62) and the core business logic, transforming KanbAI from a single-user experience into a collaborative real-time platform.

## Current State vs. Desired State

### Current State (Infrastructure Ready, But Not Broadcasting)

**SignalR Infrastructure (Established):**
- `KanbAI-Core/Hubs/KanbanHub.cs` exists and provides:
  - `JoinProjectGroup(string projectId)` - Clients can join project-specific groups
  - `LeaveProjectGroup(string projectId)` - Clients can leave project-specific groups
  - Group naming convention: `"project_{projectGuid.ToString().ToLowerInvariant()}"`
  - JWT authentication enforcement via `[Authorize]` attribute
  - Connection lifecycle logging (OnConnectedAsync, OnDisconnectedAsync)
- `KanbAI-Core/Program.cs` has SignalR registered:
  - Line 22: `builder.Services.AddSignalR();`
  - Line 68: `.AddSignalRInfrastructure();`
  - Line 89: `app.MapHub<KanbanHub>("/hubs/kanban");`
- Clients can successfully connect to the hub and join project groups, but receive no broadcasts

**TaskService (No Real-Time Broadcasting):**
- Location: `KanbAI-Core/Services/Tasks/TaskService.cs`
- Current dependencies: `ApplicationDbContext`, `ILogger<TaskService>`
- Business operations that modify state (NOT broadcasting events):
  - `CreateTaskAsync(Guid columnId, CreateTaskDto dto, Guid userId)` (lines 20-97)
    - Creates a new task in a column
    - Computes the next TaskOrder value
    - Logs: "User {UserId} created task {TaskId} in column {ColumnId} at order {TaskOrder}"
    - Returns TaskResponseDto on success
  - `MoveTaskAsync(Guid taskId, MoveTaskDto dto, Guid userId)` (lines 99-256)
    - Moves a task to a different column or reorders within the same column
    - Updates TaskOrder for affected tasks (reindexing)
    - Logs: "User {UserId} moved task {TaskId} to column {ColumnId} at order {TaskOrder}"
    - Returns TaskResponseDto on success
- Both operations persist changes to the database but DO NOT notify other connected clients

**ColumnService (No Real-Time Broadcasting):**
- Location: `KanbAI-Core/Services/Columns/ColumnService.cs`
- Current dependencies: `ApplicationDbContext`, `ILogger<ColumnService>`
- Business operations that modify state (NOT broadcasting events):
  - `CreateColumnAsync(Guid projectId, CreateColumnDto dto, Guid userId)` (lines 51-86)
    - Creates a new column in a project
    - Computes the next ColumnOrder value
    - Logs: "User {UserId} created column {ColumnId} in project {ProjectId}"
    - Returns ColumnResponseDto on success
  - `DeleteColumnAsync(Guid columnId, Guid userId)` (lines 88-114)
    - Deletes a column (and presumably cascades to tasks via EF configuration)
    - Logs: "User {UserId} deleted column {ColumnId}"
    - Returns boolean success indicator
- Both operations persist changes to the database but DO NOT notify other connected clients

**ProjectService (No Real-Time Broadcasting):**
- Location: `KanbAI-Core/Services/Projects/ProjectService.cs`
- Current dependencies: `ApplicationDbContext`, `ILogger<ProjectService>`
- Business operations that modify state (NOT broadcasting events):
  - `UpdateProjectAsync(Guid projectId, UpdateProjectDto dto, Guid userId)` (lines 91-119)
    - Updates project Name and Description
    - Logs: "User {UserId} updated project {ProjectId}"
    - Returns ProjectResponseDto on success
  - `DeleteProjectAsync(Guid projectId, Guid userId)` (lines 121-154)
    - Deletes a project (Owner role required)
    - Logs: "User {UserId} deleted project {ProjectId}"
    - Returns success/error tuple
  - `AddMemberAsync(Guid projectId, Guid userIdToAdd, Guid requestingUserId)` (lines 156-218)
    - Adds a new member to the project (Owner role required)
    - Logs: "User {RequestingUserId} added user {UserId} as Member to project {ProjectId}"
    - Returns MemberResponseDto on success
  - `RemoveMemberAsync(Guid projectId, Guid userIdToRemove, Guid requestingUserId)` (lines 220-276)
    - Removes a member from the project (Owner role required, cannot remove last owner)
    - Logs: "User {RequestingUserId} removed user {UserId} from project {ProjectId}"
    - Returns success/error tuple
- All operations persist changes to the database but DO NOT notify other connected clients

**Impact:**
When User A creates a task, User B (viewing the same board) will NOT see the new task appear on their screen unless they manually refresh the page or poll the API.

### Desired State (Real-Time Event Broadcasting Integrated)

**Service Layer Integration:**
- TaskService, ColumnService, and ProjectService inject `IHubContext<KanbanHub>` as a new dependency
- After successful database persistence (AFTER `SaveChangesAsync()` completes), each service broadcasts an event to the appropriate SignalR group
- The group name matches the KanbanHub convention: `"project_{projectId}"` (lowercase)
- Broadcast messages include sufficient data for clients to update their UI without making additional API calls

**Event Broadcasting for TaskService:**
- `CreateTaskAsync`: After successful task creation, broadcast a "TaskCreated" event to `project_{task.Column.ProjectId}` group
  - Event payload includes: full TaskResponseDto (Id, Title, Content, TaskOrder, ColumnId, AssignedId, CreatedAt, UpdatedAt)
  - Receiving clients can insert the task into the correct column at the correct order
- `MoveTaskAsync`: After successful task movement, broadcast a "TaskMoved" event to `project_{task.Column.ProjectId}` group
  - Event payload includes: taskId, oldColumnId, newColumnId, oldTaskOrder, newTaskOrder, and the full updated TaskResponseDto
  - Receiving clients can remove the task from the old column/position and insert it into the new column/position

**Event Broadcasting for ColumnService:**
- `CreateColumnAsync`: After successful column creation, broadcast a "ColumnCreated" event to `project_{projectId}` group
  - Event payload includes: full ColumnResponseDto (Id, Name, ColorCode, ColumnOrder, ProjectId, CreatedAt, UpdatedAt)
  - Receiving clients can insert the new column into their board UI at the correct order
- `DeleteColumnAsync`: After successful column deletion, broadcast a "ColumnDeleted" event to `project_{column.ProjectId}` group
  - Event payload includes: columnId, projectId
  - Receiving clients can remove the column from their board UI and handle any orphaned tasks

**Event Broadcasting for ProjectService:**
- `UpdateProjectAsync`: After successful project update, broadcast a "ProjectUpdated" event to `project_{projectId}` group
  - Event payload includes: projectId, updated Name, updated Description, UpdatedAt timestamp
  - Receiving clients can update the project title and description in their UI
- `DeleteProjectAsync`: After successful project deletion, broadcast a "ProjectDeleted" event to `project_{projectId}` group
  - Event payload includes: projectId
  - Receiving clients can redirect to the project list or display a "project deleted" message
- `AddMemberAsync`: After successfully adding a member, broadcast a "MemberAdded" event to `project_{projectId}` group
  - Event payload includes: full MemberResponseDto (UserId, Name, Email, Role, JoinedAt)
  - Receiving clients can update the member list in the project settings UI
- `RemoveMemberAsync`: After successfully removing a member, broadcast a "MemberRemoved" event to `project_{projectId}` group
  - Event payload includes: userId, projectId
  - Receiving clients can remove the member from the member list UI
  - The removed user (if connected) can detect they were removed and redirect to the project list

**Technical Patterns:**
- Use `_hubContext.Clients.Group(groupName).SendAsync(eventName, payload)` to broadcast events
- Event names follow convention: "TaskCreated", "TaskMoved", "ColumnCreated", "ColumnDeleted", "ProjectUpdated", "ProjectDeleted", "MemberAdded", "MemberRemoved"
- Broadcasting happens ONLY after successful database persistence to avoid notifying clients of operations that might fail or roll back
- Error handling: If SignalR broadcasting fails (network issue, hub unavailable), the operation should still succeed (log the error but do not roll back the database transaction)

## Milestone Context

This issue is part of Milestone #6: "Real-time Updates & SignalR Integration". The milestone contains the following issues in implementation order:

1. **Issue #61: Install and Configure SignalR** - COMPLETED (SignalR package installed, services registered, CORS configured)
2. **Issue #62: Create KanbanHub and Connection Management** - COMPLETED (KanbanHub created, group join/leave methods implemented, JWT authentication enforced)
3. **Issue #63 (This Issue): Refactor Services to Broadcast Events** - IN PROGRESS (YOU ARE HERE)
4. **Issue #64: Document AI SignalR Implementation** - OPEN (Depends on #63 completion to document the full implementation)

**Dependencies:**
- Issue #63 directly depends on issue #62 (the hub must exist before services can broadcast through it)
- Issue #64 depends on issue #63 (documentation requires the complete implementation to be available)

**Impact on Subsequent Issues:**
- Issue #64 will document the event names, payload structures, and client-side handling patterns established in this issue

## Acceptance Criteria

### 1. Services Inject IHubContext<KanbanHub> as a Dependency
- TaskService constructor includes `IHubContext<KanbanHub>` parameter and stores it in a private readonly field
- ColumnService constructor includes `IHubContext<KanbanHub>` parameter and stores it in a private readonly field
- ProjectService constructor includes `IHubContext<KanbanHub>` parameter and stores it in a private readonly field
- The DI container automatically provides the hub context when instantiating these services (no additional registration required)

### 2. TaskService Broadcasts Task Events After Successful Operations
- When `CreateTaskAsync` successfully creates a task, a "TaskCreated" event is broadcast to the project group `"project_{projectId}"` (lowercase) AFTER `SaveChangesAsync()` completes
- The "TaskCreated" event payload contains the full `TaskResponseDto` returned by the operation
- When `MoveTaskAsync` successfully moves a task, a "TaskMoved" event is broadcast to the project group `"project_{projectId}"` (lowercase) AFTER `SaveChangesAsync()` completes
- The "TaskMoved" event payload contains sufficient information for clients to remove the task from its old position and insert it at its new position (including taskId, new ColumnId, new TaskOrder, and the updated TaskResponseDto)

### 3. ColumnService Broadcasts Column Events After Successful Operations
- When `CreateColumnAsync` successfully creates a column, a "ColumnCreated" event is broadcast to the project group `"project_{projectId}"` (lowercase) AFTER `SaveChangesAsync()` completes
- The "ColumnCreated" event payload contains the full `ColumnResponseDto` returned by the operation
- When `DeleteColumnAsync` successfully deletes a column, a "ColumnDeleted" event is broadcast to the project group `"project_{projectId}"` (lowercase) AFTER `SaveChangesAsync()` completes
- The "ColumnDeleted" event payload contains the columnId and projectId so clients can remove the column from the UI

### 4. ProjectService Broadcasts Project Events After Successful Operations
- When `UpdateProjectAsync` successfully updates a project, a "ProjectUpdated" event is broadcast to the project group `"project_{projectId}"` (lowercase) AFTER `SaveChangesAsync()` completes
- The "ProjectUpdated" event payload contains the updated project information (projectId, Name, Description, UpdatedAt)
- When `DeleteProjectAsync` successfully deletes a project, a "ProjectDeleted" event is broadcast to the project group `"project_{projectId}"` (lowercase) AFTER `SaveChangesAsync()` completes
- The "ProjectDeleted" event payload contains the projectId so clients can redirect away from the deleted project
- When `AddMemberAsync` successfully adds a member, a "MemberAdded" event is broadcast to the project group `"project_{projectId}"` (lowercase) AFTER `SaveChangesAsync()` completes
- The "MemberAdded" event payload contains the full `MemberResponseDto` returned by the operation
- When `RemoveMemberAsync` successfully removes a member, a "MemberRemoved" event is broadcast to the project group `"project_{projectId}"` (lowercase) AFTER `SaveChangesAsync()` completes
- The "MemberRemoved" event payload contains the userId and projectId so clients can update the member list and the removed user can detect their removal

### 5. Broadcasting Does Not Block or Fail Database Operations
- If SignalR broadcasting fails (exception thrown by `SendAsync`), the database operation is NOT rolled back
- Broadcasting failures are logged as warnings using structured logging (e.g., "Failed to broadcast {EventName} event to group {GroupName}: {Exception}")
- The service method still returns success to the caller even if broadcasting fails (the database state is the source of truth)

### 6. Event Names and Payloads Are Consistent
- Event names follow PascalCase convention: "TaskCreated", "TaskMoved", "ColumnCreated", "ColumnDeleted", "ProjectUpdated", "ProjectDeleted", "MemberAdded", "MemberRemoved"
- Payloads are serializable objects (DTOs or anonymous types) that can be deserialized by JavaScript clients
- Payloads include all necessary data for clients to update their UI without making additional API requests
- ProjectId (or a way to derive it) is included in all payloads so clients can verify the event belongs to the project they are viewing

### 7. Existing Tests Continue to Pass
- All existing unit tests and integration tests for TaskService, ColumnService, and ProjectService continue to pass
- Test fixtures may need to mock `IHubContext<KanbanHub>` if tests break due to the new dependency
- No new integration tests are required in this issue (testing real-time behavior will be addressed separately if needed)

### 8. Logging Includes Event Broadcasting
- Successful event broadcasts are logged at Information level (e.g., "Broadcast {EventName} event to group {GroupName} for operation {OperationDescription}")
- Failed event broadcasts are logged at Warning level (e.g., "Failed to broadcast {EventName} event to group {GroupName}: {Exception}")
- Structured logging is used (no string interpolation in log message templates)

## Edge Cases & Considerations

### Edge Case: Broadcasting to an Empty Group (No Clients Connected)
- If no clients are currently connected to the project group, `SendAsync()` completes successfully but has no effect
- This is the expected behavior (messages are ephemeral, not persisted)
- Clients joining later will fetch the current state via API calls, not receive historical events
- **Acceptance Test:** Create a task when no clients are viewing the project; operation succeeds, broadcast completes, no errors logged

### Edge Case: SignalR Hub Unavailable or Not Registered
- If the SignalR hub is not properly registered in DI (e.g., due to misconfiguration), the `IHubContext<KanbanHub>` injection will fail at application startup
- This is a fatal error (application will not start) and is acceptable (fail-fast principle)
- **Mitigation:** Integration tests that instantiate services should verify the hub context is injectable

### Edge Case: Broadcasting After a Failed Database Operation
- Broadcasting should ONLY occur after `SaveChangesAsync()` succeeds
- If `SaveChangesAsync()` throws an exception, the method returns early (or the exception propagates), and no broadcast occurs
- **Acceptance Test:** Simulate a database failure (e.g., unique constraint violation); verify no event is broadcast

### Edge Case: Cross-Column Task Movement (Multiple Projects)
- `MoveTaskAsync` already prevents cross-project moves (lines 144-150 in TaskService.cs)
- The `MoveTaskResult.CrossProjectMove` error is returned, and no database changes occur
- Therefore, no broadcast is needed in this scenario
- **Acceptance Test:** Attempt to move a task to a column in a different project; operation fails, no broadcast occurs

### Edge Case: Task Move with No Position Change
- If a user moves a task to the same column at the same position, `MoveTaskAsync` returns success without modifying the database (lines 180-186)
- A "TaskMoved" event should still be broadcast (or not) depending on the desired UX
- **Decision Required:** Should the event be broadcast even if no database changes occurred? (Recommended: NO, to avoid unnecessary network traffic)

### Consideration: Event Payload Size and Performance
- TaskResponseDto, ColumnResponseDto, and MemberResponseDto are small objects (typically < 1KB JSON)
- Broadcasting these DTOs in every event is acceptable for real-time updates
- If performance becomes a concern, payloads can be reduced to include only IDs and clients can fetch full details via API
- **Out of Scope:** Performance optimization is deferred until metrics indicate a problem

### Consideration: Race Conditions (Multiple Simultaneous Edits)
- SignalR ensures event delivery order within a single connection, but NOT across multiple connections
- If User A and User B both move tasks simultaneously, the events may arrive in different orders on each client
- The database state is the source of truth; clients should re-fetch data if they detect inconsistencies
- **Out of Scope:** Optimistic concurrency control (e.g., ETags) is a separate feature and not part of this issue

### Consideration: Member Removal Event for the Removed User
- When a user is removed from a project, they should receive the "MemberRemoved" event even though they are no longer a member
- This allows their client to detect the removal and redirect them away from the project board
- The removed user is still in the SignalR group until they disconnect or explicitly leave
- **Acceptance Test:** Remove a user from a project while they are viewing the board; verify they receive the "MemberRemoved" event and can react accordingly

### Consideration: Authorization Checks in SignalR Events
- Broadcasting events to a project group assumes all clients in that group are authorized to view the project
- The KanbanHub does NOT currently enforce authorization when clients join groups (deferred from issue #62)
- This means a malicious client could join a project group they are not a member of and receive events
- **Security Risk Acknowledged:** This issue does NOT add authorization checks to `JoinProjectGroup`
- **Mitigation Plan:** Authorization checks will be added in a future security hardening issue (track separately)

### Consideration: Event Naming Conflicts with Client-Side Code
- The event names ("TaskCreated", "TaskMoved", etc.) must match what the Angular frontend expects
- If the frontend is not yet implemented, these event names should be documented for the frontend team
- **Out of Scope:** Frontend implementation is a separate effort; this issue only covers backend broadcasting

## Relevant Files

### Files to Modify
- `KanbAI-Core/Services/Tasks/TaskService.cs` - Add `IHubContext<KanbanHub>` dependency, broadcast TaskCreated and TaskMoved events
- `KanbAI-Core/Services/Columns/ColumnService.cs` - Add `IHubContext<KanbanHub>` dependency, broadcast ColumnCreated and ColumnDeleted events
- `KanbAI-Core/Services/Projects/ProjectService.cs` - Add `IHubContext<KanbanHub>` dependency, broadcast ProjectUpdated, ProjectDeleted, MemberAdded, and MemberRemoved events

### Files for Reference (Do Not Modify)
- `KanbAI-Core/Hubs/KanbanHub.cs` - Reference for group naming convention (`"project_{projectGuid.ToString().ToLowerInvariant()}"`)
- `KanbAI-Core/DTOs/TaskResponseDto.cs` - Reference for TaskCreated and TaskMoved event payloads
- `KanbAI-Core/DTOs/ColumnResponseDto.cs` - Reference for ColumnCreated event payload
- `KanbAI-Core/DTOs/MemberResponseDto.cs` - Reference for MemberAdded event payload
- `KanbAI-Core/Program.cs` - SignalR is already registered; no changes needed

### Standards and Guidelines
- `.claude/rules/code-standards.md` - Follow async/await best practices, constructor injection, structured logging
- `.claude/rules/security-safety.md` - No hardcoded secrets, proper error handling, no PII in logs
- `.claude/rules/testing-observability.md` - Structured logging for all broadcast events, appropriate log levels

---

**Prepared by:** Product Manager Agent  
**Date:** 2026-05-03
