# Issue #83: Add Task Description API Endpoints (Set, Update, Clear)

## Business Value

**Who:** Project team members using KanbAI to collaborate on Kanban boards

**What:** Enable users to manage the description field (free-form text body) of Kanban tasks through dedicated API endpoints after task creation

**Why:** Currently, users can only set a task's description (`KanbanTask.Content`) during initial task creation via `POST /api/task/column/{columnId}`. Once a task is created, there is no way to:
- Add a description to an existing task that was created without one
- Update an existing description to reflect new details or requirements
- Remove a description that is no longer relevant or was added by mistake

This gap forces users to either:
- Delete and recreate tasks to change descriptions (losing task history, order, and associated data)
- Live with outdated or incorrect descriptions
- Use workarounds like task comments to store what should be task body content

Providing dedicated endpoints to manage task descriptions enables natural task lifecycle workflows where requirements are clarified and documented incrementally as the team collaborates.

## Current State vs. Desired State

### Current State

**Task Entity:**
- Location: `KanbAI-Core/KanbAI-Core/Models/Entities/KanbanTask.cs`
- The `Content` property exists as a nullable `string?` (line 6) and is persisted to the database
- No validation constraints on the entity itself (length, format)

**Task Controller:**
- Location: `KanbAI-Core/KanbAI-Core/Controllers/TaskController.cs`
- Existing endpoints:
  - `POST /api/task/column/{columnId}` - Creates a task (lines 23-51)
  - `PUT /api/task/{taskId}/move` - Repositions a task (lines 53-78)
  - `GET /api/task/project/{projectId}` - Retrieves all tasks for a project (lines 80-99)
- No endpoints exist to modify task fields after creation (including Content)

**Task Service:**
- Location: `KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs`
- Interface: `KanbAI-Core/KanbAI-Core/Services/Tasks/ITaskService.cs`
- Current methods:
  - `CreateTaskAsync` - Sets `Content` from `CreateTaskDto.Content` during task creation (line 90 in TaskService.cs)
  - `MoveTaskAsync` - Only updates `ColumnId` and `TaskOrder`, does not touch Content
  - `GetProjectTasksAsync` - Returns tasks with Content in the response DTO

**DTOs:**
- `CreateTaskDto` (KanbAI-Core/KanbAI-Core/DTOs/CreateTaskDto.cs):
  - Contains optional `Content` property (line 11: `public string? Content { get; init; }`)
  - No validation attributes on Content (can be null, unlimited length)
- `TaskResponseDto` (KanbAI-Core/KanbAI-Core/DTOs/TaskResponseDto.cs):
  - Contains `Content` property (line 7: `public string? Content { get; init; }`)
  - Used in all task-related API responses

**Authorization Pattern:**
- All task operations enforce project membership authorization
- Example in `TaskService.CreateTaskAsync` (lines 48-56): Checks if `userId` exists in `column.Project.Members`
- Returns `UserNotProjectMember` result enum variant when authorization fails
- Controller maps this to `403 Forbidden` (lines 39-41 in TaskController.cs)

**Result Handling Pattern:**
- Services return tuples: `(TaskResponseDto? data, ResultEnum result)`
- Result enums (e.g., `CreateTaskResult`, `MoveTaskResult`) define success and failure cases
- Controllers use switch expressions to map enum variants to HTTP status codes
- Example: `CreateTaskResult.cs` (KanbAI-Core/KanbAI-Core/Services/Tasks/CreateTaskResult.cs)

**SignalR Broadcasting:**
- Pattern established in issue #63
- `TaskService` has `IHubContext<KanbanHub>` injected (line 15 in TaskService.cs)
- `BroadcastAsync` helper method (line 331 in TaskService.cs):
  - Sends events to project-specific SignalR groups
  - Group naming: `"project_{projectId.ToString().ToLowerInvariant()}"`
- Existing task events:
  - `TaskCreated` - Broadcast after `CreateTaskAsync` (line 106 in TaskService.cs)
  - `TaskMoved` - Broadcast after `MoveTaskAsync` (line 284 in TaskService.cs)
- No `TaskUpdated` event exists yet

### Desired State

**New API Endpoints:**
Users can manage task descriptions through two new endpoints:

| Endpoint | Purpose | Success Response |
|----------|---------|------------------|
| `PUT /api/task/{taskId}/description` | Set or update the task description (upsert behavior) | `200 OK` with `ApiResponse<TaskResponseDto>` containing updated task |
| `DELETE /api/task/{taskId}/description` | Clear the description (sets `Content` to `null`) | `204 No Content` |

**Request/Response Contract:**

`PUT /api/task/{taskId}/description` request body:
```json
{
  "content": "Markdown or plain text body here"
}
```

Response (200 OK):
```json
{
  "success": true,
  "data": {
    "id": "...",
    "title": "...",
    "content": "Markdown or plain text body here",
    "taskOrder": 0,
    "columnId": "...",
    "assignedId": null,
    "createdAt": "2026-05-08T...",
    "updatedAt": "2026-05-08T..."
  },
  "message": "Task description updated successfully."
}
```

`DELETE /api/task/{taskId}/description` response:
- `204 No Content` (no body)

**Authorization & Validation:**
- Both endpoints require JWT authentication via `[Authorize]` attribute
- Caller must be a member of the project that owns the task (403 Forbidden otherwise)
- Task must exist (404 Not Found otherwise)
- `PUT` endpoint validation:
  - `content` must be non-null (400 Bad Request if null)
  - `content` must not exceed 10,000 characters (400 Bad Request if over limit)
  - Trim trailing whitespace but preserve internal formatting (newlines, indentation)

**Real-Time Broadcasting:**
- Both operations emit a `TaskUpdated` SignalR event to the project's group
- Event payload: The updated `TaskResponseDto`
- Consistent with existing `TaskCreated` and `TaskMoved` event patterns (established in issue #63)
- Ensures all connected project members see description changes immediately

## Acceptance Criteria

1. `PUT /api/task/{taskId}/description` with valid content updates `KanbanTask.Content` in the database and returns `200 OK` with the updated task in `ApiResponse<TaskResponseDto>` format
2. `DELETE /api/task/{taskId}/description` sets `KanbanTask.Content` to `null` in the database and returns `204 No Content`
3. When a non-project-member calls either endpoint, the API returns `403 Forbidden` with structured error message
4. When either endpoint is called with a non-existent `taskId`, the API returns `404 Not Found` with structured error message
5. When `PUT /api/task/{taskId}/description` is called with `null` content, the API returns `400 Bad Request` with structured error message
6. When `PUT /api/task/{taskId}/description` is called with content exceeding 10,000 characters, the API returns `400 Bad Request` with structured error message
7. `PUT /api/task/{taskId}/description` trims trailing whitespace from the content before persisting
8. Integration tests cover: successful update, successful clear, 403 (non-member), 404 (task not found), 400 (null content), 400 (oversize content)
9. `TaskUpdated` SignalR event is broadcast to the project group after both `PUT` and `DELETE` operations, containing the updated task DTO
10. The `updatedAt` timestamp on `KanbanTask` (inherited from `BaseEntity`) is updated when either operation succeeds

## Open Questions / Clarifications

None. The issue body provides complete specification.

## Related Issues / Milestone Context

- **Issue #82**: Added task read endpoints (`GET /api/task/project/{projectId}`) for board hydration on page load - this issue builds on that work by adding task mutation endpoints
- **Issue #63**: Established SignalR broadcasting pattern for real-time collaboration - this issue reuses the `BroadcastAsync` pattern and introduces the `TaskUpdated` event type
- Both issues demonstrate the existing patterns for authorization (project membership checks), result handling (enum → switch expression), and real-time events

## Notes for Staff Engineer

- The project uses result enums (like `CreateTaskResult`, `MoveTaskResult`) to discriminate success and failure cases. You'll need to define a new result enum for description management operations.
- The `TaskService` already has `IHubContext<KanbanHub>` injected and a `BroadcastAsync` helper. The `TaskUpdated` event type does not exist yet - this issue introduces it.
- Follow the established pattern: service method returns `(TaskResponseDto? data, ResultEnum result)`, controller uses switch expression to map to HTTP responses.
- `DELETE` returning `204 No Content` differs from other endpoints (which return `200 OK` with ApiResponse). This is intentional and follows REST conventions for DELETE operations.
- The 10,000 character limit is a reasonable ceiling for task descriptions. If needed, this can be configured later.
