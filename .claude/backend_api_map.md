# KanbAI Core — Backend API map

This document describes HTTP routes exposed by `KanbAI-Core`, request/response shapes, and DTO definitions. JSON uses ASP.NET Core’s default serializer settings (**camelCase** property names in JSON unless configured otherwise).

## Authentication

| Aspect | Detail |
|--------|--------|
| Scheme | JWT Bearer (`Authorization: Bearer <token>`) |
| User identity | Controllers read `ClaimTypes.NameIdentifier` as `Guid` |
| Anonymous | Health check; auth register/login |
| SignalR | Browser WebSocket connections may pass the JWT as `?access_token=<jwt>` query string on requests to `/hubs/*` (handled by `JwtBearerEvents.OnMessageReceived` in `Program.cs`). |

Endpoints marked **JWT** require a valid Bearer token.

---

## Standard response wrapper (`ApiResponse`)

Most endpoints return `ApiResponse` or `ApiResponse<T>`.

| Property | Type | Description |
|----------|------|-------------|
| `success` | `boolean` | Operation outcome |
| `message` | `string \| null` | Optional human-readable message |
| `errors` | `string[]` | Validation or error messages (often empty on success) |
| `data` | `T \| null` | Payload when using `ApiResponse<T>` |

Factories in code: `ApiResponse.Ok(...)`, `ApiResponse<T>.Ok(data, ...)`, `ApiResponse.Fail(...)`.

**Auth** endpoints (`/api/auth/register`, `/api/auth/login`) return **`AuthResponseDto` directly**, not wrapped in `ApiResponse`.

---

## Endpoints

### Health

| Method | Route | Auth | Request body | Success response | Notes |
|--------|-------|------|--------------|------------------|-------|
| `GET` | `/api/health` | None | — | `200` — `ApiResponse` (`success`, `message`, `errors`) | Liveness |

---

### Auth

| Method | Route | Auth | Request body | Success | Error responses |
|--------|-------|------|--------------|---------|-----------------|
| `POST` | `/api/auth/register` | None | `RegisterRequestDto` | `201` — `AuthResponseDto` (raw body) | `400` — `{ "message": "Email is already registered." }` |
| `POST` | `/api/auth/login` | None | `LoginRequestDto` | `200` — `AuthResponseDto` (raw body) | `401` — `{ "message": "Invalid email or password." }` |

---

### Projects

| Method | Route | Auth | Request body | Success response |
|--------|-------|------|--------------|-------------------|
| `POST` | `/api/project` | JWT | `CreateProjectDto` | `201` — `ApiResponse<ProjectResponseDto>` |
| `GET` | `/api/project` | JWT | — | `200` — `ApiResponse<List<ProjectResponseDto>>` |
| `GET` | `/api/project/{id}` | JWT | — | `200` — `ApiResponse<ProjectResponseDto>` / `404` |
| `PUT` | `/api/project/{id}` | JWT | `UpdateProjectDto` | `200` — `ApiResponse<ProjectResponseDto>` / `404` |
| `DELETE` | `/api/project/{id}` | JWT | — | `204` / `403` / `404` |
| `GET` | `/api/project/{projectId}/members` | JWT | — | `200` — `ApiResponse<List<MemberResponseDto>>` / `404` |
| `POST` | `/api/project/{projectId}/members` | JWT | `AddMemberDto` | `201` — `ApiResponse<MemberResponseDto>` / `400` / `403` / `404` |
| `DELETE` | `/api/project/{projectId}/members/{userId}` | JWT | — | `204` / `400` / `403` / `404` |

**All project endpoints** may additionally return `401` — `ApiResponse.Fail("Invalid or missing user ID in token.")` — when the JWT is present but the `NameIdentifier` claim is missing or not a valid Guid. Other controllers instead surface this as a thrown `UnauthorizedAccessException` handled by the global exception middleware.

**Delete project:** `403` when caller is not owner; `404` when not found.

**List members:** returns `404 "Project not found."` both when the project is missing AND when the caller is not a member. Frontend maps this to "This project no longer exists."

**Add member:** `403` — only owner; `400` — user not found (`"User not found."` or `"No user found with email address: {email}"` — match by prefix), already member (`"User is already a member of this project."`), or missing input (`"Either UserId or Email is required."` / `"Provide either UserId or Email, not both."`); `404` — project not found.

**Remove member:** `403` — only owner (`"Only the project owner can remove members."`); `400` — cannot remove last owner (`"Cannot remove the last owner from the project."`) or target is not a member (`"User is not a member of this project."`); `404` — project not found (frontend treats this as success: already gone server-side).

---

### Columns

| Method | Route | Auth | Request body | Success response |
|--------|-------|------|--------------|-------------------|
| `GET` | `/api/column/project/{projectId}` | JWT | — | `200` — `ApiResponse<List<ColumnResponseDto>>` / `404` |
| `POST` | `/api/column/project/{projectId}` | JWT | `CreateColumnDto` | `201` — `ApiResponse<ColumnResponseDto>` / `404` |
| `DELETE` | `/api/column/{id}` | JWT | — | `204` / `404` |

---

### Tasks

| Method | Route | Auth | Request body | Success response |
|--------|-------|------|--------------|-------------------|
| `POST` | `/api/task/column/{columnId}` | JWT | `CreateTaskDto` | `201` — `ApiResponse<TaskResponseDto>` |
| `PUT` | `/api/task/{taskId}/move` | JWT | `MoveTaskDto` | `200` — `ApiResponse<TaskResponseDto>` |
| `PUT` | `/api/task/{taskId}/description` | JWT | `UpdateTaskDescriptionDto` | `200` — `ApiResponse<TaskResponseDto>` |
| `DELETE` | `/api/task/{taskId}/description` | JWT | — | `204` |

**Create task** failures (examples): `404` column not found; `403` not a project member; `400` missing title, assignee not found, or assignee not a member.

**Move task** failures (examples): `404` task or target column; `403` not a member; `400` cross-project move or invalid `taskOrder`.

**Update task description** failures: `400` — `"Task description cannot be empty."` (whitespace-only after trim) or `"Task description cannot exceed 10,000 characters."`; `403` — `"You are not a member of this project."`; `404` — `"Task not found."`. On success, the task's `content` is replaced with the trimmed value and a `TaskUpdated` SignalR event is broadcast.

**Clear task description** failures: `403` — `"You are not a member of this project."`; `404` — `"Task not found."`. On success, the task's `content` is set to `null`, the endpoint returns `204 No Content`, and a `TaskUpdated` SignalR event is broadcast.

---

### Attachments

Attachments are uploaded against a `KanbanTask` and stored as `Asset` records. Processing is asynchronous: the HTTP response returns as soon as the DB row is committed (status `Pending`), and subsequent status transitions are pushed over SignalR (see [Server-sent events](#server-sent-events-server--client)).

| Method | Route | Auth | Request body | Success response |
|--------|-------|------|--------------|-------------------|
| `POST` | `/api/attachment/task/{taskId}` | JWT | `multipart/form-data` with single `file` field (`IFormFile`) | `201` — `ApiResponse<AssetResponseDto>` |
| `GET` | `/api/attachment/task/{taskId}` | JWT | — | `200` — `ApiResponse<IEnumerable<AssetResponseDto>>` |
| `GET` | `/api/attachment/{assetId}` | JWT | — | `200` — raw file stream with `Content-Type` set from stored MIME type, `Content-Disposition: inline` for images, `attachment` otherwise |
| `DELETE` | `/api/attachment/{assetId}` | JWT | — | `204` |

**Upload constraints**

| Constraint | Value |
|------------|-------|
| Max file size | 10 MB (10,485,760 bytes) |
| Allowed extensions | `.jpg`, `.jpeg`, `.png`, `.gif`, `.pdf`, `.docx`, `.xlsx`, `.txt` |
| MIME type validation | Enforced per-extension whitelist |
| Filename sanitization | Path traversal and unsafe characters rejected |

**Upload failures**: `400` — `"File is required."`, `"File cannot be empty."`, `"File name is invalid."`, `"File type is not allowed."`; `403` — `"You are not a member of this project."`; `404` — `"Task not found."`; `413` — `"File size exceeds maximum allowed size."`; `500` — `"Failed to save file. Please try again."`.

**List failures**: `403` — `"You are not authorized to access this task's attachments."` (caller is not a member of the owning project); `404` — `"Task not found."`. Only `Completed` assets are returned, ordered by `createdAt` descending; assets in `Pending`/`Processing`/`Failed` state are omitted.

**Download failures**: `400` — `"File is still being processed."` (asset still `Pending`/`Processing`); `403` — `"You are not authorized to access this file."` (caller is not a member of the owning project); `404` — `"File not found."` (asset missing, physical file missing, or path escaped storage root) and `"File upload failed."` (asset is `Failed`).

**Delete failures**: `403` — `"You are not authorized to delete this file."` (caller is not a member of the owning project); `404` — `"File not found."` (asset missing); `500` — `"Failed to delete file. Please try again."` (I/O error removing the physical file). On success, the DB row is removed and an `AttachmentDeleted` SignalR event is broadcast. If the DB row exists but the physical file is already missing, the DB row is still removed and the delete succeeds.

---

## Real-time updates (SignalR)

A SignalR hub is exposed at `/hubs/kanban` and is used by the backend to push board updates to connected clients. Authentication is required (JWT Bearer, or `?access_token=<jwt>` query string for browsers).

### Hub methods (client → server)

| Method | Parameters | Behaviour | Errors |
|--------|------------|-----------|--------|
| `JoinProjectGroup` | `projectId: string` (Guid) | Adds the current connection to the group `project_{projectId lowercase}` so it receives that project's broadcasts. | `HubException "Project ID is required."` on null/empty; `HubException "Invalid project ID format."` on non-Guid input. |
| `LeaveProjectGroup` | `projectId: string` (Guid) | Removes the connection from the project's group. | Same as above. |

The hub does **not** verify project membership on `JoinProjectGroup` — server-side authorization is enforced at the HTTP layer where the mutations happen, and broadcasts are only emitted after a successful mutation. Clients should call `LeaveProjectGroup` when navigating away from a board. SignalR cleans up group memberships automatically on disconnect.

### Server-sent events (server → client)

All events are broadcast to the group `project_{projectId lowercase}`. Payloads use camelCase property names (default ASP.NET Core JSON serialization).

| Event name | Triggered by | Payload |
|------------|--------------|---------|
| `ProjectUpdated` | `PUT /api/project/{id}` | `ProjectUpdatedEventDto` |
| `ProjectDeleted` | `DELETE /api/project/{id}` | `ProjectDeletedEventDto` |
| `MemberAdded` | `POST /api/project/{projectId}/members` | `MemberResponseDto` |
| `MemberRemoved` | `DELETE /api/project/{projectId}/members/{userId}` | `MemberRemovedEventDto` |
| `ColumnCreated` | `POST /api/column/project/{projectId}` | `ColumnResponseDto` |
| `ColumnDeleted` | `DELETE /api/column/{id}` | `ColumnDeletedEventDto` |
| `TaskCreated` | `POST /api/task/column/{columnId}` | `TaskResponseDto` |
| `TaskMoved` | `PUT /api/task/{taskId}/move` | `TaskMovedEventDto` |
| `TaskUpdated` | `PUT /api/task/{taskId}/description`, `DELETE /api/task/{taskId}/description` | `TaskResponseDto` |
| `AssetUploadStarted` | `POST /api/attachment/task/{taskId}` | `AssetStatusEventDto` (status `Pending`) |
| `AssetProcessing` | `POST /api/attachment/task/{taskId}` | `AssetStatusEventDto` (status `Processing`) |
| `AssetCompleted` | `POST /api/attachment/task/{taskId}` | `AssetResponseDto` (status `Completed`) |
| `AssetFailed` | `POST /api/attachment/task/{taskId}` | `AssetFailedEventDto` |
| `AttachmentDeleted` | `DELETE /api/attachment/{assetId}` | `AttachmentDeletedEventDto` |

Broadcasts happen after the EF Core `SaveChangesAsync` succeeds. Broadcast failures are logged but do not fail the originating HTTP request — the mutation is the source of truth, the broadcast is best-effort notification.

**Asset lifecycle:** The four asset events fire in sequence on a single upload — `AssetUploadStarted` (row inserted as `Pending`), `AssetProcessing` (status flipped before the file write), then either `AssetCompleted` (file written successfully, full `AssetResponseDto` with final storage keys) or `AssetFailed` (file write failed, DB row rolled back). Clients should reconcile by `assetId`.

**Task description updates:** Both `PUT /api/task/{taskId}/description` and `DELETE /api/task/{taskId}/description` emit the same `TaskUpdated` event with the post-mutation `TaskResponseDto`. Clients should reconcile by `id` and use `content` as the source of truth (it is `null` after a clear).

> ⚠️ **Not yet broadcast:** `ProjectCreated` (no group exists at creation time), and no `TaskDeleted` event exists yet because a task-delete endpoint is not implemented. Clients relying on `GET /api/project` after page load will still reflect new projects.

---

## DTO reference (C# types → JSON shape)

### `ApiResponse` / `ApiResponse<T>`

See [Standard response wrapper](#standard-response-wrapper-apiresponse).

### `UserProfileDto` & `AuthResponseDto`

Defined in `DTOs/Auth/AuthResponseDto.cs`.

**`UserProfileDto`**

| JSON property | Type | Notes |
|---------------|------|--------|
| `id` | `string` | User id |
| `name` | `string` | Display name |
| `email` | `string` | Email |

**`AuthResponseDto`**

| JSON property | Type | Notes |
|---------------|------|--------|
| `token` | `string` | JWT |
| `user` | `UserProfileDto` | Profile |

### `RegisterRequestDto`

| JSON property | Type | Validation |
|---------------|------|------------|
| `name` | `string` | Required |
| `email` | `string` | Required, email format |
| `password` | `string` | Required, min length 6 |

### `LoginRequestDto`

| JSON property | Type | Validation |
|---------------|------|------------|
| `email` | `string` | Required, email format |
| `password` | `string` | Required |

### `CreateProjectDto`

| JSON property | Type | Validation |
|---------------|------|------------|
| `name` | `string` | Required, max 200 |
| `description` | `string \| null` | Optional, max 500 |

### `UpdateProjectDto`

Same shape as `CreateProjectDto`.

### `ProjectResponseDto`

| JSON property | Type | Notes |
|---------------|------|--------|
| `id` | `string` | Project id |
| `name` | `string` | |
| `description` | `string \| null` | |
| `role` | `string` | Caller’s role in project |
| `createdAt` | `string` (ISO 8601) | `DateTimeOffset` |
| `updatedAt` | `string` (ISO 8601) | `DateTimeOffset` |

### `AddMemberDto`

Either `userId` or `email` must be supplied, not both. The backend resolves the email to a user internally.

| JSON property | Type | Validation |
|---------------|------|------------|
| `userId` | `string` (GUID, optional) | Required when `email` is absent |
| `email` | `string` (optional) | Required when `userId` is absent; validated as email format |

The frontend Members UI (issue #33) sends `{ email }` only.

### `MemberResponseDto`

| JSON property | Type | Notes |
|---------------|------|--------|
| `userId` | `string` | |
| `name` | `string` | |
| `email` | `string` | |
| `role` | `string` | |
| `joinedAt` | `string` (ISO 8601) | |

### `CreateColumnDto`

| JSON property | Type | Validation |
|---------------|------|------------|
| `name` | `string` | Required, max 100 |
| `colorCode` | `string \| null` | Optional, max 20 |
| `columnOrder` | `number \| null` | Optional |

### `ColumnResponseDto`

| JSON property | Type | Notes |
|---------------|------|--------|
| `id` | `string` | |
| `name` | `string` | |
| `colorCode` | `string \| null` | |
| `columnOrder` | `number` | |
| `projectId` | `string` | |
| `createdAt` | `string` (ISO 8601) | |
| `updatedAt` | `string` (ISO 8601) | |

### `CreateTaskDto`

| JSON property | Type | Validation |
|---------------|------|------------|
| `title` | `string` | Required, max 200 |
| `content` | `string \| null` | Optional |
| `assignedId` | `string (GUID) \| null` | Optional assignee |

### `TaskResponseDto`

| JSON property | Type | Notes |
|---------------|------|--------|
| `id` | `string` | |
| `title` | `string` | |
| `content` | `string \| null` | |
| `taskOrder` | `number` | Order within column |
| `columnId` | `string` | |
| `assignedId` | `string \| null` | |
| `createdAt` | `string` (ISO 8601) | |
| `updatedAt` | `string` (ISO 8601) | |

### `MoveTaskDto`

| JSON property | Type | Validation |
|---------------|------|------------|
| `columnId` | `string` (GUID) | Required — target column |
| `taskOrder` | `number` | Required, ≥ 0 |

### `UpdateTaskDescriptionDto`

Body of `PUT /api/task/{taskId}/description`. No `[Required]` annotations — validation is performed in the service layer on the trimmed value.

| JSON property | Type | Validation |
|---------------|------|------------|
| `content` | `string` | Required, non-empty after trim, max 10,000 characters |

To clear a task's description, call `DELETE /api/task/{taskId}/description` (no body).

### `AssetResponseDto`

Returned by `POST /api/attachment/task/{taskId}` and as the payload of the `AssetCompleted` SignalR event.

| JSON property | Type | Notes |
|---------------|------|--------|
| `id` | `string` (GUID) | Asset id |
| `fileName` | `string` | Original filename (sanitized) |
| `storageKey` | `string` | Server-side storage key (`{guid}_{sanitizedName}`) — used internally; clients should prefer `GET /api/attachment/{assetId}` |
| `thumbnailKey` | `string \| null` | Reserved for future thumbnail generation |
| `mimeType` | `string` | Resolved MIME type (server-determined, not from client header) |
| `fileSize` | `number` | Bytes |
| `processingStatus` | `number` | `ProcessingStatus` enum: `0=Pending`, `1=Processing`, `2=Completed`, `3=Failed` |
| `kanbanTaskId` | `string` (GUID) | Owning task |
| `createdAt` | `string` (ISO 8601) | |
| `updatedAt` | `string` (ISO 8601) | |

### SignalR event DTOs

**`ProjectUpdatedEventDto`**

| JSON property | Type | Notes |
|---------------|------|--------|
| `projectId` | `string` (GUID) | |
| `name` | `string` | |
| `description` | `string \| null` | |
| `updatedAt` | `string` (ISO 8601) | |

**`ProjectDeletedEventDto`**

| JSON property | Type | Notes |
|---------------|------|--------|
| `projectId` | `string` (GUID) | |

**`MemberRemovedEventDto`**

| JSON property | Type | Notes |
|---------------|------|--------|
| `userId` | `string` (GUID) | |
| `projectId` | `string` (GUID) | |

**`ColumnDeletedEventDto`**

| JSON property | Type | Notes |
|---------------|------|--------|
| `columnId` | `string` (GUID) | |
| `projectId` | `string` (GUID) | |

**`TaskMovedEventDto`**

| JSON property | Type | Notes |
|---------------|------|--------|
| `taskId` | `string` (GUID) | |
| `oldColumnId` | `string` (GUID) | Column the task was in before the move |
| `newColumnId` | `string` (GUID) | Column the task is in after the move |
| `oldTaskOrder` | `number` | Previous order index |
| `newTaskOrder` | `number` | New order index |
| `task` | `TaskResponseDto` | Full post-move task state |

**`AssetStatusEventDto`** — payload of `AssetUploadStarted` and `AssetProcessing`

| JSON property | Type | Notes |
|---------------|------|--------|
| `assetId` | `string` (GUID) | |
| `taskId` | `string` (GUID) | |
| `fileName` | `string` | |
| `processingStatus` | `number` | `ProcessingStatus` enum — `0=Pending` on `AssetUploadStarted`, `1=Processing` on `AssetProcessing` |

**`AssetFailedEventDto`** — payload of `AssetFailed`

| JSON property | Type | Notes |
|---------------|------|--------|
| `assetId` | `string` (GUID) | |
| `taskId` | `string` (GUID) | |
| `errorMessage` | `string` | Human-readable reason (e.g., storage error) |

**`AttachmentDeletedEventDto`** — payload of `AttachmentDeleted`

Emitted as an anonymous object from `AttachmentController.DeleteFile` (there is no C# `AttachmentDeletedEventDto` record — the payload is built inline). The shape on the wire is:

| JSON property | Type | Notes |
|---------------|------|--------|
| `assetId` | `string` (GUID) | The asset that was deleted |
| `taskId` | `string` (GUID) | Owning task — use to scope the removal client-side |

The `MemberAdded`, `ColumnCreated`, `TaskCreated`, `TaskUpdated`, and `AssetCompleted` events send the corresponding response DTO (`MemberResponseDto`, `ColumnResponseDto`, `TaskResponseDto`, `AssetResponseDto`) directly as the payload — no event-specific wrapper.

---

## Source locations

| Area | Path |
|------|------|
| Controllers | `KanbAI-Core/KanbAI-Core/Controllers/` |
| DTOs | `KanbAI-Core/KanbAI-Core/DTOs/` |
| SignalR hub | `KanbAI-Core/KanbAI-Core/Hubs/KanbanHub.cs` |

---

## Frontend documentation — suggested artifacts

Pairing this backend map with a few frontend-focused docs keeps UI work aligned with API contracts and UX expectations:

1. **`frontend_api_client.md` (or typed SDK notes)** — Base URL per environment, how the SPA attaches `Authorization`, handling of `401`/`403`, and a mapping from UI flows to the routes above (including that auth responses are **not** wrapped in `ApiResponse`, unlike most other endpoints).

2. **Screen / route map** — App routes, guards (logged-in vs public), and which backend endpoints each screen calls (projects list, board, settings, login/register).

3. **State & caching strategy** — Where tokens live (memory vs secure storage), refresh policy if added later, and invalidation rules after mutations (e.g. after move task, refetch board vs optimistic updates).

4. **Forms & validation matrix** — Field-level rules mirrored from DTO validation (max lengths, required fields) so client messages match server behavior.

5. **Error UX contract** — How to surface `ApiResponse.errors`, auth `{ message }` bodies, and empty bodies on `204 No Content`.

6. **Optional: OpenAPI-driven types** — If you generate TypeScript clients from the OpenAPI doc (`MapOpenApi` in Development), document the generation command and where generated files live.

7. **SignalR integration notes** — How the SPA opens the `/hubs/kanban` connection (including passing the JWT via `?access_token` for browser WebSockets), when to call `JoinProjectGroup` / `LeaveProjectGroup`, and how each event (`TaskMoved`, `ColumnDeleted`, etc.) maps to local state reconciliation.

Together with **`backend_api_map.md`**, these give frontend developers enough context to implement safely without rediscovering quirks (especially the auth response shape vs `ApiResponse` and the real-time event contracts).
