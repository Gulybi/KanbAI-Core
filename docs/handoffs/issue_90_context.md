# Issue #90: Enable Task Deletion — DELETE /api/task/{taskId} + TaskDeleted SignalR Event + Attachment Cascade

## Business Value

**Who:** End users of KanbAI (project members) who need to delete tasks that are no longer needed, mistakenly created, duplicates, or obsolete.

**What:** Provide a DELETE endpoint at /api/task/{taskId} that allows project members to delete tasks, removing the task record, cascading to delete all associated attachments (both database rows and physical files), and broadcasting a TaskDeleted SignalR event to all connected clients viewing the same project.

**Why:** This is the last missing primitive in the task lifecycle CRUD operations. Currently:
- Project deletion exists (DELETE /api/project/{id}) and cascades to columns, tasks, and attachments
- Column deletion exists (DELETE /api/column/{id}) and cascades to tasks and attachments
- Task deletion DOES NOT exist — there is no way for any client to delete a task

This ticket blocks KanbAI-Web#96 sub-feature C (delete tasks from the UI). Without this server-side primitive, the frontend cannot implement task deletion, leaving users unable to clean up mistaken, duplicate, or obsolete tasks. The frontend can already delete projects and columns (server-side complete, UI pending), but task delete is the one missing piece.

## Current State vs. Desired State

### Current State (Tasks Cannot Be Deleted Once Created)

**Existing Delete Endpoints:**
- **DELETE /api/project/{id}:**
  - **Location:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Controllers\ProjectController.cs (lines 92-113)
  - **Service:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Services\Projects\ProjectService.cs (lines 141-179)
  - **Behavior:** Returns 204 No Content on success. Only project owners can delete. Broadcasts "ProjectDeleted" event to group project_{projectId}. EF cascade (BoardColumn, KanbanTask, Asset) configured via DeleteBehavior.Cascade in entity configurations.
  - **Authorization:** Checks if user is project member, then checks if user is Owner role. Returns 403 "Only the project owner can delete the project." if not owner.
  - **Error Envelope:** ApiResponse.Fail(message) returns { Success: false, Message: "...", Errors: [] }

- **DELETE /api/column/{id}:**
  - **Location:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Controllers\ColumnController.cs (lines 56-69)
  - **Service:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Services\Columns\ColumnService.cs (lines 101-137)
  - **Behavior:** Returns 204 No Content on success. Any project member can delete. Broadcasts "ColumnDeleted" event to group project_{projectId}. EF cascade (KanbanTask, Asset) configured via DeleteBehavior.Cascade.
  - **Authorization:** Checks if user is project member. Returns 404 "Column not found." if not member (same as if column doesn't exist).
  - **Payload:** ColumnDeletedEventDto { ColumnId: string, ProjectId: string }

**Task Entity & Relationships:**
- **Location:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Models\Entities\KanbanTask.cs
- **Properties:** Title, Content, TaskOrder, ColumnId (FK to BoardColumn), AssignedId (nullable FK to User), Assets (collection), Comments (collection)
- **EF Configuration:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Data\Configurations\KanbanTaskConfiguration.cs (lines 22-25)
  - KanbanTask.Column → BoardColumn.Tasks, OnDelete(DeleteBehavior.Cascade) — when a column is deleted, all its tasks are deleted
  - Asset.KanbanTask → KanbanTask.Assets, OnDelete(DeleteBehavior.Cascade) (in AssetConfiguration.cs, lines 39-42) — when a task is deleted, all its assets are deleted

**Attachment Cascade Behavior (Existing Pattern):**
- **Asset Entity:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Models\Entities\Asset.cs
  - Asset has KanbanTaskId (FK to KanbanTask), StorageKey (physical file path), FileName, MimeType, FileSize, ProcessingStatus
- **EF Cascade:** Asset → KanbanTask is configured with OnDelete(DeleteBehavior.Cascade) in C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Data\Configurations\AssetConfiguration.cs (line 42)
  - This means EF will automatically delete all Asset rows when a task is deleted
- **Physical File Deletion:** Currently NOT handled automatically by EF. When column delete or project delete cascade to tasks and then assets, the Asset DB rows are removed, but the physical files (wwwroot/uploads/) are left orphaned unless explicitly deleted.
  - Issue #84 handoff note (AttachmentController DELETE endpoint) documents the path resolution and deletion pattern: resolve storage root from config, combine with StorageKey, validate no path traversal, delete physical file, log warnings if file missing or locked.
  - The task DELETE endpoint must follow the same pattern: load all Asset records for the task, delete their physical files, then let EF cascade delete the Asset rows.

**SignalR Events (Existing Pattern):**
- **KanbanHub:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Hubs\KanbanHub.cs
  - Group name format: "project_{projectGuid.ToString().ToLowerInvariant()}" (line 43)
  - Clients join a project group via JoinProjectGroup(projectId)
  - All project-scoped events (ProjectDeleted, ColumnDeleted, TaskCreated, TaskMoved, etc.) broadcast to group project_{projectId}
- **ProjectDeleted Event:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\DTOs\ProjectDeletedEventDto.cs
  - Payload: { ProjectId: string }
- **ColumnDeleted Event:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\DTOs\ColumnDeletedEventDto.cs
  - Payload: { ColumnId: string, ProjectId: string }
- **TaskMoved Event:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\DTOs\TaskMovedEventDto.cs
  - Payload: { TaskId: string, OldColumnId: string, NewColumnId: string, OldTaskOrder: int, NewTaskOrder: int, Task: TaskResponseDto }
- **TaskDeleted Event:** DOES NOT EXIST. Must be created.

**Authorization Pattern (Existing in Task Endpoints):**
- **Location:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Controllers\TaskController.cs
- **Pattern:** All task endpoints require JWT authentication ([Authorize] attribute, line 11). GetCurrentUserId() extracts user ID from NameIdentifier claim (lines 147-158), throws UnauthorizedAccessException if missing.
- **Project Membership Check:** Task endpoints load task → column → project → members, then check if user is a project member. If not, return 403 "You are not a member of this project." (e.g., CreateTask lines 39-41, MoveTask lines 66-68, UpdateTaskDescription lines 93-95, ClearTaskDescription lines 118-120).

**Error Response Envelope:**
- **Location:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\DTOs\ApiResponse.cs
- **Shape:** ApiResponse { Success: bool, Message: string?, Errors: IReadOnlyList<string> }
- **Fail Method:** ApiResponse.Fail(message) returns { Success: false, Message: "...", Errors: [] }
- **Controller Usage:** Return StatusCode(403, ApiResponse.Fail("You are not a member of this project.")) or NotFound(ApiResponse.Fail("Task not found."))

**Missing Components:**
- DELETE /api/task/{taskId} endpoint does not exist in TaskController.cs
- TaskDeleted SignalR event DTO does not exist
- No service method exists to delete tasks and broadcast the event
- Physical attachment file deletion is not implemented for task delete (only DB cascade)

### Desired State (Users Can Delete Tasks via API)

**New DELETE Endpoint:**
- **Route:** DELETE /api/task/{taskId}
- **Controller:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\Controllers\TaskController.cs
- **Authorization:**
  - Requires JWT authentication (inherits from controller-level [Authorize] attribute)
  - Requires caller to be a member of the project that owns the task
  - Any project member can delete any task (matches column delete pattern, not restricted to owner like project delete)
- **Success Response:** 204 No Content with empty body
- **Error Responses:**
  - 401 Unauthorized: Missing or invalid JWT (handled by GetCurrentUserId throwing UnauthorizedAccessException)
  - 403 Forbidden: Authenticated user is not a member of the project. Error message: "You are not a member of this project."
  - 404 Not Found: Task does not exist (or was already deleted). Error message: "Task not found."
  - 500 Internal Server Error: Unexpected error (e.g., database failure, file deletion failure). Error message: "An unexpected error occurred."
- **Idempotency:** Deleting a task that was already deleted returns 404 (acceptable idempotency for frontend — see issue #96 acceptance criteria)

**New TaskDeleted SignalR Event:**
- **DTO Location:** C:\temp\KanbAI-Core\KanbAI-Core\KanbAI-Core\DTOs\TaskDeletedEventDto.cs (new file)
- **Payload:** { TaskId: string, ColumnId: string }
  - TaskId: The GUID of the deleted task (as string)
  - ColumnId: The GUID of the column the task was in at the moment of deletion (as string)
  - Rationale: ColumnId is included so clients can scope the removal to a single column without a lookup. By the time a client receives TaskDeleted, the task is already gone from the server, so a lookup would fail. Matches the shape of ColumnDeleted (which carries ProjectId for the same reason).
- **Event Name:** "TaskDeleted"
- **Group:** project_{projectId} (same group pattern as ProjectDeleted, ColumnDeleted, TaskCreated, TaskMoved)
- **Emission Timing:** Emitted AFTER the DB transaction commits (task and assets are deleted) and BEFORE the HTTP 204 returns, matching the existing pattern for ColumnDeleted (ColumnService.cs line 127).

**Attachment Cascade Behavior:**
- **DB Cascade:** EF Core automatically deletes all Asset rows when the task is deleted (DeleteBehavior.Cascade already configured in AssetConfiguration.cs line 42).
- **Physical File Deletion:** The service layer must explicitly delete physical files before the task is deleted:
  1. Load all Asset records for the task (include Assets collection)
  2. For each asset, resolve the physical file path using the same pattern as AttachmentController.GetFile (lines 205-219 in issue #84 handoff):
     - Resolve storage root from configuration (FileStorageOptions.StoragePath)
     - Combine storage root with StorageKey (from database, not user input)
     - Validate that the resolved path does not escape the storage root
  3. Attempt to delete the physical file:
     - If file exists and deletion succeeds, proceed
     - If file does not exist (orphaned DB record), log a warning and proceed (cleanup case)
     - If deletion fails (locked file, permission error), log an error. Behavior decision: either (A) abort the task delete and return 500, or (B) proceed with task delete and log the orphaned file. **Scope decision required before merge** (see acceptance criteria).
  4. Delete the task (which cascades to Asset rows via EF)

**Scope Decision (Required Before Merge):**
- Should cascaded deletes (via project delete / column delete) also emit per-task TaskDeleted events?
  - Option A: YES — when a project or column is deleted, emit ProjectDeleted (or ColumnDeleted), then emit TaskDeleted for each task that was cascaded. This keeps all clients fully synchronized.
  - Option B: NO — cascaded deletes only emit the parent event (ProjectDeleted or ColumnDeleted). Clients are expected to remove all child entities (columns, tasks) based on the parent event. This reduces SignalR traffic but requires more client-side logic.
  - **Either answer is acceptable; ambiguity is not.** The frontend (KanbAI-Web#96) will respect whichever is chosen. This decision must be explicitly stated in the closing comment of issue #90.

## Acceptance Criteria

These criteria are observable from an API client (Postman / curl / a SignalR test client) without reading source code.

### Route Behavior

1. DELETE /api/task/{taskId} returns 204 No Content with an empty body on success for a task owned by a project the caller is a member of.
2. DELETE /api/task/{taskId} with no Authorization header returns 401 Unauthorized.
3. DELETE /api/task/{taskId} for a task whose project the caller is NOT a member of returns 403 Forbidden with body { "success": false, "message": "You are not a member of this project.", "errors": [] }.
4. DELETE /api/task/{taskId} for a non-existent task ID returns 404 Not Found with body { "success": false, "message": "Task not found.", "errors": [] }.
5. Calling DELETE /api/task/{taskId} twice in a row (valid task → then already-deleted task) returns 204 on the first call, then 404 on the second call.

### SignalR Event Behavior

6. A successful DELETE /api/task/{taskId} broadcasts a "TaskDeleted" event to the SignalR group "project_{projectId}".
7. The "TaskDeleted" payload contains "taskId" (matching the deleted task) and "columnId" (the column the task was in at the moment of deletion).
8. The "TaskDeleted" event is emitted AFTER the DB transaction commits and the task is no longer readable via GET /api/task/project/{projectId}.
9. A client connected to group "project_A" does NOT receive "TaskDeleted" events for tasks in "project_B".
10. No "TaskDeleted" event is emitted on 401, 403, or 404 responses (only on successful delete).

### Attachment Cascade Behavior

11. After DELETE /api/task/{taskId} succeeds, GET on any attachment that belonged to that task returns 404 Not Found.
12. After DELETE /api/task/{taskId} succeeds, the underlying physical attachment files for that task are removed from disk (or, if deferred to a cleanup job, the behavior is explicitly documented in the closing comment).
13. No orphaned attachment DB rows remain that reference the deleted taskId (DB rows are automatically deleted via EF cascade).

### Regression Safety

14. Deleting a project (DELETE /api/project/{id}) still cascade-deletes its columns, tasks, and attachments, and still returns 204 No Content.
15. Deleting a column (DELETE /api/column/{id}) still cascade-deletes its tasks and their attachments, and still returns 204 No Content.
16. Existing "ProjectDeleted" and "ColumnDeleted" SignalR contracts are unchanged (payload shape, group, emission timing).

### Scope Decision (Required Before Merge)

17. The implementation explicitly states in the closing comment of issue #90 whether cascaded deletes (via project delete / column delete) now also emit per-task "TaskDeleted" events. Either answer (YES or NO) is acceptable; ambiguity is not. The frontend (KanbAI-Web#96) will respect whichever is chosen.

## Out of Scope

- Soft delete / restore / trash functionality for tasks (tasks are permanently deleted)
- Per-attachment delete endpoint changes (already implemented in issue #84, unchanged here)
- Frontend wiring (handled entirely by KanbAI-Web#96)
- Any change to project delete or column delete semantics beyond the regression-safety assertions above
- Authorization restrictions (e.g., only task creator or assignee can delete) — any project member can delete any task in the project

## Related Issues & Dependencies

- **Blocks:** KanbAI-Web#96 sub-feature C (delete tasks from the UI)
- **Does Not Block:** KanbAI-Web#96 sub-features A (project delete) and B (column delete) — those already ship server-side
- **Related:** Issue #84 (DELETE /api/attachment/{assetId}) — established the physical file deletion pattern that task delete must follow for attachments
- **Milestone:** None

## Notes for the Staff Engineer

- The error envelope shape ({ "success": false, "message": "...", "errors": [] }) and the per-status-code message strings are load-bearing. The frontend's error mapping logic (KanbAI-Web#96) maps these verbatim to user-visible copy. Changing the strings later will silently change what users read.
- ColumnId in the TaskDeleted payload is deliberately chosen over "let the client look it up." By the time a client receives TaskDeleted, the task is already gone from the server, so a lookup would fail. This matches the shape of ColumnDeleted (which carries ProjectId).
- The physical file deletion behavior decision (abort task delete on file lock vs. proceed and log orphan) must be documented in the tech spec and closing comment.
- The cascaded-delete SignalR emission decision (emit TaskDeleted for each cascaded task vs. rely on parent event only) must be documented in the tech spec and closing comment.
