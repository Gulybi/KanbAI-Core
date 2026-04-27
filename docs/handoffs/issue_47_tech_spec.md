# Technical Specification: Issue #47 - Implement Task Movement Logic (Drag-and-Drop Backend Support)

**GitHub Issue:** [#47 - Implement Task Movement Logic (Drag-and-Drop Backend Support)](https://github.com/Gulybi/KanbAI-Core/issues/47)
**Context Document:** [issue_47_context.md](./issue_47_context.md)
**Staff Engineer:** @agent_staff_engineer
**Created:** 2026-04-27

---

## 1. Overview

This specification defines the implementation of a Task Movement API that enables users to reorder tasks within a single column or move tasks between different columns through drag-and-drop interactions. The implementation extends the existing `TaskService` and `TaskController` (introduced in Issue #46) while leveraging the existing `KanbanTask` domain entity.

**Scope:**
- Extend `ITaskService` with a new `MoveTaskAsync` method
- Extend `TaskController` with a new `PUT /api/Task/{taskId}/move` endpoint
- Create one new DTO: `MoveTaskDto` (request)
- Implement automatic TaskOrder recalculation for all affected tasks (same-column reorder or cross-column move)
- Validate that both source and target columns belong to the same project
- Reuse the existing project membership authorization pattern established in Issue #46

**Out of Scope:**
- No database schema changes, entity changes, or EF Core configuration changes. All required entities (`KanbanTask`, `BoardColumn`, `Project`, `ProjectMember`) and their configurations already exist.
- No bulk task movement (moving multiple tasks in a single request)
- No task history/audit log (deferred to future work)
- No undo/redo functionality
- No optimistic concurrency control (last write wins)
- No column-level task count limits (WIP limits)
- No task movement notifications or real-time updates

**Why No Database Changes:**
The `KanbanTask` entity already contains all required properties: `TaskOrder` (int), `ColumnId` (Guid foreign key). This issue is purely an application-layer/API addition that manipulates existing data.

**Design Note - TaskOrder Recalculation:**
This specification requires atomic, transactional updates to TaskOrder values across multiple tasks. All updates must occur within a single `SaveChangesAsync()` call to ensure consistency. The context note's Acceptance Criteria explicitly require that "all tasks in both the source and target columns have sequential TaskOrder values with no gaps (0, 1, 2, 3, ...)" after each move operation.

---

## 2. Database / Domain Design

### 2.1 Existing Entities (No Changes Required)

All required entities already exist. This section documents them for reference.

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

**Key Property for This Issue:**
- `TaskOrder` (int) - Determines the task's vertical position within its column. Values should be sequential (0, 1, 2, ...) with no gaps after any move operation.

#### BoardColumn Entity

**File:** `KanbAI-Core/KanbAI-Core/Models/Entities/BoardColumn.cs`

| Property | Type | Constraints | Source |
|----------|------|-------------|--------|
| `Id` | `Guid` | Primary key | Inherited from `BaseEntity` |
| `Name` | `string` | Required, max 100 chars | Direct property |
| `ProjectId` | `Guid` | Foreign key to `Project` | Direct property |
| `Project` | `Project` | Navigation | Cascade delete |
| `Tasks` | `ICollection<KanbanTask>` | Navigation | Cascade delete |

**Key Property for This Issue:**
- `ProjectId` - Used to validate that both source and target columns belong to the same project (cross-project moves are not allowed).

### 2.2 Expected Database Queries (EF Core)

The service will execute the following queries:

| Operation | Query Pattern | Purpose |
|-----------|---------------|---------|
| **Load task with column/project/members** | `SELECT * FROM KanbanTasks kt JOIN BoardColumns bc JOIN Projects p JOIN ProjectMembers pm WHERE kt.Id = @taskId` | Combined existence check + authorization + project membership in one round-trip |
| **Load target column with project** | `SELECT * FROM BoardColumns bc JOIN Projects p WHERE bc.Id = @targetColumnId` | Validate target column exists and get ProjectId for cross-project check |
| **Load affected tasks in source column** | `SELECT * FROM KanbanTasks WHERE ColumnId = @sourceColumnId AND TaskOrder > @oldOrder` | For cross-column moves: tasks below the moved task need TaskOrder decremented by 1 |
| **Load affected tasks in target column** | `SELECT * FROM KanbanTasks WHERE ColumnId = @targetColumnId AND TaskOrder >= @newOrder` | For cross-column moves: tasks at or above insertion point need TaskOrder incremented by 1 |
| **Load tasks between old and new positions (same column)** | `SELECT * FROM KanbanTasks WHERE ColumnId = @columnId AND TaskOrder BETWEEN @min AND @max` | For same-column reorder: tasks between old and new positions need TaskOrder adjusted |
| **Update task ColumnId and TaskOrder** | `UPDATE KanbanTasks SET ColumnId = @newColumnId, TaskOrder = @newOrder WHERE Id = @taskId` | Move the task to its new location |
| **Bulk update TaskOrder values** | Multiple `UPDATE KanbanTasks SET TaskOrder = @newValue WHERE Id = @id` | Recalculate order for all affected tasks (EF Core will batch these in `SaveChangesAsync()`) |

**N+1 Prevention:**
- Initial task load uses `.Include(t => t.Column).ThenInclude(c => c.Project).ThenInclude(p => p.Members)` to load task, column, project, and members in a single query.
- Target column load uses `.Include(c => c.Project)` for cross-project validation.

**Read Optimization:**
- The initial task load and target column load are **tracked** queries (no `.AsNoTracking()`) because we will mutate the task entity (change `ColumnId` and `TaskOrder`).
- Queries for affected tasks in source/target columns are also tracked so EF Core can track TaskOrder changes.

**Transaction Semantics:**
All TaskOrder updates (source column, target column, and the moved task itself) must occur within a single `SaveChangesAsync()` call to ensure atomicity. EF Core's change tracker will manage this automatically - no explicit transaction block required.

---

## 3. API Contracts

### 3.1 Endpoint Summary

| Method | Route | Auth | Request Body | Response DTO | Success | Failure |
|--------|-------|------|--------------|--------------|---------|---------|
| PUT | `/api/Task/{taskId}/move` | Required | `MoveTaskDto` | `ApiResponse<TaskResponseDto>` | 200 OK | 400, 401, 403, 404 |

### 3.2 DTO Definitions

#### MoveTaskDto (Request)

**File:** `KanbAI-Core/KanbAI-Core/DTOs/MoveTaskDto.cs`

```csharp
namespace KanbAI_Core.DTOs;

using System.ComponentModel.DataAnnotations;

public record MoveTaskDto
{
    [Required(ErrorMessage = "Target column ID is required.")]
    public required Guid ColumnId { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "Task order must be non-negative.")]
    public required int TaskOrder { get; init; }
}
```

**Validation Rules:**
- `ColumnId`: Required (the new column to move the task to; can be the same as the current column for reordering)
- `TaskOrder`: Required, must be >= 0 (the new position within the target column; 0-based index)
- **Additional service-layer validation:** `TaskOrder` must be within the valid range:
  - If moving to a different column: `0 <= TaskOrder <= (target column task count)`
  - If reordering within the same column: `0 <= TaskOrder <= (current column task count - 1)`

**Note:** We intentionally do NOT validate the upper bound at the DTO level because it requires database access. The service layer will perform this check.

### 3.3 Example API Interactions

#### Example 1 - Move Task to Different Column (Success)

**Scenario:** Task is currently in column A at position 2 (TaskOrder=2). We move it to column B at position 1 (TaskOrder=1).

**Request:**
```http
PUT /api/Task/a1b2c3d4-e5f6-7890-abcd-ef1234567890/move HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Content-Type: application/json

{
  "columnId": "c5d6e7f8-g9h0-i1j2-k3l4-m5n6o7p8q9r0",
  "taskOrder": 1
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "message": "Task moved successfully.",
  "data": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "title": "Implement login API",
    "content": "Add JWT authentication endpoint",
    "taskOrder": 1,
    "columnId": "c5d6e7f8-g9h0-i1j2-k3l4-m5n6o7p8q9r0",
    "assignedId": "77777777-7777-7777-7777-777777777777",
    "createdAt": "2026-04-27T10:00:00Z",
    "updatedAt": "2026-04-27T14:30:00Z"
  },
  "errors": []
}
```

#### Example 2 - Reorder Task Within Same Column (Success)

**Scenario:** Task is currently at position 5 in column A. We move it to position 2 in the same column (prioritize it).

**Request:**
```http
PUT /api/Task/a1b2c3d4-e5f6-7890-abcd-ef1234567890/move HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Content-Type: application/json

{
  "columnId": "c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o",
  "taskOrder": 2
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "message": "Task moved successfully.",
  "data": {
    "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "title": "Implement login API",
    "content": "Add JWT authentication endpoint",
    "taskOrder": 2,
    "columnId": "c1a2b3c4-5d6e-7f8g-9h0i-1j2k3l4m5n6o",
    "assignedId": null,
    "createdAt": "2026-04-27T10:00:00Z",
    "updatedAt": "2026-04-27T14:35:00Z"
  },
  "errors": []
}
```

#### Example 3 - Invalid TaskOrder (Out of Range)

**Scenario:** Target column has 3 tasks (TaskOrder 0, 1, 2). User tries to move a task from another column to position 5.

**Request:**
```http
PUT /api/Task/a1b2c3d4-e5f6-7890-abcd-ef1234567890/move HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Content-Type: application/json

{
  "columnId": "c5d6e7f8-g9h0-i1j2-k3l4-m5n6o7p8q9r0",
  "taskOrder": 5
}
```

**Response (400 Bad Request):**
```json
{
  "success": false,
  "message": "TaskOrder exceeds the number of tasks in the target column.",
  "errors": []
}
```

#### Example 4 - Negative TaskOrder

**Request:**
```http
PUT /api/Task/a1b2c3d4-e5f6-7890-abcd-ef1234567890/move HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Content-Type: application/json

{
  "columnId": "c5d6e7f8-g9h0-i1j2-k3l4-m5n6o7p8q9r0",
  "taskOrder": -1
}
```

**Response (400 Bad Request):**
```json
{
  "success": false,
  "message": "TaskOrder cannot be negative.",
  "errors": []
}
```

#### Example 5 - Cross-Project Move Attempt

**Scenario:** Task is in column A (project X). User tries to move it to column B (project Y).

**Request:**
```http
PUT /api/Task/a1b2c3d4-e5f6-7890-abcd-ef1234567890/move HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Content-Type: application/json

{
  "columnId": "z9y8x7w6-5v4u-3t2s-1r0q-p9o8n7m6l5k4",
  "taskOrder": 0
}
```

**Response (400 Bad Request):**
```json
{
  "success": false,
  "message": "Cannot move task to a column in a different project.",
  "errors": []
}
```

#### Example 6 - Task Not Found

**Request:**
```http
PUT /api/Task/00000000-0000-0000-0000-000000000000/move HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Content-Type: application/json

{
  "columnId": "c5d6e7f8-g9h0-i1j2-k3l4-m5n6o7p8q9r0",
  "taskOrder": 0
}
```

**Response (404 Not Found):**
```json
{
  "success": false,
  "message": "Task not found.",
  "errors": []
}
```

#### Example 7 - User Not a Project Member

**Scenario:** User is authenticated but not a member of the project that owns the task's current column.

**Response (403 Forbidden):**
```json
{
  "success": false,
  "message": "You are not a member of this project.",
  "errors": []
}
```

#### Example 8 - Target Column Not Found

**Request:**
```http
PUT /api/Task/a1b2c3d4-e5f6-7890-abcd-ef1234567890/move HTTP/1.1
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
Content-Type: application/json

{
  "columnId": "00000000-0000-0000-0000-000000000000",
  "taskOrder": 0
}
```

**Response (404 Not Found):**
```json
{
  "success": false,
  "message": "Target column not found.",
  "errors": []
}
```

---

## 4. Application Layer Boundaries

### 4.1 Result Discriminator Enum (Extension)

Extend the existing `CreateTaskResult` enum to support the move operation. Because task movement has distinct failure modes (invalid TaskOrder range, cross-project move, target column not found), we need a new discriminator enum.

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/MoveTaskResult.cs` (NEW)

```csharp
namespace KanbAI_Core.Services.Tasks;

public enum MoveTaskResult
{
    Success,
    TaskNotFound,
    UserNotProjectMember,
    TargetColumnNotFound,
    CrossProjectMove,
    InvalidTaskOrder
}
```

### 4.2 ITaskService Interface (Extension)

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs` (EXTEND)

Add the following method to the existing interface:

```csharp
/// <summary>
/// Moves a task to a new column and/or reorders it within its current column.
/// Authorization: the caller must be a member of the project that owns the task's current column.
/// Validation:
/// - Both the task's current column and the target column must belong to the same project.
/// - The new TaskOrder must be within the valid range:
///   - If moving to a different column: 0 <= TaskOrder <= (target column task count)
///   - If reordering within the same column: 0 <= TaskOrder <= (current column task count - 1)
/// Automatic recalculation:
/// - If moving to a different column: TaskOrder values in both source and target columns are recalculated to remove gaps.
/// - If reordering within the same column: TaskOrder values between the old and new positions are adjusted.
/// </summary>
/// <param name="taskId">The ID of the task to move.</param>
/// <param name="dto">Move operation data (target ColumnId and TaskOrder).</param>
/// <param name="userId">The authenticated user's ID (from JWT claims).</param>
/// <returns>A tuple containing the updated task (on success) and a <see cref="MoveTaskResult"/> discriminator.</returns>
Task<(TaskResponseDto? data, MoveTaskResult result)> MoveTaskAsync(
    Guid taskId,
    MoveTaskDto dto,
    Guid userId);
```

### 4.3 TaskService Implementation (Extension)

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs` (EXTEND)

Add the `MoveTaskAsync` method to the existing `TaskService` class.

**Key Algorithm:**

1. **Validation - Negative TaskOrder:**
   ```csharp
   if (dto.TaskOrder < 0)
       return (null, MoveTaskResult.InvalidTaskOrder);
   ```

2. **Load task with column, project, and members:**
   ```csharp
   var task = await _context.KanbanTasks
       .Include(t => t.Column)
           .ThenInclude(c => c.Project)
               .ThenInclude(p => p.Members)
       .FirstOrDefaultAsync(t => t.Id == taskId);
   
   if (task == null)
   {
       _logger.LogWarning("Task {TaskId} not found", taskId);
       return (null, MoveTaskResult.TaskNotFound);
   }
   ```

3. **Authorization check:**
   ```csharp
   var isMember = task.Column.Project.Members.Any(m => m.UserId == userId);
   if (!isMember)
   {
       _logger.LogWarning("User {UserId} attempted to move task {TaskId} without project membership", userId, taskId);
       return (null, MoveTaskResult.UserNotProjectMember);
   }
   ```

4. **Load target column (if different from current column):**
   ```csharp
   BoardColumn targetColumn;
   if (dto.ColumnId != task.ColumnId)
   {
       targetColumn = await _context.BoardColumns
           .Include(c => c.Project)
           .FirstOrDefaultAsync(c => c.Id == dto.ColumnId);
       
       if (targetColumn == null)
       {
           _logger.LogWarning("Target column {ColumnId} not found", dto.ColumnId);
           return (null, MoveTaskResult.TargetColumnNotFound);
       }
       
       // Cross-project check
       if (targetColumn.ProjectId != task.Column.ProjectId)
       {
           _logger.LogWarning("User {UserId} attempted to move task {TaskId} to a column in a different project", userId, taskId);
           return (null, MoveTaskResult.CrossProjectMove);
       }
   }
   else
   {
       targetColumn = task.Column; // Same column - reorder only
   }
   ```

5. **Validate TaskOrder range:**
   ```csharp
   int targetColumnTaskCount;
   if (dto.ColumnId != task.ColumnId)
   {
       // Moving to different column: max TaskOrder is (task count in target column)
       targetColumnTaskCount = await _context.KanbanTasks
           .CountAsync(t => t.ColumnId == dto.ColumnId);
       
       if (dto.TaskOrder > targetColumnTaskCount)
       {
           _logger.LogWarning("Invalid TaskOrder {TaskOrder} for target column {ColumnId} with {Count} tasks", 
               dto.TaskOrder, dto.ColumnId, targetColumnTaskCount);
           return (null, MoveTaskResult.InvalidTaskOrder);
       }
   }
   else
   {
       // Reordering within same column: max TaskOrder is (task count - 1)
       targetColumnTaskCount = await _context.KanbanTasks
           .CountAsync(t => t.ColumnId == task.ColumnId);
       
       if (dto.TaskOrder > targetColumnTaskCount - 1)
       {
           _logger.LogWarning("Invalid TaskOrder {TaskOrder} for same-column reorder with {Count} tasks", 
               dto.TaskOrder, targetColumnTaskCount);
           return (null, MoveTaskResult.InvalidTaskOrder);
       }
   }
   ```

6. **No-op check (moving to same position):**
   ```csharp
   if (dto.ColumnId == task.ColumnId && dto.TaskOrder == task.TaskOrder)
   {
       // No-op: task is already at the target position
       _logger.LogInformation("Task {TaskId} is already at position {TaskOrder} in column {ColumnId} - no changes needed", 
           taskId, dto.TaskOrder, dto.ColumnId);
       return (MapToDto(task), MoveTaskResult.Success);
   }
   ```

7. **Recalculate TaskOrder values:**

   **Case A: Moving to a different column**
   ```csharp
   if (dto.ColumnId != task.ColumnId)
   {
       var oldColumnId = task.ColumnId;
       var oldTaskOrder = task.TaskOrder;
       
       // 1. Decrement TaskOrder for tasks in source column that were below the moved task
       var sourceColumnTasks = await _context.KanbanTasks
           .Where(t => t.ColumnId == oldColumnId && t.TaskOrder > oldTaskOrder)
           .ToListAsync();
       
       foreach (var t in sourceColumnTasks)
       {
           t.TaskOrder -= 1;
       }
       
       // 2. Increment TaskOrder for tasks in target column at or above the insertion point
       var targetColumnTasks = await _context.KanbanTasks
           .Where(t => t.ColumnId == dto.ColumnId && t.TaskOrder >= dto.TaskOrder)
           .ToListAsync();
       
       foreach (var t in targetColumnTasks)
       {
           t.TaskOrder += 1;
       }
       
       // 3. Update the moved task
       task.ColumnId = dto.ColumnId;
       task.TaskOrder = dto.TaskOrder;
   }
   ```

   **Case B: Reordering within the same column**
   ```csharp
   else
   {
       var oldTaskOrder = task.TaskOrder;
       var newTaskOrder = dto.TaskOrder;
       
       if (newTaskOrder < oldTaskOrder)
       {
           // Moving up (e.g., from position 5 to position 2)
           // Tasks between newTaskOrder and oldTaskOrder need TaskOrder incremented by 1
           var affectedTasks = await _context.KanbanTasks
               .Where(t => t.ColumnId == task.ColumnId && t.TaskOrder >= newTaskOrder && t.TaskOrder < oldTaskOrder)
               .ToListAsync();
           
           foreach (var t in affectedTasks)
           {
               t.TaskOrder += 1;
           }
       }
       else
       {
           // Moving down (e.g., from position 2 to position 5)
           // Tasks between oldTaskOrder and newTaskOrder need TaskOrder decremented by 1
           var affectedTasks = await _context.KanbanTasks
               .Where(t => t.ColumnId == task.ColumnId && t.TaskOrder > oldTaskOrder && t.TaskOrder <= newTaskOrder)
               .ToListAsync();
           
           foreach (var t in affectedTasks)
           {
               t.TaskOrder -= 1;
           }
       }
       
       // Update the moved task
       task.TaskOrder = newTaskOrder;
   }
   ```

8. **Save changes and return:**
   ```csharp
   await _context.SaveChangesAsync();
   
   _logger.LogInformation("User {UserId} moved task {TaskId} to column {ColumnId} at order {TaskOrder}", 
       userId, taskId, task.ColumnId, task.TaskOrder);
   
   return (MapToDto(task), MoveTaskResult.Success);
   ```

**Note:** The existing `MapToDto` method can be reused (no changes needed).

### 4.4 TaskController (Extension)

**File:** `KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs` (EXTEND)

Add the following endpoint to the existing `TaskController`:

```csharp
[HttpPut("{taskId}/move")]
public async Task<IActionResult> MoveTask(Guid taskId, [FromBody] MoveTaskDto dto)
{
    var userId = GetCurrentUserId();

    var (data, result) = await _taskService.MoveTaskAsync(taskId, dto, userId);

    return result switch
    {
        MoveTaskResult.Success =>
            Ok(ApiResponse<TaskResponseDto>.Ok(data!, "Task moved successfully.")),
        MoveTaskResult.TaskNotFound =>
            NotFound(ApiResponse.Fail("Task not found.")),
        MoveTaskResult.UserNotProjectMember =>
            StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("You are not a member of this project.")),
        MoveTaskResult.TargetColumnNotFound =>
            NotFound(ApiResponse.Fail("Target column not found.")),
        MoveTaskResult.CrossProjectMove =>
            BadRequest(ApiResponse.Fail("Cannot move task to a column in a different project.")),
        MoveTaskResult.InvalidTaskOrder =>
            BadRequest(ApiResponse.Fail("TaskOrder is invalid.")),
        _ => StatusCode(StatusCodes.Status500InternalServerError,
                 ApiResponse.Fail("Unexpected error."))
    };
}
```

**Note:** The endpoint reuses the existing `GetCurrentUserId()` helper method (no changes needed).

---

## 5. Implementation Steps for @agent_developer

### Step 1 - Create the MoveTaskDto

**File:** `KanbAI-Core/KanbAI-Core/DTOs/MoveTaskDto.cs`

Create the new DTO using the exact code from Section 3.2.

### Step 2 - Create the MoveTaskResult Enum

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/MoveTaskResult.cs`

Create the new enum using the exact code from Section 4.1.

### Step 3 - Extend ITaskService Interface

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs`

Add the `MoveTaskAsync` method signature from Section 4.2. The file will now contain two methods: `CreateTaskAsync` (existing) and `MoveTaskAsync` (new).

### Step 4 - Implement MoveTaskAsync in TaskService

**File:** `KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs`

Add the `MoveTaskAsync` method to the existing `TaskService` class. Follow the algorithm in Section 4.3 exactly. The method should be approximately 150-200 lines long (including comments and logging).

**Key Implementation Points:**
- All TaskOrder updates must happen within the same database context before calling `SaveChangesAsync()` (ensures atomicity).
- Use structured logging (parameterized templates, no string interpolation).
- Reuse the existing `MapToDto` method (no changes needed).
- Follow the existing code style in `CreateTaskAsync` (same logging patterns, same navigation property loading patterns).

### Step 5 - Extend TaskController with MoveTask Endpoint

**File:** `KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs`

Add the `MoveTask` endpoint from Section 4.4. The controller will now have two endpoints: `CreateTask` (existing, POST) and `MoveTask` (new, PUT).

### Step 6 - Build & Verify

```bash
cd KanbAI-Core/KanbAI-Core
dotnet build --no-incremental
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

### Step 7 - Smoke Run (Optional)

Run the app (`dotnet run`), open Scalar UI, authenticate, and manually test the move endpoint:
1. Create a project, columns, and tasks
2. Move a task to a different column
3. Verify TaskOrder values are sequential in both columns
4. Reorder a task within the same column
5. Verify TaskOrder values adjust correctly

---

## 6. QA Guidance for @agent_tester_qa

### 6.1 Test File Locations

| Test File | Location | Type | Purpose |
|-----------|----------|------|---------|
| `TaskServiceMoveTests.cs` | `KanbAI-Core.Tests/Services/Tasks/` | Unit | Business logic for move operations with EF Core in-memory DbContext |
| `TaskControllerMoveTests.cs` | `KanbAI-Core.Tests/Controllers/` | Unit | HTTP response mapping for move endpoint; DTO-level validation |
| `TaskMoveApiIntegrationTests.cs` | `KanbAI-Core.Tests/Integration/` | Integration | HTTP-level auth + validation for move endpoint |

### 6.2 Test Case Tables

#### TaskServiceMoveTests.cs (Unit)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 1 | `MoveTaskAsync_DifferentColumn_UpdatesColumnIdAndTaskOrder` | Cross-column | Move task from column A to column B; verify task.ColumnId and task.TaskOrder updated |
| 2 | `MoveTaskAsync_DifferentColumn_RecalculatesSourceColumnOrders` | Cross-column | After moving task from position 2 (column A with 5 tasks), verify remaining tasks have TaskOrder 0,1,2,3 (no gap) |
| 3 | `MoveTaskAsync_DifferentColumn_RecalculatesTargetColumnOrders` | Cross-column | After moving task to position 1 (column B with 3 tasks), verify target column tasks have TaskOrder 0,1,2,3 (new task at 1, old tasks shifted) |
| 4 | `MoveTaskAsync_SameColumn_MoveUp_RecalculatesOrders` | Same-column | Move task from position 5 to position 2; verify tasks at 2,3,4 shift to 3,4,5 |
| 5 | `MoveTaskAsync_SameColumn_MoveDown_RecalculatesOrders` | Same-column | Move task from position 2 to position 5; verify tasks at 3,4,5 shift to 2,3,4 |
| 6 | `MoveTaskAsync_SameColumn_SamePosition_NoOp` | Same-column | Move task to its current position; verify no TaskOrder changes, returns Success |
| 7 | `MoveTaskAsync_TaskNotFound_ReturnsTaskNotFound` | 404 path | Random Guid for taskId |
| 8 | `MoveTaskAsync_UserNotProjectMember_ReturnsUserNotProjectMember` | 403 path | User not in project |
| 9 | `MoveTaskAsync_TargetColumnNotFound_ReturnsTargetColumnNotFound` | 404 path | Random Guid for target ColumnId |
| 10 | `MoveTaskAsync_CrossProjectMove_ReturnsCrossProjectMove` | 400 path | Task in project A, target column in project B |
| 11 | `MoveTaskAsync_InvalidTaskOrder_Negative_ReturnsInvalidTaskOrder` | Validation | TaskOrder = -1 |
| 12 | `MoveTaskAsync_InvalidTaskOrder_ExceedsTargetColumnCount_ReturnsInvalidTaskOrder` | Validation | TaskOrder = (target column task count + 1) |
| 13 | `MoveTaskAsync_InvalidTaskOrder_ExceedsSameColumnCount_ReturnsInvalidTaskOrder` | Validation | Same-column move, TaskOrder = (column task count) |
| 14 | `MoveTaskAsync_MultipleSequentialMoves_MaintainsSequentialOrders` | Ordering | Move task A, then task B, verify all TaskOrder values remain sequential |

#### TaskControllerMoveTests.cs (Unit - mocked service)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 15 | `MoveTask_ServiceReturnsSuccess_Returns200OK` | HTTP | 200 + ApiResponse.Ok with updated TaskResponseDto |
| 16 | `MoveTask_ServiceReturnsTaskNotFound_Returns404` | HTTP | NotFound + ApiResponse.Fail("Task not found.") |
| 17 | `MoveTask_ServiceReturnsUserNotProjectMember_Returns403` | HTTP | StatusCode(403) + ApiResponse.Fail(...) |
| 18 | `MoveTask_ServiceReturnsTargetColumnNotFound_Returns404` | HTTP | NotFound body matches spec |
| 19 | `MoveTask_ServiceReturnsCrossProjectMove_Returns400` | HTTP | BadRequest with "Cannot move task to a column in a different project." |
| 20 | `MoveTask_ServiceReturnsInvalidTaskOrder_Returns400` | HTTP | BadRequest with "TaskOrder is invalid." |
| 21 | `MoveTask_MissingColumnId_FailsDataAnnotationsValidation` | Validation | Validator.TryValidateObject rejects missing required field |
| 22 | `MoveTask_NegativeTaskOrder_FailsDataAnnotationsValidation` | Validation | [Range(0, int.MaxValue)] rejects -1 |

#### TaskMoveApiIntegrationTests.cs (Integration)

| # | Test Name | Category | Description |
|---|-----------|----------|-------------|
| 23 | `PutMoveTask_Unauthenticated_Returns401` | Security | [Authorize] enforcement |
| 24 | `PutMoveTask_MissingColumnId_Returns400` | Validation | [ApiController] rejects missing required field |
| 25 | `PutMoveTask_NegativeTaskOrder_Returns400` | Validation | [Range(0, int.MaxValue)] enforced |

### 6.3 Test Infrastructure

- **Unit tests (service):** EF Core In-Memory provider. Seed `Project`, `ProjectMember`, `BoardColumn` (2+ columns in same project), and `KanbanTask` rows (3+ tasks per column) in `Arrange`. Use `Guid.NewGuid()` for all keys.
- **Unit tests (controller):** Mock `ITaskService`, construct `ClaimsPrincipal` with `NameIdentifier` claim, assign via `ControllerContext`.
- **Integration tests:** Follow `integration-testing.md` pattern (TestAuthHandler, remove Negotiate auth, set FallbackPolicy = null).
- **Naming:** strictly `MethodName_StateUnderTest_ExpectedBehavior`.
- **AAA layout:** blank lines between Arrange/Act/Assert.

### 6.4 Key Test Scenarios for QA

**Scenario 1: Cross-Column Move with TaskOrder Recalculation**
- Setup: Column A has tasks [0: "A", 1: "B", 2: "C", 3: "D"]. Column B has tasks [0: "X", 1: "Y"].
- Action: Move task "C" (currently at A[2]) to column B at position 1.
- Verify:
  - Column A tasks are now [0: "A", 1: "B", 2: "D"] (no gap at position 2).
  - Column B tasks are now [0: "X", 1: "C", 2: "Y"].
  - Task "C" has ColumnId = B, TaskOrder = 1.

**Scenario 2: Same-Column Reorder (Move Up)**
- Setup: Column A has tasks [0: "A", 1: "B", 2: "C", 3: "D", 4: "E", 5: "F"].
- Action: Move task "E" (currently at position 4) to position 1.
- Verify:
  - Column A tasks are now [0: "A", 1: "E", 2: "B", 3: "C", 4: "D", 5: "F"].
  - Tasks "B", "C", "D" shifted from [1,2,3] to [2,3,4].

**Scenario 3: Same-Column Reorder (Move Down)**
- Setup: Column A has tasks [0: "A", 1: "B", 2: "C", 3: "D"].
- Action: Move task "B" (currently at position 1) to position 3.
- Verify:
  - Column A tasks are now [0: "A", 1: "C", 2: "D", 3: "B"].
  - Tasks "C", "D" shifted from [2,3] to [1,2].

**Scenario 4: Cross-Project Move Attempt**
- Setup: Project X has column A with task "T1". Project Y has column B.
- Action: Move task "T1" from column A to column B.
- Verify: Returns 400 Bad Request with "Cannot move task to a column in a different project."

**Scenario 5: Invalid TaskOrder (Out of Range)**
- Setup: Column A has 3 tasks (TaskOrder 0, 1, 2). Moving task from column B.
- Action: Move task to column A at position 5.
- Verify: Returns 400 Bad Request with "TaskOrder is invalid."

---

## 7. Known Caveats

| # | Caveat | Impact | Mitigation |
|---|--------|--------|-----------|
| 1 | **No unique constraint on TaskOrder** | Two concurrent moves to the same column could produce duplicate TaskOrder values. | Race window is small. `OrderBy(t => t.TaskOrder)` produces stable sort. Documented as known limitation for MVP. Future issue can add optimistic concurrency control. |
| 2 | **No task history/audit log** | No record of task movements (who moved what, when). | Out of scope. Future issue can add audit table. |
| 3 | **No undo/redo** | Users cannot revert a move operation. | Out of scope. Frontend can implement local undo via caching. |
| 4 | **Last write wins** | If two users move the same task simultaneously, the second move overwrites the first. | EF Core's default concurrency model. Future issue can add row versioning via `[Timestamp]` property. |
| 5 | **No bulk move** | Cannot move multiple tasks in a single request. | Intentionally deferred to future issue (bulk operations add complexity). |
| 6 | **Same-column no-op returns 200 OK** | Moving a task to its current position returns success with no database changes. | Intentional: simplifies frontend logic (idempotent operation). |
| 7 | **TaskOrder validation error message is generic** | Service returns "TaskOrder is invalid." without distinguishing "negative" vs "out of range". | Intentional: reduces error handling complexity. DTO-level validation already catches negative values; service-level check only catches out-of-range (upper bound). |
| 8 | **No WIP limits** | No enforcement of work-in-progress limits per column. | Out of scope for Issue #47. Future enhancement. |

---

## 8. Design Validation (Self-Check)

| Check | Question | Status |
|-------|----------|--------|
| **Namespaces** | Do all referenced namespaces match `RootNamespace = KanbAI_Core` and existing conventions? | ✅ Pass - `KanbAI_Core.DTOs`, `KanbAI_Core.Services.Tasks`, `KanbAI_Core.Controllers` |
| **Folder Paths** | Do all file paths reference real folders, or are "create new" steps included? | ✅ Pass - `DTOs/`, `Services/Tasks/`, `Controllers/` already exist |
| **Dependencies** | Are all required NuGet packages already in the `.csproj`, or are install steps included? | ✅ Pass - `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `System.ComponentModel.DataAnnotations` all present; no new packages |
| **Naming Conflicts** | Do any new class/enum names conflict with existing types? | ✅ Pass - `MoveTaskDto`, `MoveTaskResult` are new names; `TaskController`, `TaskService`, `ITaskService` are existing types being extended (not replaced) |
| **BaseEntity Compliance** | Do new entities inherit from `BaseEntity`? | ✅ N/A - no new entities; `KanbanTask` already inherits from `BaseEntity` |
| **Code Standards** | File-scoped namespaces, no blocking async, constructor injection, `AsNoTracking` on reads? | ✅ Pass - verified in spec sections 4 & 5. Note: task and column loads are **tracked** (not AsNoTracking) because we mutate entities |
| **Security** | No hardcoded secrets; no PII in logs; authorization enforced; mass-assignment prevented via DTO; input validation on DTO + service? | ✅ Pass - only `userId`/`taskId`/`columnId` (GUIDs) in logs; no PII; authorization via project membership check; DTOs bound, not entities; validation at DTO level ([Range(0, int.MaxValue)]) and service level (upper bound check) |

**Result:** All checks pass. Design is ready for implementation.

---

**Document Status:** Ready for Implementation
**Last Updated:** 2026-04-27
**Next Steps:** @agent_developer should read this tech spec and begin implementation following Section 5 strictly.

The technical specification is saved. You can now instruct the developer to read the tech spec and begin implementation.

---

## 9. Development Status

**Developer:** @agent_developer
**Completed:** 2026-04-27

### 9.1 Files Created

| File | Purpose |
|------|---------|
| [KanbAI-Core/KanbAI-Core/DTOs/MoveTaskDto.cs](../../KanbAI-Core/KanbAI-Core/DTOs/MoveTaskDto.cs) | Request DTO for the task move endpoint. `required Guid ColumnId` + `required int TaskOrder` with `[Range(0, int.MaxValue)]`. |
| [KanbAI-Core/KanbAI-Core/Services/Tasks/MoveTaskResult.cs](../../KanbAI-Core/KanbAI-Core/Services/Tasks/MoveTaskResult.cs) | Discriminator enum for move outcomes (`Success`, `TaskNotFound`, `UserNotProjectMember`, `TargetColumnNotFound`, `CrossProjectMove`, `InvalidTaskOrder`). |

### 9.2 Files Modified

| File | Change |
|------|--------|
| [KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs](../../KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs) | Added `MoveTaskAsync(Guid taskId, MoveTaskDto dto, Guid userId)` signature with XML documentation. |
| [KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs](../../KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs) | Implemented `MoveTaskAsync` per Section 4.3 algorithm: validate negative TaskOrder, load task with eager `Column -> Project -> Members`, project membership check, load target column (if different) with cross-project guard, upper-bound range validation (different-column: `<= count`; same-column: `<= count - 1`), no-op short circuit, then branching recalculation — cross-column decrements source-tail / increments target-head, same-column up/down adjusts the affected window — and a single `SaveChangesAsync()` for atomicity. |
| [KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs](../../KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs) | Added `PUT /api/Task/{taskId}/move` endpoint (`MoveTask`) that forwards to `ITaskService.MoveTaskAsync` and maps `MoveTaskResult` values to HTTP status codes (200 / 400 / 403 / 404 / 500) using the existing `ApiResponse` helpers. Reuses `GetCurrentUserId()`. |

### 9.3 Build & Test Results

- **Build:** `dotnet build --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
- **Tests:** `dotnet test --verbosity quiet` → **340 passed, 0 failed, 2 skipped** (342 total). No pre-existing failures; no new failures introduced by the change.

### 9.4 Infrastructure Notes

- No new NuGet packages, no EF Core migrations, no schema changes, no DI wiring changes required. The existing `ITaskService` DI registration (from Issue #46) covers the new method.
- No workarounds or test-only infrastructure were added.

### 9.5 Edge Cases for QA

1. **No-op same-position move** — `MoveTaskAsync` returns `Success` with the unchanged task when `ColumnId == task.ColumnId && TaskOrder == task.TaskOrder`. The method exits BEFORE `SaveChangesAsync()`, so `UpdatedAt` is not bumped and no UPDATE statements are emitted. Verify behavior matches spec (Section 7, Caveat 6) and that tests do not assert on `UpdatedAt` for this path.
2. **Sequential TaskOrder invariant** — After any cross-column or same-column move, both affected columns must have `TaskOrder` values `0, 1, 2, ...` with no gaps and no duplicates. This is the key acceptance criterion; assert by querying each column ordered by `TaskOrder` and comparing to the expected index sequence.
3. **Boundary TaskOrders** — Explicitly test:
   - `TaskOrder = 0` (insert at top of target column).
   - `TaskOrder = targetColumnCount` (append to a *different* column — valid, creates position = new count).
   - `TaskOrder = targetColumnCount` for a *same-column* reorder — INVALID (must be `<= count - 1`), returns `InvalidTaskOrder`.
4. **Same-column move up vs move down** — The two branches use different predicates (`>= new && < old` vs `> old && <= new`). Cover both directions and confirm the moved task itself is not double-adjusted (it is mutated only after the affected-window update).
5. **Cross-project move** — Ensure the 400 path triggers when the target column's `ProjectId` differs from the task's current column's `ProjectId`, even if the caller *is* a member of the source project.
6. **DTO validation vs service validation** — `[Range(0, int.MaxValue)]` on `TaskOrder` blocks negatives at the model-binding layer (HTTP 400 with ModelState errors). The service's `dto.TaskOrder < 0` guard is a defensive duplicate for direct service callers (unit tests). Both paths should be exercised.
7. **Authorization precedence** — The service checks membership BEFORE loading the target column, so a non-member attempting a cross-project move will receive `UserNotProjectMember` (403), not `CrossProjectMove` (400). Tests should reflect this ordering.
8. **Concurrency caveat** — No optimistic concurrency control; concurrent moves to the same column can produce duplicate `TaskOrder` values (Caveat 1). Out of scope to fix, but QA may want to document a reproduction for future work.

---

## 10. QA Status

**QA Tester:** @agent_tester_qa
**Completed:** 2026-04-27

### 10.1 Test Files Created

| Test File | Type | Coverage |
|-----------|------|----------|
| [KanbAI-Core/KanbAI-Core.Tests/Services/Tasks/TaskServiceMoveTests.cs](../../KanbAI-Core/KanbAI-Core.Tests/Services/Tasks/TaskServiceMoveTests.cs) | Unit (EF Core In-Memory) | 16 tests covering the `MoveTaskAsync` algorithm — cross-column moves (ColumnId/TaskOrder update, source decrement, target increment, append-to-end boundary), same-column reorder (move up, move down, same-position no-op), 404/403 paths (task not found, user not project member, target column not found), cross-project guard, TaskOrder validation (negative, exceeds target count, exceeds same-column count), and the sequential-invariant property across three back-to-back moves. |
| [KanbAI-Core/KanbAI-Core.Tests/Controllers/TaskControllerTests.cs](../../KanbAI-Core/KanbAI-Core.Tests/Controllers/TaskControllerTests.cs) (extended) | Unit (mocked service) | 8 new tests: all six `MoveTaskResult → HTTP` mappings (200 / 404 / 403 / 404 / 400 / 400) plus two `MoveTaskDto` DataAnnotations tests (negative TaskOrder fails, TaskOrder=0 boundary passes). |
| [KanbAI-Core/KanbAI-Core.Tests/Integration/TaskApiIntegrationTests.cs](../../KanbAI-Core/KanbAI-Core.Tests/Integration/TaskApiIntegrationTests.cs) (extended) | Integration (WebApplicationFactory) | 3 new tests: `PutMoveTask_Unauthenticated_Returns401`, `PutMoveTask_MissingColumnId_Returns400`, `PutMoveTask_NegativeTaskOrder_Returns400`. |

### 10.2 Test Results

- **Build:** `dotnet build` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
- **Tests:** `dotnet test --verbosity quiet` → **366 passed, 0 failed, 2 skipped** (368 total).
- **Delta vs Issue #46 baseline:** +26 new tests (340 → 366). No pre-existing tests were broken.

### 10.3 Acceptance Criteria Coverage

| Acceptance Criterion | Covered By |
|----------------------|------------|
| PUT `/api/Task/{taskId}/move` endpoint exists, [Authorize] enforced | `PutMoveTask_Unauthenticated_Returns401` |
| 404 if task does not exist | `MoveTaskAsync_TaskNotFound_ReturnsTaskNotFound`, `MoveTask_ServiceReturnsTaskNotFound_Returns404` |
| 404 if target column does not exist | `MoveTaskAsync_TargetColumnNotFound_ReturnsTargetColumnNotFound`, `MoveTask_ServiceReturnsTargetColumnNotFound_Returns404` |
| 403 if user is not a project member | `MoveTaskAsync_UserNotProjectMember_ReturnsUserNotProjectMember`, `MoveTask_ServiceReturnsUserNotProjectMember_Returns403` |
| 400 if target column is in a different project | `MoveTaskAsync_CrossProjectMove_ReturnsCrossProjectMove`, `MoveTask_ServiceReturnsCrossProjectMove_Returns400` |
| Cross-column move: ColumnId + TaskOrder updated | `MoveTaskAsync_DifferentColumn_UpdatesColumnIdAndTaskOrder` |
| Cross-column move: source column gap removed | `MoveTaskAsync_DifferentColumn_RecalculatesSourceColumnOrders` |
| Cross-column move: target column makes room | `MoveTaskAsync_DifferentColumn_RecalculatesTargetColumnOrders` |
| Same-column reorder (move up) | `MoveTaskAsync_SameColumn_MoveUp_RecalculatesOrders` |
| Same-column reorder (move down) | `MoveTaskAsync_SameColumn_MoveDown_RecalculatesOrders` |
| Same-position no-op succeeds | `MoveTaskAsync_SameColumn_SamePosition_NoOp` |
| TaskOrder sequential with no gaps after multiple moves | `MoveTaskAsync_MultipleSequentialMoves_MaintainsSequentialOrders` |
| Negative TaskOrder rejected (DTO-level) | `MoveTaskDto_NegativeTaskOrder_FailsDataAnnotationsValidation`, `PutMoveTask_NegativeTaskOrder_Returns400` |
| Negative TaskOrder rejected (service-level defensive) | `MoveTaskAsync_InvalidTaskOrder_Negative_ReturnsInvalidTaskOrder` |
| TaskOrder > target column count (cross-column) | `MoveTaskAsync_InvalidTaskOrder_ExceedsTargetColumnCount_ReturnsInvalidTaskOrder` |
| TaskOrder > column count − 1 (same-column) | `MoveTaskAsync_InvalidTaskOrder_ExceedsSameColumnCount_ReturnsInvalidTaskOrder` |
| Append to end of a different column (TaskOrder = count) | `MoveTaskAsync_DifferentColumn_AppendToEnd_AddsAtBoundary` |
| Missing ColumnId → 400 via model binding | `PutMoveTask_MissingColumnId_Returns400` |

### 10.4 Bugs Found & Fixed

None. The implementation matched the tech spec exactly on first review and all 26 new tests passed without requiring any production-code changes.

### 10.5 Outstanding Issues

None. Known limitations from Section 7 (no optimistic concurrency, no audit log, no WIP limits) are out of scope for Issue #47 and explicitly deferred to future work.

---

QA testing is complete. All tests pass and the implementation meets the acceptance criteria.
