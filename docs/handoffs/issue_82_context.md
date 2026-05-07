# Issue #82: Add Task-Read Endpoint(s) So the Board Can Hydrate Tasks on Page Load

## Business Value

**Who:** Any authenticated user who opens, refreshes, or navigates back to a project board. Includes project owners, project members, and any team member working collaboratively on a Kanban board.

**What:** Provide an API endpoint that returns all tasks for a given project (or column) so the frontend can display historical task data on page load rather than showing an empty board after every refresh.

**Why:** The KanbAI backend persists tasks correctly via POST /api/task/column/{columnId}, but there is no HTTP endpoint to read tasks back. After a page refresh, the Angular client successfully loads columns (from GET /api/column/project/{projectId}) but has no way to fetch the tasks that belong to them. As a result, the board appears empty even though the database holds all the task data. Tasks only re-appear in the UI if a fresh TaskCreated or TaskMoved SignalR event fires after the client has connected — historical tasks are invisible.

This is the single missing piece that breaks the illusion of persistence end-to-end. Without this endpoint:
- Users perceive the system as "losing" their tasks after every refresh
- The product feels broken and unreliable despite correct data persistence
- Downstream features (task description editing in KanbAI-Web issue #83, task editing, task deletion, task assignment) cannot function because they depend on tasks being visible in the UI
- The frontend cannot implement a proper task hydration flow in BoardPageComponent.ngOnInit

Shipping this endpoint:
- Closes the "persistence illusion" gap and makes the product feel real
- Unblocks KanbAI-Web issue #83 (task description read/edit), which requires tasks to be visible beyond the current session
- Establishes the read pattern that every future task-level feature (edit title, assign, comment, due date, labels) will need
- Sets the contract, pagination strategy, and authorization pattern for all future task operations

## Current State vs. Desired State

### Current State

**TaskController Endpoints:**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs
- **Existing Routes:**
  - POST /api/task/column/{columnId} — Creates a task in a column, returns TaskResponseDto on success
  - PUT /api/task/{taskId}/move — Moves a task to a different column or reorders within the same column, returns TaskResponseDto on success
- **Missing:** No GET endpoint exists. No way to retrieve tasks by project or by column.

**TaskResponseDto Contract:**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/DTOs/TaskResponseDto.cs
- **Shape:** The DTO is already defined and used consistently in create/move operations:
  - Id (string, required)
  - Title (string, required)
  - Content (string?, nullable)
  - TaskOrder (int, required)
  - ColumnId (string, required)
  - AssignedId (string?, nullable)
  - CreatedAt (DateTimeOffset, required)
  - UpdatedAt (DateTimeOffset, required)
- **Note:** This exact shape MUST be preserved. The frontend already consumes this DTO on create/move and projects it into BoardTask via standardized mapping.

**KanbanTask Entity:**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Entities/KanbanTask.cs
- **Key Properties:**
  - Title (string)
  - Content (string?, nullable)
  - TaskOrder (int)
  - ColumnId (Guid, foreign key to BoardColumn)
  - AssignedId (Guid?, nullable, foreign key to User)
  - Column (BoardColumn, navigation property)
  - AssignedUser (User?, navigation property)
- **Note:** Entity has navigation to BoardColumn → Project via Column.Project, which enables project membership authorization checks.

**TaskService Interface:**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs
- **Existing Methods:**
  - CreateTaskAsync(columnId, dto, userId) → Returns (TaskResponseDto?, CreateTaskResult)
  - MoveTaskAsync(taskId, dto, userId) → Returns (TaskResponseDto?, MoveTaskResult)
- **Missing:** No method for reading tasks by project or by column.

**Authorization Pattern (Established in ColumnController):**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/ColumnController.cs
- **Example:** GET /api/column/project/{projectId} verifies project membership before returning columns
- **Implementation:** Calls IColumnService.GetProjectColumnsAsync(projectId, userId), which returns null if the project doesn't exist or the user is not a member, resulting in a 404 response
- **Pattern:** Project-scoped reads follow the convention: authenticate user → verify project membership → return resource or 404 if not found/not authorized

**Frontend Context (KanbAI-Web):**
- **Current Behavior:**
  - BoardPageComponent.ngOnInit only calls loadColumns(projectId)
  - No loadTasks call exists because there is no backend endpoint to consume
  - BoardStateService has reconcilers for TaskCreated/TaskMoved SignalR events and HTTP success appliers for create, but no bulk hydration path
  - Tasks are visible ONLY if they were created/moved during the current session or if a SignalR event fires while the user is connected
- **Reproduction:**
  1. User signs in, opens a project board
  2. User creates several tasks via the per-column "Add task" flow → tasks appear (optimistic local apply + SignalR echo)
  3. User refreshes the browser (F5) or navigates away and back to /projects/{projectId}
  4. Board loads columns successfully but every column is empty
  5. Database still holds every task; client has no way to fetch them

**What's Missing:**
- No GET endpoint in TaskController for reading tasks
- No service method in ITaskService for fetching tasks by project or column
- No established pattern for whether tasks should be fetched project-wide (Option A) or column-by-column (Option B)

### Desired State

**One of the Following Read Shapes (Backend/Staff Engineer Decision):**

**Option A (Recommended): One Project-Scoped Read**
- **Route:** GET /api/task/project/{projectId}
- **Returns:** ApiResponse<List<TaskResponseDto>>
- **Behavior:**
  - Returns every task across every column of the project
  - Tasks sorted by (columnId, taskOrder ASC) so frontend can bucket client-side
  - Single HTTP round-trip on board load
  - Authorization: same membership check as column list (403 on non-member, 404 on non-existent project)
- **Advantages:**
  - Single round-trip, simpler frontend logic
  - Follows the pattern established by GET /api/column/project/{projectId}

**Option B: Column-Scoped Read**
- **Route:** GET /api/task/column/{columnId}
- **Returns:** ApiResponse<List<TaskResponseDto>>
- **Behavior:**
  - Returns tasks for a single column
  - Tasks sorted by taskOrder ASC
  - Frontend calls this endpoint once per column (N requests for N columns)
  - Authorization: verify user is a member of the column's project (403 on non-member, 404 on non-existent column)
- **Advantages:**
  - More granular, aligns with RESTful resource hierarchy
  - Could support future lazy-loading patterns

**Option C: Extended Column List Endpoint**
- **Route:** Modify existing GET /api/column/project/{projectId} to include tasks
- **Returns:** ColumnResponseDto with embedded tasks
- **Note:** This option would require a breaking change to the column endpoint and is NOT recommended unless explicitly chosen by the Staff Engineer

**In All Options:**
- Response MUST use the existing TaskResponseDto shape verbatim — no new fields, no reshaping
- Response MUST be wrapped in the standard ApiResponse<T> envelope (success, data, message, errors)
- JWT auth required; membership enforced server-side
- Sorting is server-authoritative: tasks returned sorted ascending by taskOrder within each columnId, so the client can trust and render directly without re-sorting
- Tasks in deleted columns MUST NOT be returned
- 404 when the project (or column) does not exist or the caller was never a member
- 403 when the caller is not a member of the project

**Performance Considerations:**
- Query must use .AsNoTracking() for read-only data
- Query must prevent N+1 issues by using .Include() for necessary navigation properties
- Query should be a single indexed query on columnId or projectId (via column.projectId)
- Expected to handle 500-task boards without pagination (pagination is out of scope for MVP)

### Out of Scope

- Task update/edit endpoint (tracked separately, required for KanbAI-Web issue #83 Phase 2)
- Task delete endpoint (separate ticket)
- TaskUpdated/TaskDeleted SignalR events (separate ticket)
- Pagination (boards expected to hold hundreds, not tens of thousands of tasks; full read acceptable for MVP)
- Filtering/search (not needed for board hydration)
- Archived/soft-deleted tasks (no archival model exists yet)

## Milestone Context

This issue is **not explicitly part of a milestone** in the GitHub issue metadata, but it is a **critical blocker** for the frontend feature tracked in KanbAI-Web issue #83 (task description read/edit). Without this endpoint, the frontend cannot display tasks after a page refresh, making any task-level feature impossible to use beyond a single session.

### Related Issues

| Repository | Issue # | Title | State | Relationship |
|------------|---------|-------|-------|--------------|
| **KanbAI-Core** | **#82** | **Add Task-Read Endpoint(s)** | **Open** | **THIS ISSUE** |
| KanbAI-Web | #83 | Task Description Read/Edit | Open | **Blocked by #82** — Requires tasks to be visible after page load |

### Implementation Context

The task creation and movement features have been fully implemented:
- POST /api/task/column/{columnId} creates tasks with proper authorization and SignalR broadcasting
- PUT /api/task/{taskId}/move handles task reordering and cross-column moves
- TaskResponseDto is consistently returned by both endpoints
- Frontend has complete state management for TaskCreated/TaskMoved events

Issue #82 completes the "read" portion of the CRUD contract and enables the frontend to hydrate board state on cold load.

## Acceptance Criteria

### 1. Endpoint Exists and Is Accessible
- [ ] A new GET endpoint is defined at one of: /api/task/project/{projectId} OR /api/task/column/{columnId} (OR extended column endpoint if Option C is chosen)
- [ ] Endpoint is decorated with [HttpGet] and appropriate route attribute
- [ ] Endpoint is protected by [Authorize] attribute (JWT authentication required)
- [ ] Endpoint accepts either Guid projectId or Guid columnId as a route parameter
- [ ] Endpoint includes CancellationToken cancellationToken parameter for graceful cancellation support

### 2. Authentication and Authorization
- [ ] Endpoint calls GetCurrentUserId() to extract the authenticated user's ID from the JWT token
- [ ] Endpoint verifies the authenticated user is a member of the project that owns the tasks
- [ ] If the project (or column) does not exist, endpoint returns 404 Not Found with ApiResponse.Fail("Project not found.") or ApiResponse.Fail("Column not found.")
- [ ] If the authenticated user is not a member of the project, endpoint returns 403 Forbidden with ApiResponse.Fail("You are not a member of this project.")

### 3. Task Query
- [ ] Endpoint queries KanbanTask entities for the specified project or column
- [ ] Query uses .AsNoTracking() since the results are read-only
- [ ] Query uses .Include() to load necessary navigation properties (Column, AssignedUser) to avoid N+1 queries
- [ ] Tasks belonging to deleted columns are NOT returned (if soft-delete exists; otherwise this is implicitly handled by foreign key constraints)

### 4. Result Ordering
- [ ] Results are ordered by (ColumnId, TaskOrder ASC) for project-scoped reads, or by TaskOrder ASC for column-scoped reads
- [ ] Ordering is server-authoritative so the client can render directly without re-sorting

### 5. Response Mapping
- [ ] KanbanTask entities are mapped to TaskResponseDto objects
- [ ] All required properties are populated: Id, Title, Content, TaskOrder, ColumnId, AssignedId, CreatedAt, UpdatedAt
- [ ] The TaskResponseDto shape is identical to the one returned by POST /api/task/column/{columnId} and PUT /api/task/{taskId}/move (no drift)

### 6. Success Response
- [ ] Endpoint returns 200 OK with ApiResponse<List<TaskResponseDto>>.Ok(data, "Tasks retrieved successfully.") or similar message
- [ ] Response body includes the full list of TaskResponseDto objects

### 7. Empty List Handling
- [ ] If the project (or column) exists but has no tasks, endpoint returns 200 OK with an empty array (not 404)
- [ ] Response message is "Tasks retrieved successfully." even when the array is empty

### 8. Error Responses
- [ ] 404 Not Found is returned when the project (or column) does not exist
- [ ] 403 Forbidden is returned when the user is not a project member
- [ ] 401 Unauthorized is returned when JWT token is missing or expired
- [ ] Error responses include ApiResponse.Fail(message) with a clear error message

### 9. Logging
- [ ] Successful requests are logged at Information level with structured logging: "User {UserId} retrieved {Count} tasks for project {ProjectId}" (or column)
- [ ] Authorization failures are logged at Warning level: "User {UserId} attempted to access tasks for project {ProjectId} without authorization"
- [ ] Project/column not found scenarios are logged at Information level: "User {UserId} requested tasks for non-existent project {ProjectId}"

### 10. Security: Authorization Check
- [ ] Endpoint verifies project membership BEFORE querying tasks
- [ ] Endpoint does not expose task data to users who are not project members
- [ ] Endpoint does not accept any filter or query parameters that could bypass authorization

### 11. Frontend Integration Ready
- [ ] The response format matches the TaskResponseDto structure expected by the frontend (same as create/move responses)
- [ ] The endpoint returns an array (not a single object) to support multiple tasks
- [ ] The endpoint does not require any special query parameters or headers beyond standard JWT authentication

### 12. Consistent with Existing Patterns
- [ ] Endpoint follows the same authorization pattern as GET /api/column/project/{projectId} (project membership check)
- [ ] Endpoint follows the same response pattern as other TaskController endpoints (ApiResponse wrapper)
- [ ] Endpoint follows the same error handling pattern as other controllers (404 for not found, 403 for unauthorized)

### 13. No Breaking Changes
- [ ] Adding this endpoint does not modify or break existing POST /api/task/column/{columnId} or PUT /api/task/{taskId}/move endpoints
- [ ] The new endpoint is purely additive and does not change any existing behavior
- [ ] Existing tests for TaskController continue to pass

### 14. Database Query Efficiency
- [ ] Query uses a single database roundtrip (or minimal roundtrips) to fetch both authorization data and tasks
- [ ] Query prevents N+1 issues by including necessary navigation properties
- [ ] Query is performant for boards with 500+ tasks (no unnecessary joins or data loading)

### 15. Testing Coverage
- [ ] Integration/controller tests cover: (a) happy path returns DTOs sorted correctly, (b) 403 for non-member, (c) 404 for missing project/column, (d) empty-project returns 200 with data: [] (not 404), (e) deleted columns' tasks are excluded (if soft-delete exists)
- [ ] Tests verify that the TaskResponseDto shape matches the DTOs returned by create/move endpoints
- [ ] Tests verify that tasks are returned in the correct sort order
