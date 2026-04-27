# Technical Specification: Issue #46 — Implement Kanban Task Creation and Management

**GitHub Issue:** [#46 - Implement Kanban Task Creation and Management](https://github.com/Gulybi/KanbAI-Core/issues/46)
**Context Document:** [issue_46_context.md](./issue_46_context.md)
**Staff Engineer:** @agent_staff_engineer
**Created:** 2026-04-27

---

## 1. Overview

This specification defines the implementation of a Kanban Task creation API that allows authenticated project members to create task cards in a board column, with optional assignment of the task to another project member. The implementation introduces a new service (`TaskService`) and controller (`TaskController`) while leveraging the existing `KanbanTask` domain entity and the project-membership authorization pattern established in Issue #45.

**Scope:**
- Create `ITaskService` interface and `TaskService` implementation.
- Create `TaskController` exposing **one** REST endpoint: `POST /api/Task/column/{columnId}`.
- Create two DTO records: `CreateTaskDto` (request), `TaskResponseDto` (response).
- Implement membership-based authorization (user must be a member of the column's owning project).
- Validate the optional `AssignedId`: referenced user must exist **and** be a member of the same project.
- Auto-compute `TaskOrder` as `max(existing TaskOrder in the column) + 1`, or `0` when the column is empty.
- Register `TaskService` in DI.

**Out of Scope (per context note):**
- No `GET`, `PUT`, or `DELETE` task endpoints in this issue (reserved for future issues).
- No moving tasks between columns or reordering (deferred to Issue #47).
- No task comments, attachments, filtering, due dates, priorities, or bulk operations.
- **No database schema changes, entity changes, or EF Core configuration changes.** `KanbanTask`, `KanbanTaskConfiguration`, and the `KanbanTasks` migration already exist.

**Why No Database Changes:**
All required entities (`KanbanTask`, `BoardColumn`, `Project`, `ProjectMember`, `User`) and their EF configurations already exist and were introduced in Issues #23 and #26. This issue is purely an application-layer/API addition.

**Design Note — Status Code Divergence from Column API:**
The context note's "Error Handling" and Acceptance Criteria explicitly require **403 Forbidden** when the caller is not a project member (AC: *"If the authenticated user's JWT is valid but they are not a member of the project, the endpoint returns 403 Forbidden (not 404)"*). This is an intentional departure from the `ColumnService` obfuscation pattern (which conflates "not found" and "not a member" under 404). Distinguishing the two is justified here because `columnId` is a `Guid` — enumeration attacks are not practical, and explicit `403` improves diagnosability for legitimate clients. See Section 7, Caveat #1.

---

## 2. Database / Domain Design

### 2.1 Existing Entities (No Changes Required)

#### KanbanTask Entity

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/KanbanTask.cs`

| Property | Type | Constraints | Source |
|----------|------|-------------|--------|
| `Id` | `Guid` | Primary key | Inherited from `BaseEntity` |
| `Title` | `string` | Required, max 200 chars | Direct property |
| `Content` | `string?` | Optional | Direct property |
| `TaskOrder` | `int` | Required | Direct property |
| `ColumnId` | `Guid` | Foreign key to `BoardColumn` (cascade) | Direct property |
| `AssignedId` | `Guid?` | Foreign key to `User` (set null) | Direct property |
| `CreatedAt` | `DateTimeOffset` | Auto-set on insert | Inherited from `BaseEntity` |
| `UpdatedAt` | `DateTimeOffset` | Auto-updated on modify | Inherited from `BaseEntity` |
| `Column` | `BoardColumn` | Navigation | — |
| `AssignedUser` | `User?` | Navigation | — |
| `Assets` | `ICollection<Asset>` | Navigation (cascade from Task) | — |
| `Comments` | `ICollection<TaskComment>` | Navigation (cascade from Task) | — |

#### KanbanTaskConfiguration

**File:** `KanbAI-Core/KanbAI-Core/Data/Configurations/KanbanTaskConfiguration.cs`

| Property | Constraint | Purpose |
|----------|-----------|---------|
| `Title` | `.IsRequired().HasMaxLength(200)` | AC: Title required; enforced at DB level |
| `Content` | (none) | Optional free-text description |
| `TaskOrder` | `.IsRequired()` | Position within column |
| `Column` | `.OnDelete(DeleteBehavior.Cascade)` | When column deleted, tasks are removed |
| `AssignedUser` | `.IsRequired(false).OnDelete(DeleteBehavior.SetNull)` | When user deleted, task remains unassigned |

### 2.2 Expected Database Queries (EF Core)

The service will execute the following queries:

| Operation | Query Pattern | Purpose |
|-----------|---------------|---------|
| **Load column with project + members** | `SELECT ... FROM BoardColumns c JOIN Projects p JOIN ProjectMembers m WHERE c.Id = @columnId` | Combined existence check + authorization in one round-trip |
| **Compute next TaskOrder** | `SELECT MAX(TaskOrder) FROM KanbanTasks WHERE ColumnId = @columnId` | Auto-order new task at bottom |
| **Validate assigned user exists** | `SELECT 1 FROM Users WHERE Id = @assignedId` (fold into member check) | AC: 400 if assigned user not found |
| **Validate assigned user is a project member** | `SELECT 1 FROM ProjectMembers WHERE ProjectId = @projectId AND UserId = @assignedId` | AC: 400 if assigned user not a member |
| **Insert task** | `INSERT INTO KanbanTasks ...` | Create task |

**N+1 Prevention:** The column load uses `.Include(c => c.Project).ThenInclude(p => p.Members)` so a single round-trip retrieves the column, its project, and the member list. `SaveChangesAsync` triggers the `BaseEntity` timestamp override in `ApplicationDbContext`.

**Read Optimization:** The column load is a **tracked** query (no `.AsNoTracking()`) — although the column entity itself isn't mutated, the membership scan operates on the loaded graph; tracking is inexpensive for a single row with a small `Members` collection and avoids subtle bugs if the service later adds navigation-property mutations. The "compute max TaskOrder" query and the member-validation query may use `.AsNoTracking()`.

---

## 3. API Contracts

### 3.1 Endpoint Summary

| Method | Route | Auth | Request Body | Response DTO | Success | Failure |
|--------|-------|------|--------------|--------------|---------|---------|
| POST | `/api/Task/column/{columnId}` | Required | `CreateTaskDto` | `ApiResponse<TaskResponseDto>` | 201 Created | 400, 401, 403, 404 |

### 3.2 DTO Definitions

#### CreateTaskDto (Request)

**File:** `KanbAI-Core/KanbAI-Core/DTOs/CreateTaskDto.cs`

```csharp
namespace KanbAI_Core.DTOs;

using System.ComponentModel.DataAnnotations;

public record CreateTaskDto
{
    [Required(ErrorMessage = "Task title is required.")]
    [MaxLength(200, ErrorMessage = "Task title cannot exceed 200 characters.")]
    public required string Title { get; init; }

    public string? Content { get; init; }

    public Guid? AssignedId { get; init; }
}
```

**Validation Rules:**
- `Title`: Required, max 200 characters (mirrors `KanbanTaskConfiguration`).
- `Content`: Optional free-text (no length cap at DTO level — matches the entity configuration, which has no max length).
- `AssignedId`: Optional. When null/omitted the task is unassigned; when present it must reference an existing `User` who is a member of the column's project.
- **Whitespace-only Title:** `[Required]` rejects `null` and `""` but does NOT reject `"   "`. The service layer adds an explicit `string.IsNullOrWhiteSpace(dto.Title)` check to satisfy AC: *"empty or whitespace-only Title returns 400 Bad Request"*.

#### TaskResponseDto (Response)

**File:** `KanbAI-Core/KanbAI-Core/DTOs/TaskResponseDto.cs`

```csharp
namespace KanbAI_Core.DTOs;

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

**Property Notes:**
- `Id`, `ColumnId`, `AssignedId`: stringified `Guid` for JSON friendliness (consistent with `ColumnResponseDto`).
- `AssignedId` is nullable to reflect unassigned tasks.
- `CreatedAt` / `UpdatedAt`: ISO 8601 timestamps from `BaseEntity`.

### 3.3 Example API Interactions

#### Example 1 — Create Task, No Assignment, Empty Column (Success)

**Request:**
```http
POST /api/Task/column/c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Content-Type: application/json

{
  "title": "Design schema",
  "content": "Draft the initial EF Core entities."
}
```

**Response (201 Created):**
```json
{
  "success": true,
  "message": "Task created successfully.",
  "data": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "title": "Design schema",
    "content": "Draft the initial EF Core entities.",
    "taskOrder": 0,
    "columnId": "c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o",
    "assignedId": null,
    "createdAt": "2026-04-27T10:00:00Z",
    "updatedAt": "2026-04-27T10:00:00Z"
  },
  "errors": []
}
```

#### Example 2 — Create Task with Assignment, Non-Empty Column (Success)

**Request:**
```http
POST /api/Task/column/c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o HTTP/1.1
Authorization: Bearer ...
Content-Type: application/json

{
  "title": "Review PR",
  "assignedId": "77777777-7777-7777-7777-777777777777"
}
```

**Response (201 Created):**
```json
{
  "success": true,
  "message": "Task created successfully.",
  "data": {
    "id": "b2c3d4e5-...",
    "title": "Review PR",
    "content": null,
    "taskOrder": 3,
    "columnId": "c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o",
    "assignedId": "77777777-7777-7777-7777-777777777777",
    "createdAt": "2026-04-27T10:05:00Z",
    "updatedAt": "2026-04-27T10:05:00Z"
  },
  "errors": []
}
```

#### Example 3 — Missing Title (Validation Failure)

**Request body:**
```json
{ "content": "oops, no title" }
```

**Response (400 Bad Request):** — standard `[ApiController]` `ValidationProblemDetails` response (handled by framework). Also returned for whitespace-only titles via the service-layer check, wrapped as `ApiResponse.Fail("Task title is required.")`.

#### Example 4 — Column Not Found

**Response (404 Not Found):**
```json
{
  "success": false,
  "message": "Column not found.",
  "errors": []
}
```

#### Example 5 — User Not a Project Member

**Response (403 Forbidden):**
```json
{
  "success": false,
  "message": "You are not a member of this project.",
  "errors": []
}
```

#### Example 6 — Assigned User Not a Project Member

**Response (400 Bad Request):**
```json
{
  "success": false,
  "message": "Assigned user is not a member of this project.",
  "errors": []
}
```

#### Example 7 — Assigned User Does Not Exist

**Response (400 Bad Request):**
```json
{
  "success": false,
  "message": "Assigned user not found.",
  "errors": []
}
```

---

## 4. Application Layer Boundaries

### 4.1 Result Discriminator Enum

Because this endpoint must distinguish **four** failure modes (404 column-not-found, 403 caller-not-member, 400 assigned-user-not-found, 400 assigned-user-not-member) on top of the success path, the service cannot simply return `TaskResponseDto?` the way `IColumnService` does. We introduce a small discriminator enum colocated with the service.

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/CreateTaskResult.cs`

```csharp
namespace KanbAI_Core.Services.Tasks;

public enum CreateTaskResult
{
    Success,
    ColumnNotFound,
    UserNotProjectMember,
    AssignedUserNotFound,
    AssignedUserNotProjectMember,
    InvalidTitle
}
```

### 4.2 ITaskService Interface

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs`

```csharp
namespace KanbAI_Core.Services.Tasks;

using KanbAI_Core.DTOs;

public interface ITaskService
{
    /// <summary>
    /// Creates a new task in the specified column.
    /// Authorization: the caller must be a member of the project that owns the column.
    /// If <paramref name="dto"/>.AssignedId is provided, it must reference a user who is a member of the same project.
    /// The new task's TaskOrder is automatically set to (max existing TaskOrder in column) + 1, or 0 if the column is empty.
    /// </summary>
    /// <param name="columnId">The target column ID.</param>
    /// <param name="dto">Task creation data.</param>
    /// <param name="userId">The authenticated user's ID (from JWT claims).</param>
    /// <returns>A tuple containing the created task (on success) and a <see cref="CreateTaskResult"/> discriminator.</returns>
    Task<(TaskResponseDto? data, CreateTaskResult result)> CreateTaskAsync(
        Guid columnId,
        CreateTaskDto dto,
        Guid userId);
}
```

### 4.3 TaskService Implementation

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs`

**Dependencies (Constructor Injection):**
- `ApplicationDbContext _context`
- `ILogger<TaskService> _logger`

**Key Algorithm for `CreateTaskAsync`:**

1. If `string.IsNullOrWhiteSpace(dto.Title)` → return `(null, InvalidTitle)`.
2. Load the column together with its project and project members in a single query:
   ```csharp
   var column = await _context.BoardColumns
       .Include(c => c.Project)
           .ThenInclude(p => p.Members)
       .FirstOrDefaultAsync(c => c.Id == columnId);
   ```
3. If `column == null` → log warning, return `(null, ColumnNotFound)`.
4. If the caller is not a member of `column.Project`:
   - Log warning.
   - Return `(null, UserNotProjectMember)`.
5. If `dto.AssignedId.HasValue`:
   - Verify the user exists: `await _context.Users.AsNoTracking().AnyAsync(u => u.Id == dto.AssignedId.Value)`.
     - If not → return `(null, AssignedUserNotFound)`.
   - Verify the user is a member of `column.ProjectId`:
     - Reuse the already-loaded `column.Project.Members` collection — no extra DB round-trip.
     - If not → return `(null, AssignedUserNotProjectMember)`.
6. Compute `taskOrder`:
   ```csharp
   var maxOrder = await _context.KanbanTasks
       .Where(t => t.ColumnId == columnId)
       .MaxAsync(t => (int?)t.TaskOrder);
   int taskOrder = (maxOrder ?? -1) + 1;
   ```
   (Same null-projection pattern as `ColumnService.ComputeNextColumnOrderAsync`.)
7. Construct and persist the entity:
   ```csharp
   var task = new KanbanTask
   {
       Title = dto.Title,
       Content = dto.Content,
       TaskOrder = taskOrder,
       ColumnId = columnId,
       AssignedId = dto.AssignedId
   };
   _context.KanbanTasks.Add(task);
   await _context.SaveChangesAsync();
   ```
8. Log structured info event (`User {UserId} created task {TaskId} in column {ColumnId}`).
9. Return `(MapToDto(task), Success)`.

**Static Mapper:**

```csharp
private static TaskResponseDto MapToDto(KanbanTask task) =>
    new()
    {
        Id = task.Id.ToString(),
        Title = task.Title,
        Content = task.Content,
        TaskOrder = task.TaskOrder,
        ColumnId = task.ColumnId.ToString(),
        AssignedId = task.AssignedId?.ToString(),
        CreatedAt = task.CreatedAt,
        UpdatedAt = task.UpdatedAt
    };
```

### 4.4 TaskController

**File:** `KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs`

- `[ApiController]`, `[Route("api/[controller]")]` → `/api/Task`.
- `[Authorize]` on the class.
- Constructor-injects `ITaskService` and `ILogger<TaskController>`.
- Reuses the exact `GetCurrentUserId()` helper from `ColumnController` (copy verbatim — same pattern, different logger generic type).

**Endpoint Mapping:**

| Result | HTTP Response |
|--------|---------------|
| `Success` | `201 Created` via `CreatedAtAction` — `actionName: nameof(CreateTask)`, `routeValues: new { columnId = result.ColumnId }`, body `ApiResponse<TaskResponseDto>.Ok(result, "Task created successfully.")`. There is no GET endpoint, so the `Location` header will point back at the POST route — documented in §7 Caveat #4 (matches the Column API precedent). |
| `InvalidTitle` | `400 Bad Request` with `ApiResponse.Fail("Task title is required.")` |
| `ColumnNotFound` | `404 Not Found` with `ApiResponse.Fail("Column not found.")` |
| `UserNotProjectMember` | `403 Forbidden` — `StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("You are not a member of this project."))` |
| `AssignedUserNotFound` | `400 Bad Request` with `ApiResponse.Fail("Assigned user not found.")` |
| `AssignedUserNotProjectMember` | `400 Bad Request` with `ApiResponse.Fail("Assigned user is not a member of this project.")` |

**Skeleton:**

```csharp
namespace KanbAI_Core.Controllers;

using KanbAI_Core.DTOs;
using KanbAI_Core.Services.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class TaskController : ControllerBase
{
    private readonly ITaskService _taskService;
    private readonly ILogger<TaskController> _logger;

    public TaskController(ITaskService taskService, ILogger<TaskController> logger)
    {
        _taskService = taskService;
        _logger = logger;
    }

    [HttpPost("column/{columnId}")]
    public async Task<IActionResult> CreateTask(Guid columnId, [FromBody] CreateTaskDto dto)
    {
        var userId = GetCurrentUserId();

        var (data, result) = await _taskService.CreateTaskAsync(columnId, dto, userId);

        return result switch
        {
            CreateTaskResult.Success =>
                CreatedAtAction(
                    nameof(CreateTask),
                    new { columnId = data!.ColumnId },
                    ApiResponse<TaskResponseDto>.Ok(data, "Task created successfully.")),
            CreateTaskResult.ColumnNotFound =>
                NotFound(ApiResponse.Fail("Column not found.")),
            CreateTaskResult.UserNotProjectMember =>
                StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse.Fail("You are not a member of this project.")),
            CreateTaskResult.InvalidTitle =>
                BadRequest(ApiResponse.Fail("Task title is required.")),
            CreateTaskResult.AssignedUserNotFound =>
                BadRequest(ApiResponse.Fail("Assigned user not found.")),
            CreateTaskResult.AssignedUserNotProjectMember =>
                BadRequest(ApiResponse.Fail("Assigned user is not a member of this project.")),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                     ApiResponse.Fail("Unexpected error."))
        };
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim in JWT token");
            throw new UnauthorizedAccessException("Invalid or missing user ID in token.");
        }
        return userId;
    }
}
```

---

## 5. Implementation Steps for @agent_developer

### Step 1 — Create the Request DTO

**File:** `KanbAI-Core/KanbAI-Core/DTOs/CreateTaskDto.cs`
Create the new folder is **not** required — `DTOs/` already exists.
Use the exact code from §3.2 `CreateTaskDto`.

### Step 2 — Create the Response DTO

**File:** `KanbAI-Core/KanbAI-Core/DTOs/TaskResponseDto.cs`
Use the exact code from §3.2 `TaskResponseDto`.

### Step 3 — Create the `Services/Tasks/` Folder

The folder does not yet exist. It will be created implicitly by writing the first file into it. No extra tooling step required.

### Step 4 — Create the `CreateTaskResult` Enum

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/CreateTaskResult.cs`
Use the exact code from §4.1.

### Step 5 — Create the Service Interface

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs`
Use the exact code from §4.2. Include the XML doc comment verbatim.

### Step 6 — Implement `TaskService`

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs`

Implement the class according to the algorithm in §4.3. The full file body (synthesized from the algorithm and the project's conventions) is:

```csharp
namespace KanbAI_Core.Services.Tasks;

using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class TaskService : ITaskService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TaskService> _logger;

    public TaskService(ApplicationDbContext context, ILogger<TaskService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<(TaskResponseDto? data, CreateTaskResult result)> CreateTaskAsync(
        Guid columnId,
        CreateTaskDto dto,
        Guid userId)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            return (null, CreateTaskResult.InvalidTitle);
        }

        var column = await _context.BoardColumns
            .Include(c => c.Project)
                .ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(c => c.Id == columnId);

        if (column == null)
        {
            _logger.LogWarning("Column {ColumnId} not found", columnId);
            return (null, CreateTaskResult.ColumnNotFound);
        }

        var projectMembers = column.Project.Members;

        if (!projectMembers.Any(m => m.UserId == userId))
        {
            _logger.LogWarning(
                "User {UserId} attempted to create a task in column {ColumnId} without project membership",
                userId, columnId);
            return (null, CreateTaskResult.UserNotProjectMember);
        }

        if (dto.AssignedId.HasValue)
        {
            var assignedId = dto.AssignedId.Value;

            var assignedUserExists = await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == assignedId);

            if (!assignedUserExists)
            {
                _logger.LogWarning("Assigned user {AssignedId} not found", assignedId);
                return (null, CreateTaskResult.AssignedUserNotFound);
            }

            if (!projectMembers.Any(m => m.UserId == assignedId))
            {
                _logger.LogWarning(
                    "Assigned user {AssignedId} is not a member of project {ProjectId}",
                    assignedId, column.ProjectId);
                return (null, CreateTaskResult.AssignedUserNotProjectMember);
            }
        }

        var maxOrder = await _context.KanbanTasks
            .Where(t => t.ColumnId == columnId)
            .MaxAsync(t => (int?)t.TaskOrder);

        var taskOrder = (maxOrder ?? -1) + 1;

        var task = new KanbanTask
        {
            Title = dto.Title,
            Content = dto.Content,
            TaskOrder = taskOrder,
            ColumnId = columnId,
            AssignedId = dto.AssignedId
        };

        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} created task {TaskId} in column {ColumnId} at order {TaskOrder}",
            userId, task.Id, columnId, task.TaskOrder);

        return (MapToDto(task), CreateTaskResult.Success);
    }

    private static TaskResponseDto MapToDto(KanbanTask task) =>
        new()
        {
            Id = task.Id.ToString(),
            Title = task.Title,
            Content = task.Content,
            TaskOrder = task.TaskOrder,
            ColumnId = task.ColumnId.ToString(),
            AssignedId = task.AssignedId?.ToString(),
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
}
```

### Step 7 — Create `TaskController`

**File:** `KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs`
Use the exact skeleton from §4.4.

### Step 8 — Register `TaskService` in DI

**File:** `KanbAI-Core/KanbAI-Core/Program.cs`

1. Add a `using` directive at the top: `using KanbAI_Core.Services.Tasks;`.
2. Register the scoped service **immediately after** the `IColumnService` registration (line 18):

```csharp
builder.Services.AddScoped<ITaskService, TaskService>();
```

**Lifetime rationale:** `Scoped` matches `ApplicationDbContext` and all other services in this application (per `@rule_code_standards` — DbContext per request).

### Step 9 — Build & Verify

```bash
cd KanbAI-Core/KanbAI-Core
dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If warnings appear, do **not** suppress — fix them per `@rule_code_standards`.

### Step 10 — Smoke Run (Optional)

Run the app (`dotnet run`), open Scalar UI in Development, authenticate, and manually `POST /api/Task/column/{columnId}` to confirm the happy path persists to the database. This is not a substitute for automated tests — it is a quick sanity check before handing off to QA.

---

## 6. QA Guidance for @agent_tester_qa

### 6.1 Test File Locations

| Test File | Location | Type | Purpose |
|-----------|----------|------|---------|
| `TaskServiceTests.cs` | `KanbAI-Core.Tests/Services/Tasks/` | Unit | Business logic with EF Core in-memory DbContext |
| `TaskControllerTests.cs` | `KanbAI-Core.Tests/Controllers/` | Unit | HTTP response mapping; claims extraction; DTO-level validation |
| `TaskApiIntegrationTests.cs` | `KanbAI-Core.Tests/Integration/` | Integration | HTTP-level auth + validation using `WebApplicationFactory<Program>` |

### 6.2 Test Case Tables

#### TaskServiceTests.cs (Unit)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 1 | `CreateTaskAsync_UserIsMember_EmptyColumn_CreatesTaskWithOrderZero` | Ordering | First task in column gets `TaskOrder = 0` |
| 2 | `CreateTaskAsync_UserIsMember_NonEmptyColumn_TaskOrderIsMaxPlusOne` | Ordering | Subsequent task gets `max + 1` |
| 3 | `CreateTaskAsync_MultipleSequentialCreations_AssignsDistinctOrders` | Ordering | N calls → orders 0..N-1 |
| 4 | `CreateTaskAsync_UserIsMember_NoAssignment_PersistsWithNullAssignedId` | Happy path | Unassigned task persists correctly |
| 5 | `CreateTaskAsync_UserIsMember_ValidAssignment_PersistsWithAssignedId` | Happy path | Assigned task persists with correct FK |
| 6 | `CreateTaskAsync_ColumnDoesNotExist_ReturnsColumnNotFound` | 404 path | `CreateTaskResult.ColumnNotFound` |
| 7 | `CreateTaskAsync_UserNotProjectMember_ReturnsUserNotProjectMember` | 403 path | Non-member caller |
| 8 | `CreateTaskAsync_AssignedUserDoesNotExist_ReturnsAssignedUserNotFound` | 400 path | Random Guid for `AssignedId` |
| 9 | `CreateTaskAsync_AssignedUserIsNotProjectMember_ReturnsAssignedUserNotProjectMember` | 400 path | User exists but not in project |
| 10 | `CreateTaskAsync_WhitespaceTitle_ReturnsInvalidTitle` | Validation | `"   "` title |
| 11 | `CreateTaskAsync_EmptyContent_PersistsWithNullContent` | Edge case | `Content = null` is accepted |
| 12 | `CreateTaskAsync_FailureResultPath_DoesNotPersistEntity` | Safety | After any non-Success result, DB contains no new row |

#### TaskControllerTests.cs (Unit — mocked service)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 13 | `CreateTask_ServiceReturnsSuccess_Returns201CreatedWithLocationHeader` | HTTP | 201 + `Location` header present |
| 14 | `CreateTask_ServiceReturnsColumnNotFound_Returns404` | HTTP | NotFound + `ApiResponse.Fail("Column not found.")` |
| 15 | `CreateTask_ServiceReturnsUserNotProjectMember_Returns403` | HTTP | `StatusCode(403)` + `ApiResponse.Fail(...)` |
| 16 | `CreateTask_ServiceReturnsAssignedUserNotFound_Returns400` | HTTP | BadRequest body matches spec |
| 17 | `CreateTask_ServiceReturnsAssignedUserNotProjectMember_Returns400` | HTTP | BadRequest body matches spec |
| 18 | `CreateTask_ServiceReturnsInvalidTitle_Returns400` | HTTP | BadRequest for whitespace title |
| 19 | `CreateTask_MissingNameIdentifierClaim_ThrowsUnauthorizedAccessException` | Security | Claims extraction |
| 20 | `CreateTaskDto_MissingTitle_FailsDataAnnotationsValidation` | Validation | `Validator.TryValidateObject` rejects missing `Title` |
| 21 | `CreateTaskDto_TitleExceeds200Chars_FailsDataAnnotationsValidation` | Validation | 201-char title rejected |

#### TaskApiIntegrationTests.cs (Integration)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 22 | `PostTask_Unauthenticated_Returns401` | Security | `[Authorize]` enforcement |
| 23 | `PostTask_MissingTitleInBody_Returns400` | Validation | `[ApiController]` rejects missing required field |
| 24 | `PostTask_TitleTooLong_Returns400` | Validation | `MaxLength(200)` enforced |

### 6.3 Test Infrastructure

- **Unit tests (service):** EF Core In-Memory provider (`Microsoft.EntityFrameworkCore.InMemory`). Seed `Project`, `ProjectMember`, `BoardColumn`, and `User` rows in `Arrange`. Use `Guid.NewGuid()` for all keys — do not hard-code.
- **Unit tests (controller):** Mock `ITaskService` (e.g., `Mock<ITaskService>`), construct `ClaimsPrincipal` with a `NameIdentifier` claim, assign via `ControllerContext`.
- **Integration tests:** Follow `@rule_integration_testing` exactly — register `TestAuthHandler`, remove Negotiate auth config, set `FallbackPolicy = null`. Use only HTTP-level assertions (do not seed/query the DB directly; that is what unit tests cover — see Column spec §10.4 Bug #3).
- **Naming:** strictly `MethodName_StateUnderTest_ExpectedBehavior` per `@rule_testing_observability`.
- **AAA layout:** blank lines between Arrange/Act/Assert.

---

## 7. Known Caveats

| # | Caveat | Impact | Mitigation |
|---|--------|--------|-----------|
| 1 | **403 vs 404 divergence from ColumnService pattern** | Slightly reveals column existence to callers who know valid `columnId`s but aren't project members. | Accepted per AC #"returns 403 Forbidden (not 404)". `columnId` is a Guid → enumeration risk is negligible. Document in Caveats so future consistency reviews know this is intentional. |
| 2 | **No update endpoint** | A task created with wrong title/assignment cannot be fixed via API. | Out of scope. Tracked for future issue. |
| 3 | **No unique constraint on `TaskOrder`** | Two concurrent POSTs to the same column could compute the same `max+1`. | Race window is small and the outcome is still visually tolerable (two tasks with the same order — `OrderBy` is stable). Documented for the Move-Task issue #47, which will introduce proper reordering logic. No fix in this issue. |
| 4 | **`Location` header points back at the POST route** | `CreatedAtAction(nameof(CreateTask), new { columnId }, ...)` produces `Location: /api/Task/column/{columnId}` (the same POST route) because no GET-by-id exists yet. | Same trade-off as `ColumnController` (see issue #45 spec §7). Future issue will add `GET /api/Task/{id}` and update the Location. |
| 5 | **Whitespace title check is duplicated** | `[Required]` rejects null/empty; service rejects whitespace-only. Two layers doing similar work. | Intentional: DataAnnotations + `[ApiController]` auto-produces `ValidationProblemDetails` for null/empty (helpful for clients); service-layer check catches whitespace and returns the project's standard `ApiResponse.Fail(...)` envelope. |
| 6 | **In-memory provider and `.MaxAsync` nullable projection** | EF Core InMemory supports the `(int?)` cast pattern used for `maxOrder`, but behavior varies across providers. | Verified against existing `ColumnService.ComputeNextColumnOrderAsync` which uses the identical pattern in production SQL Server without issue. |
| 7 | **`column.Project` navigation is required-loaded** | If `BoardColumn.Project` were `null` (e.g., orphan row), `.ThenInclude(p => p.Members)` would NRE. | `BoardColumnConfiguration` enforces `ProjectId` as a required FK with cascade delete — orphans are impossible under the current schema. |
| 8 | **TestServer infra** | `WebApplicationFactory` + Negotiate auth has known limitations (see `@rule_integration_testing`). | Integration tests must use the `TestAuthHandler` pattern — same as Column tests. |

---

## 8. Design Validation (Self-Check)

| Check | Question | Status |
|-------|----------|--------|
| **Namespaces** | Do all referenced namespaces match `RootNamespace = KanbAI_Core` and existing conventions? | ✅ Pass — `KanbAI_Core.Controllers`, `KanbAI_Core.DTOs`, `KanbAI_Core.Services.Tasks` |
| **Folder Paths** | Do all file paths reference real folders, or are "create new" steps included? | ✅ Pass — `Services/Tasks/` creation handled by Step 3; `Controllers/` and `DTOs/` already exist |
| **Dependencies** | Are all required NuGet packages already in the `.csproj`, or are install steps included? | ✅ Pass — `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `System.ComponentModel.DataAnnotations` all present; no new packages |
| **Naming Conflicts** | Do any new class/enum names conflict with existing types? | ✅ Pass — `TaskController`, `TaskService`, `ITaskService`, `CreateTaskResult`, `CreateTaskDto`, `TaskResponseDto` do **not** clash with `System.Threading.Tasks.Task` in usage (we always return `Task<...>` and reference the entity as `KanbanTask`, avoiding ambiguity). Note: if the developer encounters a `Task` ambiguity in DI or DbSet usage, fully-qualify as `KanbAI_Core.Models.Entities.KanbanTask`. |
| **BaseEntity Compliance** | Do new entities inherit from `BaseEntity`? | ✅ N/A — no new entities; `KanbanTask` already inherits from `BaseEntity`. |
| **Code Standards** | File-scoped namespaces, no blocking async, constructor injection, `AsNoTracking` on reads? | ✅ Pass — verified in spec sections 4 & 5 |
| **Security** | No hardcoded secrets; no PII in logs; authorization enforced; mass-assignment prevented via DTO; input validation on DTO + service? | ✅ Pass — only `userId`/`taskId`/`columnId` (GUIDs) in logs; no `Email`, `Name`, `PasswordHash`; DTOs bound, not entities |

**Result:** All checks pass. Design is ready for implementation.

---

**Document Status:** Ready for Implementation
**Last Updated:** 2026-04-27
**Next Steps:** @agent_developer should read this tech spec and begin implementation following Section 5 strictly.

---

## 9. Development Status

**Implemented by:** @agent_developer
**Date:** 2026-04-27
**Status:** ✅ Complete — build passes, test suite green

### 9.1 Files Created

| File | Purpose |
|------|---------|
| `KanbAI-Core/KanbAI-Core/DTOs/CreateTaskDto.cs` | Request DTO with `[Required]` + `[MaxLength(200)]` on `Title`; optional `Content`, optional `AssignedId`. |
| `KanbAI-Core/KanbAI-Core/DTOs/TaskResponseDto.cs` | Response DTO: stringified `Id`/`ColumnId`/`AssignedId`, timestamps from `BaseEntity`. |
| `KanbAI-Core/KanbAI-Core/Services/Tasks/CreateTaskResult.cs` | Discriminator enum: `Success`, `ColumnNotFound`, `UserNotProjectMember`, `AssignedUserNotFound`, `AssignedUserNotProjectMember`, `InvalidTitle`. |
| `KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs` | Service contract — single `CreateTaskAsync` method returning `(TaskResponseDto?, CreateTaskResult)`. |
| `KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs` | Implementation per §4.3 algorithm. Single column-load round-trip with `.Include(c => c.Project).ThenInclude(p => p.Members)`. Reuses loaded `Members` collection to validate `AssignedId` membership (no extra DB call). `.AsNoTracking()` on user existence check. `MaxAsync((int?)TaskOrder)` pattern mirrors `ColumnService.ComputeNextColumnOrderAsync`. |
| `KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs` | `[ApiController]`/`[Authorize]`, one endpoint `POST /api/Task/column/{columnId}`. Pattern-match switch maps `CreateTaskResult` → HTTP response. `GetCurrentUserId()` copied verbatim from `ColumnController`. |

### 9.2 Files Modified

| File | Change |
|------|--------|
| `KanbAI-Core/KanbAI-Core/Program.cs` | Added `using KanbAI_Core.Services.Tasks;` and `builder.Services.AddScoped<ITaskService, TaskService>();` immediately after the `IColumnService` registration. |

### 9.3 Files NOT Modified (Intentional, per spec §1 "Out of Scope")

- `Models/Entities/KanbanTask.cs` — already correct.
- `Data/Configurations/KanbanTaskConfiguration.cs` — already correct.
- `Data/ApplicationDbContext.cs` — `KanbanTasks` DbSet already exists.
- No new migration was generated — zero schema changes.

### 9.4 Build & Test Results

**Build (main project):**
```
dotnet build --no-incremental
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:10.67
```

**Test suite (solution-wide):**
```
dotnet test --verbosity quiet
Passed!  - Failed:     0, Passed:   311, Skipped:     2, Total:   313, Duration: 4 s
```

- No new tests were added in this phase — automated tests are QA's responsibility per the workflow.
- All 311 pre-existing tests continue to pass.
- The 2 skipped tests are pre-existing and unrelated to this change.

### 9.5 Infrastructure Notes

None. The implementation is pure application/API-layer code — no tooling, no migrations, no config changes beyond the single DI line in `Program.cs`.

### 9.6 Edge Cases for @agent_tester_qa

The QA tester should pay particular attention to the following behaviours, which are easy to regress:

1. **`TaskOrder` computation on empty column** — `maxOrder` returns `null` via `(int?)` projection, which collapses to `-1 + 1 = 0`. Verify the first task in a fresh column gets `TaskOrder = 0` (not `1`).
2. **Whitespace-only Title** — rejected by the explicit `string.IsNullOrWhiteSpace` check in the service (NOT by `[Required]`). Make sure the integration-level test for whitespace titles asserts the service-layer path (returns `CreateTaskResult.InvalidTitle` → 400 with `"Task title is required."` body), distinct from the framework `ValidationProblemDetails` returned for missing/empty titles.
3. **403 vs 404 divergence** — This endpoint deliberately returns **403** when the caller isn't a project member (see §7 Caveat #1), unlike `ColumnController` which returns 404. Assertions must pin this to 403 exactly.
4. **Assigned user dual-check** — Two distinct 400 paths: `AssignedUserNotFound` (user doesn't exist at all) vs `AssignedUserNotProjectMember` (user exists but isn't in this project). Both return 400 but with different messages; tests should assert on the message string.
5. **Reusing the loaded `Members` collection** — the service validates the assigned user's membership against `column.Project.Members` (already materialized), NOT via a fresh query. Unit tests with the in-memory provider must set up `ProjectMember` rows before the column is loaded — not after — or the check will miss them.
6. **`CreatedAtAction` Location header** — points back at the same POST route (`/api/Task/column/{columnId}`) because no GET-by-id exists yet. Tests should assert the header is present but not be strict about the URL format (see §7 Caveat #4).
7. **Tracking semantics** — the column-load query is intentionally NOT `.AsNoTracking()` (see §2.2 "Read Optimization"). If QA writes a test that asserts zero entries in `ChangeTracker.Entries()` after `CreateTaskAsync`, it will fail. This is expected.
8. **Race on concurrent POSTs** — `max(TaskOrder) + 1` is not transactional. Two parallel POSTs to the same column can produce duplicate orders. Documented as Caveat #3; do not write a test that fails on this (it's a known limitation tracked for issue #47).

### 9.7 Self-Validation

All checks from Step 5 of the developer workflow pass:

| Check | Status |
|-------|--------|
| File placement matches spec | ✅ |
| Namespaces match `RootNamespace = KanbAI_Core` | ✅ |
| File-scoped namespaces used everywhere | ✅ |
| Constructor injection, async-only, no `.Result`/`.Wait()` | ✅ |
| `.AsNoTracking()` on user-existence check | ✅ |
| N+1 prevented via `.Include`/`.ThenInclude` | ✅ |
| No hardcoded secrets, no PII in logs (only GUIDs) | ✅ |
| DTO-bound (mass-assignment prevention) | ✅ |
| Tech spec fidelity — every file listed in §5 implemented verbatim | ✅ |
| No extras beyond the spec | ✅ |
| No schema / migration changes | ✅ (per §1) |

---

## 10. QA Status

**Tested by:** @agent_tester_qa
**Date:** 2026-04-27
**Status:** ✅ Complete — all 29 new tests pass, no regressions

### 10.1 Test Files Created

| File | Type | Tests | Coverage |
|------|------|-------|----------|
| [KanbAI-Core/KanbAI-Core.Tests/Services/Tasks/TaskServiceTests.cs](../../KanbAI-Core/KanbAI-Core.Tests/Services/Tasks/TaskServiceTests.cs) | Unit (EF Core In-Memory) | 14 | Full `CreateTaskAsync` algorithm: ordering (0, max+1, sequential), happy paths (unassigned/assigned), four failure discriminators (ColumnNotFound / UserNotProjectMember / AssignedUserNotFound / AssignedUserNotProjectMember), whitespace-title theory, null content, multi-failure no-persist safety |
| [KanbAI-Core/KanbAI-Core.Tests/Controllers/TaskControllerTests.cs](../../KanbAI-Core/KanbAI-Core.Tests/Controllers/TaskControllerTests.cs) | Unit (mocked `ITaskService`) | 11 | All six `CreateTaskResult` → HTTP mappings, `CreatedAtAction` Location routing, missing claim + invalid claim `UnauthorizedAccessException`, DTO DataAnnotations validation (missing/too-long/boundary Title) |
| [KanbAI-Core/KanbAI-Core.Tests/Integration/TaskApiIntegrationTests.cs](../../KanbAI-Core/KanbAI-Core.Tests/Integration/TaskApiIntegrationTests.cs) | Integration (`WebApplicationFactory<Program>`) | 4 | `[Authorize]` enforcement (401), `[ApiController]` DTO validation (missing / empty / too-long Title → 400) |

### 10.2 Test Results

```
dotnet test --filter "FullyQualifiedName~TaskServiceTests|FullyQualifiedName~TaskControllerTests|FullyQualifiedName~TaskApiIntegrationTests"
Passed!  - Failed:     0, Passed:    29, Skipped:     0, Total:    29, Duration: 2 s

dotnet test (solution-wide)
Total tests: 342
     Passed: 340
    Skipped: 2 (pre-existing, unrelated)
Total time: 7.2 Seconds
```

- **29 new tests added** (311 → 340 passing).
- **Zero regressions** in the pre-existing 311-test suite.
- **Zero build warnings**, zero errors.

### 10.3 Acceptance Criteria Coverage

Every AC from `issue_46_context.md` is validated by at least one test:

| Acceptance Criterion | Covered By |
|---------------------|-----------|
| POST endpoint exists at `/api/Task/column/{columnId}` | `PostTask_*` integration suite |
| Endpoint requires valid JWT | `PostTask_Unauthenticated_Returns401` |
| Accepts minimum Title in body | `CreateTaskAsync_UserIsMember_EmptyColumn_CreatesTaskWithOrderZero` |
| Accepts optional Content / AssignedId | `CreateTaskAsync_EmptyContent_PersistsWithNullContent`, `CreateTaskAsync_UserIsMember_ValidAssignment_PersistsWithAssignedId` |
| 403 when caller not a project member | `CreateTaskAsync_UserNotProjectMember_ReturnsUserNotProjectMember` + `CreateTask_ServiceReturnsUserNotProjectMember_Returns403` |
| 404 when column does not exist | `CreateTaskAsync_ColumnDoesNotExist_ReturnsColumnNotFound` + `CreateTask_ServiceReturnsColumnNotFound_Returns404` |
| 400 when Title missing or empty | `PostTask_MissingTitleInBody_Returns400`, `PostTask_EmptyTitleInBody_Returns400`, `CreateTaskDto_MissingTitle_FailsDataAnnotationsValidation` |
| 400 when Title whitespace-only | `CreateTaskAsync_WhitespaceTitle_ReturnsInvalidTitle` theory (`"   "`, `"\t"`, `"\n"`) + `CreateTask_ServiceReturnsInvalidTitle_Returns400` |
| TaskOrder = 0 in empty column | `CreateTaskAsync_UserIsMember_EmptyColumn_CreatesTaskWithOrderZero` |
| TaskOrder = max + 1 in populated column | `CreateTaskAsync_UserIsMember_NonEmptyColumn_TaskOrderIsMaxPlusOne` |
| Sequential creations get distinct orders | `CreateTaskAsync_MultipleSequentialCreations_AssignsDistinctOrders` |
| 201 Created with task details and Location header | `CreateTask_ServiceReturnsSuccess_Returns201CreatedWithLocationHeader` |
| CreatedAt / UpdatedAt timestamps set by BaseEntity | Verified via `TaskResponseDto` assertions on success tests |
| AssignedId: valid member → success | `CreateTaskAsync_UserIsMember_ValidAssignment_PersistsWithAssignedId` |
| AssignedId: existing user but not member → 400 | `CreateTaskAsync_AssignedUserIsNotProjectMember_ReturnsAssignedUserNotProjectMember` + `CreateTask_ServiceReturnsAssignedUserNotProjectMember_Returns400` |
| AssignedId: non-existent user → 400 | `CreateTaskAsync_AssignedUserDoesNotExist_ReturnsAssignedUserNotFound` + `CreateTask_ServiceReturnsAssignedUserNotFound_Returns400` |
| AssignedId omitted/null → success unassigned | `CreateTaskAsync_UserIsMember_NoAssignment_PersistsWithNullAssignedId` |
| 403 vs 404 divergence (not collapsed to 404) | `CreateTask_ServiceReturnsUserNotProjectMember_Returns403` asserts `StatusCodes.Status403Forbidden` exactly |
| Title > 200 chars rejected | `CreateTaskDto_TitleExceeds200Chars_FailsDataAnnotationsValidation`, `PostTask_TitleTooLong_Returns400` |

### 10.4 Bugs Found & Fixed

**None.** Implementation matched the tech spec precisely, and every test passed on first run. No production-code changes were required.

### 10.5 Edge-Case Handling Verified

Per §9.6 ("Edge Cases for @agent_tester_qa"), I verified each of the eight callouts:

1. **TaskOrder on empty column = 0** — Confirmed by `CreateTaskAsync_UserIsMember_EmptyColumn_CreatesTaskWithOrderZero`.
2. **Whitespace title rejected by service (not DataAnnotations)** — Theory covers `"   "`, `"\t"`, `"\n"`; `CreateTask_ServiceReturnsInvalidTitle_Returns400` confirms the 400 + `"Task title is required."` message from the service path, distinct from the framework `ValidationProblemDetails` path verified in `PostTask_EmptyTitleInBody_Returns400`.
3. **403 vs 404 divergence pinned** — `CreateTask_ServiceReturnsUserNotProjectMember_Returns403` asserts status code equals `StatusCodes.Status403Forbidden`.
4. **Two 400 paths distinguished by message** — `AssignedUserNotFound` asserts `"Assigned user not found."`; `AssignedUserNotProjectMember` asserts `"Assigned user is not a member of this project."`.
5. **Reuses loaded Members collection** — Seed order in `SeedProjectWithColumnAsync` inserts members **before** the column-load query runs in the service, matching the tech spec's warning.
6. **Location header** — Assertion checks presence + `RouteValues["columnId"]` matches the response `ColumnId`, without pinning the exact URL format (per Caveat #4).
7. **Tracking semantics** — No test asserts on `ChangeTracker.Entries()`; only persisted entity state is verified.
8. **Concurrent POST race** — Not tested; acknowledged as known limitation per Caveat #3 (to be fixed in Issue #47).

### 10.6 Outstanding Issues

**None.** All acceptance criteria are covered, all tests pass, and no bugs were discovered. The implementation is ready for merge.

---

QA testing is complete. All tests pass and the implementation meets the acceptance criteria.

