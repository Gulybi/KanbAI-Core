# Issue #47: Implement Task Movement Logic (Drag-and-Drop Backend Support)

**GitHub Issue:** [#47 - Implement Task Movement Logic (Drag-and-Drop Backend Support)](https://github.com/Gulybi/KanbAI-Core/issues/47)  
**Milestone:** Project and Kanban Business Logic (Issue #47 of 2)  
**Assignee:** @Gulybi  
**Created:** 2026-04-27

---

## 📊 Business Value & Context

### What Are We Building?
A Task Movement API that enables users to reorder tasks within a single column or move tasks between different columns through drag-and-drop interactions. This includes:
- **Moving tasks between columns** - Users can change a task's workflow stage by dragging it from one column (e.g., "To Do") to another (e.g., "In Progress")
- **Reordering tasks within a column** - Users can prioritize work by changing the visual order of tasks in a column
- **Automatic order recalculation** - When a task is moved, all affected tasks' TaskOrder values are automatically updated to maintain consistent, sequential ordering

### Who Is This For?
**Project Members** (anyone with access to a project) who need to:
- Manage their workflow by moving tasks through stages (columns) as work progresses
- Prioritize work by reordering tasks within the same column (moving urgent tasks to the top)
- Visualize and control the flow of work across their Kanban board

**Project Owners and Team Leads** benefit by:
- Monitoring the flow of work across workflow stages in real-time
- Identifying bottlenecks when too many tasks pile up in one column
- Maintaining an accurate visual representation of work in progress

### Why Is This Valuable?
**Task movement is the core interaction** in a Kanban system. The three fundamental Kanban practices are:
1. Visualize work
2. Limit work in progress
3. **Manage flow**

Without task movement, users can create and view tasks, but they cannot update task status as work progresses or reprioritize work. The Kanban board becomes a static snapshot rather than a dynamic workflow management tool. This feature unlocks:
- **Real workflow tracking** - Tasks move from "To Do" to "In Progress" to "Done" as work happens
- **Dynamic prioritization** - Urgent tasks can be moved to the top of a column
- **Accurate work-in-progress visualization** - The board reflects the current state of work, not a stale initial assignment

---

## 🗺️ Milestone Context

**Milestone:** Project and Kanban Business Logic  
**Total Issues:** 2 remaining in this milestone

| Issue | Title | Status | Dependency |
|-------|-------|--------|------------|
| #44 | Implement Project Member Management | COMPLETED | Foundation for authorization |
| #45 | Implement Board Columns API | COMPLETED | Provides columns for tasks to live in |
| #46 | Implement Kanban Task Creation and Management | COMPLETED | Provides tasks that can be moved |
| **#47** | **Implement Task Movement Logic (Drag-and-Drop Backend Support)** | **OPEN** | **← YOU ARE HERE** |

**Prerequisites (Completed):**
- Issue #44: Project Member Management exists, providing user-to-project relationships and authorization patterns
- Issue #45: Board Columns API exists at `/api/Column/project/{projectId}` (GET, POST) and `/api/Column/{id}` (DELETE)
- Issue #46: Task Creation API exists at POST `/api/Task/column/{columnId}`, creating tasks with automatic TaskOrder assignment

**Relationship to Other Issues:**
- **Depends on #46**: Tasks must exist before they can be moved. The task creation endpoint already demonstrates authorization patterns (project membership checks) and TaskOrder logic (automatic assignment at the bottom of a column).
- **Completes the Milestone**: This is the final issue in the "Project and Kanban Business Logic" milestone. After this, users will have a fully functional Kanban board with CRUD operations for projects, columns, and tasks, plus the ability to move tasks across the board.

---

## 🔍 Current State vs. Desired State

### Current State

**Domain Model (Fully Implemented):**
- `KanbanTask` entity exists in `KanbAI-Core/Models/Entities/KanbanTask.cs`:
  - Properties:
    - `Title` (string, required)
    - `Content` (string, nullable)
    - `TaskOrder` (int, required) - Determines the task's position within its column
    - `ColumnId` (Guid, foreign key, required) - The board column this task belongs to
    - `AssignedId` (Guid, nullable)
  - Navigation properties:
    - `Column` (BoardColumn) - The parent column
    - `AssignedUser` (User, nullable)
  - Inherits from `BaseEntity` (provides Id, CreatedAt, UpdatedAt)

- `BoardColumn` entity exists in `KanbAI-Core/Models/Entities/BoardColumn.cs`:
  - Properties: `Name`, `ColorCode`, `ColumnOrder`, `ProjectId`
  - Navigation property `Tasks` (ICollection of KanbanTask)
  - Inherits from `BaseEntity`

**API Layer:**
- `TaskController` exists in `KanbAI-Core/Controllers/TaskController.cs`:
  - Single endpoint: POST `/api/Task/column/{columnId}` (create task)
  - Authorization: [Authorize] attribute (JWT-based authentication)
  - Helper: `GetCurrentUserId()` extracts authenticated user ID from JWT claims
  - Response pattern: Uses `ApiResponse<T>` wrapper for standardized responses
  - **No task movement endpoint exists**

- `ColumnController` exists in `KanbAI-Core/Controllers/ColumnController.cs`:
  - Endpoints: GET `/api/Column/project/{projectId}`, POST `/api/Column/project/{projectId}`, DELETE `/api/Column/{id}`
  - Same authorization and response patterns as TaskController

**Service Layer:**
- `ITaskService` interface exists in `KanbAI-Core/Services/Tasks/ITaskService.cs`:
  - Single method: `CreateTaskAsync(Guid columnId, CreateTaskDto dto, Guid userId)` returns `(TaskResponseDto? data, CreateTaskResult result)`
  - **No method exists for moving tasks**

- `TaskService` implementation exists in `KanbAI-Core/Services/Tasks/TaskService.cs`:
  - Implements task creation with:
    - Authorization: Verifies user is a member of the project that owns the target column
    - Validation: Checks title is not empty, assigned user (if provided) is a project member
    - Automatic TaskOrder assignment: Calculates `(max existing TaskOrder in column) + 1`, or 0 if column is empty
  - Uses `CreateTaskResult` enum to communicate operation outcomes (Success, ColumnNotFound, UserNotProjectMember, InvalidTitle, etc.)

**Data Transfer Objects:**
- `CreateTaskDto` exists in `KanbAI-Core/DTOs/CreateTaskDto.cs`:
  - Properties: `Title` (required, max 200 chars), `Content` (optional), `AssignedId` (optional)
- `TaskResponseDto` exists in `KanbAI-Core/DTOs/TaskResponseDto.cs`:
  - Properties: `Id`, `Title`, `Content`, `TaskOrder`, `ColumnId`, `AssignedId`, `CreatedAt`, `UpdatedAt`
- **No DTO exists for task movement operations**

**Authorization Pattern (Established):**
- Task creation verifies that the user is a member of the project that owns the column
- Pattern: Service layer queries `BoardColumn.Project.Members` to check for a matching `UserId`
- Returns `CreateTaskResult.UserNotProjectMember` if user is not a member
- Controller maps this to 403 Forbidden
- This same pattern should be reused for task movement operations

**TaskOrder Logic (Established):**
- Task creation automatically sets `TaskOrder` to place new tasks at the bottom of a column
- Calculation: `(max existing TaskOrder in column) + 1`, or 0 if no tasks exist
- **No logic exists to recalculate TaskOrder values when a task is moved**

### Desired State

**API Layer:**
- New endpoint in `TaskController`:
  - **PUT `/api/Task/{taskId}/move`** - Move a task to a new column and/or reorder it within a column
  - Accepts request body with `ColumnId` (new column, required) and `TaskOrder` (new position, required)
  - Returns responses wrapped in the standard `ApiResponse<T>` format
  - Protected with the `[Authorize]` attribute
  - Reuses the `GetCurrentUserId()` pattern

**Service Layer:**
- New method in `ITaskService`:
  - Move a task to a new column and/or position within its current column
  - Automatically recalculate TaskOrder values for all affected tasks
  - Verify user is a member of the project that owns both the source and target columns

**Data Transfer Objects:**
- New `MoveTaskDto` (or similar name) for task movement requests:
  - `ColumnId` (Guid, required) - The new column to move the task to
  - `TaskOrder` (int, required) - The new position within the column (0-based index)

**Business Rules:**
1. **Project membership required** - Only users who are members of the project can move tasks in that project's columns
2. **Cross-column validation** - When moving a task from one column to another, both columns must belong to the same project
3. **Automatic order recalculation** - When a task is moved:
   - If moved to a different column: Remove gaps in the source column (decrement TaskOrder for tasks that were below the moved task), insert into target column at the specified position (increment TaskOrder for tasks at or below the insertion point)
   - If reordered within the same column: Shift tasks between the old and new positions to accommodate the change
4. **Position validation** - The new TaskOrder value must be within the valid range (0 to current task count in the target column)
5. **Task existence** - The TaskId must reference an existing task
6. **Column existence** - The new ColumnId must reference an existing column in the same project as the task's current column

**Error Handling:**
- 400 Bad Request: TaskOrder is out of range (negative or exceeds task count), ColumnId does not belong to the same project as the task's current column
- 403 Forbidden: User is not a member of the project
- 404 Not Found: Task does not exist, Column does not exist, or user is not a member of the project

---

## ✅ Acceptance Criteria

**As a project member, I can move a task to a different column so that I can reflect workflow stage changes (e.g., move from "To Do" to "In Progress").**

- [ ] A PUT `/api/Task/{taskId}/move` endpoint exists
- [ ] The endpoint requires a valid JWT token (authenticated user)
- [ ] The endpoint accepts a request body containing the new ColumnId and new TaskOrder
- [ ] The endpoint returns 403 Forbidden if the authenticated user is not a member of the project that owns the task's current column
- [ ] The endpoint returns 404 Not Found if the task does not exist
- [ ] The endpoint returns 404 Not Found if the new column does not exist
- [ ] The endpoint returns 400 Bad Request if the new column belongs to a different project than the task's current column
- [ ] Upon successful move to a different column, the task's ColumnId is updated to the new column
- [ ] Upon successful move to a different column, the task's TaskOrder is set to the specified value
- [ ] The endpoint returns 200 OK with the updated task's details (id, title, taskOrder, columnId, createdAt, updatedAt)

**Task order is automatically recalculated for affected tasks when a task is moved between columns.**

- [ ] When a task is moved from column A to column B, all tasks in column A that were below the moved task have their TaskOrder decremented by 1 (to remove the gap left by the moved task)
- [ ] When a task is moved to column B at position N, all tasks in column B at position N or higher have their TaskOrder incremented by 1 (to make room for the incoming task)
- [ ] After the move, all tasks in both the source and target columns have sequential TaskOrder values with no gaps (0, 1, 2, 3, ...)
- [ ] The recalculation happens atomically within a single database transaction

**As a project member, I can reorder a task within the same column so that I can prioritize work.**

- [ ] When the new ColumnId is the same as the task's current ColumnId, the task is reordered within the same column
- [ ] If moving a task to a lower position (e.g., from position 5 to position 2), all tasks between the new and old positions have their TaskOrder incremented by 1
- [ ] If moving a task to a higher position (e.g., from position 2 to position 5), all tasks between the old and new positions have their TaskOrder decremented by 1
- [ ] After reordering, all tasks in the column have sequential TaskOrder values with no gaps
- [ ] The reordering happens atomically within a single database transaction

**Position validation ensures data integrity.**

- [ ] If the new TaskOrder is negative, the endpoint returns 400 Bad Request with a clear error message (e.g., "TaskOrder cannot be negative.")
- [ ] If the new TaskOrder exceeds the task count in the target column, the endpoint returns 400 Bad Request with a clear error message (e.g., "TaskOrder exceeds the number of tasks in the column.")
- [ ] If moving to a different column, the valid TaskOrder range is 0 to (target column task count)
- [ ] If reordering within the same column, the valid TaskOrder range is 0 to (current column task count - 1)

**Authorization follows the established project membership pattern.**

- [ ] Task movement verifies that the authenticated user is a member of the project that owns the task's current column
- [ ] The authorization check uses the same pattern as TaskService.CreateTaskAsync (query `BoardColumn.Project.Members`)
- [ ] If the user is not a member of the project, the endpoint returns 403 Forbidden
- [ ] The error response uses the standard `ApiResponse.Fail()` pattern

**The implementation follows existing architectural patterns.**

- [ ] The move endpoint is added to the existing `TaskController` (no new controller needed)
- [ ] The move method is added to the existing `ITaskService` interface
- [ ] The move implementation is added to the existing `TaskService` class
- [ ] A new DTO (e.g., MoveTaskDto) is created in the `KanbAI-Core/DTOs/` folder following the record pattern
- [ ] All responses use the `ApiResponse<T>` wrapper for consistency
- [ ] The service method returns a result enum (similar to `CreateTaskResult`) to communicate operation outcomes

**Edge cases are handled gracefully.**

- [ ] Attempting to move a non-existent task returns 404 Not Found
- [ ] Attempting to move a task to a non-existent column returns 404 Not Found
- [ ] Attempting to move a task to a column in a different project returns 400 Bad Request with a specific error message (e.g., "Cannot move task to a column in a different project.")
- [ ] Attempting to move a task with an out-of-range TaskOrder returns 400 Bad Request with a specific error message
- [ ] If the authenticated user's JWT is valid but they are not a member of the project, the endpoint returns 403 Forbidden (not 404)
- [ ] Moving a task to its current position within its current column is a no-op (succeeds without side effects)

---

## 🎯 Out of Scope (Future Enhancements)

These capabilities are explicitly **not** part of this issue and should be deferred to future work:

- **Bulk task movement** - No ability to move multiple tasks in a single request
- **Task history/audit log** - No record of task movements (who moved what task from where to where and when)
- **Undo/redo functionality** - No way to revert a task movement
- **Optimistic concurrency control** - No handling of simultaneous task movements by multiple users (last write wins)
- **Drag-and-drop frontend implementation** - This issue only covers the backend API; frontend integration is a separate concern
- **Column-level task count limits** - No enforcement of WIP (work-in-progress) limits per column
- **Task movement notifications** - No real-time updates or notifications when tasks are moved by other users
- **TaskOrder auto-adjustment on task deletion** - When a task is deleted, gaps remain in TaskOrder sequence (can be addressed separately)

---

## 📚 Reference Materials

**Related Issues:**
- [#44 - Implement Project Member Management](https://github.com/Gulybi/KanbAI-Core/issues/44) - Provides user-to-project relationships for authorization
- [#45 - Implement Board Columns API](https://github.com/Gulybi/KanbAI-Core/issues/45) - Provides the columns that tasks belong to
- [#46 - Implement Kanban Task Creation and Management](https://github.com/Gulybi/KanbAI-Core/issues/46) - Provides task creation with automatic TaskOrder assignment

**Relevant Files:**
- `KanbAI-Core/Models/Entities/KanbanTask.cs` - Task entity with Title, TaskOrder, ColumnId properties
- `KanbAI-Core/Models/Entities/BoardColumn.cs` - Column entity with ProjectId and Tasks navigation property
- `KanbAI-Core/Controllers/TaskController.cs` - Existing controller with task creation endpoint
- `KanbAI-Core/Services/Tasks/ITaskService.cs` - Existing service interface to be extended
- `KanbAI-Core/Services/Tasks/TaskService.cs` - Existing service implementation demonstrating authorization and TaskOrder logic
- `KanbAI-Core/DTOs/TaskResponseDto.cs` - Existing DTO demonstrating the record pattern

**Coding Standards:**
- `.claude/rules/code-standards.md` - Modern C# conventions (file-scoped namespaces, async/await, DI patterns, EF Core optimizations)
- `.claude/rules/security-safety.md` - Authorization checks, input validation, no PII in logs
- `.claude/rules/testing-observability.md` - Unit test structure (AAA pattern), xUnit naming conventions

---

## 🚦 Success Metrics

This feature is considered complete when:
1. A project member can successfully move a task to a different column via the API with automatic TaskOrder recalculation for all affected tasks
2. A project member can successfully reorder a task within the same column via the API with correct order shifts for affected tasks
3. A non-project-member receives a 403 Forbidden when attempting to move a task in a project's columns
4. All edge cases (out-of-range position, cross-project move attempt, non-existent task/column) return appropriate error codes and messages
5. The implementation passes code review with zero concurrency issues (all database updates happen atomically within a transaction)
6. Integration tests demonstrate the full workflow: create project, create columns, create multiple tasks, move task between columns, verify all TaskOrder values are sequential with no gaps

---

**Prepared by:** Product Manager Agent  
**Date:** 2026-04-27
