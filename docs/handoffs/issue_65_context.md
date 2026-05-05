# Issue #65: Setup Local File Storage Infrastructure

## Business Value

**Who:** KanbAI users who need to attach files to tasks (screenshots, documents, design mockups, logs, etc.) as part of their project management workflow

**What:** Establish the foundational file storage infrastructure that enables users to upload files that will be associated with Kanban tasks. This includes defining where uploaded files are physically stored on the server, enforcing security constraints on file size and type, and preparing the groundwork for asynchronous file processing.

**Why:** Before users can attach files to tasks (the core user-facing feature), the application must have a secure, well-organized file storage system in place. Without this infrastructure:
- The application has nowhere safe to store uploaded files
- There are no safeguards against malicious file uploads (e.g., executable files, excessively large files that could exhaust disk space)
- Future asynchronous processing features (thumbnails, virus scanning, metadata extraction) would have no standardized storage location to reference
- Security vulnerabilities could arise from unrestricted file types or unvalidated file paths

This is the first building block in the "Asynchronous File Attachments" milestone, establishing the storage foundation that subsequent issues (#66 AssetService, #67 AttachmentController) will build upon.

## Current State vs. Desired State

### Current State (Database Schema Exists, No Storage Infrastructure)

**Asset Entity Schema (Established):**
- Location: `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Models/Entities/Asset.cs`
- Properties:
  - `FileName` (string) - Original filename uploaded by the user
  - `StorageKey` (string) - Unique identifier for the file on disk (currently has no defined storage location)
  - `ThumbnailKey` (string?) - Optional identifier for a generated thumbnail (currently has no defined storage location)
  - `MimeType` (string) - MIME type of the file (e.g., "image/png", "application/pdf")
  - `FileSize` (long) - Size of the file in bytes (no validation constraint enforced yet)
  - `ProcessingStatus` (ProcessingStatus enum) - Workflow state: Pending, Processing, Completed, Failed
  - `KanbanTaskId` (Guid) - Foreign key to the parent task
- EF configuration exists: `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Data/Configurations/AssetConfiguration.cs`
- Database migration applied: `20260411130153_AddAssetAndTaskCommentEntities.cs`

**Project Structure (No wwwroot or Storage Folder):**
- Main application directory: `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/`
- Existing folders: Controllers, DTOs, Data, Extensions, Hubs, Middleware, Migrations, Models, Services
- **Missing:** No `wwwroot` folder exists
- **Missing:** No dedicated storage directory (e.g., `wwwroot/uploads`, `Storage/`, `App_Data/uploads`)

**Configuration (No File Storage Settings):**
- Location: `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/appsettings.json` and `appsettings.Development.json`
- Existing sections: `ConnectionStrings`, `Cors`, `Logging`, `JwtSettings`
- **Missing:** No `FileStorage` or equivalent configuration section
- **Missing:** No settings for:
  - Physical storage path (e.g., "wwwroot/uploads")
  - Maximum file size limit (e.g., 10MB)
  - Allowed file extensions (e.g., [".jpg", ".png", ".pdf", ".docx"])
  - Disallowed MIME types (e.g., executable files)

**Program.cs (No Static File Middleware):**
- Location: `/c/temp/KanbAI-Core/KanbAI-Core/KanbAI-Core/Program.cs`
- Current middleware pipeline includes: Exception handler, HTTPS redirection, CORS, Authentication, Authorization
- **Missing:** No `app.UseStaticFiles()` registration (required if files need to be directly accessible via HTTP)
- **Missing:** No file storage service registration (e.g., `IFileStorageService`, `IFileValidator`)

**Security Considerations (No Validation Layer):**
- No enforcement of maximum file size (users could theoretically upload gigabyte-sized files)
- No enforcement of allowed file types (users could attempt to upload .exe, .bat, .sh scripts)
- No path traversal protection (malicious filenames like `../../etc/passwd` need sanitization)

**Impact:**
When issue #67 (AttachmentController endpoints) is implemented, there will be no safe location to save uploaded files, no validation rules to enforce, and potential security vulnerabilities. This issue must be completed first to establish the storage contract.

### Desired State (Secure Local File Storage Configuration)

**Configuration Section Defined:**
- `appsettings.json` (production defaults - restrictive):
  ```json
  "FileStorage": {
    "StoragePath": "wwwroot/uploads",
    "MaxFileSizeBytes": 10485760,
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".gif", ".pdf", ".docx", ".xlsx", ".txt"]
  }
  ```
- `appsettings.Development.json` (development overrides - may be more permissive for testing):
  ```json
  "FileStorage": {
    "StoragePath": "wwwroot/uploads",
    "MaxFileSizeBytes": 52428800,
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".gif", ".pdf", ".docx", ".xlsx", ".txt", ".log", ".json"]
  }
  ```

**Physical Storage Location Prepared:**
- `wwwroot/uploads/` folder exists (or is created automatically on application startup)
- Folder structure supports subdirectories if needed (e.g., `uploads/{year}/{month}/` or `uploads/tasks/{taskId}/`)
- `.gitignore` updated to exclude uploaded files from source control (e.g., `wwwroot/uploads/*`)

**Static File Middleware Configured (If Required):**
- If files need to be directly accessible via URL (e.g., `https://localhost:5001/uploads/abc123.jpg`), `app.UseStaticFiles()` is registered in the middleware pipeline
- If files should only be served through authenticated API endpoints (more secure), static file middleware is NOT needed

**File Validation Rules Defined:**
- Maximum file size enforced BEFORE file is written to disk (reject uploads that exceed `MaxFileSizeBytes`)
- File extension validated against `AllowedExtensions` whitelist (case-insensitive)
- MIME type validation (verify the MIME type matches the file extension to prevent MIME type spoofing)
- Filename sanitization (remove or escape path traversal characters: `..`, `/`, `\`)

**Storage Path Determination:**
- Application can resolve the storage path at runtime (relative to the application root or absolute path)
- If using `wwwroot`, the path is relative to the web root (default for ASP.NET Core static files)
- If using a path outside `wwwroot` (e.g., `/var/uploads` on Linux), ensure the application has read/write permissions

**Error Handling for Storage Failures:**
- If the storage directory does not exist and cannot be created, the application logs an error and fails gracefully (does not crash on startup)
- If disk space is exhausted during an upload, the application returns a clear error message to the user (e.g., "Server storage unavailable")

**Documentation (Optional):**
- Configuration keys documented in a comment or README so future developers understand how to customize storage settings
- Security considerations noted (e.g., "Never allow executable extensions like .exe, .bat, .sh")

## Milestone Context

This issue is part of **Milestone #7: Asynchronous File Attachments**, which enables users to attach files to Kanban tasks with background processing.

### Related Issues in Milestone

| Issue # | Title | State | Relationship |
|---------|-------|-------|--------------|
| **#65** | **Setup Local File Storage Infrastructure** | **Open** | **THIS ISSUE - Foundation** |
| #66 | Create AssetService for Async Processing | Open | **Depends on #65** - AssetService will use the storage path and validation rules defined here |
| #67 | Implement AttachmentController Endpoints | Open | **Depends on #65 and #66** - Controller will validate uploads using #65's rules and delegate storage to #66's service |
| #68 | Document AI File Handling Implementation (AI_LOGS.md) | Open | **Depends on #67** - Documents the completed feature after implementation |

### Implementation Order

1. **#65 (THIS ISSUE)** - Establish where files go, how big they can be, and what types are allowed
2. **#66** - Build the service that saves files to disk, generates storage keys, and updates the Asset entity
3. **#67** - Expose HTTP endpoints that accept file uploads and delegate to the AssetService
4. **#68** - Document the entire flow for future maintenance

**Critical Path:** Issues #66 and #67 cannot proceed safely without the storage infrastructure defined in #65.

## Acceptance Criteria

### 1. Configuration Section Exists
- [ ] `appsettings.json` contains a `FileStorage` section with `StoragePath`, `MaxFileSizeBytes`, and `AllowedExtensions` keys
- [ ] `appsettings.Development.json` contains a `FileStorage` section (may have different values for development, e.g., larger max file size for testing)
- [ ] `StoragePath` specifies a valid folder path (e.g., `"wwwroot/uploads"` or `"Storage/uploads"`)
- [ ] `MaxFileSizeBytes` is set to a reasonable production limit (10MB recommended, 10485760 bytes)
- [ ] `AllowedExtensions` is an array of lowercase file extensions (e.g., `[".jpg", ".png", ".pdf"]`) that includes common file types users need (images, documents) but excludes executable extensions (`.exe`, `.bat`, `.sh`, `.dll`, `.so`)

### 2. Storage Directory is Prepared
- [ ] The folder specified in `StoragePath` exists in the repository structure (or is created automatically on application startup if missing)
- [ ] `.gitignore` includes the uploads folder (e.g., `wwwroot/uploads/*` or `Storage/uploads/*`) so that uploaded files are NOT committed to source control
- [ ] A `.gitkeep` or `README.md` file exists inside the storage folder to preserve the empty directory structure in Git

### 3. Static File Serving (If Applicable)
- [ ] If files need to be publicly accessible via URL (e.g., for image thumbnails displayed in the UI), `app.UseStaticFiles()` is registered in `Program.cs` middleware pipeline
- [ ] If files should only be served through authenticated API endpoints (more secure), static file middleware is NOT registered (document this decision in the handoff or code comments)

### 4. File Size Limit is Enforced at the Web Server Level
- [ ] The web server is configured to reject file uploads that exceed `MaxFileSizeBytes` BEFORE the entire file is read into memory or written to disk
- [ ] An HTTP 413 (Payload Too Large) or 400 (Bad Request) response is returned when a user attempts to upload a file larger than the configured limit

### 5. Allowed Extensions are Enforced via Whitelist
- [ ] The `AllowedExtensions` configuration array does NOT include dangerous file types:
  - Executables: `.exe`, `.bat`, `.cmd`, `.sh`, `.ps1`, `.dll`, `.so`, `.dylib`
  - Scripts: `.js`, `.vbs`, `.wsf`, `.hta`
  - Archives (if not needed): `.zip`, `.rar`, `.7z` (only allow if explicitly required by the feature)
- [ ] File extension validation is case-insensitive (e.g., `.JPG` and `.jpg` are both accepted if `.jpg` is in the whitelist)

### 6. Storage Path is Resolved Correctly at Runtime
- [ ] The application resolves the storage path correctly (whether relative to the application root or absolute) without throwing path resolution errors
- [ ] If the storage path is outside `wwwroot`, the application has the necessary file system permissions to read/write to that directory

### 7. Path Traversal Prevention is Enforced
- [ ] Filenames containing path traversal characters (`..`, `/`, `\`) are rejected OR sanitized (e.g., `../../etc/passwd.jpg` is rejected with HTTP 400)
- [ ] Uploaded files are stored using server-generated storage keys (not the original user-provided filename) to prevent filename-based attacks

### 8. MIME Type Validation is Planned
- [ ] The configuration or documentation notes that MIME type validation will be implemented in issue #66 (AssetService) to verify that uploaded file MIME types match their file extensions (prevents MIME type spoofing)

### 9. Storage Folder Access Control is Defined
- [ ] If files contain sensitive user data (e.g., private documents), the storage folder is NOT publicly accessible via direct URL (static file middleware is disabled for that path, files are served through authenticated API endpoints)
- [ ] If files are intended to be public (e.g., user avatars, public images), static file middleware serves them from `wwwroot`

### 10. Storage Directory Creation Failure Handling
- [ ] If the storage directory does not exist and cannot be created at application startup (e.g., due to insufficient permissions), the application logs an error message that includes the attempted path and the reason for failure
- [ ] The application fails to start if the storage directory cannot be created (fail-fast behavior prevents silent misconfigurations)

### 11. Disk Space Exhaustion Handling
- [ ] If disk space is exhausted during an upload, the application catches the I/O exception and returns an HTTP 500 or 507 response with a message indicating the server storage is unavailable (does not leak internal file paths or stack traces to the user)

### 12. Configuration Validation: StoragePath
- [ ] At application startup, the application validates that `FileStorage:StoragePath` is not null, empty, or whitespace
- [ ] If the validation fails, the application logs an error message and refuses to start (fail-fast behavior)

### 13. Configuration Validation: MaxFileSizeBytes
- [ ] At application startup, the application validates that `FileStorage:MaxFileSizeBytes` is a positive integer greater than zero
- [ ] If the validation fails, the application logs an error message and refuses to start

### 14. Configuration Validation: AllowedExtensions
- [ ] At application startup, the application validates that `FileStorage:AllowedExtensions` is not null or empty and contains at least one valid file extension (e.g., starts with a period)
- [ ] If the validation fails, the application logs an error message and refuses to start

### 15. Security Rationale is Recorded
- [ ] A comment in `appsettings.json`, a code comment in the configuration class, or a note in this handoff document explains that executable file extensions (`.exe`, `.bat`, `.sh`, etc.) are excluded from the allowed list to prevent malware uploads
- [ ] The documentation or comments reference that this configuration is consumed by AssetService (issue #66) and AttachmentController (issue #67)
