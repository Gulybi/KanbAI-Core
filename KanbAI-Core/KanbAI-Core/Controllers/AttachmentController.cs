namespace KanbAI_Core.Controllers;

using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Hubs;
using KanbAI_Core.Models.Configuration;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Assets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class AttachmentController : ControllerBase
{
    // Must stay in sync with FileStorageOptions.MaxFileSizeBytes in appsettings.json.
    // The service layer re-enforces this limit; this attribute rejects oversized uploads
    // at the pipeline level before the controller buffers the stream.
    private const long MaxUploadBytes = 10_485_760; // 10 MB

    private readonly IAssetService _assetService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AttachmentController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly FileStorageOptions _storageOptions;
    private readonly IHubContext<KanbanHub> _hubContext;

    public AttachmentController(
        IAssetService assetService,
        ApplicationDbContext context,
        ILogger<AttachmentController> logger,
        IWebHostEnvironment environment,
        IOptions<FileStorageOptions> storageOptions,
        IHubContext<KanbanHub> hubContext)
    {
        _assetService = assetService;
        _context = context;
        _logger = logger;
        _environment = environment;
        _storageOptions = storageOptions.Value;
        _hubContext = hubContext;
    }

    [HttpPost("task/{taskId}")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> UploadFile(
        Guid taskId,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        if (file is null)
        {
            return BadRequest(ApiResponse.Fail("File is required."));
        }

        if (file.Length == 0)
        {
            return BadRequest(ApiResponse.Fail("File cannot be empty."));
        }

        await using var stream = file.OpenReadStream();

        var (data, result) = await _assetService.UploadAssetAsync(
            taskId,
            stream,
            file.FileName,
            file.ContentType,
            file.Length,
            userId,
            cancellationToken);

        return result switch
        {
            UploadAssetResult.Success =>
                CreatedAtAction(
                    nameof(GetFile),
                    new { assetId = data!.Id },
                    ApiResponse<AssetResponseDto>.Ok(data, "File uploaded successfully.")),
            UploadAssetResult.TaskNotFound =>
                NotFound(ApiResponse.Fail("Task not found.")),
            UploadAssetResult.UserNotAuthorized =>
                StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse.Fail("You are not a member of this project.")),
            UploadAssetResult.FileTooLarge =>
                StatusCode(StatusCodes.Status413PayloadTooLarge,
                    ApiResponse.Fail("File size exceeds maximum allowed size.")),
            UploadAssetResult.InvalidFileType =>
                BadRequest(ApiResponse.Fail("File type is not allowed.")),
            UploadAssetResult.InvalidFileName =>
                BadRequest(ApiResponse.Fail("File name is invalid.")),
            UploadAssetResult.StorageError =>
                StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Fail("Failed to save file. Please try again.")),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Fail("Unexpected error."))
        };
    }

    [HttpGet("task/{taskId}")]
    public async Task<IActionResult> ListAttachmentsForTask(
        Guid taskId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        // Single query: authorization chain + filtered assets.
        // The filtered Include fetches only Completed assets in one roundtrip.
        // Ordering is applied in-memory after materialization to ensure compatibility
        // with both EF InMemory (tests) and SQL Server (production).
        var task = await _context.KanbanTasks
            .AsNoTracking()
            .Include(t => t.Column)
                .ThenInclude(c => c.Project)
                    .ThenInclude(p => p.Members)
            .Include(t => t.Assets.Where(a => a.ProcessingStatus == ProcessingStatus.Completed))
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task is null)
        {
            _logger.LogInformation(
                "User {UserId} requested attachments for non-existent task {TaskId}",
                userId, taskId);
            return NotFound(ApiResponse.Fail("Task not found."));
        }

        var isMember = task.Column.Project.Members.Any(m => m.UserId == userId);
        if (!isMember)
        {
            _logger.LogWarning(
                "User {UserId} attempted to access attachments for task {TaskId} in project {ProjectId} without authorization",
                userId, taskId, task.Column.ProjectId);
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("You are not authorized to access this task's attachments."));
        }

        var data = task.Assets
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => new AssetResponseDto
            {
                Id = a.Id.ToString(),
                FileName = a.FileName,
                StorageKey = a.StorageKey,
                ThumbnailKey = a.ThumbnailKey,
                MimeType = a.MimeType,
                FileSize = a.FileSize,
                ProcessingStatus = a.ProcessingStatus,
                KanbanTaskId = a.KanbanTaskId.ToString(),
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            })
            .ToList();

        _logger.LogInformation(
            "User {UserId} retrieved {Count} attachments for task {TaskId}",
            userId, data.Count, taskId);

        return Ok(ApiResponse<IEnumerable<AssetResponseDto>>.Ok(
            data, "Attachments retrieved successfully."));
    }

    [HttpGet("{assetId}")]
    public async Task<IActionResult> GetFile(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();

        var asset = await _context.Assets
            .AsNoTracking()
            .Include(a => a.KanbanTask)
                .ThenInclude(t => t.Column)
                    .ThenInclude(c => c.Project)
                        .ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);

        if (asset is null)
        {
            return NotFound(ApiResponse.Fail("File not found."));
        }

        var isMember = asset.KanbanTask.Column.Project.Members.Any(m => m.UserId == userId);
        if (!isMember)
        {
            _logger.LogWarning(
                "User {UserId} attempted to access asset {AssetId} in project {ProjectId} without authorization",
                userId, assetId, asset.KanbanTask.Column.ProjectId);
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("You are not authorized to access this file."));
        }

        switch (asset.ProcessingStatus)
        {
            case ProcessingStatus.Pending:
            case ProcessingStatus.Processing:
                return BadRequest(ApiResponse.Fail("File is still being processed."));
            case ProcessingStatus.Failed:
                return NotFound(ApiResponse.Fail("File upload failed."));
        }

        // Resolve path server-side from the database record only. StorageKey is generated by
        // the service layer and sanitized at upload time; never trust user input for the path.
        var storageRoot = Path.GetFullPath(Path.Combine(
            _environment.ContentRootPath,
            _storageOptions.StoragePath));
        var resolvedPath = Path.GetFullPath(Path.Combine(storageRoot, asset.StorageKey));

        // Defense-in-depth: ensure the resolved path has not escaped the storage root even if
        // a malformed StorageKey slipped past sanitization.
        if (!resolvedPath.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(resolvedPath, storageRoot, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Resolved file path {FilePath} for asset {AssetId} escaped storage root {StorageRoot}",
                resolvedPath, assetId, storageRoot);
            return NotFound(ApiResponse.Fail("File not found."));
        }

        if (!System.IO.File.Exists(resolvedPath))
        {
            _logger.LogWarning(
                "Asset {AssetId} exists in database but physical file is missing at {FilePath}",
                assetId, resolvedPath);
            return NotFound(ApiResponse.Fail("File not found."));
        }

        // Explicitly set Content-Disposition so images render inline while other MIME types
        // trigger a download. ContentDispositionHeaderValue handles RFC 5987 encoding for
        // filenames containing spaces or non-ASCII characters.
        var disposition = asset.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? "inline"
            : "attachment";

        var contentDisposition = new ContentDispositionHeaderValue(disposition);
        contentDisposition.SetHttpFileName(asset.FileName);
        Response.Headers.ContentDisposition = contentDisposition.ToString();

        return PhysicalFile(resolvedPath, asset.MimeType, enableRangeProcessing: false);
    }

    [HttpDelete("{assetId}")]
    public async Task<IActionResult> DeleteFile(
        Guid assetId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();

        var asset = await _context.Assets
            .Include(a => a.KanbanTask)
                .ThenInclude(t => t.Column)
                    .ThenInclude(c => c.Project)
                        .ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(a => a.Id == assetId, cancellationToken);

        if (asset is null)
        {
            _logger.LogInformation(
                "User {UserId} attempted to delete non-existent asset {AssetId}",
                userId, assetId);
            return NotFound(ApiResponse.Fail("File not found."));
        }

        var isMember = asset.KanbanTask.Column.Project.Members.Any(m => m.UserId == userId);
        if (!isMember)
        {
            _logger.LogWarning(
                "User {UserId} attempted to delete asset {AssetId} in project {ProjectId} without authorization",
                userId, assetId, asset.KanbanTask.Column.ProjectId);
            return StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse.Fail("You are not authorized to delete this file."));
        }

        // Resolve path server-side from the database record only. StorageKey is generated by
        // the service layer and sanitized at upload time; never trust user input for the path.
        var storageRoot = Path.GetFullPath(Path.Combine(
            _environment.ContentRootPath,
            _storageOptions.StoragePath));
        var resolvedPath = Path.GetFullPath(Path.Combine(storageRoot, asset.StorageKey));

        // Defense-in-depth: ensure the resolved path has not escaped the storage root even if
        // a malformed StorageKey slipped past sanitization.
        if (!resolvedPath.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(resolvedPath, storageRoot, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Resolved file path {FilePath} for asset {AssetId} escaped storage root {StorageRoot}",
                resolvedPath, assetId, storageRoot);
            return NotFound(ApiResponse.Fail("File not found."));
        }

        // Attempt to delete the physical file.
        if (System.IO.File.Exists(resolvedPath))
        {
            try
            {
                System.IO.File.Delete(resolvedPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to delete physical file for asset {AssetId} at {FilePath}",
                    assetId, resolvedPath);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Fail("Failed to delete file. Please try again."));
            }
        }
        else
        {
            _logger.LogWarning(
                "Asset {AssetId} physical file missing at {FilePath}, cleaning up DB record",
                assetId, resolvedPath);
        }

        // Delete the database record.
        _context.Assets.Remove(asset);
        await _context.SaveChangesAsync(cancellationToken);

        // Broadcast SignalR event to the project's group.
        var projectId = asset.KanbanTask.Column.ProjectId;
        var groupName = $"project_{projectId.ToString().ToLowerInvariant()}";
        var payload = new { assetId = asset.Id.ToString(), taskId = asset.KanbanTaskId.ToString() };
        await _hubContext.Clients.Group(groupName).SendAsync("AttachmentDeleted", payload, cancellationToken);

        _logger.LogInformation(
            "User {UserId} deleted asset {AssetId} from task {TaskId}",
            userId, assetId, asset.KanbanTaskId);

        return NoContent();
    }

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
}
