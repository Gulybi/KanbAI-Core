# Issue #80: Add List-Attachments-By-Task Endpoint for Session-Resume Visibility

## Business Value

**Who:** End users of KanbAI (project owners and project members) who upload files against a task, close their browser, and return the next day — and every teammate who opens a task they did not personally upload to in this session.

**What:** Provide an API endpoint that returns all attachments associated with a given task, enabling users to see files that were previously uploaded regardless of whether they uploaded them, whether they were present when the upload occurred, or whether they've refreshed the page.

**Why:** The frontend shipped attachment list and download UI (KanbAI-Web issue #51) under a session-local-only constraint because the backend currently exposes no way to list the attachments of a given task. The attachment list in the UI is populated purely by:
1. The 201 response from a local upload (POST /api/attachment/task/{taskId})
2. The AssetCompleted SignalR broadcast received during the live session

On cold load — page refresh, next-day login, different device, teammate opening a task they didn't upload to — the list is empty for every task, even tasks that have files stored on the server.

This is a data-reachability gap, not a UI gap. The files exist on the server (behind GET /api/attachment/{assetId}), the frontend has state and rendering for them, but there is no endpoint that returns "here are all the assets for task T." Without the asset IDs, the client cannot call GET /api/attachment/{assetId} either, because that endpoint is keyed by assetId, not taskId. The loop is closed only for the session in which an upload happened.

This issue prevents:
- Users from seeing their own previously uploaded files after a page refresh
- Team members from seeing files uploaded by colleagues unless they were online when the upload completed
- Any persistent file attachment feature from being genuinely useful in a production context
- The frontend from displaying accurate file counts on task cards

## Current State vs. Desired State

### Current State (Files Exist But Are Invisible After Session Ends)

**AttachmentController Endpoints (Completed by Issue #67):**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/AttachmentController.cs
- **Existing Endpoints:**
  - POST /api/attachment/task/{taskId} — Uploads a file to a task, returns 201 with AssetResponseDto on success
  - GET /api/attachment/{assetId} — Downloads a file by asset ID, requires project membership authorization
- **Missing:** No endpoint accepts a taskId and returns a collection of AssetResponseDto objects

**Asset Entity Relationship:**
- **Location:** c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Entities/Asset.cs
- **Schema:** Asset entity has a foreign key `KanbanTaskId` (Guid) that references the parent task
- **Relationship:** KanbanTask entity (c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Entities/KanbanTask.cs) has a collection navigation property `ICollection<Asset> Assets`
- **Database State:** Assets with ProcessingStatus.Completed are persisted and queryable, but no controller endpoint exposes them by task

**Frontend Impact (KanbAI-Web Issue #51):**
- Frontend renders attachment lists and download buttons based on local session state
- When a user uploads a file, the 201 response contains an AssetResponseDto that is stored in the frontend's task state
- When a SignalR AssetCompleted event is received, the frontend adds that asset to the task's local state
- When the page is refreshed or a different user opens the task, the frontend has no way to fetch the existing assets, so the list is empty
- The UI shows "0 attachments" even when files exist in the database

**Authorization Pattern (Established):**
- All AttachmentController endpoints require JWT authentication (Authorize attribute on the controller)
- GET /api/attachment/{assetId} verifies that the authenticated user is a member of the asset's task's project before serving the file (loads Asset → KanbanTask → BoardColumn → Project → Members)
- The same project membership authorization pattern should apply to the list endpoint

**Response Pattern (Established):**
- Controllers return ApiResponse<T> wrappers with Success, Message, and Data properties
- Collections are returned as ApiResponse<IEnumerable<T>> or ApiResponse<List<T>>
- 404 Not Found responses use ApiResponse.Fail(message) when the requested resource does not exist
- 403 Forbidden responses use ApiResponse.Fail(message) when the user lacks authorization

**What's Missing:**
- No GET endpoint at /api/attachment/task/{taskId} or equivalent route
- No method in AttachmentController that queries Assets by KanbanTaskId
- No authorization check that verifies the authenticated user is a member of the task's project before returning the list
- No filtering by ProcessingStatus (the endpoint should only return Completed assets, not Pending, Processing, or Failed assets)

### Desired State (Users Can Always See All Attachments for Any Task They Have Access To)

**New Endpoint:**
- **Route:** GET /api/attachment/task/{taskId} (follows RESTful convention, matches the existing POST /api/attachment/task/{taskId} pattern)
- **Parameters:**
  - taskId (Guid, from route) — The ID of the task whose attachments should be listed
- **Authorization:** Requires JWT authentication (inherits from controller-level Authorize attribute)
- **Behavior:**
  1. Extract authenticated user ID via GetCurrentUserId() (existing helper method in the controller)
  2. Query the database to load the KanbanTask with .Include(t => t.Column).ThenInclude(c => c.Project).ThenInclude(p => p.Members)
  3. Verify the task exists (return 404 Not Found if not)
  4. Verify the authenticated user is a member of the task's project (return 403 Forbidden if not)
  5. Query all Asset entities where KanbanTaskId == taskId AND ProcessingStatus == ProcessingStatus.Completed
  6. Order the results by CreatedAt descending (most recent first)
  7. Map the Asset entities to AssetResponseDto objects
  8. Return 200 OK with ApiResponse<IEnumerable<AssetResponseDto>>.Ok(data, "Attachments retrieved successfully.")

**Acceptance Criteria Quality Gate Validation:**

| Criterion | Testable | Specific | Independent | Implementation-Free | Complete |
|-----------|----------|----------|-------------|---------------------|----------|
| Task exists check | Yes — returns 404 if task does not exist | Yes — 404 status code and error message | Yes — single verifiable outcome | Yes — describes what (404), not how (EF query) | Yes — covers edge case |
| User is project member | Yes — returns 403 if user not authorized | Yes — 403 status code and error message | Yes — single verifiable outcome | Yes — describes authorization check, not implementation | Yes — covers security case |
| Only Completed assets returned | Yes — only assets with ProcessingStatus.Completed | Yes — excludes Pending, Processing, Failed | Yes — single verifiable outcome | Yes — describes filtering rule, not query syntax | Yes — covers edge case |
| Empty list for task with no attachments | Yes — returns 200 with empty array | Yes — empty array, not 404 | Yes — single verifiable outcome | Yes — describes behavior, not code | Yes — covers edge case |
| Results ordered by CreatedAt descending | Yes — most recent asset appears first | Yes — descending order, specific field | Yes — single verifiable outcome | Yes — describes sort order, not LINQ syntax | Yes — complete |

**Response Format:**
```json
{
  "success": true,
  "message": "Attachments retrieved successfully.",
  "errors": [],
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "fileName": "screenshot.png",
      "storageKey": "3fa85f64-5717-4562-b3fc-2c963f66afa6_screenshot.png",
      "thumbnailKey": null,
      "mimeType": "image/png",
      "fileSize": 245760,
      "processingStatus": 2,
      "kanbanTaskId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
      "createdAt": "2026-05-06T10:30:00Z",
      "updatedAt": "2026-05-06T10:30:05Z"
    }
  ]
}
```

**Error Responses:**
- 404 Not Found when task does not exist: `{ "success": false, "message": "Task not found.", "errors": [], "data": null }`
- 403 Forbidden when user is not a project member: `{ "success": false, "message": "You are not authorized to access this task's attachments.", "errors": [], "data": null }`

**Frontend Integration (KanbAI-Web Issue #51):**
- When the frontend opens a task detail view, it calls GET /api/attachment/task/{taskId}
- The returned AssetResponseDto array is merged with any session-local assets (from recent uploads or SignalR events)
- The UI displays the combined list with download buttons (each button calls GET /api/attachment/{assetId})
- The attachment count badge on the task card is updated to reflect the actual number of files

**Security Considerations:**
- Authorization check prevents users from listing attachments for tasks in projects they don't belong to (prevents enumeration attacks via guessing task IDs)
- Only Completed assets are returned (Pending, Processing, or Failed assets are not exposed to clients until they are ready)
- The endpoint does not accept any filter or query parameters that could bypass authorization (taskId is the only input)

**Logging:**
- Successful requests logged at Information level: "User {UserId} retrieved {Count} attachments for task {TaskId}"
- Authorization failures logged at Warning level: "User {UserId} attempted to access attachments for task {TaskId} in project {ProjectId} without authorization"
- Task not found logged at Information level: "User {UserId} requested attachments for non-existent task {TaskId}"

## Milestone Context

This issue is **not explicitly part of a milestone** in the GitHub issue metadata, but it is a **direct dependency** for the frontend feature shipped in KanbAI-Web issue #51 (Build Attachment List and Download UI). It completes the file attachment feature by closing the data-reachability gap that prevents users from seeing previously uploaded files.

### Related Issues

| Repository | Issue # | Title | State | Relationship |
|------------|---------|-------|-------|--------------|
| KanbAI-Core | #65 | Setup Local File Storage Infrastructure | Merged | **Prerequisite** — Storage infrastructure exists |
| KanbAI-Core | #66 | Create AssetService for Async Processing | Merged | **Prerequisite** — Assets are persisted with ProcessingStatus workflow |
| KanbAI-Core | #67 | Implement AttachmentController Endpoints | Merged | **Prerequisite** — Upload and download endpoints exist |
| **KanbAI-Core** | **#80** | **Add List-Attachments-By-Task Endpoint** | **Open** | **THIS ISSUE** |
| KanbAI-Web | #51 | Build Attachment List and Download UI | Open | **Blocked by #80** — Frontend needs this endpoint to display attachments on cold load |

### Implementation Context

The file attachment feature was implemented in three phases:
1. Issue #65 — Storage infrastructure and configuration
2. Issue #66 — AssetService for async file processing and SignalR broadcasting
3. Issue #67 — AttachmentController with upload and download endpoints

Issue #80 was **not originally planned** in the milestone but became necessary when the frontend team discovered that the download endpoint (GET /api/attachment/{assetId}) is keyed by assetId, not taskId, creating a circular dependency: the client needs the asset IDs to download files, but has no way to fetch the asset IDs for a given task unless it was present during the upload session.

## Acceptance Criteria

### 1. Endpoint Exists
- [ ] GET endpoint is defined at route [HttpGet("task/{taskId}")] (results in GET /api/attachment/task/{taskId})
- [ ] Endpoint accepts Guid taskId as a route parameter
- [ ] Endpoint signature includes CancellationToken cancellationToken parameter for graceful cancellation support

### 2. Authentication and Authorization
- [ ] Endpoint calls GetCurrentUserId() to extract the authenticated user's ID from the JWT token
- [ ] Endpoint queries the KanbanTask with .Include(t => t.Column).ThenInclude(c => c.Project).ThenInclude(p => p.Members)
- [ ] If the task does not exist, endpoint returns 404 Not Found with ApiResponse.Fail("Task not found.")
- [ ] If the authenticated user is not a member of the task's project, endpoint returns 403 Forbidden with ApiResponse.Fail("You are not authorized to access this task's attachments.")

### 3. Asset Query
- [ ] Endpoint queries all Asset entities where KanbanTaskId equals the provided taskId
- [ ] Query filters to only include assets where ProcessingStatus equals ProcessingStatus.Completed
- [ ] Query excludes assets with Pending, Processing, or Failed status
- [ ] Query uses .AsNoTracking() since the results are read-only

### 4. Result Ordering
- [ ] Results are ordered by CreatedAt descending (most recent first)

### 5. Response Mapping
- [ ] Asset entities are mapped to AssetResponseDto objects
- [ ] All required properties are populated: Id, FileName, StorageKey, ThumbnailKey, MimeType, FileSize, ProcessingStatus, KanbanTaskId, CreatedAt, UpdatedAt

### 6. Success Response
- [ ] Endpoint returns 200 OK with ApiResponse<IEnumerable<AssetResponseDto>>.Ok(data, "Attachments retrieved successfully.")
- [ ] Response body includes the full list of AssetResponseDto objects

### 7. Empty List Handling
- [ ] If the task exists but has no completed attachments, endpoint returns 200 OK with an empty array (not 404)
- [ ] Response message is "Attachments retrieved successfully." even when the array is empty

### 8. Error Responses
- [ ] 404 Not Found is returned when the task does not exist
- [ ] 403 Forbidden is returned when the user is not a project member
- [ ] Error responses include ApiResponse.Fail(message) with a clear error message

### 9. Logging
- [ ] Successful requests are logged at Information level with structured logging: "User {UserId} retrieved {Count} attachments for task {TaskId}"
- [ ] Authorization failures are logged at Warning level: "User {UserId} attempted to access attachments for task {TaskId} in project {ProjectId} without authorization"
- [ ] Task not found scenarios are logged at Information level: "User {UserId} requested attachments for non-existent task {TaskId}"

### 10. Security: Authorization Check
- [ ] Endpoint verifies project membership BEFORE querying assets
- [ ] Endpoint does not expose asset data to users who are not project members
- [ ] Endpoint does not accept any filter or query parameters that could bypass authorization

### 11. Security: Only Completed Assets Exposed
- [ ] Assets with Pending, Processing, or Failed status are never returned
- [ ] The endpoint only exposes assets that are fully uploaded and available for download

### 12. Frontend Integration Ready
- [ ] The response format matches the AssetResponseDto structure expected by the frontend (KanbAI-Web issue #51)
- [ ] The endpoint returns an array (not a single object) to support multiple attachments per task
- [ ] The endpoint does not require any special query parameters or headers beyond standard JWT authentication

### 13. Consistent with Existing Patterns
- [ ] Endpoint follows the same authorization pattern as GET /api/attachment/{assetId} (loading task → column → project → members)
- [ ] Endpoint follows the same response pattern as other AttachmentController endpoints (ApiResponse wrapper)
- [ ] Endpoint follows the same error handling pattern as other controllers (404 for not found, 403 for unauthorized)

### 14. No Breaking Changes
- [ ] Adding this endpoint does not modify or break existing POST /api/attachment/task/{taskId} or GET /api/attachment/{assetId} endpoints
- [ ] The new endpoint is purely additive and does not change any existing behavior

### 15. Database Query Efficiency
- [ ] Query uses a single database roundtrip to fetch both task authorization data and assets
- [ ] Query includes only the necessary navigation properties (task → column → project → members for authorization, assets by taskId for data)
- [ ] Query uses .AsNoTracking() to avoid change tracking overhead for read-only data
