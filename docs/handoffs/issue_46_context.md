# Issue #46: Implement Kanban Task Creation and Management

**GitHub Issue:** [#46 - Implement Kanban Task Creation and Management](https://github.com/Gulybi/KanbAI-Core/issues/46)  
**Milestone:** Project and Kanban Business Logic (Issue #46 of 5)  
**Assignee:** @Gulybi  
**Created:** 2026-04-27

---

## 📊 Business Value & Context

### What Are We Building?
A Kanban Task Management API that enables project members to create and manage task cards on their boards. This includes:
- **Creating new task cards** - Users can add tasks to specific board columns with title, content, and assigned user
- **Assigning users to tasks** - Tasks can be assigned to specific project members for accountability
- **Proper task ordering** - When a task is created, it is automatically positioned at the bottom (or top) of its column via the TaskOrder property

### Who Is This For?
**Project Members** (anyone with access to a project) who need to:
- Create actionable work items (tasks) within their Kanban boards
- Track task ownership by assigning work to specific team members
- Organize work visually within board columns that represent workflow stages

**Project Owners and Team Leads** benefit by:
- Distributing work assignments across team members
- Monitoring workload and task assignments through the board interface
- Building structured workflows that reflect their team's process

### Why Is This Valuable?
**Task cards are the atomic unit of work** in a Kanban system. Without the ability to create and manage tasks:
- Board columns exist but remain empty, providing no value to users
- Teams cannot track individual work items, only abstract workflow stages
- The core promise of Kanban (visualizing work in progress, limiting WIP, managing flow) cannot be fulfilled

This feature transforms board columns from empty containers into actionable workflow systems where teams can capture, assign, and track individual pieces of work.

---

## 🗺️ Milestone Context

**Milestone:** Project and Kanban Business Logic  
**Total Issues:** 5

| Issue | Title | Status | Dependency |
|-------|-------|--------|------------|
| #43 | Implement Project CRUD Operations | COMPLETED | Foundation |
| #44 | Implement Project Member Management | COMPLETED | Provides user-project relationships |
| #45 | Implement Board Columns API | COMPLETED | Provides columns for tasks to live in |
| **#46** | **Implement Kanban Task Creation and Management** | **OPEN** | **← YOU ARE HERE** |
| #47 | Implement Task Movement Logic | TBD | Requires #46 (tasks must exist before they can be moved) |

**Prerequisites (Completed):**
- Issue #43: Project CRUD operations exist, providing the foundation for project-scoped resources
- Issue #44: Project Member Management exists, enabling user-to-project relationships and authorization checks
- Issue #45: Board Columns API exists at `/api/Column/project/{projectId}` (GET, POST) and `/api/Column/{id}` (DELETE), providing the columns that tasks will belong to

**Relationship to Other Issues:**
- **Depends on #45**: Tasks cannot be created without columns to place them in. The ColumnId foreign key relationship is mandatory.
- **Foundation for #47**: Task Movement Logic (drag-and-drop) depends on tasks existing and having TaskOrder properties that can be updated.

---

## 🔍 Current State vs. Desired State

### Current State

**Domain Model (Fully Implemented):**
- `KanbanTask` entity exists in `KanbAI-Core/Models/Entities/KanbanTask.cs`:
  - Properties:
    - `Title` (string, required) - The task's headline/summary
    - `Content` (string, nullable) - The task's detailed description
    - `TaskOrder` (int, required) - Determines the task's position within its column
    - `ColumnId` (Guid, foreign key, required) - The board column this task belongs to
    - `AssignedId` (Guid, nullable) - The user assigned to this task (optional)
  - Navigation properties:
    - `Column` (BoardColumn) - The parent column
    - `AssignedUser` (User, nullable) - The assigned user
    - `Assets` (ICollection) - File attachments (future feature)
    - `Comments` (ICollection) - Task comments (future feature)
  - Inherits from `BaseEntity` (provides Id, CreatedAt, UpdatedAt)

- `BoardColumn` entity exists in `KanbAI-Core/Models/Entities/BoardColumn.cs`:
  - Properties include `ProjectId`, `Name`, `ColorCode`, `ColumnOrder`
  - Navigation property `Tasks` (ICollection of KanbanTask)

**API Layer (Columns Only):**
- `ColumnController` exists in `KanbAI-Core/Controllers/ColumnController.cs`:
  - Endpoints: GET /api/Column/project/{projectId}, POST /api/Column/project/{projectId}, DELETE /api/Column/{id}
  - Authorization: All endpoints require [Authorize] attribute (JWT-based authentication)
  - Helper: `GetCurrentUserId()` extracts authenticated user ID from JWT claims
  - Response pattern: Uses `ApiResponse<T>` wrapper for standardized responses
  - **No task management endpoints exist**

**Service Layer (Columns Only):**
- `IColumnService` interface exists in `KanbAI-Core/Services/Columns/IColumnService.cs`:
  - Methods: `GetProjectColumnsAsync`, `CreateColumnAsync`, `DeleteColumnAsync`
  - All methods accept `userId` parameter for authorization checks
  - **No service or interface exists for task management**

**Data Transfer Objects (Columns Only):**
- `ColumnResponseDto`, `CreateColumnDto` exist in `KanbAI-Core/DTOs/`
- **No DTOs exist for task operations**

**Authorization Pattern (Established):**
- Column operations verify that the user is a member of the project that owns the column
- Pattern: Service layer checks `ProjectMembers` table for a matching `(ProjectId, UserId)` pair
- 404 Not Found returned if project doesn't exist or user is not a member
- This same pattern should be reused for task operations

### Desired State

**API Layer:**
- New `TaskController` with RESTful endpoints for task management:
  - **POST /api/Task/column/{columnId}** - Create a new task in a specific column
  - Additional task management endpoints (GET, UPDATE, DELETE) as defined in technical specification
- All endpoints return responses wrapped in the standard `ApiResponse<T>` format
- All endpoints are protected with the `[Authorize]` attribute
- Controller reuses the `GetCurrentUserId()` pattern from ColumnController

**Service Layer:**
- New `ITaskService` interface defining the contract for task operations
- `TaskService` implementation handling business logic:
  - Verifies user is a member of the project that owns the target column
  - Automatically calculates TaskOrder when creating a task (e.g., max existing TaskOrder + 1, or 0 if no tasks exist)
  - Validates that assigned users (if provided) are members of the project
  - Ensures ColumnId references a valid, existing column

**Data Transfer Objects:**
- `CreateTaskDto` for task creation requests (Title, Content, AssignedId optional)
- `TaskResponseDto` for task responses (includes all task fields plus formatted timestamps)
- DTOs follow the `record` pattern established by existing DTOs (e.g., ColumnResponseDto)

**Business Rules:**
1. **Project membership required** - Only users who are members of the project that owns the target column can create tasks in that column
2. **Automatic task ordering** - When a task is created, its TaskOrder is automatically set to place it at the bottom of the column (max existing TaskOrder + 1)
3. **Optional task assignment** - The AssignedId field is nullable; tasks can be created without an assigned user
4. **Assignment validation** - If an AssignedId is provided, it must reference a user who is a member of the project
5. **Column existence** - The ColumnId must reference an existing column before a task can be created

**Error Handling:**
- 400 Bad Request: Title is missing/empty, ColumnId is invalid, AssignedId references non-member user
- 403 Forbidden: User is not a member of the project that owns the column
- 404 Not Found: Column does not exist, or user is not a member of the project

---

## ✅ Acceptance Criteria

**As a project member, I can create a task card in a board column so that I can track actionable work items.**

- [ ] A POST /api/Task/column/{columnId} endpoint exists
- [ ] The endpoint requires a valid JWT token (authenticated user)
- [ ] The endpoint accepts a request body containing at minimum the task Title
- [ ] The endpoint accepts optional fields: Content (description) and AssignedId (user to assign the task to)
- [ ] The endpoint returns 403 Forbidden if the authenticated user is not a member of the project that owns the target column
- [ ] The endpoint returns 404 Not Found if the column does not exist
- [ ] The endpoint returns 400 Bad Request if the Title field is missing or empty
- [ ] Upon successful creation, the task is assigned a TaskOrder value that places it at the bottom of the column (max existing TaskOrder in that column + 1, or 0 if no tasks exist)
- [ ] Upon successful creation, a KanbanTask record is persisted to the database with CreatedAt and UpdatedAt timestamps
- [ ] The endpoint returns 201 Created with the newly created task's details (id, title, content, taskOrder, columnId, assignedId, createdAt, updatedAt)

**As a project member, I can assign a task to a specific user so that work ownership is clear.**

- [ ] When creating a task, the AssignedId field can be included in the request body
- [ ] If AssignedId is provided and references a valid user who is a member of the project, the task is successfully created with that assignment
- [ ] If AssignedId is provided but references a user who is NOT a member of the project, the endpoint returns 400 Bad Request with a clear error message (e.g., "Assigned user is not a member of this project.")
- [ ] If AssignedId is provided but references a non-existent user, the endpoint returns 400 Bad Request with a clear error message (e.g., "Assigned user not found.")
- [ ] If AssignedId is omitted or null, the task is successfully created without an assigned user

**Task ordering is automatically managed to maintain visual consistency.**

- [ ] When a task is created in an empty column, its TaskOrder is set to 0
- [ ] When a task is created in a column with existing tasks, its TaskOrder is set to (maximum existing TaskOrder + 1)
- [ ] The TaskOrder calculation happens automatically in the service layer without requiring the client to provide a TaskOrder value
- [ ] Multiple tasks created in the same column receive unique, sequential TaskOrder values

**Authorization follows the established project membership pattern.**

- [ ] Task creation verifies that the authenticated user is a member of the project that owns the target column (not just that the user is authenticated)
- [ ] The authorization check uses the same ProjectMembers table lookup pattern as ColumnService
- [ ] If the user is not a member of the project, the endpoint returns 403 Forbidden
- [ ] The error response uses the standard `ApiResponse.Fail()` pattern

**The implementation follows existing architectural patterns.**

- [ ] TaskController exists in the `KanbAI-Core/Controllers/` folder and follows the same structure as ColumnController
- [ ] ITaskService interface exists in `KanbAI-Core/Services/Tasks/` folder
- [ ] TaskService implementation exists and is registered in the dependency injection container
- [ ] DTOs (CreateTaskDto, TaskResponseDto) exist in the `KanbAI-Core/DTOs/` folder and follow the record pattern
- [ ] All responses use the `ApiResponse<T>` wrapper for consistency
- [ ] The controller uses the same `GetCurrentUserId()` pattern to extract the authenticated user from JWT claims

**Edge cases are handled gracefully.**

- [ ] Attempting to create a task in a non-existent column returns 404 Not Found
- [ ] Attempting to create a task with an empty or whitespace-only Title returns 400 Bad Request
- [ ] Attempting to assign a task to a user who is not a project member returns 400 Bad Request with a specific error message
- [ ] If the authenticated user's JWT is valid but they are not a member of the project, the endpoint returns 403 Forbidden (not 404)

---

## 🎯 Out of Scope (Future Enhancements)

These capabilities are explicitly **not** part of this issue and should be deferred to future work:

- **Updating existing tasks** - No PUT /api/Task/{id} endpoint (title, content, or assignment changes)
- **Deleting tasks** - No DELETE /api/Task/{id} endpoint
- **Fetching tasks** - No GET /api/Task/{id} or GET /api/Task/column/{columnId} endpoints (reading task lists)
- **Moving tasks between columns** - No drag-and-drop support or TaskOrder updates (deferred to Issue #47)
- **Task comments** - No API for creating or reading TaskComment entities
- **Task attachments** - No API for uploading or managing Asset entities
- **Task filtering/search** - No query parameters for filtering by assignee, column, or search terms
- **Task metadata** - No due dates, priority levels, status flags, or labels
- **Bulk operations** - No ability to create, update, or delete multiple tasks in a single request

---

## 📚 Reference Materials

**Related Issues:**
- [#43 - Implement Project CRUD Operations](https://github.com/Gulybi/KanbAI-Core/issues/43) - Foundation for ProjectController and project-scoped resources
- [#44 - Implement Project Member Management](https://github.com/Gulybi/KanbAI-Core/issues/44) - Provides user-to-project relationships for authorization
- [#45 - Implement Board Columns API](https://github.com/Gulybi/KanbAI-Core/issues/45) - Provides the columns that tasks belong to
- [#47 - Implement Task Movement Logic](https://github.com/Gulybi/KanbAI-Core/issues/47) - Future feature that depends on tasks existing

**Relevant Files:**
- `KanbAI-Core/Models/Entities/KanbanTask.cs` - Task entity with Title, Content, TaskOrder, ColumnId, AssignedId
- `KanbAI-Core/Models/Entities/BoardColumn.cs` - Column entity with Tasks navigation property
- `KanbAI-Core/Controllers/ColumnController.cs` - Existing controller demonstrating API patterns (authorization, response wrapping)
- `KanbAI-Core/Services/Columns/IColumnService.cs` - Existing service interface demonstrating service layer patterns
- `KanbAI-Core/DTOs/ColumnResponseDto.cs` - Existing DTO demonstrating the record pattern and property naming conventions

**Coding Standards:**
- `.claude/rules/code-standards.md` - Modern C# conventions (file-scoped namespaces, async/await, DI patterns, EF Core optimizations)
- `.claude/rules/security-safety.md` - Authorization checks, input validation, no PII in logs
- `.claude/rules/testing-observability.md` - Unit test structure (AAA pattern), xUnit naming conventions

---

## 🚦 Success Metrics

This feature is considered complete when:
1. A project member can successfully create a task in a board column via the API with automatic TaskOrder assignment
2. A project member can successfully assign a task to another project member during task creation
3. A non-project-member receives a 403 Forbidden when attempting to create a task in a project's column
4. All edge cases (empty title, non-existent column, non-member assignment) return appropriate error codes and messages
5. The implementation passes code review with zero security vulnerabilities related to authorization bypass
6. Integration tests demonstrate the full workflow: create project, create column, create task with and without assignment, verify task order

---

**Prepared by:** Product Manager Agent  
**Date:** 2026-04-27

---

The business context is defined and saved. You can now instruct the staff-engineer to read the context note and design the technical specification.
