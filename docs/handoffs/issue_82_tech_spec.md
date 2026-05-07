# Technical Specification: Issue #82 - Add Task-Read Endpoint(s) So the Board Can Hydrate Tasks on Page Load

**GitHub Issue:** [#82 - Add task-read endpoint(s) so the board can hydrate tasks on page load](https://github.com/Gulybi/KanbAI-Core/issues/82)  
**Context Document:** [issue_82_context.md](./issue_82_context.md)  
**Staff Engineer:** @agent_staff_engineer  
**Created:** 2026-05-08

---

## 1. Overview

This specification defines a new HTTP API endpoint that enables authenticated project members to retrieve all tasks for a project on page load, closing the critical data-reachability gap that prevents the KanbAI-Web frontend from displaying tasks after a browser refresh. The endpoint adds a single `GET /api/task/project/{projectId}` action to the existing `TaskController` and reuses the authorization pattern already established on `GET /api/column/project/{projectId}`.

**Design Decision: Project-Scoped Read (Option A from Context Note)**

After evaluating the three options presented in the context note, Option A (project-scoped read) is selected because:
- Single HTTP round-trip on board load minimizes frontend complexity and latency
- Mirrors the established pattern from `GET /api/column/project/{projectId}` for consistency
- Enables the frontend to bucket tasks client-side without N HTTP requests
- Aligns with the product's collaboration model where users work across the entire board, not just individual columns

**Scope:**
- Add one controller action on `TaskController`: `GetProjectTasks(Guid projectId, CancellationToken)`.
- Extend `ITaskService` and `TaskService` to add a single service method: `GetProjectTasksAsync(projectId, userId)`.
- Reuse the existing `TaskResponseDto` with no changes.
- Return `ApiResponse<List<TaskResponseDto>>` with 200 OK on success, 404 on missing project, 403 on non-member.
- Tasks are sorted server-authoritative by (ColumnId ASC, TaskOrder ASC) so the client can render directly without re-sorting.
- Add structured logging at Information (success, not-found) and Warning (authorization failure) levels.
- Add unit tests to the existing `TaskControllerTests` and `TaskServiceTests` test classes covering the new action and service method.

**Out of Scope:**
- No database, entity, enum, or migration changes.
- No pagination, filtering, or sorting parameters. The endpoint accepts only `projectId` as input to avoid authorization-bypass surface.
- No exposure of tasks in soft-deleted columns (if soft-delete exists — currently the schema uses hard delete via FK cascade).
- No changes to the existing POST/PUT endpoints. The change is purely additive.
- No task update/edit/delete endpoints.
- No TaskUpdated/TaskDeleted SignalR events.

**Design Principles:**
1. **Authorization before data:** Project membership is verified via the same `Column → Project → Members` eager-loaded chain used by `ColumnController.GetProjectColumns`, so the security posture of task-read and column-read endpoints stays identical.
2. **Single-roundtrip read:** The service method uses `.Include(t => t.Column)` for a single JOIN to fetch tasks with their column associations for sorting, plus a separate membership check query (following the established two-query pattern from `TaskService.CreateTaskAsync`).
3. **Read-only query:** The query uses `.AsNoTracking()` since the results are read-only projections (AC #14).
4. **Service-layer abstraction:** Unlike issue #80 (AttachmentController reads directly from DbContext), this issue extends `ITaskService` because tasks are core domain entities and future task-read operations (filtering, pagination) may need the abstraction point.

---

## 2. Database/Domain Design

**N/A** — No entity, enum, `IEntityTypeConfiguration<T>`, `DbSet<T>`, or migration changes are required. The action queries existing entities (`KanbanTask`, `BoardColumn`, `Project`, `ProjectMember`) and reuses the existing `TaskResponseDto`.

No new indexes are required. The existing FK index on `KanbanTasks.ColumnId` (created by `KanbanTaskConfiguration`) and the FK index on `BoardColumns.ProjectId` (created by `BoardColumnConfiguration`) are sufficient for the query plan: `WHERE t.Column.ProjectId = @projectId`.

**Expected Table Schema (Existing, No Changes):**

| Table | Relevant Columns | Constraints |
|-------|------------------|-------------|
| `KanbanTasks` | `Id (PK)`, `Title`, `Content`, `TaskOrder`, `ColumnId (FK → BoardColumns.Id)`, `AssignedId (FK → Users.Id, nullable)`, `CreatedAt`, `UpdatedAt` | FK cascade delete on `ColumnId` |
| `BoardColumns` | `Id (PK)`, `ProjectId (FK → Projects.Id)`, `Name`, `ColumnOrder`, `CreatedAt`, `UpdatedAt` | FK cascade delete on `ProjectId` |
| `ProjectMembers` | `UserId (FK → Users.Id)`, `ProjectId (FK → Projects.Id)`, composite PK `(UserId, ProjectId)` | Unique composite key enforces one membership row per user-project pair |

---

## 3. API Contracts

### 3.1 GET Endpoint: Retrieve All Tasks for a Project

**Route:** `GET /api/task/project/{projectId}`  
**Authentication:** Required (inherits `[Authorize]` from `TaskController`)  
**Content-Type (response):** `application/json`

**Route Parameters:**

| Name | Type | Required | Description |
|------|------|----------|-------------|
| `projectId` | `Guid` | Yes | The ID of the project whose tasks should be retrieved |

**Query/Body Parameters:** None. Any unknown query parameters are ignored by model binding; the action accepts no additional input to avoid authorization-bypass surface (AC #10).

**Success Response (200 OK):**

```json
{
  "success": true,
  "message": "Tasks retrieved successfully.",
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "title": "Design login page",
      "content": "Create mockups for the authentication flow",
      "taskOrder": 0,
      "columnId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "assignedId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
      "createdAt": "2026-05-01T10:00:00Z",
      "updatedAt": "2026-05-02T14:30:00Z"
    },
    {
      "id": "5fb97g75-6828-5673-c4gd-3d074g01bfb7",
      "title": "Implement JWT authentication",
      "content": null,
      "taskOrder": 1,
      "columnId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "assignedId": null,
      "createdAt": "2026-05-01T11:00:00Z",
      "updatedAt": "2026-05-01T11:00:00Z"
    }
  ],
  "errors": []
}
```

The `data` array is always present. It is `[]` when the project exists but has no tasks (AC #7). Elements are ordered by `(ColumnId ASC, TaskOrder ASC)` (AC #4).

**Error Responses:**

| Status Code | Reason | Response Body |
|-------------|--------|---------------|
| 401 Unauthorized | JWT token missing or `NameIdentifier` claim invalid | `{"success": false, "message": "Invalid or missing user ID in token.", "errors": []}` (via existing `GetCurrentUserId()` + global exception middleware) |
| 403 Forbidden | Authenticated user is not a member of the project | `{"success": false, "message": "You are not a member of this project.", "errors": []}` |
| 404 Not Found | Project with `projectId` does not exist | `{"success": false, "message": "Project not found.", "errors": []}` |

**DTO (existing, unchanged):**

```csharp
// KanbAI-Core/KanbAI-Core/DTOs/TaskResponseDto.cs
public record TaskResponseDto
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Content { get; init; }
    public required int TaskOrder { get; init; }
    public required string ColumnId { get; init; }
    public string? AssignedId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
```

### 3.2 Action Signature

```csharp
[HttpGet("project/{projectId}")]
public async Task<IActionResult> GetProjectTasks(
    Guid projectId,
    CancellationToken cancellationToken)
```

**Order-of-operations contract (enforced by the Developer):**
1. `GetCurrentUserId()` (existing helper) — extracts JWT subject; throws on invalid claim.
2. Call `ITaskService.GetProjectTasksAsync(projectId, userId)`.
3. Service returns `null` when project not found or user not a member → log Warning, return `404 NotFound(ApiResponse.Fail("Project not found."))`.
4. Service returns non-null `List<TaskResponseDto>` (may be empty) → log Information with `{UserId}`, `{Count}`, `{ProjectId}` — return `200 OK` with `ApiResponse<List<TaskResponseDto>>.Ok(data, "Tasks retrieved successfully.")`.

**Note:** The service layer returns `null` for both "project not found" and "user not member" cases, intentionally collapsing them to a single 404 response to prevent project ID enumeration. This matches the pattern from `ColumnService.GetProjectColumnsAsync`.

---

## 4. Application Layer Boundaries

### 4.1 Service Interface Extension

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs`

Add the following method signature to `ITaskService`:

```csharp
/// <summary>
/// Retrieves all tasks for the specified project.
/// Authorization: the caller must be a member of the project.
/// Tasks are returned sorted by (ColumnId ASC, TaskOrder ASC) for client-side bucketing.
/// </summary>
/// <param name="projectId">The project ID.</param>
/// <param name="userId">The authenticated user's ID (from JWT claims).</param>
/// <returns>
/// A list of <see cref="TaskResponseDto"/> objects when the project exists and the user is a member.
/// Returns <c>null</c> when the project does not exist or the user is not a project member.
/// Returns an empty list when the project exists, the user is a member, but the project has no tasks.
/// </returns>
Task<List<TaskResponseDto>?> GetProjectTasksAsync(Guid projectId, Guid userId);
```

### 4.2 Service Implementation

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs`

Implement the new method with the following logic:

1. Load the project with eager-loaded `Members` collection to check membership.
2. If project is `null` → log Information, return `null`.
3. If user is not a member → log Warning, return `null`.
4. Query tasks with `.Include(t => t.Column)` to enable sorting by `ColumnId`.
5. Order by `(t.Column.ProjectId == projectId) && (t.ColumnId, t.TaskOrder)` — filter and sort server-side.
6. Project to `TaskResponseDto` using the existing `MapToDto` helper.
7. Log Information with `{UserId}`, `{Count}`, `{ProjectId}` — return the list.

**Implementation pattern:**

```csharp
public async Task<List<TaskResponseDto>?> GetProjectTasksAsync(Guid projectId, Guid userId)
{
    // Check project existence and membership
    var project = await _context.Projects
        .Include(p => p.Members)
        .FirstOrDefaultAsync(p => p.Id == projectId);

    if (project == null)
    {
        _logger.LogInformation(
            "User {UserId} requested tasks for non-existent project {ProjectId}",
            userId, projectId);
        return null;
    }

    if (!project.Members.Any(m => m.UserId == userId))
    {
        _logger.LogWarning(
            "User {UserId} attempted to access tasks for project {ProjectId} without authorization",
            userId, projectId);
        return null;
    }

    // Fetch and order tasks
    var tasks = await _context.KanbanTasks
        .AsNoTracking()
        .Include(t => t.Column)
        .Where(t => t.Column.ProjectId == projectId)
        .OrderBy(t => t.ColumnId)
            .ThenBy(t => t.TaskOrder)
        .ToListAsync();

    _logger.LogInformation(
        "User {UserId} retrieved {Count} tasks for project {ProjectId}",
        userId, tasks.Count, projectId);

    return tasks.Select(MapToDto).ToList();
}
```

**Note:** The existing `MapToDto(KanbanTask task)` helper (lines 311-322 in `TaskService.cs`) is reused for consistency with create/move operations.

---

## 5. Implementation Steps

All paths are relative to the repository root `c:\temp\KanbAI-Core\`.

Follow `rules/code-standards.md` (file-scoped namespaces, constructor injection, no `.Result`/`.Wait()`, `.AsNoTracking()` for reads, no N+1).

### Step 1 — Extend `ITaskService` interface

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs`

Add the method signature from Section 4.1 to the `ITaskService` interface, after the existing `MoveTaskAsync` method signature (line 42).

### Step 2 — Implement `GetProjectTasksAsync` in `TaskService`

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs`

Add the implementation from Section 4.2 to the `TaskService` class, after the existing `MoveTaskAsync` method (line 288), before the private helper methods section.

No new `using` directives are required — `Microsoft.EntityFrameworkCore`, `KanbAI_Core.DTOs`, and `Microsoft.Extensions.Logging` are already imported.

### Step 3 — Add `GetProjectTasks` action to `TaskController`

**File:** `KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs`

Add the action **after** the existing `MoveTask` action (line 78) so the HTTP verb/route cluster reads top-to-bottom (POST, PUT, GET). No new `using` directives are required.

```csharp
[HttpGet("project/{projectId}")]
public async Task<IActionResult> GetProjectTasks(
    Guid projectId,
    CancellationToken cancellationToken)
{
    var userId = GetCurrentUserId();

    var tasks = await _taskService.GetProjectTasksAsync(projectId, userId);

    if (tasks == null)
    {
        return NotFound(ApiResponse.Fail("Project not found."));
    }

    _logger.LogInformation(
        "User {UserId} retrieved {Count} tasks for project {ProjectId}",
        userId, tasks.Count, projectId);

    return Ok(ApiResponse<List<TaskResponseDto>>.Ok(tasks, "Tasks retrieved successfully."));
}
```

**Note:** The service layer already logs the detailed Information/Warning entries, so the controller only logs the final success case for HTTP-level observability.

### Step 4 — Build and verify

Run from the repository root:

```bash
dotnet build KanbAI-Core/KanbAI-Core.sln -c Debug
```

Expected: clean build with no new warnings. If the build warns about ambiguous overloads between `ApiResponse.Fail` and `ApiResponse<T>.Fail`, adjust the `NotFound(...)` call to use the non-generic `ApiResponse.Fail(...)` form (matches the existing `CreateTask` pattern in the same controller).

---

## 6. QA Guidance

### 6.1 Test File Locations

**Extend two existing test files** (do **not** create new ones):

1. **`KanbAI-Core/KanbAI-Core.Tests/Controllers/TaskControllerTests.cs`**  
   Add a new `#region GetProjectTasks` grouping all controller action tests, following the existing regions (`CreateTask`, `MoveTask`). Use the existing class-level test fixture (`_taskService`, `_controller`, `SetupUserClaims`) — the current fixture already provides a mocked `ITaskService`, which is all that's needed.

2. **`KanbAI-Core/KanbAI-Core.Tests/Services/TaskServiceTests.cs`**  
   Add a new `#region GetProjectTasksAsync` grouping all service method tests, following the existing regions (`CreateTaskAsync`, `MoveTaskAsync`). Use the existing class-level test fixture (`_context`, `_service`, `SetupUserClaims`) — the current fixture already provides an InMemory `ApplicationDbContext`.

### 6.2 Testing Approach

- **Unit tests only** are required, using the existing InMemory `ApplicationDbContext` fixture for service tests and mocked `ITaskService` for controller tests. This matches the pattern already in place for `CreateTask` and `MoveTask`.
- **No integration tests against `WebApplicationFactory<Program>`** are required for this issue — no middleware, auth, or pipeline behavior changes. Only the controller action and service method are new, and they share infrastructure (`[Authorize]`, `ApiResponse<T>`, `GetCurrentUserId()`) that is already covered by existing tests.
- **Logger assertions** should verify the log was emitted at the correct level with the expected structured properties using `Mock<ILogger<T>>.Verify(...)` on `It.IsAny<LogLevel>()` matchers — same pattern used by existing tests.

### 6.3 Controller Test Cases

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 1 | `GetProjectTasks_ProjectExistsWithTasks_Returns200WithOrderedDtos` | Happy Path | Seeds a project with 2 columns and 3 tasks (2 in col A, 1 in col B); asserts 200 OK, `data.Count == 3`, ordered by `(ColumnId, TaskOrder)`, and `message == "Tasks retrieved successfully."` |
| 2 | `GetProjectTasks_ProjectExistsWithNoTasks_Returns200WithEmptyList` | Edge Case | Seeds a project with columns but no tasks; asserts 200 OK, `data` is non-null empty list, **not** 404 (AC #7) |
| 3 | `GetProjectTasks_ProjectDoesNotExist_Returns404NotFound` | Edge Case | Service returns `null`; asserts `NotFoundObjectResult` with `ApiResponse.Fail("Project not found.")` (AC #2, #8) |
| 4 | `GetProjectTasks_UserNotProjectMember_Returns404NotFound` | Security | Service returns `null` (user not member); asserts `NotFoundObjectResult` with `ApiResponse.Fail("Project not found.")` — intentionally same as "project not found" to prevent enumeration (AC #2, #10) |
| 5 | `GetProjectTasks_InvalidJwtClaim_ThrowsUnauthorizedAccessException` | Security | Configures controller with a user principal lacking a `NameIdentifier` claim; asserts `UnauthorizedAccessException` is thrown (via existing `GetCurrentUserId()`). Mirrors existing test for `CreateTask`. |
| 6 | `GetProjectTasks_SuccessfulRetrieval_LogsInformationWithCount` | Observability | Service returns 2 tasks; asserts Information-level log invocation with structured properties `{UserId}`, `{Count}` == 2, `{ProjectId}` (AC #9) |

### 6.4 Service Test Cases

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 1 | `GetProjectTasksAsync_ProjectExistsWithTasks_ReturnsOrderedDtos` | Happy Path | Seeds a project with 2 columns (col A order 0, col B order 1) and 5 tasks (col A: tasks 0, 1, 2; col B: tasks 0, 1); user is member; asserts result is non-null, count == 5, ordered by (ColumnId, TaskOrder), and all DTO fields match entity properties |
| 2 | `GetProjectTasksAsync_ProjectExistsWithNoTasks_ReturnsEmptyList` | Edge Case | Seeds a project with columns but no tasks; user is member; asserts non-null empty list (AC #7) |
| 3 | `GetProjectTasksAsync_ProjectDoesNotExist_ReturnsNull` | Edge Case | No seed; asserts `null` return and Information-level log entry is emitted (AC #2, #9) |
| 4 | `GetProjectTasksAsync_UserNotProjectMember_ReturnsNull` | Security | Seeds a project whose members do not include the authenticated user; asserts `null` return and Warning-level log with `{UserId}`, `{ProjectId}` (AC #2, #9, #10) |
| 5 | `GetProjectTasksAsync_MultipleProjectsExist_ReturnsTasksForRequestedProjectOnly` | Data Isolation | Seeds two projects (A and B), each with tasks; authenticated user is member of both; requests project A; asserts no task from project B leaks into the response |
| 6 | `GetProjectTasksAsync_TasksFromDeletedColumnsExist_DoesNotReturnDeletedColumnTasks` | Regression / Edge Case | If soft-delete on columns is implemented, seeds a project with 1 active column (with tasks) and 1 soft-deleted column (with tasks); asserts only active column's tasks are returned. **If hard delete is used (current schema),** this test is **N/A** — skip or replace with a comment explaining that FK cascade delete prevents orphaned tasks. |
| 7 | `GetProjectTasksAsync_OrderingByColumnIdThenTaskOrder_IsCorrect` | Happy Path | Seeds a project with 3 columns (IDs: `colC`, `colA`, `colB` — deliberately non-alphabetical) with tasks (colC: [0, 2, 1], colA: [1, 0], colB: [0]); asserts result is ordered by (ColumnId lexicographically ASC, TaskOrder numerically ASC) |
| 8 | `GetProjectTasksAsync_SuccessfulRetrieval_LogsInformationWithCount` | Observability | Seeds a project with 3 tasks; asserts Information-level log invocation with structured properties `{UserId}`, `{Count}` == 3, `{ProjectId}` (AC #9) |
| 9 | `GetProjectTasksAsync_QueryUsesAsNoTracking_DoesNotTrackEntities` | Performance | Seeds a project with 2 tasks; calls service method; asserts `_context.ChangeTracker.Entries<KanbanTask>().Count() == 0` (no entities are tracked) |

**Naming convention:** All tests follow `MethodName_StateUnderTest_ExpectedBehavior` per `rules/testing-observability.md §1`.

**AAA structure:** Every test must have visually separated Arrange / Act / Assert sections (blank lines).

### 6.5 Seed Helpers

Reuse any existing seed helpers present in `TaskControllerTests.cs` and `TaskServiceTests.cs` (e.g., `SetupUserClaims`, `SeedProject`, `SeedColumn`, `SeedTask` if they exist). If no task-seeding helper exists yet, add small private helpers on the test class:

**For TaskServiceTests.cs:**

```csharp
private Project SeedProjectWithMember(Guid userId, string projectName = "Test Project")
{
    var project = new Project
    {
        Id = Guid.NewGuid(),
        Name = projectName,
        Description = "Test Description",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    var member = new ProjectMember
    {
        ProjectId = project.Id,
        UserId = userId
    };
    _context.Projects.Add(project);
    _context.ProjectMembers.Add(member);
    _context.SaveChanges();
    return project;
}

private BoardColumn SeedColumn(Guid projectId, string name = "To Do", int order = 0)
{
    var column = new BoardColumn
    {
        Id = Guid.NewGuid(),
        ProjectId = projectId,
        Name = name,
        ColumnOrder = order,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    _context.BoardColumns.Add(column);
    _context.SaveChanges();
    return column;
}

private KanbanTask SeedTask(Guid columnId, string title, int taskOrder, string? content = null, Guid? assignedId = null)
{
    var task = new KanbanTask
    {
        Id = Guid.NewGuid(),
        ColumnId = columnId,
        Title = title,
        Content = content,
        TaskOrder = taskOrder,
        AssignedId = assignedId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
    _context.KanbanTasks.Add(task);
    _context.SaveChanges();
    return task;
}
```

Keep the helpers `private` and nested in the test class.

---

## 7. Known Caveats

| Caveat | Impact | Mitigation |
|---|---|---|
| **EF Core InMemory provider does not enforce referential-integrity constraints on seeded data.** | A test that seeds a `KanbanTask` with a `ColumnId` that does not match any seeded `BoardColumn` will still materialize, masking schema drift. | Test #5 explicitly seeds both sides (columns + tasks) for both projects. Review seeded graphs when tests pass unexpectedly. |
| **`SaveChangesAsync` override auto-stamps `CreatedAt` / `UpdatedAt`** (see `ApplicationDbContext`). | Tests that want deterministic `CreatedAt` ordering cannot easily control the timestamp without overwriting after save. | Seed via `_context.KanbanTasks.Add(...)`, call `SaveChangesAsync`. For sorting tests (test #7), use `ColumnId` and `TaskOrder` which are deterministic; `CreatedAt` is not used for sorting in this endpoint. |
| **`_logger.LogWarning` with structured properties is asserted via `Mock<ILogger<T>>.Verify(...)` on `It.IsAny<LogLevel>()`** — structured property inspection is non-trivial with `ILogger`. | Verifying exact property values (e.g., `{UserId}` == authenticated user's Guid) requires casting the `state` parameter to `IReadOnlyList<KeyValuePair<string, object>>` inside a custom matcher. | Follow the existing pattern in `TaskServiceTests` — if prior tests only assert log level and message presence, mirror that. Only introduce deeper property inspection if a bug is found that requires it. |
| **The service returns `null` for both "project not found" and "user not member" cases.** | An authenticated user cannot distinguish between "project does not exist" and "project exists but I'm not a member" by observing the HTTP status code. Both return 404. | This is **intentional** to prevent project ID enumeration. It matches the pattern from `ColumnController.GetProjectColumns` (see `ColumnService.GetProjectColumnsAsync` which returns `null` for both cases). |
| **No pagination is implemented.** | Projects with extremely large task counts (thousands) will return an oversized response body and may cause performance issues on the client. | Out of scope for issue #82 per the context note. If operational data later shows large task counts, add a paginated overload (e.g., `?skip=&take=`) in a follow-up issue — but only if AC #10's "no filter parameters that bypass authorization" is preserved. |
| **Hard delete (FK cascade) vs. soft delete:** | The current schema uses hard delete on `BoardColumn` (FK cascade to `KanbanTask`), so deleted columns' tasks are automatically removed from the database and cannot leak. If soft-delete is introduced later, the query must be updated to filter `WHERE t.Column.IsDeleted == false`. | Test #6 in the service test suite documents this assumption. If soft-delete is added, update the query and the test. |
| **Query ordering by `ColumnId` (Guid) is lexicographic, not semantic.** | GUIDs are sorted as strings (e.g., `00000000-...`, `11111111-...`, `aaaaaaaa-...`), not by creation order. The client must bucket by `ColumnId` and should **not** assume that the first N tasks in the response belong to the "first" column visually. | This is acceptable because `BoardColumn` has a separate `ColumnOrder` property that the frontend already uses to determine visual column order. The server sorts tasks by `(ColumnId, TaskOrder)` purely for bucketing efficiency; the client is responsible for rendering columns in the correct visual order. |

---

## 8. Design Validation (Self-Check)

| Check | Result |
|-------|--------|
| **Namespaces** | `KanbAI_Core.Controllers`, `KanbAI_Core.Services.Tasks`, `KanbAI_Core.DTOs` — all match existing usages in `TaskController.cs` and `TaskService.cs`. |
| **Folder Paths** | Only existing files are modified; no new folders needed. |
| **Dependencies** | No new NuGet packages. `Microsoft.EntityFrameworkCore` v10.0.x already provides `.Include()` and `.AsNoTracking()`. |
| **Naming Conflicts** | `GetProjectTasks` is unique on `TaskController`. `GetProjectTasksAsync` is unique on `ITaskService`. The route `GET project/{projectId}` does not collide with existing routes. |
| **BaseEntity Compliance** | No new entity — N/A. |
| **Code Standards** | File-scoped namespace preserved, constructor injection (existing DI), `await`/`async` throughout, `.AsNoTracking()` used, no string interpolation in log templates, no `.Result`/`.Wait()`, no N+1 (single query with `.Include(t => t.Column)`). |
| **Security** | Authorization-before-data ordering verified; enumeration attack surface minimized (404 for both "not found" and "not member" cases, matching `ColumnController` pattern); no additional query parameters accepted. Reuses existing `TaskResponseDto` which does not expose sensitive data (no password hashes, no internal state). |

---

## 9. Relationship to Existing Issues

| Issue | State | Role in #82 |
|---|---|---|
| #61 — Add Column Creation/Deletion Endpoints | Merged | Established the pattern for `ColumnController.GetProjectColumns` (project-scoped GET with membership check) that this issue mirrors for tasks |
| #67 — Implement AttachmentController Endpoints | Merged | Established the pattern for reading resources from `ApplicationDbContext` directly in controllers (AttachmentController) — this issue uses service-layer abstraction instead because tasks are core domain entities |
| #77 — Fix SignalR CORS Policy Error | Merged | Unblocked SignalR connections from the frontend, which will enable the TaskCreated/TaskMoved events to work alongside this new GET endpoint for full board state sync |
| #80 — Add List-Attachments-By-Task Endpoint | Merged | Provides the pattern for filtered `.Include()` queries and authorization-before-data ordering that this issue adapts for task reads |
| **#82 — This issue** | **Open** | **Adds `GET /api/task/project/{projectId}`** |
| KanbAI-Web #83 — Task Description Read/Edit | Open | Direct consumer — blocked until #82 ships (requires tasks to be visible after page load) |

---

The technical specification is saved. You can now instruct the developer to read the tech spec and begin implementation.

---

## 10. Development Status

**Developer:** @agent_developer
**Completed:** 2026-05-08
**Status:** ✅ Implementation complete, build passing, all existing tests green.

### 10.1 Files Modified

| File | Change |
|---|---|
| `KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs` | Added `GetProjectTasksAsync(Guid projectId, Guid userId)` method signature with XML documentation after `MoveTaskAsync`. |
| `KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs` | Added `GetProjectTasksAsync` implementation after `MoveTaskAsync` (immediately before the `BuildProjectGroupName` private helper). Uses `.AsNoTracking()` on both the project membership query and the task query; single `.Include(t => t.Column)` to enable the `WHERE t.Column.ProjectId == projectId` filter and `OrderBy(ColumnId).ThenBy(TaskOrder)` server-side sort. Reuses the existing private static `MapToDto` helper. |
| `KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs` | Added `[HttpGet("project/{projectId}")] GetProjectTasks(Guid projectId, CancellationToken)` action after `MoveTask` and before `GetCurrentUserId`. Returns `200 OK` with `ApiResponse<List<TaskResponseDto>>` on success; `404 NotFound` with `ApiResponse.Fail("Project not found.")` for both project-not-found and user-not-member cases. |

### 10.2 Files Created

None. All changes were additive to existing files.

### 10.3 Build & Test Results

**Build:** `dotnet build KanbAI-Core.sln -c Debug` → **Build succeeded, 0 errors, 6 warnings.**

- All 6 warnings are **pre-existing** `CS8604` warnings in `KanbAI-Core.Tests/Services/Assets/AssetServiceTests.cs` lines 316, 322, 328, 385, 440, 473. They relate to `AssetServiceTests` SignalR mock setup and are unrelated to this change.
- No new warnings introduced.

**Tests:** `dotnet test KanbAI-Core.sln --no-build --verbosity quiet` → **Passed! 580 passed, 0 failed, 2 skipped, Total 582, Duration 6s.**

- No new tests were written in this handoff — unit tests are the QA Tester's responsibility per tech spec §6.
- No existing tests broke. The new `GetProjectTasksAsync` method on `ITaskService` is additive; existing mock setups (e.g., `Mock<ITaskService>` in `TaskControllerTests`) continue to work because Moq auto-implements unconfigured interface methods.

### 10.4 Infrastructure Notes

No workarounds or infrastructure changes. The implementation fits within the existing service/controller/DTO abstractions.

**Minor location note vs. tech spec §5 Step 2:** The spec said to insert `GetProjectTasksAsync` "after the existing `MoveTaskAsync` method, before the private helper methods section." The implementation follows that guidance precisely — the new public method is positioned immediately after `MoveTaskAsync` and immediately before `BuildProjectGroupName` (the first private helper). File is logically organized: public surface area first, then private helpers, then `MapToDto`.

### 10.5 Edge Cases for QA

1. **Enumeration prevention (AC #10):** The service returns `null` for both "project does not exist" and "user is not a member" cases. The controller maps both to `404 NotFound` with the **same** message (`"Project not found."`). QA must assert that an authenticated user cannot distinguish between these two failure modes by status code, response body, or timing (within reasonable variance). See tech spec §7 row 4 for the design rationale.

2. **Server-authoritative ordering (AC #4):** Tasks are ordered by `(ColumnId ASC, TaskOrder ASC)`. `ColumnId` is a `Guid` — ordering is **lexicographic**, not semantic. Tech spec §6.4 test #7 validates this; QA should not assume alphabetical or creation-order sorting. The frontend orders columns using `BoardColumn.ColumnOrder`, which is a separate concern.

3. **Empty list ≠ 404 (AC #7):** A project with no tasks returns `200 OK` with an empty `data` array. QA must verify `data` is present (not omitted, not `null`) and serializes to `[]` in JSON.

4. **`AsNoTracking()` enforcement (§9 test case):** The query must not track returned entities. QA can validate via `_context.ChangeTracker.Entries<KanbanTask>().Count() == 0` after the service call returns. This matters if a future code change accidentally removes `.AsNoTracking()` — the test would catch the performance regression.

5. **Tasks across multiple columns in one project (§6.4 test #1, #5):** Seed at least 2 columns and at least 2 tasks per column to verify the JOIN filter and sort are correct. Ensure no tasks from other projects (§6.4 test #5) leak into the response.

6. **Logger structured-property assertions:** The service emits:
   - `LogInformation("User {UserId} requested tasks for non-existent project {ProjectId}", ...)` when project not found.
   - `LogWarning("User {UserId} attempted to access tasks for project {ProjectId} without authorization", ...)` when user not a member.
   - `LogInformation("User {UserId} retrieved {Count} tasks for project {ProjectId}", ...)` on success.

   The controller also emits the `{UserId} retrieved {Count} tasks ...` Information log on success (see tech spec §5 Step 3 note — the controller logs at the HTTP boundary; the service logs at the domain boundary; both are intentional, not duplication).

7. **Test file paths:** Tech spec §6.1 references `KanbAI-Core.Tests/Services/TaskServiceTests.cs`, but the actual file lives at **`KanbAI-Core.Tests/Services/Tasks/TaskServiceTests.cs`** (note the `Tasks/` subfolder). QA should add the new `#region GetProjectTasksAsync` test block to the correct existing file and reuse its existing fixture (`CreateInMemoryContext`, `SeedProjectWithColumnAsync`, `CreateHubContextMock`). `TaskControllerTests.cs` is at the path given in the spec.

8. **Pre-existing warnings in `AssetServiceTests.cs`:** Not caused by this change. QA can ignore them for issue #82 scope, but they may be worth a follow-up cleanup ticket.

---

Development is complete and files are saved. You can now instruct the QA tester to review the implementation and write automated tests.

---

## 11. QA Status

**QA Tester:** @agent_tester_qa
**Completed:** 2026-05-08
**Status:** All tests pass, implementation meets acceptance criteria.

### 11.1 Test Files Modified

| File | Tests Added | Description |
|---|---|---|
| `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Controllers\TaskControllerTests.cs` | 6 | Controller action tests for `GetProjectTasks` endpoint covering happy path, empty list, 404 responses, authorization failures, and logging |
| `c:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core.Tests\Services\Tasks\TaskServiceTests.cs` | 8 | Service method tests for `GetProjectTasksAsync` covering happy path, empty list, null returns, project isolation, ordering correctness, logging, and AsNoTracking verification |

### 11.2 Test Coverage Summary

**Controller Tests (6 tests):**
1. `GetProjectTasks_ProjectExistsWithTasks_Returns200WithOrderedDtos` - Verifies 200 OK response with properly ordered tasks when project has tasks in multiple columns
2. `GetProjectTasks_ProjectExistsWithNoTasks_Returns200WithEmptyList` - Verifies 200 OK with empty array when project exists but has no tasks (AC #7)
3. `GetProjectTasks_ProjectDoesNotExist_Returns404NotFound` - Verifies 404 response with proper error message when project does not exist
4. `GetProjectTasks_UserNotProjectMember_Returns404NotFound` - Verifies 404 response (same as project not found) when user is not a project member, preventing enumeration attacks (AC #2, #10)
5. `GetProjectTasks_InvalidJwtClaim_ThrowsUnauthorizedAccessException` - Verifies UnauthorizedAccessException when JWT NameIdentifier claim is missing or invalid
6. `GetProjectTasks_SuccessfulRetrieval_LogsInformationWithCount` - Verifies Information-level logging with structured properties {UserId}, {Count}, {ProjectId} on success (AC #9)

**Service Tests (8 tests):**
1. `GetProjectTasksAsync_ProjectExistsWithTasks_ReturnsOrderedDtos` - Verifies service returns 5 tasks across 2 columns in correct order (ColumnId ASC, TaskOrder ASC), validates all DTO properties match entity properties
2. `GetProjectTasksAsync_ProjectExistsWithNoTasks_ReturnsEmptyList` - Verifies non-null empty list when project has columns but no tasks (AC #7)
3. `GetProjectTasksAsync_ProjectDoesNotExist_ReturnsNull` - Verifies null return and Information-level log when project does not exist (AC #2, #9)
4. `GetProjectTasksAsync_UserNotProjectMember_ReturnsNull` - Verifies null return and Warning-level log when user is not a project member (AC #2, #9, #10)
5. `GetProjectTasksAsync_MultipleProjectsExist_ReturnsTasksForRequestedProjectOnly` - Verifies no data leakage between projects when user is member of multiple projects (Data Isolation)
6. `GetProjectTasksAsync_OrderingByColumnIdThenTaskOrder_IsCorrect` - Verifies correct lexicographic ColumnId ordering followed by numeric TaskOrder ordering with deliberately non-alphabetical column IDs (AC #4)
7. `GetProjectTasksAsync_SuccessfulRetrieval_LogsInformationWithCount` - Verifies Information-level logging with structured properties on success (AC #9)
8. `GetProjectTasksAsync_QueryUsesAsNoTracking_DoesNotTrackEntities` - Verifies `.AsNoTracking()` usage by asserting ChangeTracker has no tracked KanbanTask entities after query (AC #14, Performance)

**Note on Test #6 (Soft-Deleted Columns):** As documented in tech spec §7 and §10.5, the current schema uses hard delete (FK cascade) on `BoardColumn → KanbanTask`, so soft-deleted columns cannot exist. No test was added for this case per QA guidance in tech spec §6.4 test #6.

### 11.3 Test Results

**Command:** `dotnet test KanbAI-Core.sln --no-build --verbosity quiet`

**Result:**
```
Passed!  - Failed:     0, Passed:   594, Skipped:     2, Total:   596, Duration: 6 s
```

**Comparison to Baseline:**
- Baseline (from tech spec §10.3): 580 passed, 0 failed, 2 skipped, 582 total
- After QA: 594 passed, 0 failed, 2 skipped, 596 total
- **Delta:** +14 tests added (6 controller + 8 service)

**Build Result:** `dotnet build KanbAI-Core.sln -c Debug` succeeded with 6 pre-existing warnings in `AssetServiceTests.cs` (CS8604, documented in tech spec §10.3). No new warnings introduced.

### 11.4 Bugs Found & Fixed

**None.** The implementation correctly matches the technical specification. All test cases passed on first run after fixing a single test implementation issue (using deterministic column GUIDs for ordering assertions).

### 11.5 Implementation Verification

The following acceptance criteria from `issue_82_context.md` were verified through automated tests:

- **AC #1-5:** Endpoint exists, authenticated, authorized, queries correctly, results ordered ✓
- **AC #6-7:** Response mapping correct, empty list returns 200 OK ✓
- **AC #8:** Error responses (404, 401) correct ✓
- **AC #9:** Structured logging at correct levels (Information, Warning) ✓
- **AC #10:** Authorization enforced, no enumeration attack surface ✓
- **AC #11-12:** Response format matches existing DTOs, consistent patterns ✓
- **AC #13:** No breaking changes, existing tests still pass ✓
- **AC #14:** Query uses `.AsNoTracking()` and single roundtrip with `.Include()` ✓
- **AC #15:** Test coverage complete for all paths ✓

### 11.6 Outstanding Issues

**None.** All acceptance criteria met, all tests pass, implementation is production-ready.

---

QA testing is complete. All tests pass and the implementation meets the acceptance criteria.
