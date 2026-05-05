# Issue #66: Create AssetService for Async Processing

## Business Value

**Who:** KanbAI users who need to attach files to Kanban tasks and receive real-time feedback on upload progress and completion status.

**What:** Build an asynchronous service that handles the complete file upload lifecycle: reading incoming file streams, saving them to disk using unique storage keys, creating corresponding Asset database records, and broadcasting real-time status updates to connected clients via SignalR.

**Why:** This service bridges the gap between file storage infrastructure (Issue #65, now completed) and the public API endpoints (Issue #67, planned). Without AssetService:
- AttachmentController endpoints would have no service layer to delegate file operations to, violating clean architecture principles
- File naming conflicts could occur if multiple users upload files with the same name simultaneously (no unique storage key generation)
- Users would have no visibility into whether their large file uploads succeeded or failed (no real-time status broadcasting)
- The Asset entity's ProcessingStatus workflow (Pending → Processing → Completed/Failed) would not be implemented, leaving the database in an inconsistent state
- File metadata (MIME type, file size) validation and persistence would be duplicated across multiple controllers instead of centralized

This is the second building block in the "Asynchronous File Attachments" milestone, consuming the storage infrastructure from #65 and providing the service layer that #67's endpoints will call.

## Current State vs. Desired State

### Current State (Infrastructure Ready, Service Layer Missing)

**File Storage Infrastructure (Established by Issue #65):**
- **Configuration:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/appsettings.json` contains `FileStorage` section:
  - `StoragePath`: "wwwroot/uploads"
  - `MaxFileSizeBytes`: 10485760 (10 MB)
  - `AllowedExtensions`: [".jpg", ".jpeg", ".png", ".gif", ".pdf", ".docx", ".xlsx", ".txt"]
- **Options Class:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Configuration/FileStorageOptions.cs` binds configuration values
- **Validation:** Configuration is validated at application startup via `AddFileStorage()` extension in `ServiceCollectionExtensions.cs` (fail-fast if invalid)
- **Physical Storage:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/wwwroot/uploads/` directory exists with `.gitkeep` file

**Asset Entity Schema (Ready for Use):**
- **Location:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Entities/Asset.cs`
- **Properties:**
  - `FileName` (string) - Original filename uploaded by the user
  - `StorageKey` (string) - Unique identifier for the file on disk (currently no service generates this)
  - `ThumbnailKey` (string?) - Optional identifier for a generated thumbnail (nullable, can be null initially)
  - `MimeType` (string) - MIME type of the file (e.g., "image/png", "application/pdf")
  - `FileSize` (long) - Size of the file in bytes
  - `ProcessingStatus` (ProcessingStatus enum) - Workflow state: Pending, Processing, Completed, Failed
  - `KanbanTaskId` (Guid) - Foreign key to the parent task
- **ProcessingStatus Enum:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Enums/ProcessingStatus.cs` defines 4 states:
  - `Pending = 0` - Upload initiated but not yet started
  - `Processing = 1` - File is being written to disk
  - `Completed = 2` - File successfully saved and Asset record created
  - `Failed = 3` - Upload failed due to validation error, I/O error, or other exception
- **Database Integration:** `ApplicationDbContext.cs` includes `DbSet<Asset> Assets` property

**Existing Service Patterns (Established Conventions):**
- **Folder Structure:** Services are organized by domain feature in subdirectories (e.g., `Services/Auth/`, `Services/Tasks/`, `Services/Projects/`, `Services/Columns/`)
- **Interface + Implementation:** Each service has an interface (e.g., `ITaskService`) in the same folder as its implementation (e.g., `TaskService`)
- **Dependency Injection:** Services are registered in `Program.cs` with appropriate lifetimes:
  - Scoped: `IProjectService`, `IColumnService`, `ITaskService` (services that depend on EF Core DbContext)
  - Transient: `ITokenService` (stateless services with no DbContext dependency)
  - Singleton: Configuration options, password hashers
- **Constructor Injection:** Services receive dependencies via constructor (e.g., `ApplicationDbContext`, `ILogger<T>`, `IHubContext<KanbanHub>`)
- **Naming Convention:** `{Feature}Service` for implementation, `I{Feature}Service` for interface

**Event Broadcasting Infrastructure (Established by Issues #72 and #74):**
- **SignalR Hub:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Hubs/KanbanHub.cs` provides real-time communication
  - Hub methods: `JoinProjectGroup(string projectId)`, `LeaveProjectGroup(string projectId)`
  - Group naming convention: `project_{projectId_lowercase}` (e.g., `project_123e4567-e89b-12d3-a456-426614174000`)
  - Clients join project-specific groups to receive updates scoped to their project
- **Broadcasting Pattern (from TaskService):** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Tasks/TaskService.cs` demonstrates established pattern:
  1. Inject `IHubContext<KanbanHub>` via constructor
  2. After persisting entity changes, call `_hubContext.Clients.Group(groupName).SendAsync(eventName, payload)`
  3. Wrap broadcast in try-catch to prevent exceptions from failing the entire operation (log warning if broadcast fails)
  4. Event names follow PascalCase convention (e.g., "TaskCreated", "TaskMoved")
  5. Event payloads are DTOs (e.g., `TaskMovedEventDto`) that include both event metadata and full entity DTO
- **Event DTO Example:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/DTOs/TaskMovedEventDto.cs` shows structure:
  ```csharp
  public record TaskMovedEventDto
  {
      public required string TaskId { get; init; }
      public required string OldColumnId { get; init; }
      public required string NewColumnId { get; init; }
      public required int OldTaskOrder { get; init; }
      public required int NewTaskOrder { get; init; }
      public required TaskResponseDto Task { get; init; }
  }
  ```

**What's Missing:**
- **No AssetService Interface:** No `IAssetService` exists to define the contract for file upload operations
- **No AssetService Implementation:** No service class exists to handle file streaming, storage key generation, or Asset entity persistence
- **No Storage Key Generation Logic:** No mechanism exists to generate unique, collision-resistant filenames (e.g., `Guid_originalFilename.ext` or hash-based naming)
- **No Stream Processing:** No code exists to read `Stream` or `IFormFile` inputs, validate file size, and write bytes to disk asynchronously
- **No Asset Event Broadcasting:** No real-time events are defined or broadcasted for asset upload lifecycle (e.g., "AssetUploading", "AssetCompleted", "AssetFailed")
- **No MIME Type Validation:** File storage infrastructure validates extensions, but no service enforces that the file's actual MIME type matches its extension (prevents MIME type spoofing)
- **No Transaction Management:** If file write succeeds but database insert fails (or vice versa), the system could end up in an inconsistent state (orphaned files or database records with no physical file)
- **No Folder Creation Logic:** If the `wwwroot/uploads/` directory is accidentally deleted after application startup, writes will fail (no runtime folder existence check)

**Impact:**
Issue #67 (AttachmentController) cannot be implemented without AssetService. The controller needs a service to call for file uploads, and that service must handle the complexity of stream I/O, storage key generation, database persistence, and real-time broadcasting.

### Desired State (AssetService Ready for Controller Integration)

**Service Interface Defined:**
- **Location:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Assets/IAssetService.cs`
- **Method Contract (Example):**
  ```csharp
  Task<(AssetResponseDto? data, UploadAssetResult result)> UploadAssetAsync(
      Guid taskId,
      Stream fileStream,
      string fileName,
      string contentType,
      long fileSize,
      Guid userId);
  ```
  - **Parameters:**
    - `taskId`: The parent KanbanTask's ID (validates that the task exists and the user has access)
    - `fileStream`: The incoming file stream from the HTTP request (e.g., `IFormFile.OpenReadStream()`)
    - `fileName`: The original filename provided by the client (sanitized before use)
    - `contentType`: The MIME type from the Content-Type header (validated against the file extension)
    - `fileSize`: The file size in bytes (validated against `MaxFileSizeBytes` before streaming)
    - `userId`: The authenticated user's ID (for authorization checks — user must be a member of the task's project)
  - **Return Type:** A tuple with:
    - `AssetResponseDto?`: The created asset's DTO (null if operation failed)
    - `UploadAssetResult`: An enum discriminator indicating success or the specific failure reason (e.g., `Success`, `TaskNotFound`, `FileTooLarge`, `InvalidFileType`, `StorageError`, `UserNotAuthorized`)

**Service Implementation:**
- **Location:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Services/Assets/AssetService.cs`
- **Dependencies (Constructor Injection):**
  - `ApplicationDbContext _context` (for querying KanbanTask, persisting Asset entity)
  - `ILogger<AssetService> _logger` (structured logging for observability)
  - `IHubContext<KanbanHub> _hubContext` (real-time event broadcasting)
  - `IOptions<FileStorageOptions> _storageOptions` (access to StoragePath, MaxFileSizeBytes, AllowedExtensions)
  - `IWebHostEnvironment _environment` (to resolve the absolute path to `wwwroot/uploads`)

**Core Logic Workflow:**

1. **Authorization & Validation:**
   - Query the database to load the `KanbanTask` entity with `.Include(t => t.Column).ThenInclude(c => c.Project).ThenInclude(p => p.Members)`
   - Verify the task exists (return `TaskNotFound` if not)
   - Verify the authenticated user is a member of the task's project (return `UserNotAuthorized` if not)
   - Validate `fileSize` does not exceed `FileStorageOptions.MaxFileSizeBytes` (return `FileTooLarge` if exceeded)
   - Validate the file extension (extracted from `fileName`) is in `FileStorageOptions.AllowedExtensions` (case-insensitive comparison, return `InvalidFileType` if not allowed)
   - Validate the `contentType` matches the file extension (e.g., `.jpg` should have `image/jpeg` MIME type, return `InvalidFileType` on mismatch)

2. **Storage Key Generation:**
   - Generate a unique storage key to prevent filename collisions and path traversal attacks
   - Example strategy: `{Guid.NewGuid()}_{sanitizedFileName}` (e.g., `3fa85f64-5717-4562-b3fc-2c963f66afa6_screenshot.png`)
   - Sanitize `fileName` by removing or escaping path traversal characters (`..`, `/`, `\`)
   - Resolve the full physical path: `Path.Combine(_environment.ContentRootPath, _storageOptions.Value.StoragePath, storageKey)`

3. **Asset Entity Creation (Pending Status):**
   - Create a new `Asset` entity with:
     - `FileName = fileName` (original filename)
     - `StorageKey = storageKey` (unique generated key)
     - `ThumbnailKey = null` (thumbnail generation is out of scope for this issue)
     - `MimeType = contentType` (validated MIME type)
     - `FileSize = fileSize` (validated file size)
     - `ProcessingStatus = ProcessingStatus.Pending` (initial state before file write)
     - `KanbanTaskId = taskId` (foreign key)
   - Add the entity to `_context.Assets`
   - Call `await _context.SaveChangesAsync()` to persist the entity and generate its `Id`
   - Broadcast an "AssetUploadStarted" event to the task's project group (includes `AssetId`, `TaskId`, `FileName`, `ProcessingStatus = Pending`)

4. **Asynchronous File Write (Processing Status):**
   - Update `asset.ProcessingStatus = ProcessingStatus.Processing`
   - Call `await _context.SaveChangesAsync()` to persist the status change
   - Broadcast an "AssetProcessing" event to notify clients that the file write has started
   - Ensure the storage directory exists at runtime:
     ```csharp
     var storageDirectory = Path.GetDirectoryName(fullPath);
     if (!Directory.Exists(storageDirectory))
     {
         Directory.CreateDirectory(storageDirectory);
     }
     ```
   - Open a `FileStream` with `FileMode.Create`, `FileAccess.Write`, `FileShare.None` (exclusive write lock)
   - Copy the incoming `fileStream` to the `FileStream` asynchronously:
     ```csharp
     await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
     await inputStream.CopyToAsync(fileStream, cancellationToken);
     await fileStream.FlushAsync(cancellationToken);
     ```
   - Use a `CancellationToken` to allow graceful cancellation if the user disconnects mid-upload

5. **Success Path (Completed Status):**
   - If the file write succeeds:
     - Update `asset.ProcessingStatus = ProcessingStatus.Completed`
     - Call `await _context.SaveChangesAsync()`
     - Broadcast an "AssetCompleted" event with the full `AssetResponseDto` payload
     - Return `(assetDto, UploadAssetResult.Success)`

6. **Failure Path (Failed Status):**
   - Wrap the file write in a try-catch block
   - If any exception occurs (I/O error, disk full, invalid stream):
     - Update `asset.ProcessingStatus = ProcessingStatus.Failed`
     - Call `await _context.SaveChangesAsync()`
     - Log the exception with structured logging: `_logger.LogError(ex, "Failed to upload asset {AssetId} to {StoragePath}", asset.Id, fullPath)`
     - Broadcast an "AssetFailed" event with the error (do not expose internal paths or stack traces to clients)
     - Delete the partially written file if it exists: `if (File.Exists(fullPath)) File.Delete(fullPath);`
     - Return `(null, UploadAssetResult.StorageError)`

7. **Transaction Consistency (Critical):**
   - If the database insert fails but the file write succeeds, the file becomes an orphan (no Asset record references it)
   - If the file write fails but the database insert succeeds, the Asset record points to a non-existent file
   - **Mitigation Strategy:**
     - Persist the Asset entity with `ProcessingStatus = Pending` BEFORE attempting the file write (establishes the database record first)
     - If the file write fails, update the status to `Failed` (the database record exists but indicates failure)
     - Optionally, implement a background cleanup job that periodically deletes orphaned files (files in `wwwroot/uploads/` with no corresponding Asset record) — this is out of scope for #66 but should be documented as a future improvement

**DTO Definitions:**

- **AssetResponseDto:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/DTOs/AssetResponseDto.cs`
  ```csharp
  public record AssetResponseDto
  {
      public required string Id { get; init; }
      public required string FileName { get; init; }
      public required string StorageKey { get; init; }
      public string? ThumbnailKey { get; init; }
      public required string MimeType { get; init; }
      public required long FileSize { get; init; }
      public required ProcessingStatus ProcessingStatus { get; init; }
      public required string KanbanTaskId { get; init; }
      public required DateTimeOffset CreatedAt { get; init; }
      public required DateTimeOffset UpdatedAt { get; init; }
  }
  ```

- **UploadAssetResult Enum:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/DTOs/UploadAssetResult.cs`
  ```csharp
  public enum UploadAssetResult
  {
      Success,
      TaskNotFound,
      UserNotAuthorized,
      FileTooLarge,
      InvalidFileType,
      StorageError
  }
  ```

- **SignalR Event DTOs (Optional but Recommended):**
  - `AssetUploadStartedEventDto`: `{ AssetId, TaskId, FileName, ProcessingStatus }`
  - `AssetCompletedEventDto`: `{ AssetId, TaskId, Asset: AssetResponseDto }`
  - `AssetFailedEventDto`: `{ AssetId, TaskId, ErrorMessage }`

**Service Registration:**
- **Location:** `c:/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Program.cs`
- **Registration:** `builder.Services.AddScoped<IAssetService, AssetService>();` (scoped lifetime because it depends on `ApplicationDbContext`)

**Error Handling & Logging:**
- All exceptions during file I/O are caught, logged with structured logging, and result in `ProcessingStatus.Failed`
- Broadcast failures are logged as warnings but do not fail the operation (same pattern as `TaskService.BroadcastAsync()`)
- File path and directory information is logged at `Information` level for successful operations and `Error` level for failures
- PII (user-uploaded file contents) is NEVER logged

**Security Considerations:**
- **Path Traversal Prevention:** Filenames are sanitized to remove `..`, `/`, `\` before constructing the storage path
- **MIME Type Validation:** The service validates that the `contentType` matches the file extension (e.g., a file named `.jpg` must have a MIME type of `image/jpeg` or `image/jpg`)
- **File Size Enforcement:** Files exceeding `MaxFileSizeBytes` are rejected BEFORE the stream is read (validated at the controller level or service entry point)
- **Authorization:** Only users who are members of the task's project can upload assets to that task

## Milestone Context

This issue is part of **Milestone #7: Asynchronous File Attachments**, which enables users to attach files to Kanban tasks with real-time progress updates.

### Related Issues in Milestone

| Issue # | Title | State | Relationship |
|---------|-------|-------|--------------|
| #65 | Setup Local File Storage Infrastructure | **Merged** | **Prerequisite COMPLETED** - AssetService consumes the FileStorageOptions configuration and wwwroot/uploads directory established by #65 |
| **#66** | **Create AssetService for Async Processing** | **Open** | **THIS ISSUE - Core Service Layer** |
| #67 | Implement AttachmentController Endpoints | Open | **Depends on #66** - Controller will call AssetService methods to handle file uploads |
| #68 | Document AI File Handling Implementation (AI_LOGS.md) | Open | **Depends on #67** - Documents the completed feature after implementation |

### Implementation Order

1. ~~#65 - File storage infrastructure~~ (COMPLETED - config, folder, validation all in place)
2. **#66 (THIS ISSUE)** - Build the service that orchestrates file I/O, entity persistence, and event broadcasting
3. #67 - Expose HTTP endpoints that accept multipart/form-data uploads and delegate to AssetService
4. #68 - Document the entire flow for future maintenance

**Critical Path:** Issue #67 cannot proceed without the AssetService interface and implementation.

## Acceptance Criteria

### 1. Service Interface Defined
- [ ] `IAssetService` interface exists at `Services/Assets/IAssetService.cs`
- [ ] Interface defines an `UploadAssetAsync` method (or equivalent) that accepts: task ID, file stream, filename, content type, file size, and user ID
- [ ] Method returns a tuple containing an optional `AssetResponseDto` and a result discriminator enum (e.g., `UploadAssetResult`)

### 2. Service Implementation Exists
- [ ] `AssetService` class implements `IAssetService` at `Services/Assets/AssetService.cs`
- [ ] Constructor injects: `ApplicationDbContext`, `ILogger<AssetService>`, `IHubContext<KanbanHub>`, `IOptions<FileStorageOptions>`, `IWebHostEnvironment`

### 3. Authorization Check: User is Project Member
- [ ] Service queries the KanbanTask with `.Include(t => t.Column).ThenInclude(c => c.Project).ThenInclude(p => p.Members)`
- [ ] If the task does not exist, the method returns `(null, UploadAssetResult.TaskNotFound)`
- [ ] If the authenticated user is not a member of the task's project, the method returns `(null, UploadAssetResult.UserNotAuthorized)`

### 4. File Size Validation
- [ ] Service validates that the `fileSize` parameter does not exceed `FileStorageOptions.MaxFileSizeBytes`
- [ ] If the file is too large, the method returns `(null, UploadAssetResult.FileTooLarge)` WITHOUT attempting to read or write the stream

### 5. File Extension Validation
- [ ] Service extracts the file extension from the `fileName` parameter (case-insensitive)
- [ ] Service validates that the extension is present in `FileStorageOptions.AllowedExtensions`
- [ ] If the extension is not allowed, the method returns `(null, UploadAssetResult.InvalidFileType)`

### 6. MIME Type Validation
- [ ] Service validates that the `contentType` parameter matches the expected MIME type for the file extension (e.g., `.jpg` → `image/jpeg`, `.pdf` → `application/pdf`)
- [ ] If the MIME type does not match the extension (MIME type spoofing attempt), the method returns `(null, UploadAssetResult.InvalidFileType)`

### 7. Storage Key Generation
- [ ] Service generates a unique storage key for each uploaded file (e.g., `{Guid}_{sanitizedFileName}`)
- [ ] Storage key generation sanitizes the original filename by removing or escaping path traversal characters (`..`, `/`, `\`)
- [ ] Two files uploaded simultaneously with the same filename receive different storage keys (no collision)

### 8. Asset Entity Created with Pending Status
- [ ] Service creates an `Asset` entity with `ProcessingStatus = ProcessingStatus.Pending`
- [ ] Asset entity includes: `FileName`, `StorageKey`, `MimeType`, `FileSize`, `KanbanTaskId`, `ThumbnailKey = null`
- [ ] Asset is persisted to the database BEFORE the file write begins (establishes the database record first)

### 9. Real-Time Event: Upload Started
- [ ] After persisting the Asset entity with `Pending` status, the service broadcasts an "AssetUploadStarted" event (or equivalent) to the task's project group
- [ ] Event payload includes: `AssetId`, `TaskId`, `FileName`, `ProcessingStatus`
- [ ] Broadcast uses the project group naming convention: `project_{projectId_lowercase}`

### 10. Processing Status Update
- [ ] Before writing the file to disk, the service updates `asset.ProcessingStatus = ProcessingStatus.Processing`
- [ ] Service calls `SaveChangesAsync()` to persist the status change
- [ ] Service broadcasts an "AssetProcessing" event (or equivalent) to notify clients

### 11. Storage Directory Creation
- [ ] Before writing the file, the service checks if the storage directory exists (e.g., `wwwroot/uploads/`)
- [ ] If the directory does not exist, the service creates it using `Directory.CreateDirectory()`
- [ ] If directory creation fails, the service updates the Asset status to `Failed` and returns `(null, UploadAssetResult.StorageError)`

### 12. Asynchronous File Write
- [ ] Service opens a `FileStream` with `FileMode.Create`, `FileAccess.Write`, `FileShare.None` (exclusive write lock)
- [ ] Service calls `await inputStream.CopyToAsync(fileStream)` to write the file asynchronously
- [ ] Service flushes the `FileStream` after the copy completes
- [ ] File write supports `CancellationToken` to allow graceful cancellation if the user disconnects

### 13. Success Path: Completed Status
- [ ] If the file write succeeds, the service updates `asset.ProcessingStatus = ProcessingStatus.Completed`
- [ ] Service calls `SaveChangesAsync()` to persist the status change
- [ ] Service broadcasts an "AssetCompleted" event with the full `AssetResponseDto` payload
- [ ] Service returns `(assetDto, UploadAssetResult.Success)`

### 14. Failure Path: Failed Status
- [ ] If any exception occurs during file write, the service catches the exception
- [ ] Service updates `asset.ProcessingStatus = ProcessingStatus.Failed`
- [ ] Service calls `SaveChangesAsync()` to persist the failed status
- [ ] Service logs the exception with structured logging (includes `AssetId`, `StoragePath`, exception message)
- [ ] Service broadcasts an "AssetFailed" event (does NOT expose internal file paths or stack traces to clients)
- [ ] Service deletes the partially written file if it exists: `if (File.Exists(fullPath)) File.Delete(fullPath);`
- [ ] Service returns `(null, UploadAssetResult.StorageError)`

### 15. Broadcast Failure Handling
- [ ] SignalR broadcast calls are wrapped in try-catch blocks (same pattern as `TaskService.BroadcastAsync()`)
- [ ] If a broadcast fails, the service logs a warning but does NOT fail the entire upload operation
- [ ] Structured logging includes: `EventName`, `GroupName`, exception message

### 16. DTOs Defined
- [ ] `AssetResponseDto` record exists at `DTOs/AssetResponseDto.cs` with all Asset entity properties mapped (Id, FileName, StorageKey, ThumbnailKey, MimeType, FileSize, ProcessingStatus, KanbanTaskId, CreatedAt, UpdatedAt)
- [ ] `UploadAssetResult` enum exists at `DTOs/UploadAssetResult.cs` with discriminators: `Success`, `TaskNotFound`, `UserNotAuthorized`, `FileTooLarge`, `InvalidFileType`, `StorageError`

### 17. Service Registered in DI Container
- [ ] `Program.cs` includes the line: `builder.Services.AddScoped<IAssetService, AssetService>();`
- [ ] Service is registered with `Scoped` lifetime (because it depends on `ApplicationDbContext`, which is scoped)

### 18. Path Traversal Prevention
- [ ] Filename sanitization removes or escapes the following characters: `..`, `/`, `\`
- [ ] Storage key construction uses `Path.Combine()` to safely join paths (prevents manual concatenation errors)
- [ ] If a filename contains path traversal characters after sanitization fails, the service returns `InvalidFileType`

### 19. No Secrets or PII in Logs
- [ ] Structured logs include: `AssetId`, `TaskId`, `FileName`, `StoragePath` (relative path only), `FileSize`, `MimeType`, `ProcessingStatus`
- [ ] Logs do NOT include: file contents, user IP addresses, full file system paths (use relative paths like "wwwroot/uploads/file.jpg")
- [ ] Exception messages logged at `Error` level do not expose sensitive information (sanitize stack traces if necessary)

### 20. Transaction Consistency Documented
- [ ] Code comments or this handoff document explain the transaction consistency strategy:
  - Asset entity is created with `Pending` status BEFORE file write
  - If file write fails, Asset status is updated to `Failed` (database record exists but indicates failure)
  - Future enhancement: implement a background cleanup job to delete orphaned files (files with no corresponding Asset record) or orphaned Asset records (Asset with `Completed` status but missing file)
