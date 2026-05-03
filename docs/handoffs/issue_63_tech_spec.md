# Technical Specification: Issue #63 - Refactor Services to Broadcast Events

**GitHub Issue:** [#63 - Refactor Services to Broadcast Events](https://github.com/Gulybi/KanbAI-Core/issues/63)
**Context Document:** [issue_63_context.md](./issue_63_context.md)
**Staff Engineer:** @agent_staff_engineer
**Created:** 2026-05-03

---

## 1. Overview

This specification defines the integration of SignalR real-time event broadcasting into the existing service layer so that state-changing operations are visible to all connected clients of a project in real time. The SignalR hub infrastructure (`KanbanHub`, group naming convention `project_{projectGuid}`, JWT authentication enforcement) was delivered in issues #61 and #62. This issue wires the existing `TaskService`, `ColumnService`, and `ProjectService` to broadcast named events to project-scoped SignalR groups **after** each successful `SaveChangesAsync()`.

**Scope:**
- Inject `IHubContext<KanbanHub>` into `TaskService`, `ColumnService`, and `ProjectService` via constructor injection.
- Broadcast 8 events after successful database persistence:
  - `TaskCreated`, `TaskMoved` (from `TaskService`)
  - `ColumnCreated`, `ColumnDeleted` (from `ColumnService`)
  - `ProjectUpdated`, `ProjectDeleted`, `MemberAdded`, `MemberRemoved` (from `ProjectService`)
- Add new event payload DTOs for events whose payload is not an existing response DTO (`TaskMovedEventDto`, `ColumnDeletedEventDto`, `ProjectUpdatedEventDto`, `ProjectDeletedEventDto`, `MemberRemovedEventDto`).
- Introduce a small helper (private method) in each service that safely broadcasts to a group with structured logging, swallowing broadcast exceptions so they do not roll back or fail the database operation.
- Update the service DI registrations: **no change required** — ASP.NET Core's `AddSignalR()` (already registered in `Program.cs`) automatically registers `IHubContext<KanbanHub>` in the DI container.
- Update existing unit/integration tests that construct these services to supply a mock or stub `IHubContext<KanbanHub>`.

**Out of Scope:**
- No database schema changes, migrations, or entity changes.
- No changes to `KanbanHub.cs` (group membership management remains unchanged). Authorization checks for `JoinProjectGroup` are deferred to a future security hardening issue per the context note §Consideration: Authorization Checks.
- No new REST endpoints, controllers, or controller changes.
- No frontend / Angular client changes.
- No new integration tests for real-time event delivery (acceptance criterion #7 explicitly says "No new integration tests are required in this issue"). Existing tests must continue to pass.
- No optimistic concurrency (ETag) or cross-connection event ordering guarantees.

**Design Principles:**
1. **Broadcast after persistence:** `SendAsync` is only invoked after `SaveChangesAsync()` has completed successfully. If the DB operation returns early (validation failure, authorization failure, no-op), no event is broadcast.
2. **Broadcast failures never fail the caller:** Broadcast is wrapped in a `try/catch` that logs a warning and swallows. The database is the source of truth; clients that miss a broadcast will reconcile on their next refresh or reconnect.
3. **Group naming parity:** Events are sent to `project_{projectGuid.ToString().ToLowerInvariant()}` — the **exact same** group name construction used by `KanbanHub.JoinProjectGroup`. This is non-negotiable; a mismatch in case or format means clients will not receive the broadcast.
4. **Payload self-containment:** Each payload contains everything a client needs to update its UI without making a follow-up API call. `ProjectId` is included in every payload (explicitly or via the DTO) so clients can verify the event matches the project they are viewing.

---

## 2. Database/Domain Design

**N/A** - No database, entity, enum, or EF Core configuration changes are required. This issue is purely an application-layer refactor: it adds a dependency (`IHubContext<KanbanHub>`) and new DTOs, then augments existing service methods with broadcast calls after `SaveChangesAsync()`.

The existing entities (`Project`, `BoardColumn`, `KanbanTask`, `ProjectMember`, `User`) and their configurations remain untouched. No new migration will be generated.

---

## 3. API Contracts

### 3.1 REST API Endpoints

**N/A** - No REST API endpoints are added, removed, or modified for this issue. The existing controllers (`TaskController`, `ColumnController`, `ProjectController`) continue to call the service methods unchanged; they have no awareness of SignalR broadcasting.

### 3.2 SignalR Server-to-Client Events

The following named events are broadcast from the server to all connections in the target SignalR group. Clients subscribe via the SignalR JS/TS client:

```typescript
connection.on("TaskCreated", (payload) => { /* update UI */ });
```

| # | Event Name       | Origin Service   | Origin Method          | Target Group                                     | Payload Type                   |
|---|------------------|------------------|------------------------|--------------------------------------------------|--------------------------------|
| 1 | `TaskCreated`    | `TaskService`    | `CreateTaskAsync`      | `project_{column.ProjectId.ToLowerInvariant()}`  | `TaskResponseDto`              |
| 2 | `TaskMoved`      | `TaskService`    | `MoveTaskAsync`        | `project_{task.Column.ProjectId.ToLowerInvariant()}` | `TaskMovedEventDto`        |
| 3 | `ColumnCreated`  | `ColumnService`  | `CreateColumnAsync`    | `project_{projectId.ToLowerInvariant()}`         | `ColumnResponseDto`            |
| 4 | `ColumnDeleted`  | `ColumnService`  | `DeleteColumnAsync`    | `project_{column.ProjectId.ToLowerInvariant()}`  | `ColumnDeletedEventDto`        |
| 5 | `ProjectUpdated` | `ProjectService` | `UpdateProjectAsync`   | `project_{projectId.ToLowerInvariant()}`         | `ProjectUpdatedEventDto`       |
| 6 | `ProjectDeleted` | `ProjectService` | `DeleteProjectAsync`   | `project_{projectId.ToLowerInvariant()}`         | `ProjectDeletedEventDto`       |
| 7 | `MemberAdded`    | `ProjectService` | `AddMemberAsync`       | `project_{projectId.ToLowerInvariant()}`         | `MemberResponseDto`            |
| 8 | `MemberRemoved`  | `ProjectService` | `RemoveMemberAsync`    | `project_{projectId.ToLowerInvariant()}`         | `MemberRemovedEventDto`        |

**Authentication/Authorization at Broadcast Time:**
- SignalR does not re-validate the recipient's identity at broadcast time. Any connection that has joined the group via `JoinProjectGroup` will receive the message.
- Because `KanbanHub` does not currently verify project membership during group join (deferred), broadcasts effectively trust the group membership. This security gap is acknowledged in the context note and tracked separately.

### 3.3 Event Payload DTO Definitions

All new DTOs live under `KanbAI-Core/DTOs/` and use the `KanbAI_Core.DTOs` namespace. They are `record` types with `required` init-only properties, consistent with the existing DTO conventions (`TaskResponseDto`, `ColumnResponseDto`, `MemberResponseDto`, `ProjectResponseDto`).

#### 3.3.1 `TaskMovedEventDto` (new file)

**File:** `KanbAI-Core/DTOs/TaskMovedEventDto.cs`

```csharp
namespace KanbAI_Core.DTOs;

public record TaskMovedEventDto
{
    public required string TaskId { get; init; }
    public required string OldColumnId { get; init; }
    public required string NewColumnId { get; init; }
    public required int OldTaskOrder { get; init; }
    public required int NewTaskOrder { get; init; }
    public required TaskResponseDto Task { get; init; }
}
```

**Rationale:** Clients need both the old position (to remove from the source column) and the new position (to insert into the target column). Including the full `TaskResponseDto` avoids the need for a follow-up GET request.

#### 3.3.2 `ColumnDeletedEventDto` (new file)

**File:** `KanbAI-Core/DTOs/ColumnDeletedEventDto.cs`

```csharp
namespace KanbAI_Core.DTOs;

public record ColumnDeletedEventDto
{
    public required string ColumnId { get; init; }
    public required string ProjectId { get; init; }
}
```

**Rationale:** A deletion event cannot carry the full column DTO because the record is gone. The minimum required data is the column identifier and the owning project identifier (so clients can confirm the event belongs to the project they are viewing).

#### 3.3.3 `ProjectUpdatedEventDto` (new file)

**File:** `KanbAI-Core/DTOs/ProjectUpdatedEventDto.cs`

```csharp
namespace KanbAI_Core.DTOs;

public record ProjectUpdatedEventDto
{
    public required string ProjectId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
```

**Rationale:** `ProjectResponseDto` contains a per-viewer `Role` field that varies by recipient. A dedicated event DTO omits `Role` so the payload is identical for every recipient, and clients can merge the update into their local project object without role confusion.

#### 3.3.4 `ProjectDeletedEventDto` (new file)

**File:** `KanbAI-Core/DTOs/ProjectDeletedEventDto.cs`

```csharp
namespace KanbAI_Core.DTOs;

public record ProjectDeletedEventDto
{
    public required string ProjectId { get; init; }
}
```

**Rationale:** Minimal payload — clients only need the project ID to redirect away from the deleted board.

#### 3.3.5 `MemberRemovedEventDto` (new file)

**File:** `KanbAI-Core/DTOs/MemberRemovedEventDto.cs`

```csharp
namespace KanbAI_Core.DTOs;

public record MemberRemovedEventDto
{
    public required string UserId { get; init; }
    public required string ProjectId { get; init; }
}
```

**Rationale:** The removed user is no longer a member, so there is no valid `MemberResponseDto` to send. Clients need the `userId` to remove the member from local state and, critically, the removed user (still connected to the group until they disconnect) needs `projectId` to detect the removal and redirect.

### 3.4 Group Name Construction

Every service that broadcasts must construct the group name identically to `KanbanHub.JoinProjectGroup`. A shared helper is defined inside each service to guarantee consistency:

```csharp
private static string BuildProjectGroupName(Guid projectId) =>
    $"project_{projectId.ToString().ToLowerInvariant()}";
```

**Invariant:** The string must be `$"project_{projectId.ToString().ToLowerInvariant()}"` — no prefix, suffix, case, or delimiter variations. If `KanbanHub.cs` ever changes its group naming convention, this helper must change in lockstep.

---

## 4. Application Layer Boundaries

### 4.1 Service Interfaces — Unchanged

The public signatures of `ITaskService`, `IColumnService`, and `IProjectService` **do not change**. Broadcasting is a side effect wholly encapsulated within the service implementations. Controllers and callers continue to use the existing interfaces as-is.

### 4.2 `TaskService` Changes

**File:** `KanbAI-Core/Services/Tasks/TaskService.cs`

**New dependency:** `IHubContext<KanbanHub>` injected via constructor.

**Constructor change:**

```csharp
private readonly ApplicationDbContext _context;
private readonly ILogger<TaskService> _logger;
private readonly IHubContext<KanbanHub> _hubContext;

public TaskService(
    ApplicationDbContext context,
    ILogger<TaskService> logger,
    IHubContext<KanbanHub> hubContext)
{
    _context = context;
    _logger = logger;
    _hubContext = hubContext;
}
```

**New `using` directives:**

```csharp
using KanbAI_Core.Hubs;
using Microsoft.AspNetCore.SignalR;
```

**`CreateTaskAsync` — broadcast insertion point:**

After line 90 (`await _context.SaveChangesAsync();`) and after the existing log on lines 92–94, add:

```csharp
var payload = MapToDto(task);
await BroadcastAsync(
    BuildProjectGroupName(column.ProjectId),
    "TaskCreated",
    payload);

return (payload, CreateTaskResult.Success);
```

The existing `return (MapToDto(task), CreateTaskResult.Success);` on line 96 is replaced by the two-line block above so the DTO is materialized once.

**`MoveTaskAsync` — broadcast insertion point:**

Two modifications are needed in `MoveTaskAsync`:

1. **Capture the original position before mutation** (near line 130, right after the `isSameColumn` calculation):

   ```csharp
   var originalColumnId = task.ColumnId;
   var originalTaskOrder = task.TaskOrder;
   var projectId = task.Column.ProjectId;
   ```

   These are captured **before** the reindex blocks mutate `task.ColumnId` and `task.TaskOrder`.

2. **Broadcast after `SaveChangesAsync`** (after line 249):

   ```csharp
   var payload = MapToDto(task);
   var eventPayload = new TaskMovedEventDto
   {
       TaskId = task.Id.ToString(),
       OldColumnId = originalColumnId.ToString(),
       NewColumnId = task.ColumnId.ToString(),
       OldTaskOrder = originalTaskOrder,
       NewTaskOrder = task.TaskOrder,
       Task = payload
   };
   await BroadcastAsync(
       BuildProjectGroupName(projectId),
       "TaskMoved",
       eventPayload);

   return (payload, MoveTaskResult.Success);
   ```

**No-op same-column-same-position case (lines 180–186):**

Per the context note (§Edge Case: Task Move with No Position Change, "Recommended: NO"), the early return when `isSameColumn && dto.TaskOrder == task.TaskOrder` **does NOT broadcast** — because no database state changed, there is nothing to communicate to other clients. This preserves the "broadcast only after successful persistence" principle (no persistence → no broadcast).

**Cross-project / validation / not-found paths:** All existing `return (null, ...)` early returns remain unchanged. No broadcast occurs because no `SaveChangesAsync` call was made.

**New private helper methods** (add near the end of the class):

```csharp
private static string BuildProjectGroupName(Guid projectId) =>
    $"project_{projectId.ToString().ToLowerInvariant()}";

private async Task BroadcastAsync(string groupName, string eventName, object payload)
{
    try
    {
        await _hubContext.Clients.Group(groupName).SendAsync(eventName, payload);
        _logger.LogInformation(
            "Broadcast {EventName} event to group {GroupName}",
            eventName, groupName);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(
            ex,
            "Failed to broadcast {EventName} event to group {GroupName}",
            eventName, groupName);
    }
}
```

### 4.3 `ColumnService` Changes

**File:** `KanbAI-Core/Services/Columns/ColumnService.cs`

**New dependency:** `IHubContext<KanbanHub>` via constructor (mirrors `TaskService`).

**New `using` directives:** `KanbAI_Core.Hubs`, `Microsoft.AspNetCore.SignalR`.

**`CreateColumnAsync` — broadcast insertion point:**

After line 81 (`await _context.SaveChangesAsync();`) and after the existing log on line 83, replace the existing `return MapToDto(column);` with:

```csharp
var payload = MapToDto(column);
await BroadcastAsync(
    BuildProjectGroupName(projectId),
    "ColumnCreated",
    payload);

return payload;
```

**`DeleteColumnAsync` — broadcast insertion point:**

Capture `projectId` before deletion (on line 107, right before `_context.BoardColumns.Remove(column);`):

```csharp
var projectId = column.ProjectId;
_context.BoardColumns.Remove(column);
await _context.SaveChangesAsync();
```

After line 111 (the existing log), replace `return true;` with:

```csharp
await BroadcastAsync(
    BuildProjectGroupName(projectId),
    "ColumnDeleted",
    new ColumnDeletedEventDto
    {
        ColumnId = columnId.ToString(),
        ProjectId = projectId.ToString()
    });

return true;
```

**Private helpers:** Add the same `BuildProjectGroupName` and `BroadcastAsync` methods as in `TaskService`. (These two methods are duplicated across services; see §7 Known Caveats — intentional to avoid premature abstraction per project YAGNI rules.)

### 4.4 `ProjectService` Changes

**File:** `KanbAI-Core/Services/Projects/ProjectService.cs`

**New dependency:** `IHubContext<KanbanHub>` via constructor.

**New `using` directives:** `KanbAI_Core.Hubs`, `Microsoft.AspNetCore.SignalR`.

**`UpdateProjectAsync` — broadcast insertion point:**

After line 114 (`await _context.SaveChangesAsync();`) and after the existing log on line 116, replace `return MapToDto(project, member.Role);` with:

```csharp
var payload = MapToDto(project, member.Role);

await BroadcastAsync(
    BuildProjectGroupName(projectId),
    "ProjectUpdated",
    new ProjectUpdatedEventDto
    {
        ProjectId = projectId.ToString(),
        Name = project.Name,
        Description = project.Description,
        UpdatedAt = project.UpdatedAt
    });

return payload;
```

**`DeleteProjectAsync` — broadcast insertion point:**

After line 149 (`await _context.SaveChangesAsync();`) and after the existing log on line 151, before `return (true, null);`, add:

```csharp
await BroadcastAsync(
    BuildProjectGroupName(projectId),
    "ProjectDeleted",
    new ProjectDeletedEventDto { ProjectId = projectId.ToString() });
```

**`AddMemberAsync(Guid, Guid, Guid)` — broadcast insertion point:**

After line 212 (`await _context.SaveChangesAsync();`) and after the existing log on lines 214–215, replace `return (MapToMemberDto(newMember, userToAdd), null);` with:

```csharp
var payload = MapToMemberDto(newMember, userToAdd);

await BroadcastAsync(
    BuildProjectGroupName(projectId),
    "MemberAdded",
    payload);

return (payload, null);
```

**`AddMemberAsync(Guid, AddMemberDto, Guid)` overload:** No changes needed. It delegates to the above overload, which already handles broadcasting.

**`RemoveMemberAsync` — broadcast insertion point:**

After line 270 (`await _context.SaveChangesAsync();`) and after the existing log on lines 272–273, before `return (true, null);`, add:

```csharp
await BroadcastAsync(
    BuildProjectGroupName(projectId),
    "MemberRemoved",
    new MemberRemovedEventDto
    {
        UserId = userIdToRemove.ToString(),
        ProjectId = projectId.ToString()
    });
```

**`CreateProjectAsync`, `GetUserProjectsAsync`, `GetProjectByIdAsync`, `GetProjectMembersAsync`:** No broadcast needed. These are either creation events for a project that no group yet exists for (per the context note, project creation does not broadcast because a project group is only joined after it exists), or pure read operations.

**Private helpers:** Add `BuildProjectGroupName` and `BroadcastAsync` as in §4.2.

### 4.5 DI Registration

**File:** `KanbAI-Core/Program.cs`

**No changes required.**

- `builder.Services.AddSignalR();` (already registered on line 22) automatically adds `IHubContext<KanbanHub>` as a singleton to the DI container. The existing `AddScoped<ITaskService, TaskService>`, `AddScoped<IColumnService, ColumnService>`, and `AddScoped<IProjectService, ProjectService>` registrations will resolve the new constructor parameter without modification.
- The hub endpoint mapping (`app.MapHub<KanbanHub>("/hubs/kanban");`) is already present.

**Verification:** After implementation, confirm the application starts without DI resolution errors. A startup failure with "Unable to resolve service for type 'IHubContext`1[KanbanHub]'" would indicate `AddSignalR()` was accidentally removed.

---

## 5. Implementation Steps

Execute in order. Each step is independent and buildable (with the exception of Step 7, which requires Steps 1–6 to succeed first).

### Step 1: Create `TaskMovedEventDto`

- **File (create):** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\DTOs\TaskMovedEventDto.cs`
- **Content:** Exact `record` definition from §3.3.1.

### Step 2: Create `ColumnDeletedEventDto`

- **File (create):** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\DTOs\ColumnDeletedEventDto.cs`
- **Content:** Exact `record` definition from §3.3.2.

### Step 3: Create `ProjectUpdatedEventDto`

- **File (create):** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\DTOs\ProjectUpdatedEventDto.cs`
- **Content:** Exact `record` definition from §3.3.3.

### Step 4: Create `ProjectDeletedEventDto`

- **File (create):** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\DTOs\ProjectDeletedEventDto.cs`
- **Content:** Exact `record` definition from §3.3.4.

### Step 5: Create `MemberRemovedEventDto`

- **File (create):** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\DTOs\MemberRemovedEventDto.cs`
- **Content:** Exact `record` definition from §3.3.5.

### Step 6: Refactor `TaskService`

- **File (modify):** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Services\Tasks\TaskService.cs`
- Add `using` directives for `KanbAI_Core.Hubs` and `Microsoft.AspNetCore.SignalR`.
- Add `_hubContext` field and extend the constructor signature per §4.2.
- In `CreateTaskAsync`, after `SaveChangesAsync` and the existing log, materialize the DTO and broadcast `TaskCreated` to `project_{column.ProjectId}` group.
- In `MoveTaskAsync`:
  - Capture `originalColumnId`, `originalTaskOrder`, and `projectId` before any mutation of `task`.
  - Preserve the existing early-return for the same-position no-op (no broadcast).
  - After `SaveChangesAsync` and the existing log, broadcast `TaskMoved` with a `TaskMovedEventDto` payload.
- Append private helpers `BuildProjectGroupName(Guid)` and `BroadcastAsync(string, string, object)` as defined in §4.2.

### Step 7: Refactor `ColumnService`

- **File (modify):** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Services\Columns\ColumnService.cs`
- Apply all changes from §4.3:
  - Add usings, field, and constructor parameter.
  - `CreateColumnAsync`: broadcast `ColumnCreated` with `ColumnResponseDto` payload.
  - `DeleteColumnAsync`: capture `projectId` before removal; broadcast `ColumnDeleted` with `ColumnDeletedEventDto` payload.
  - Append `BuildProjectGroupName` and `BroadcastAsync` helpers.

### Step 8: Refactor `ProjectService`

- **File (modify):** `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Services\Projects\ProjectService.cs`
- Apply all changes from §4.4:
  - Add usings, field, and constructor parameter.
  - `UpdateProjectAsync`: broadcast `ProjectUpdated`.
  - `DeleteProjectAsync`: broadcast `ProjectDeleted`.
  - `AddMemberAsync(Guid, Guid, Guid)`: broadcast `MemberAdded` with `MemberResponseDto` payload.
  - `RemoveMemberAsync`: broadcast `MemberRemoved` with `MemberRemovedEventDto` payload.
  - Append `BuildProjectGroupName` and `BroadcastAsync` helpers.

### Step 9: Update existing test fixtures that instantiate services

- **Files (modify as needed):**
  - `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Services\Tasks\TaskServiceTests.cs`
  - `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Services\Columns\ColumnServiceTests.cs`
  - `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Services\Projects\ProjectServiceTests.cs`
  - (Or whatever existing test files construct these services directly.)
- **Change:** Every `new TaskService(...)`, `new ColumnService(...)`, `new ProjectService(...)` call must pass a mock `IHubContext<KanbanHub>`. See §6.3 for the standard mock setup.

### Step 10: Build and verify

- **Command:**
  ```bash
  cd c:/temp/KanbAI-Core/KanbAI-Core
  dotnet build --no-incremental
  ```
- **Expected:** `Build succeeded. 0 Warning(s) 0 Error(s)`.

### Step 11: Run the existing test suite

- **Command:**
  ```bash
  cd c:/temp/KanbAI-Core/KanbAI-Core
  dotnet test --no-build
  ```
- **Expected:** All pre-existing tests pass. Per acceptance criterion #7, no new failures introduced. Test count should match the pre-refactor baseline plus any mock adjustments.

---

## 6. QA Guidance

Acceptance criterion #7 explicitly states: *"No new integration tests are required in this issue (testing real-time behavior will be addressed separately if needed)."* Therefore, QA's primary obligations for this issue are:

1. **Confirm existing tests still pass** after the refactor.
2. **Add targeted unit tests** that verify each broadcast is wired correctly (event name, group name, payload shape) using a mocked `IHubContext<KanbanHub>`. These are cheap, fast unit tests — they do not exercise real WebSocket infrastructure.
3. **Add failure-mode unit tests** that verify broadcast exceptions are swallowed (the service still returns success).

### 6.1 Test File Locations

| Test File                             | Location                                                                             | Type | Purpose                                                                                                           |
|---------------------------------------|--------------------------------------------------------------------------------------|------|-------------------------------------------------------------------------------------------------------------------|
| `TaskServiceTests.cs` (existing + new) | `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Services\Tasks\`                 | Unit | Add broadcast verification tests for `CreateTaskAsync` and `MoveTaskAsync`; update existing tests with hub mock.  |
| `ColumnServiceTests.cs` (existing + new) | `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Services\Columns\`             | Unit | Add broadcast verification tests for `CreateColumnAsync` and `DeleteColumnAsync`; update existing tests.          |
| `ProjectServiceTests.cs` (existing + new) | `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Services\Projects\`            | Unit | Add broadcast verification tests for `UpdateProjectAsync`, `DeleteProjectAsync`, `AddMemberAsync`, `RemoveMemberAsync`. |

### 6.2 Test Case Tables

#### TaskService Broadcast Tests

| # | Test Name                                                                            | Category    | Description                                                                                                      |
|---|--------------------------------------------------------------------------------------|-------------|------------------------------------------------------------------------------------------------------------------|
| 1 | `CreateTaskAsync_Success_BroadcastsTaskCreatedEventToProjectGroup`                   | Broadcast   | Valid create → `Clients.Group("project_{projectid}")` receives `"TaskCreated"` with `TaskResponseDto` payload.   |
| 2 | `CreateTaskAsync_ValidationFails_DoesNotBroadcast`                                   | Broadcast   | Empty title → early return `InvalidTitle` → `SendAsync` never invoked.                                           |
| 3 | `CreateTaskAsync_ColumnNotFound_DoesNotBroadcast`                                    | Broadcast   | Missing column → `ColumnNotFound` → no broadcast.                                                                |
| 4 | `CreateTaskAsync_UserNotMember_DoesNotBroadcast`                                     | Broadcast   | Unauthorized user → `UserNotProjectMember` → no broadcast.                                                       |
| 5 | `CreateTaskAsync_BroadcastThrows_StillReturnsSuccess`                                | Resilience  | Mock `SendAsync` to throw → method returns `Success` + valid DTO; warning log observed.                          |
| 6 | `MoveTaskAsync_SuccessCrossColumn_BroadcastsTaskMovedWithOldAndNewPositions`         | Broadcast   | Cross-column move → `"TaskMoved"` payload contains `OldColumnId`, `NewColumnId`, `OldTaskOrder`, `NewTaskOrder`. |
| 7 | `MoveTaskAsync_SuccessSameColumnReorder_BroadcastsTaskMoved`                         | Broadcast   | Same column, different order → `"TaskMoved"` broadcast with `OldColumnId == NewColumnId`.                        |
| 8 | `MoveTaskAsync_SameColumnSamePosition_DoesNotBroadcast`                              | Broadcast   | No-op move (same column, same order) → no broadcast, no DB write.                                                |
| 9 | `MoveTaskAsync_CrossProjectMove_DoesNotBroadcast`                                    | Broadcast   | `CrossProjectMove` result → no broadcast.                                                                        |
| 10| `MoveTaskAsync_BroadcastThrows_StillReturnsSuccess`                                  | Resilience  | Same as test #5 but for MoveTaskAsync.                                                                           |
| 11| `MoveTaskAsync_Success_GroupNameIsLowercaseProjectGuid`                              | Consistency | Verify group name exactly matches `$"project_{projectId.ToString().ToLowerInvariant()}"`.                        |

#### ColumnService Broadcast Tests

| #  | Test Name                                                                            | Category    | Description                                                                                                      |
|----|--------------------------------------------------------------------------------------|-------------|------------------------------------------------------------------------------------------------------------------|
| 12 | `CreateColumnAsync_Success_BroadcastsColumnCreatedEventToProjectGroup`               | Broadcast   | Valid create → `"ColumnCreated"` with `ColumnResponseDto` payload to `project_{projectId}`.                      |
| 13 | `CreateColumnAsync_ProjectNotFound_DoesNotBroadcast`                                 | Broadcast   | Missing project → null result → no broadcast.                                                                    |
| 14 | `CreateColumnAsync_UserNotMember_DoesNotBroadcast`                                   | Broadcast   | Unauthorized → no broadcast.                                                                                     |
| 15 | `CreateColumnAsync_BroadcastThrows_StillReturnsDto`                                  | Resilience  | `SendAsync` throws → method still returns the created DTO; warning logged.                                       |
| 16 | `DeleteColumnAsync_Success_BroadcastsColumnDeletedEventWithColumnIdAndProjectId`     | Broadcast   | Valid delete → `"ColumnDeleted"` payload `{ ColumnId, ProjectId }`.                                              |
| 17 | `DeleteColumnAsync_ColumnNotFound_DoesNotBroadcast`                                  | Broadcast   | Missing column → returns false → no broadcast.                                                                   |
| 18 | `DeleteColumnAsync_UserNotMember_DoesNotBroadcast`                                   | Broadcast   | Unauthorized → no broadcast.                                                                                     |
| 19 | `DeleteColumnAsync_BroadcastThrows_StillReturnsTrue`                                 | Resilience  | `SendAsync` throws → method still returns `true`; warning logged.                                                |

#### ProjectService Broadcast Tests

| #  | Test Name                                                                            | Category    | Description                                                                                                      |
|----|--------------------------------------------------------------------------------------|-------------|------------------------------------------------------------------------------------------------------------------|
| 20 | `UpdateProjectAsync_Success_BroadcastsProjectUpdatedEventWithNameDescriptionUpdatedAt` | Broadcast | Valid update → `"ProjectUpdated"` payload omits `Role`, includes `ProjectId`, `Name`, `Description`, `UpdatedAt`. |
| 21 | `UpdateProjectAsync_ProjectNotFound_DoesNotBroadcast`                                | Broadcast   | Missing project → no broadcast.                                                                                  |
| 22 | `UpdateProjectAsync_UserNotMember_DoesNotBroadcast`                                  | Broadcast   | Unauthorized → no broadcast.                                                                                     |
| 23 | `UpdateProjectAsync_BroadcastThrows_StillReturnsDto`                                 | Resilience  | `SendAsync` throws → method still returns updated DTO; warning logged.                                           |
| 24 | `DeleteProjectAsync_Success_BroadcastsProjectDeletedEventWithProjectId`              | Broadcast   | Owner deletes → `"ProjectDeleted"` payload `{ ProjectId }`.                                                      |
| 25 | `DeleteProjectAsync_NonOwner_DoesNotBroadcast`                                       | Broadcast   | Member (non-owner) → returns `(false, ...)` → no broadcast.                                                      |
| 26 | `DeleteProjectAsync_BroadcastThrows_StillReturnsSuccess`                             | Resilience  | `SendAsync` throws → returns `(true, null)`; warning logged.                                                     |
| 27 | `AddMemberAsync_Success_BroadcastsMemberAddedEventWithMemberDto`                     | Broadcast   | Valid add → `"MemberAdded"` with full `MemberResponseDto` payload.                                               |
| 28 | `AddMemberAsync_UserAlreadyMember_DoesNotBroadcast`                                  | Broadcast   | Duplicate → error tuple → no broadcast.                                                                          |
| 29 | `AddMemberAsync_RequestingUserNotOwner_DoesNotBroadcast`                             | Broadcast   | Non-owner requester → no broadcast.                                                                              |
| 30 | `AddMemberAsync_BroadcastThrows_StillReturnsMember`                                  | Resilience  | `SendAsync` throws → returns valid `MemberResponseDto`; warning logged.                                          |
| 31 | `RemoveMemberAsync_Success_BroadcastsMemberRemovedEventWithUserIdAndProjectId`       | Broadcast   | Valid remove → `"MemberRemoved"` payload `{ UserId, ProjectId }`.                                                |
| 32 | `RemoveMemberAsync_LastOwner_DoesNotBroadcast`                                       | Broadcast   | Trying to remove last owner → error → no broadcast.                                                              |
| 33 | `RemoveMemberAsync_UserNotMember_DoesNotBroadcast`                                   | Broadcast   | Target user not in project → no broadcast.                                                                       |
| 34 | `RemoveMemberAsync_BroadcastThrows_StillReturnsSuccess`                              | Resilience  | `SendAsync` throws → returns `(true, null)`; warning logged.                                                     |

### 6.3 Mock Setup Pattern for `IHubContext<KanbanHub>`

SignalR's `IHubContext<T>` has a two-level shape: `_hubContext.Clients.Group(name).SendAsync(eventName, payload, CancellationToken)`. Mocking requires mocking the intermediate `IHubClients` and `IClientProxy` objects.

```csharp
using Microsoft.AspNetCore.SignalR;
using Moq;

// Arrange: build a layered mock
var mockClientProxy = new Mock<IClientProxy>();
var mockClients = new Mock<IHubClients>();
mockClients
    .Setup(c => c.Group(It.IsAny<string>()))
    .Returns(mockClientProxy.Object);

var mockHubContext = new Mock<IHubContext<KanbanHub>>();
mockHubContext
    .Setup(h => h.Clients)
    .Returns(mockClients.Object);

var service = new TaskService(context, logger, mockHubContext.Object);

// Act
await service.CreateTaskAsync(columnId, dto, userId);

// Assert: group name and event name
mockClients.Verify(
    c => c.Group($"project_{projectId.ToString().ToLowerInvariant()}"),
    Times.Once);
mockClientProxy.Verify(
    p => p.SendCoreAsync(
        "TaskCreated",
        It.Is<object[]>(args => args.Length == 1 && args[0] is TaskResponseDto),
        It.IsAny<CancellationToken>()),
    Times.Once);
```

**Why `SendCoreAsync`:** The `SendAsync(eventName, arg1)` extension method internally calls `SendCoreAsync(eventName, new[] { arg1 }, cancellationToken)`. Moq cannot intercept extension methods, so verification must target `SendCoreAsync`.

**Broadcast-failure test pattern:**

```csharp
mockClientProxy
    .Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
    .ThrowsAsync(new InvalidOperationException("Simulated hub failure"));

var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

data.Should().NotBeNull();
result.Should().Be(CreateTaskResult.Success);
// Verify warning was logged via ILogger mock (optional).
```

### 6.4 Verifying "No Broadcast" Cases

For any test that asserts **no broadcast occurred**, verify that `mockClients.Group(...)` was never called:

```csharp
mockClients.Verify(c => c.Group(It.IsAny<string>()), Times.Never);
```

Or equivalently, that `SendCoreAsync` was never invoked on the client proxy:

```csharp
mockClientProxy.Verify(
    p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()),
    Times.Never);
```

### 6.5 Naming Conventions

All new test method names MUST follow `MethodName_StateUnderTest_ExpectedBehavior` per `.claude/rules/testing-observability.md`. Examples above conform.

### 6.6 AAA Structure

Every test uses the Arrange-Act-Assert pattern with blank-line separation, consistent with existing tests in the solution.

---

## 7. Known Caveats

| # | Caveat                                                                                                     | Impact                                                                                                                                                                                                 | Mitigation / Note                                                                                                                                                                             |
|---|------------------------------------------------------------------------------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 1 | **`BuildProjectGroupName` and `BroadcastAsync` helpers duplicated across 3 services**                      | Three near-identical copies of two small private methods.                                                                                                                                               | Accepted per project YAGNI rules. Extracting a shared `ISignalRBroadcaster` adds indirection without meaningfully simplifying the code; revisit only if a fourth consumer is added.          |
| 2 | **No project-membership authorization on hub `JoinProjectGroup`**                                          | Any authenticated client can join any `project_{id}` group and receive broadcasts. Inherited from issue #62; explicitly called out in the context note as deferred.                                     | Tracked separately as a security hardening issue. Not part of this issue's scope.                                                                                                             |
| 3 | **Broadcast failures are silently swallowed (only warning-logged)**                                        | If the hub backplane becomes degraded (e.g., in a future Redis-backed multi-server deployment), clients may miss events with no loud signal to the caller.                                              | Acceptable for now: the context note explicitly requires broadcast failures not to affect DB operations. Structured warning logs in Serilog allow ops to alert on spikes.                     |
| 4 | **No retry on broadcast failure**                                                                          | A transient Redis/backplane blip will cause permanent event loss for that broadcast.                                                                                                                    | Acceptable. SignalR broadcasts are ephemeral by design; clients must reconcile state on reconnect via normal GET endpoints. Retry logic would add complexity without solving the core problem.|
| 5 | **`TaskMovedEventDto` includes both flat fields and nested `TaskResponseDto`**                             | Minor data duplication (the nested `Task.ColumnId` equals `NewColumnId`, etc.).                                                                                                                         | Intentional: flat fields let clients implement simple old→new move animations without unpacking the nested DTO; the nested DTO preserves full task state for cache-update scenarios.          |
| 6 | **`ProjectUpdated` event omits `Role`**                                                                    | Clients that try to read `.Role` from the event will get `undefined`.                                                                                                                                   | By design — `Role` is per-viewer. Document the event payload shape clearly for frontend consumers (future issue #64 covers documentation).                                                    |
| 7 | **`MemberRemoved` carries only IDs, not the member DTO**                                                   | Clients that keyed their member list on something other than `userId` would need adaptation.                                                                                                            | All existing UI patterns key members by `userId`; no consumer is harmed. Sending the full DTO post-deletion would be misleading (the `ProjectMember` row no longer exists).                   |
| 8 | **`IHubContext<KanbanHub>` is a singleton; injecting into a scoped service is safe**                       | None.                                                                                                                                                                                                   | ASP.NET Core's built-in DI lifetime validation permits singleton → scoped injection. No captive-dependency issue because `IHubContext` is stateless per call.                                 |
| 9 | **Mocking SignalR extension methods requires targeting `SendCoreAsync`**                                   | Test authors unfamiliar with this detail will write setups on `SendAsync` that never match.                                                                                                             | Documented in §6.3. Provide the layered mock pattern as a copy-paste template in the first test file; subsequent tests can follow.                                                            |
| 10| **InMemory EF Core provider limitation (test infra)**                                                      | The in-memory provider does not execute database triggers, unique-constraint checks, or cascade deletes identically to SQL Server. Existing tests already accommodate this.                             | Out of scope for this issue — no new DB behavior is introduced.                                                                                                                               |
| 11| **TestServer limitations for any future SignalR integration test**                                         | If a follow-up issue needs to verify end-to-end broadcast delivery, `TestServer` requires `LongPolling` transport (documented in issue #62 QA notes).                                                    | Not relevant for this issue (no new integration tests). Noted for future work.                                                                                                                |
| 12| **`ProjectDeletedEventDto` is broadcast to a group whose members are all about to receive "project gone"** | After the broadcast, the group still exists in SignalR's in-memory store with stale connections until clients disconnect or explicitly leave.                                                           | Acceptable. SignalR automatically cleans up groups when the last connection leaves; residual groups are zero-cost.                                                                            |

---

## 8. Design Validation (Self-Check)

| Check                  | Question                                                                                                 | Status                                                                                                                                                                                                                  |
|------------------------|----------------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **Namespaces**         | Do all referenced namespaces match `KanbAI_Core.*`?                                                      | Pass — new DTOs live in `KanbAI_Core.DTOs`; services retain their existing namespaces; `KanbAI_Core.Hubs` reused.                                                                                                       |
| **Folder Paths**       | Do all file paths reference existing folders?                                                            | Pass — `DTOs/`, `Services/Tasks/`, `Services/Columns/`, `Services/Projects/`, `Hubs/` all exist.                                                                                                                        |
| **Dependencies**       | Are all required NuGet packages already present?                                                         | Pass — `Microsoft.AspNetCore.SignalR` is part of the ASP.NET Core framework (no separate package for .NET 10). No new test-project dependencies required (Moq and existing mocking infra are already available).       |
| **Naming Conflicts**   | Do any new type names conflict with existing types?                                                      | Pass — `TaskMovedEventDto`, `ColumnDeletedEventDto`, `ProjectUpdatedEventDto`, `ProjectDeletedEventDto`, `MemberRemovedEventDto` are new names with no collisions.                                                      |
| **BaseEntity Compliance** | Do new entities inherit `BaseEntity`?                                                                 | N/A — no new entities; only DTOs, which are records.                                                                                                                                                                    |
| **Code Standards**     | File-scoped namespaces, async/await, constructor injection, structured logging?                          | Pass — all new DTOs use file-scoped namespace; all broadcast code is async with `await`; `IHubContext` injected via constructor; `BroadcastAsync` uses parameterized log templates (no string interpolation).          |
| **Security**           | No hardcoded secrets, no PII in logs, authorization enforced, input validated?                           | Pass — logs emit only GUIDs and event/group names (no PII); broadcasting does not weaken existing authorization (callers remain subject to service-level membership checks); payloads include no password/secret data. |

**Result:** All checks pass. Design is ready for implementation.

---

**Next Steps:** Instruct the developer to read this tech spec and begin implementation starting at Step 1.

---

## 9. Development Status

**Implemented:** 2026-05-03
**Developer:** @agent_developer

### 9.1 Files Created

| File | Purpose |
|------|---------|
| `KanbAI-Core/KanbAI-Core/DTOs/TaskMovedEventDto.cs` | Payload for `TaskMoved` broadcast — includes old/new column & order plus nested `TaskResponseDto`. |
| `KanbAI-Core/KanbAI-Core/DTOs/ColumnDeletedEventDto.cs` | Payload for `ColumnDeleted` broadcast — carries `ColumnId` + `ProjectId`. |
| `KanbAI-Core/KanbAI-Core/DTOs/ProjectUpdatedEventDto.cs` | Payload for `ProjectUpdated` broadcast — omits per-viewer `Role`. |
| `KanbAI-Core/KanbAI-Core/DTOs/ProjectDeletedEventDto.cs` | Payload for `ProjectDeleted` broadcast — minimal `{ ProjectId }`. |
| `KanbAI-Core/KanbAI-Core/DTOs/MemberRemovedEventDto.cs` | Payload for `MemberRemoved` broadcast — `{ UserId, ProjectId }`. |

### 9.2 Files Modified

| File | Change |
|------|--------|
| `KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs` | Added `IHubContext<KanbanHub>` dependency; broadcast `TaskCreated` in `CreateTaskAsync`; capture original state and broadcast `TaskMoved` in `MoveTaskAsync` (no-op same-position move still does not broadcast); added `BuildProjectGroupName` + `BroadcastAsync` private helpers. |
| `KanbAI-Core/KanbAI-Core/Services/Columns/ColumnService.cs` | Added `IHubContext<KanbanHub>` dependency; broadcast `ColumnCreated` in `CreateColumnAsync`; capture `projectId` before removal and broadcast `ColumnDeleted` in `DeleteColumnAsync`; added helpers. |
| `KanbAI-Core/KanbAI-Core/Services/Projects/ProjectService.cs` | Added `IHubContext<KanbanHub>` dependency; broadcast `ProjectUpdated`, `ProjectDeleted`, `MemberAdded`, `MemberRemoved` at the matching insertion points; added helpers. `CreateProjectAsync` does not broadcast (no group exists pre-creation). |
| `KanbAI-Core/KanbAI-Core.Tests/Services/Tasks/TaskServiceTests.cs` | Added `_hubContextMock` field + `CreateHubContextMock()` helper; all 13 `new TaskService(...)` call sites updated to pass the mock. |
| `KanbAI-Core/KanbAI-Core.Tests/Services/Tasks/TaskServiceMoveTests.cs` | Same mock pattern; all 16 call sites updated. |
| `KanbAI-Core/KanbAI-Core.Tests/Services/Columns/ColumnServiceTests.cs` | Same mock pattern; all 13 call sites updated. |
| `KanbAI-Core/KanbAI-Core.Tests/Services/Projects/ProjectServiceTests.cs` | Same mock pattern; all 40 call sites updated. |

### 9.3 Build & Test Results

- **`dotnet build --no-incremental`** — `Build succeeded. 0 Warning(s) 0 Error(s)`.
- **`dotnet test --no-build`** — `Passed: 453, Failed: 0, Skipped: 2, Total: 455`. No regressions; test count matches pre-refactor baseline.

### 9.4 Infrastructure Notes

- **DI registration:** No changes to `Program.cs` — `AddSignalR()` already registers `IHubContext<KanbanHub>` as a singleton; the scoped services resolve it without modification.
- **Shared test helper:** A `CreateHubContextMock()` static method was added to each of the 4 test fixtures. Rationale: the existing test files do not share a common base class, and per the YAGNI stance in §7 Known Caveats, I duplicated the small 9-line helper rather than introducing a shared test utility class that only 4 callers would use.
- **Mock exposes `Clients.All`** in addition to `Clients.Group(...)`. This is defensive against future tests that might want to verify a non-group broadcast; it is not used by any current broadcast site.

### 9.5 Edge Cases for QA

1. **No-op move broadcast suppression:** The same-column-same-position early return in `MoveTaskAsync` (around line 183) **must not** call `_hubContext.Clients.Group(...).SendAsync(...)`. QA test `MoveTaskAsync_SameColumnSamePosition_DoesNotBroadcast` (tech spec test #8) is the key verification.
2. **Early returns never broadcast:** All `return (null, ...)`, `return false`, and `return (false, ...)` paths in the three services predate `SaveChangesAsync` — verifying that `SendCoreAsync` was never invoked on the client proxy in these paths is a required QA check (tests #2–#4, #9, #13–#14, #17–#18, #21–#22, #25, #28–#29, #32–#33).
3. **Broadcast failure isolation:** The `try/catch` inside `BroadcastAsync` swallows any exception thrown from `SendAsync` and logs a warning. QA resilience tests (#5, #10, #15, #19, #23, #26, #30, #34) must verify the service still returns the success result even when `SendCoreAsync` throws.
4. **Group name case invariance:** All three services call `BuildProjectGroupName`, which lower-cases the GUID. QA should verify (per test #11) that the group name exactly equals `$"project_{projectId.ToString().ToLowerInvariant()}"` — **not** `project_{ProjectId}` or any other casing.
5. **Moq extension method limitation:** `SendAsync(eventName, payload)` is an extension method; Moq cannot intercept it directly. All broadcast verifications must target `SendCoreAsync(eventName, object[] args, CancellationToken ct)` where `args[0]` is the payload. See tech spec §6.3 for the canonical pattern.
6. **`TaskMoved` payload carries duplicated data:** The flat fields (`OldColumnId`, `NewColumnId`, `OldTaskOrder`, `NewTaskOrder`) intentionally duplicate values inside the nested `Task` DTO. Tests asserting the payload shape must verify **both** the flat fields and the nested `Task` object.
7. **Member-removal target user connection:** `MemberRemoved` is broadcast to the project group the removed user was still joined to. QA integration-style tests (future scope) should verify the removed user's client receives the event before it leaves the group.

---

## 10. QA Status

**QA Completed:** 2026-05-03
**QA Tester:** @agent_tester_qa

### 10.1 Test Files Created

| File | Tests | Purpose |
|------|-------|---------|
| `KanbAI-Core.Tests/Services/Tasks/TaskServiceBroadcastTests.cs` | 11 | Covers tech-spec test cases #1–#11 — `TaskCreated` / `TaskMoved` broadcast wiring, no-op same-position suppression, cross-project suppression, group-name casing invariance, broadcast-failure resilience. |
| `KanbAI-Core.Tests/Services/Columns/ColumnServiceBroadcastTests.cs` | 8 | Covers #12–#19 — `ColumnCreated` / `ColumnDeleted` broadcast wiring, early-return suppression on missing project / non-member, broadcast-failure resilience. |
| `KanbAI-Core.Tests/Services/Projects/ProjectServiceBroadcastTests.cs` | 15 | Covers #20–#34 — `ProjectUpdated`, `ProjectDeleted`, `MemberAdded`, `MemberRemoved` broadcast wiring; payload shape assertions; authorization suppression (non-owner, non-member, last-owner); broadcast-failure resilience. |

All 34 test names map 1-to-1 with the tech spec's §6.2 test case tables, preserving the `MethodName_StateUnderTest_ExpectedBehavior` naming convention.

### 10.2 Test Results

- **Build:** `dotnet build --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
- **Broadcast-only run:** `dotnet test --filter "FullyQualifiedName~BroadcastTests"` → `Passed: 34, Failed: 0, Skipped: 0, Total: 34`.
- **Full suite:** `dotnet test --no-build` → `Passed: 487, Failed: 0, Skipped: 2, Total: 489`. This is 453 (pre-refactor baseline) + 34 (new broadcast tests) = 487. No regressions introduced.

### 10.3 Acceptance Criteria Verification

| AC # | Criterion | Verified By |
|------|-----------|-------------|
| AC 1 | Services inject `IHubContext<KanbanHub>` | Constructor signature is exercised by every test in this suite; compilation confirms DI shape. |
| AC 2 | `TaskService` broadcasts `TaskCreated` / `TaskMoved` post-persistence | `CreateTaskAsync_Success_Broadcasts…`, `MoveTaskAsync_SuccessCrossColumn_Broadcasts…`, `MoveTaskAsync_SuccessSameColumnReorder_BroadcastsTaskMoved`. |
| AC 3 | `ColumnService` broadcasts `ColumnCreated` / `ColumnDeleted` post-persistence | `CreateColumnAsync_Success_Broadcasts…`, `DeleteColumnAsync_Success_Broadcasts…`. |
| AC 4 | `ProjectService` broadcasts all four project events post-persistence | `UpdateProjectAsync_Success_…`, `DeleteProjectAsync_Success_…`, `AddMemberAsync_Success_…`, `RemoveMemberAsync_Success_…`. |
| AC 5 | Broadcast failures never roll back DB / never fail the caller | Six resilience tests (`*_BroadcastThrows_StillReturns*`) — all assert both success result AND persisted DB state. |
| AC 6 | Event names & payload shapes are consistent | Every success test asserts on the exact event name literal and verifies the payload type via `args[0] is <DtoType>`. |
| AC 7 | Existing tests continue to pass | Full-suite run confirmed 453 pre-existing tests still pass after test-fixture mock adjustments and new tests added. |
| AC 8 | Logging includes broadcasting events | Info/Warning log templates in `BroadcastAsync` use parameterized structured logging; no string interpolation. Verified by code inspection (log-call side-effect, not strictly asserted in mocks). |

### 10.4 Bugs Found & Fixed

**None.** Every acceptance criterion and every test in the §6.2 case tables passed on first execution of the test suite. The implementation matches the tech spec exactly:

- Group names are lowercased (`MoveTaskAsync_Success_GroupNameIsLowercaseProjectGuid` passed — verified that `c.Group(expectedGroup)` was called exactly once and `c.Group(any-other-string)` was never called).
- No-op same-position move suppresses broadcast (`MoveTaskAsync_SameColumnSamePosition_DoesNotBroadcast` passed — `Group(...)` and `SendCoreAsync(...)` both Times.Never).
- All early-return paths (validation, not-found, unauthorized, last-owner, already-member) correctly skip broadcasting.
- Broadcast exceptions are swallowed in every one of the 6 resilience tests; the underlying DB state is verified to have persisted.

### 10.5 Outstanding Issues

**None.** The implementation is complete, all 34 specified tests are implemented and passing, and no regressions were introduced in the pre-existing test suite. Items explicitly out of scope remain so:

- No end-to-end SignalR integration tests (acceptance criterion #7 exempts this issue from adding them).
- `JoinProjectGroup` authorization is still deferred to a future security hardening issue (§7 Caveat #2).
- Log-side-effect assertions on `ILogger<T>` mocks were not added because they add significant test verbosity for little benefit; the structured-logging contract is enforced by code review and the `BroadcastAsync` helper is identical across all three services.

### 10.6 Mock Pattern Notes (for future SignalR test authors)

All 34 broadcast tests use a per-test layered mock built inline (not the shared `_hubContextMock` field on the non-broadcast fixtures). This is deliberate:

1. **Setup must occur before Act:** The broadcast-failure resilience tests must call `clientProxy.Setup(... ThrowsAsync(...))` *before* the service is exercised. A constructor-built shared mock is fine for pass-through behavior but awkward when a single test needs custom setup.
2. **Assertion targets must be accessible:** `Mock<IHubClients>` and `Mock<IClientProxy>` are captured separately so tests can call `.Verify(...)` on each. A singleton-built `IHubContext` mock would lose these handles.
3. **`SendCoreAsync` — not `SendAsync`:** Every verification uses `SendCoreAsync(eventName, object[] args, CancellationToken ct)`. As the spec's §6.3 notes and §7 Caveat #9 flags, `SendAsync(name, payload)` is an extension method and Moq cannot intercept it — verification must always target the underlying core method.
