# Issue #67: Implement AttachmentController Endpoints

## Business Value

**Who:** KanbAI users who need to attach files to Kanban tasks (screenshots, documents, design assets, logs) and retrieve those files from their browser as part of their project management workflow.

**What:** Create HTTP API endpoints that allow authenticated users to upload files to tasks and download/view previously uploaded files. The controller serves as the public-facing API layer that validates HTTP requests, delegates file processing to the AssetService (Issue #66, now completed), and returns appropriate HTTP responses.

**Why:** Even though the file storage infrastructure (Issue #65) and the AssetService (Issue #66) are complete, users cannot interact with the file attachment feature without HTTP endpoints. Without AttachmentController:
- Users have no way to upload files from the web UI, mobile app, or API clients (no POST endpoint)
- Users cannot retrieve uploaded files to view them in the browser or download them (no GET endpoint)
- The existing AssetService remains unused and untestable from the user's perspective
- Authorization checks at the HTTP layer are missing (the service validates project membership, but the controller must extract and validate the authenticated user's ID from the JWT token)
- Proper HTTP status codes and error messages are not returned to clients (e.g., 400 for invalid file type, 403 for unauthorized access, 413 for file too large)
- Multipart form-data parsing is not implemented (the standard HTTP format for file uploads)

This is the third and final building block in the "Asynchronous File Attachments" milestone, exposing the infrastructure and service layer to end users via REST API endpoints.

## Current State vs. Desired State

### Current State (Service Layer Ready, HTTP Layer Missing)

**AssetService Available (Completed by Issue #66):**
- **Location:** `C:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Assets/AssetService.cs`
- **Interface:** `C:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Assets/IAssetService.cs`
- **Method Signature:**
  ```csharp
  Task<(AssetResponseDto? data, UploadAssetResult result)> UploadAssetAsync(
      Guid taskId,
      Stream fileStream,
      string fileName,
      string contentType,
      long fileSize,
      Guid userId,
      CancellationToken cancellationToken = default);
  ```
- **Service Capabilities:**
  - Validates that the user is a member of the task's project (authorization)
  - Validates file size against `FileStorageOptions.MaxFileSizeBytes` (10 MB default)
  - Validates file extension against `FileStorageOptions.AllowedExtensions` whitelist
  - Validates MIME type matches the file extension (prevents MIME type spoofing)
  - Generates unique storage keys to prevent filename collisions
  - Writes files asynchronously to `wwwroot/uploads/` with proper error handling
  - Updates Asset entity through Pending → Processing → Completed/Failed state transitions
  - Broadcasts real-time events via SignalR to project group members
- **Return Values:** Tuple of `(AssetResponseDto?, UploadAssetResult)` where result enum includes:
  - `Success`, `TaskNotFound`, `UserNotAuthorized`, `FileTooLarge`, `InvalidFileType`, `InvalidFileName`, `StorageError`

**File Storage Configuration (Established by Issue #65):**
- **Location:** `C:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Configuration/FileStorageOptions.cs`
- **Configuration Binding:** Reads from `appsettings.json` section `"FileStorage"`
- **Settings:**
  - `StoragePath`: "wwwroot/uploads" (relative path from application root)
  - `MaxFileSizeBytes`: 10485760 (10 MB production limit)
  - `AllowedExtensions`: [".jpg", ".jpeg", ".png", ".gif", ".pdf", ".docx", ".xlsx", ".txt"]
- **Physical Directory:** `C:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/wwwroot/uploads/` exists with `.gitkeep` file
- **Security:** Configuration is validated at application startup; fails fast if invalid

**Existing Controller Patterns (Established Conventions):**
- **Location:** Controllers live in `C:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/`
- **Examples:** `TaskController.cs`, `ProjectController.cs`, `ColumnController.cs`, `AuthController.cs`
- **Authorization:** All controllers except `AuthController` use `[Authorize]` attribute (require JWT authentication)
- **Route Convention:** `[Route("api/[controller]")]` (e.g., `api/task`, `api/project`)
- **Authentication Pattern:** Controllers extract user ID via:
  ```csharp
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
  ```
- **Response Pattern:** Controllers return standardized `ApiResponse<T>` or `ApiResponse` DTOs:
  - Success: `ApiResponse<T>.Ok(data, message)` with 200/201 status
  - Validation Failure: `BadRequest(ApiResponse.Fail(message))` with 400 status
  - Not Found: `NotFound(ApiResponse.Fail(message))` with 404 status
  - Forbidden: `StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(message))` with 403 status
- **Service Integration:** Controllers use constructor injection to receive service dependencies and call service methods, then map service result enums to HTTP status codes via switch expressions

**DTOs Available:**
- **AssetResponseDto:** `C:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/DTOs/AssetResponseDto.cs`
  - Properties: `Id`, `FileName`, `StorageKey`, `ThumbnailKey`, `MimeType`, `FileSize`, `ProcessingStatus`, `KanbanTaskId`, `CreatedAt`, `UpdatedAt`
- **ApiResponse<T>:** `C:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/DTOs/ApiResponse.cs`
  - Generic wrapper with `Success`, `Message`, `Errors`, `Data` properties

**What's Missing:**

1. **No AttachmentController Exists:**
   - No controller file at `C:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/AttachmentController.cs`
   - No HTTP endpoints exposed for file upload or download

2. **No POST Endpoint for File Upload:**
   - No endpoint to accept multipart/form-data requests (the standard HTTP format for file uploads)
   - No parsing of `IFormFile` from HTTP requests
   - No validation of Content-Type header (should be `multipart/form-data`)
   - No rate limiting or request size limits configured at the controller level
   - No integration with the AssetService.UploadAssetAsync method

3. **No GET Endpoint for File Serving:**
   - No endpoint to retrieve uploaded files by their storage key or asset ID
   - No authorization check to ensure users can only access files from tasks in projects they are members of
   - No Content-Disposition header configuration (inline for images, attachment for downloads)
   - No MIME type header configuration (must return the file's original MIME type)
   - No 404 handling if the requested file does not exist on disk or in the database
   - No support for byte range requests (optional HTTP feature for resumable downloads, but not required for MVP)

4. **No Middleware Configuration for File Serving:**
   - `Program.cs` does not include `app.UseStaticFiles()` middleware (not needed if files are served through authenticated endpoints rather than direct URL access)
   - Decision needed: Should files be publicly accessible via direct URL (e.g., `https://localhost/uploads/file.jpg`) or only via authenticated API endpoints (more secure)?

5. **No Request Size Limit Enforcement at Web Server Level:**
   - ASP.NET Core has a default request body size limit (30 MB by default), but no explicit configuration exists to align it with `FileStorageOptions.MaxFileSizeBytes` (10 MB)
   - Controllers should use `[RequestSizeLimit]` or `[DisableRequestSizeLimit]` attributes to control request size per endpoint

6. **No Integration Tests for Controller Endpoints:**
   - While AssetService has unit and integration tests, no tests exist to verify the HTTP layer behavior:
     - Does the POST endpoint correctly parse multipart/form-data?
     - Are authorization failures (403) returned when users try to upload to tasks in projects they're not members of?
     - Are validation failures (400) returned for invalid file types, oversized files, or missing form fields?
     - Does the GET endpoint correctly serve files with appropriate headers?

**Impact:**
The file attachment feature cannot be used by end users until these HTTP endpoints exist. The AssetService is complete and tested, but there is no HTTP interface to call it.

### Desired State (HTTP Endpoints Ready for User Interaction)

**AttachmentController Exists:**
- **Location:** `C:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Controllers/AttachmentController.cs`
- **Authorization:** Decorated with `[Authorize]` attribute (requires JWT authentication)
- **Route:** `[Route("api/[controller]")]` (results in `api/attachment` or `api/attachments` - naming convention TBD)
- **Dependencies (Constructor Injection):**
  - `IAssetService _assetService` (to delegate file upload operations)
  - `ILogger<AttachmentController> _logger` (structured logging for observability)
  - `IWebHostEnvironment _environment` (to resolve physical file paths for the GET endpoint)
  - `IOptions<FileStorageOptions> _storageOptions` (to access storage path configuration)

**POST Endpoint for File Upload:**

- **Route:** `[HttpPost("task/{taskId}")]` (e.g., `POST /api/attachment/task/{taskId}`)
- **Request Format:** `multipart/form-data` with a single file field (name TBD, e.g., `"file"`)
- **Parameters:**
  - `Guid taskId` (from route) - The ID of the task to attach the file to
  - `IFormFile file` (from form) - The uploaded file
- **Request Size Limit:** Decorated with `[RequestSizeLimit(10485760)]` or configured value from `FileStorageOptions.MaxFileSizeBytes`
- **Behavior:**
  1. Extract authenticated user ID via `GetCurrentUserId()` helper (follows existing pattern from TaskController)
  2. Validate that `file` is not null (return 400 if missing)
  3. Validate that `file.Length > 0` (return 400 if empty)
  4. Extract file metadata: `file.FileName`, `file.ContentType`, `file.Length`
  5. Open the file stream: `await file.OpenReadStream()`
  6. Call `await _assetService.UploadAssetAsync(taskId, stream, fileName, contentType, fileSize, userId, cancellationToken)`
  7. Map `UploadAssetResult` enum to HTTP responses:
     - `Success` → 201 Created with `ApiResponse<AssetResponseDto>.Ok(data, "File uploaded successfully.")`
     - `TaskNotFound` → 404 Not Found with `ApiResponse.Fail("Task not found.")`
     - `UserNotAuthorized` → 403 Forbidden with `ApiResponse.Fail("You are not a member of this project.")`
     - `FileTooLarge` → 413 Payload Too Large with `ApiResponse.Fail("File size exceeds maximum allowed size.")`
     - `InvalidFileType` → 400 Bad Request with `ApiResponse.Fail("File type is not allowed.")`
     - `InvalidFileName` → 400 Bad Request with `ApiResponse.Fail("File name is invalid.")`
     - `StorageError` → 500 Internal Server Error with `ApiResponse.Fail("Failed to save file. Please try again.")`
- **201 Response Headers (Success Case):**
  - `Location: /api/attachment/{assetId}` (points to the GET endpoint for the uploaded file)
- **Error Handling:**
  - If file stream reading fails (e.g., corrupted upload), return 400 with appropriate error message
  - If `GetCurrentUserId()` throws `UnauthorizedAccessException`, middleware catches and returns 401 Unauthorized

**GET Endpoint for File Serving:**

- **Route:** `[HttpGet("{assetId}")]` (e.g., `GET /api/attachment/{assetId}`) or `[HttpGet("file/{storageKey}")]` (TBD)
- **Parameters:**
  - `Guid assetId` (from route) - The ID of the asset to retrieve
- **Behavior:**
  1. Extract authenticated user ID via `GetCurrentUserId()`
  2. Query the database to load the Asset entity with `.Include(a => a.KanbanTask).ThenInclude(t => t.Column).ThenInclude(c => c.Project).ThenInclude(p => p.Members)`
  3. Verify the asset exists (return 404 if not)
  4. Verify the authenticated user is a member of the asset's task's project (return 403 if not)
  5. Verify the asset's `ProcessingStatus` is `Completed` (return 400 or 404 if `Pending`, `Processing`, or `Failed`)
  6. Resolve the physical file path: `Path.Combine(_environment.ContentRootPath, _storageOptions.Value.StoragePath, asset.StorageKey)`
  7. Verify the file exists on disk (return 404 if missing - indicates orphaned database record)
  8. Return the file using `PhysicalFile(filePath, asset.MimeType, asset.FileName)` or `File(fileStream, asset.MimeType, asset.FileName)`:
     - Set `Content-Type` header to `asset.MimeType` (e.g., "image/png", "application/pdf")
     - Set `Content-Disposition` header:
       - For images: `inline; filename="{asset.FileName}"` (displays in browser)
       - For documents: `attachment; filename="{asset.FileName}"` (triggers download)
     - Set `Content-Length` header to `asset.FileSize`
- **Response Status Codes:**
  - 200 OK with file stream (success case)
  - 400 Bad Request if asset is not yet completed (still processing)
  - 403 Forbidden if user is not a project member
  - 404 Not Found if asset does not exist in database or file does not exist on disk
- **Security Considerations:**
  - Authorization check prevents users from accessing files in projects they don't belong to (prevents path traversal via guessing asset IDs)
  - File path is constructed server-side from the database record, never from user input (prevents path traversal attacks)

**Naming Convention Decision:**
- Controller name: `AttachmentController` (singular) vs. `AttachmentsController` (plural)
- Route prefix: `api/attachment` (singular) vs. `api/attachments` (plural)
- Recommendation: Follow existing conventions from the codebase (check ProjectController, TaskController, ColumnController for plurality)

**Request Size Limit Alignment:**
- Apply `[RequestSizeLimit]` attribute to POST endpoint to enforce `FileStorageOptions.MaxFileSizeBytes` at the web server level
- Prevents clients from uploading large files that consume memory before validation logic runs
- Aligned with security best practices (fail fast, minimize resource consumption)

**Static File Middleware Decision (Deferred):**
- If files should be accessible via direct URL (e.g., `https://localhost/uploads/file.jpg`), add `app.UseStaticFiles()` to `Program.cs`
- If files should only be served through authenticated endpoints (recommended for security), do NOT add static file middleware
- This decision should be documented in the handoff note or in code comments
- Recommendation: Serve files ONLY through the authenticated GET endpoint to ensure authorization checks are always enforced

**Error Logging:**
- Log all unexpected exceptions at `Error` level with structured logging (include `AssetId`, `TaskId`, `UserId`, exception message)
- Log authorization failures at `Warning` level (include `UserId`, `TaskId`, `AssetId`)
- Do NOT log file contents or PII (user-uploaded data)

## Milestone Context

This issue is part of **Milestone #7: Asynchronous File Attachments**, which enables users to attach files to Kanban tasks with real-time progress updates via SignalR.

### Related Issues in Milestone

| Issue # | Title | State | Relationship |
|---------|-------|-------|--------------|
| #65 | Setup Local File Storage Infrastructure | **Merged** | **Prerequisite COMPLETED** - AttachmentController consumes FileStorageOptions configuration and wwwroot/uploads directory |
| #66 | Create AssetService for Async Processing | **Merged** | **Prerequisite COMPLETED** - AttachmentController delegates file processing to IAssetService.UploadAssetAsync |
| **#67** | **Implement AttachmentController Endpoints** | **Open** | **THIS ISSUE - HTTP API Layer** |
| #68 | Document AI File Handling Implementation (AI_LOGS.md) | Open | **Depends on #67** - Documents the completed feature after implementation |

### Implementation Order

1. ~~#65 - File storage infrastructure~~ (COMPLETED)
2. ~~#66 - AssetService for async file processing~~ (COMPLETED)
3. **#67 (THIS ISSUE)** - Expose HTTP endpoints for file upload and download
4. #68 - Document the entire flow for future maintenance

**Critical Path:** Issue #68 (documentation) is blocked until #67 (this issue) is complete. The file attachment feature is not user-accessible until this issue is resolved.

## Acceptance Criteria

### 1. AttachmentController Exists
- [ ] Controller file exists at `Controllers/AttachmentController.cs`
- [ ] Controller is decorated with `[ApiController]`, `[Route("api/[controller]")]`, and `[Authorize]` attributes
- [ ] Constructor injects: `IAssetService`, `ILogger<AttachmentController>`, `IWebHostEnvironment`, `IOptions<FileStorageOptions>`

### 2. POST Endpoint: Route and Attributes
- [ ] POST endpoint is defined with route `[HttpPost("task/{taskId}")]` (results in `POST /api/attachment/task/{taskId}`)
- [ ] Endpoint accepts `Guid taskId` as a route parameter
- [ ] Endpoint accepts `IFormFile` as a form parameter (e.g., `[FromForm] IFormFile file`)
- [ ] Endpoint is decorated with `[RequestSizeLimit]` attribute set to `FileStorageOptions.MaxFileSizeBytes` value (or a reasonable default like 10485760)
- [ ] Endpoint signature includes `CancellationToken cancellationToken` parameter for graceful cancellation support

### 3. POST Endpoint: File Validation
- [ ] Endpoint validates that the `file` parameter is not null (returns 400 Bad Request if missing with message "File is required.")
- [ ] Endpoint validates that `file.Length > 0` (returns 400 Bad Request if empty with message "File cannot be empty.")
- [ ] Endpoint extracts file metadata: `file.FileName`, `file.ContentType`, `file.Length`

### 4. POST Endpoint: Authentication and Authorization
- [ ] Endpoint calls `GetCurrentUserId()` helper method to extract the authenticated user's ID from the JWT token (follows existing pattern from TaskController)
- [ ] If `GetCurrentUserId()` throws `UnauthorizedAccessException`, the exception propagates to the global exception handler (returns 401 Unauthorized)

### 5. POST Endpoint: AssetService Integration
- [ ] Endpoint opens the file stream using `file.OpenReadStream()`
- [ ] Endpoint calls `await _assetService.UploadAssetAsync(taskId, fileStream, file.FileName, file.ContentType, file.Length, userId, cancellationToken)`
- [ ] Endpoint disposes of the file stream properly (using `using` statement or `await using` for async disposal)

### 6. POST Endpoint: Response Mapping
- [ ] Endpoint maps `UploadAssetResult.Success` to 201 Created with `ApiResponse<AssetResponseDto>.Ok(data, "File uploaded successfully.")`
- [ ] Endpoint sets `Location` header to `/api/attachment/{assetId}` on 201 Created response
- [ ] Endpoint maps `UploadAssetResult.TaskNotFound` to 404 Not Found with `ApiResponse.Fail("Task not found.")`
- [ ] Endpoint maps `UploadAssetResult.UserNotAuthorized` to 403 Forbidden with `ApiResponse.Fail("You are not a member of this project.")`
- [ ] Endpoint maps `UploadAssetResult.FileTooLarge` to 413 Payload Too Large with `ApiResponse.Fail("File size exceeds maximum allowed size.")`
- [ ] Endpoint maps `UploadAssetResult.InvalidFileType` to 400 Bad Request with `ApiResponse.Fail("File type is not allowed.")`
- [ ] Endpoint maps `UploadAssetResult.InvalidFileName` to 400 Bad Request with `ApiResponse.Fail("File name is invalid.")`
- [ ] Endpoint maps `UploadAssetResult.StorageError` to 500 Internal Server Error with `ApiResponse.Fail("Failed to save file. Please try again.")`

### 7. GET Endpoint: Route and Parameters
- [ ] GET endpoint is defined with route `[HttpGet("{assetId}")]` (results in `GET /api/attachment/{assetId}`)
- [ ] Endpoint accepts `Guid assetId` as a route parameter

### 8. GET Endpoint: Authorization Check
- [ ] Endpoint calls `GetCurrentUserId()` to extract the authenticated user's ID
- [ ] Endpoint queries the database to load the Asset entity with `.Include(a => a.KanbanTask).ThenInclude(t => t.Column).ThenInclude(c => c.Project).ThenInclude(p => p.Members)`
- [ ] If the asset does not exist in the database, endpoint returns 404 Not Found with `ApiResponse.Fail("File not found.")`
- [ ] If the authenticated user is not a member of the asset's task's project, endpoint returns 403 Forbidden with `ApiResponse.Fail("You are not authorized to access this file.")`

### 9. GET Endpoint: Processing Status Check
- [ ] Endpoint checks that `asset.ProcessingStatus == ProcessingStatus.Completed`
- [ ] If the asset is in `Pending` or `Processing` status, endpoint returns 400 Bad Request or 404 Not Found with message "File is still being processed." or "File not ready yet."
- [ ] If the asset is in `Failed` status, endpoint returns 404 Not Found with message "File upload failed."

### 10. GET Endpoint: File Existence Check
- [ ] Endpoint resolves the physical file path using `Path.Combine(_environment.ContentRootPath, _storageOptions.Value.StoragePath, asset.StorageKey)`
- [ ] Endpoint checks if the file exists on disk using `File.Exists(filePath)`
- [ ] If the file does not exist on disk (orphaned database record), endpoint returns 404 Not Found with message "File not found."

### 11. GET Endpoint: File Serving
- [ ] Endpoint returns the file using `PhysicalFile(filePath, asset.MimeType, asset.FileName)` or `File(fileStream, asset.MimeType, asset.FileName)`
- [ ] Response includes `Content-Type` header set to `asset.MimeType`
- [ ] Response includes `Content-Disposition` header:
  - For image MIME types (e.g., `image/png`, `image/jpeg`): `inline; filename="{asset.FileName}"` (displays in browser)
  - For non-image MIME types: `attachment; filename="{asset.FileName}"` (triggers download)
- [ ] Response includes `Content-Length` header set to `asset.FileSize`
- [ ] Response returns HTTP 200 OK status code

### 12. GET Endpoint: Security - Path Traversal Prevention
- [ ] File path is constructed exclusively from database-stored `asset.StorageKey`, NEVER from user input in the request
- [ ] Endpoint does not accept any file path or filename parameters from the user (only `assetId`)
- [ ] Resolved file path is validated to be within the configured storage directory before serving (e.g., using `Path.GetFullPath` and verifying the resolved path starts with the storage directory path)

### 13. Error Handling and Logging
- [ ] All unexpected exceptions during POST or GET operations are logged at `Error` level with structured logging (include `AssetId`, `TaskId`, `UserId`, exception message, stack trace)
- [ ] Authorization failures (403 responses) are logged at `Warning` level (include `UserId`, `TaskId`, `AssetId`, reason)
- [ ] File validation failures (400 responses) are logged at `Information` level (include `UserId`, `TaskId`, `FileName`, `FileSize`, `ContentType`, validation failure reason)
- [ ] Logs do NOT include file contents or PII (user-uploaded data)

### 14. Helper Method: GetCurrentUserId
- [ ] `GetCurrentUserId()` private helper method exists in the controller (follows existing pattern from TaskController)
- [ ] Method extracts `ClaimTypes.NameIdentifier` from `User.FindFirst()`
- [ ] Method validates that the claim value is not null or empty and can be parsed as a Guid
- [ ] Method throws `UnauthorizedAccessException` with message "Invalid or missing user ID in token." if validation fails
- [ ] Method logs an error message if the claim is invalid or missing

### 15. Static File Middleware Decision Documented
- [ ] A code comment in `AttachmentController.cs` or in the handoff notes documents the decision NOT to use static file middleware (files are served ONLY through authenticated endpoints for security)
- [ ] OR: If static file middleware is used, `Program.cs` includes `app.UseStaticFiles()` and a code comment explains why direct URL access is enabled

### 16. Request Size Limit Aligned with Configuration
- [ ] POST endpoint's `[RequestSizeLimit]` attribute value matches or is configurable based on `FileStorageOptions.MaxFileSizeBytes`
- [ ] If a client attempts to upload a file larger than the request size limit, ASP.NET Core middleware returns 413 Payload Too Large BEFORE the controller action is invoked (framework behavior, but should be verified)

### 17. Content-Disposition Logic Based on MIME Type
- [ ] GET endpoint determines `Content-Disposition` header value based on the MIME type:
  - If `asset.MimeType.StartsWith("image/")`: use `inline; filename="{asset.FileName}"`
  - Otherwise: use `attachment; filename="{asset.FileName}"`
- [ ] Filename in the `Content-Disposition` header is properly escaped or quoted to handle filenames with special characters (e.g., spaces, commas)

### 18. Integration Test Coverage (Recommended)
- [ ] Integration tests exist for the POST endpoint:
  - Test successful upload returns 201 with valid `AssetResponseDto` in response body
  - Test upload to non-existent task returns 404
  - Test upload by user not in project returns 403
  - Test oversized file upload returns 413 or 400
  - Test invalid file type upload returns 400
  - Test empty file upload returns 400
- [ ] Integration tests exist for the GET endpoint:
  - Test successful file retrieval returns 200 with correct Content-Type and Content-Disposition headers
  - Test retrieval of non-existent asset returns 404
  - Test retrieval by user not in project returns 403
  - Test retrieval of asset with Failed status returns 404 or 400
  - Test retrieval of asset with Pending/Processing status returns 400 or 404

### 19. Controller Naming Convention Consistency
- [ ] Controller name and route follow existing naming conventions in the codebase (singular vs. plural - verify consistency with `TaskController`, `ProjectController`, `ColumnController`)
- [ ] Endpoint route paths are RESTful and intuitive (e.g., `POST /api/attachment/task/{taskId}`, `GET /api/attachment/{assetId}`)

### 20. CancellationToken Support
- [ ] POST endpoint includes a `CancellationToken cancellationToken` parameter in its signature
- [ ] CancellationToken is passed to `_assetService.UploadAssetAsync()` to support graceful cancellation if the client disconnects mid-upload
- [ ] GET endpoint optionally supports CancellationToken for file stream operations (less critical but recommended for consistency)
