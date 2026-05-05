namespace KanbAI_Core.Services.Assets;

using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Hubs;
using KanbAI_Core.Models.Configuration;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class AssetService : IAssetService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AssetService> _logger;
    private readonly IHubContext<KanbanHub> _hubContext;
    private readonly IOptions<FileStorageOptions> _storageOptions;
    private readonly IWebHostEnvironment _environment;

    private static readonly IReadOnlyDictionary<string, string[]> AllowedMimeTypesByExtension =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"]  = ["image/jpeg", "image/jpg"],
            [".jpeg"] = ["image/jpeg", "image/jpg"],
            [".png"]  = ["image/png"],
            [".gif"]  = ["image/gif"],
            [".pdf"]  = ["application/pdf"],
            [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
            [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
            [".txt"]  = ["text/plain"]
        };

    public AssetService(
        ApplicationDbContext context,
        ILogger<AssetService> logger,
        IHubContext<KanbanHub> hubContext,
        IOptions<FileStorageOptions> storageOptions,
        IWebHostEnvironment environment)
    {
        _context = context;
        _logger = logger;
        _hubContext = hubContext;
        _storageOptions = storageOptions;
        _environment = environment;
    }

    public async Task<(AssetResponseDto? data, UploadAssetResult result)> UploadAssetAsync(
        Guid taskId,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Filename sanitization & pre-validation
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return (null, UploadAssetResult.InvalidFileName);
        }

        var sanitizedFileName = Path.GetFileName(fileName);
        if (sanitizedFileName != fileName ||
            fileName.Contains("..") ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            _logger.LogWarning("Invalid filename {FileName} rejected", fileName);
            return (null, UploadAssetResult.InvalidFileName);
        }

        var extension = Path.GetExtension(sanitizedFileName).ToLowerInvariant();
        if (!_storageOptions.Value.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning("File extension {Extension} not allowed for file {FileName}", extension, sanitizedFileName);
            return (null, UploadAssetResult.InvalidFileType);
        }

        if (!IsMimeTypeValidForExtension(extension, contentType))
        {
            _logger.LogWarning(
                "MIME type {ContentType} does not match extension {Extension} for file {FileName}",
                contentType, extension, sanitizedFileName);
            return (null, UploadAssetResult.InvalidFileType);
        }

        if (fileSize <= 0 || fileSize > _storageOptions.Value.MaxFileSizeBytes)
        {
            _logger.LogWarning(
                "File size {FileSize} exceeds maximum {MaxSize} for file {FileName}",
                fileSize, _storageOptions.Value.MaxFileSizeBytes, sanitizedFileName);
            return (null, UploadAssetResult.FileTooLarge);
        }

        // Step 2: Load task + authorization
        var task = await _context.KanbanTasks
            .Include(t => t.Column)
                .ThenInclude(c => c.Project)
                    .ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", taskId);
            return (null, UploadAssetResult.TaskNotFound);
        }

        if (!task.Column.Project.Members.Any(m => m.UserId == userId))
        {
            _logger.LogWarning(
                "User {UserId} attempted to upload asset to task {TaskId} without project membership",
                userId, taskId);
            return (null, UploadAssetResult.UserNotAuthorized);
        }

        var projectId = task.Column.ProjectId;
        var groupName = BuildProjectGroupName(projectId);

        // Step 3: Generate storage key
        var storageKey = $"{Guid.NewGuid():N}_{sanitizedFileName}";

        // Step 4: Resolve absolute physical path & ensure directory
        var fullPath = Path.Combine(
            _environment.ContentRootPath,
            _storageOptions.Value.StoragePath,
            storageKey);
        var storageDir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(storageDir);

        // Step 5: Persist Asset with Pending status
        var asset = new Asset
        {
            FileName = sanitizedFileName,
            StorageKey = storageKey,
            ThumbnailKey = null,
            MimeType = contentType,
            FileSize = fileSize,
            ProcessingStatus = ProcessingStatus.Pending,
            KanbanTaskId = taskId
        };
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync(cancellationToken);

        var pendingStatusDto = new AssetStatusEventDto
        {
            AssetId = asset.Id.ToString(),
            TaskId = taskId.ToString(),
            FileName = sanitizedFileName,
            ProcessingStatus = ProcessingStatus.Pending
        };
        await BroadcastAsync(groupName, "AssetUploadStarted", pendingStatusDto);

        // Step 6: Transition to Processing, broadcast, then write file
        asset.ProcessingStatus = ProcessingStatus.Processing;
        await _context.SaveChangesAsync(cancellationToken);

        var processingStatusDto = new AssetStatusEventDto
        {
            AssetId = asset.Id.ToString(),
            TaskId = taskId.ToString(),
            FileName = sanitizedFileName,
            ProcessingStatus = ProcessingStatus.Processing
        };
        await BroadcastAsync(groupName, "AssetProcessing", processingStatusDto);

        try
        {
            await using var output = new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            await fileStream.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);

            // Step 7: Success path - Completed
            asset.ProcessingStatus = ProcessingStatus.Completed;
            await _context.SaveChangesAsync(cancellationToken);
            var dto = MapToDto(asset);
            await BroadcastAsync(groupName, "AssetCompleted", dto);
            return (dto, UploadAssetResult.Success);
        }
        catch (Exception ex)
        {
            // Step 8: Failure path - Failed + cleanup
            _logger.LogError(ex,
                "Failed to upload asset {AssetId} for task {TaskId} to {StorageKey}",
                asset.Id, taskId, storageKey);

            asset.ProcessingStatus = ProcessingStatus.Failed;
            await _context.SaveChangesAsync(CancellationToken.None);

            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx,
                    "Failed to delete partial file for asset {AssetId} at {StorageKey}",
                    asset.Id, storageKey);
            }

            await BroadcastAsync(groupName, "AssetFailed", new AssetFailedEventDto
            {
                AssetId = asset.Id.ToString(),
                TaskId = taskId.ToString(),
                ErrorMessage = "File write failed."
            });
            return (null, UploadAssetResult.StorageError);
        }
    }

    private static bool IsMimeTypeValidForExtension(string extension, string contentType) =>
        AllowedMimeTypesByExtension.TryGetValue(extension, out var allowed)
        && allowed.Contains(contentType, StringComparer.OrdinalIgnoreCase);

    private static string BuildProjectGroupName(Guid projectId) =>
        $"project_{projectId.ToString().ToLowerInvariant()}";

    private async Task BroadcastAsync(string groupName, string eventName, object payload)
    {
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync(eventName, payload);
            _logger.LogInformation(
                "Broadcast {EventName} event to group {GroupName}",
                eventName, groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to broadcast {EventName} event to group {GroupName}",
                eventName, groupName);
        }
    }

    private static AssetResponseDto MapToDto(Asset a) =>
        new()
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
        };
}
