using System.Security.Claims;
using System.Text;
using FluentAssertions;
using KanbAI_Core.Controllers;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Hubs;
using KanbAI_Core.Models.Configuration;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Assets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace KanbAI_Core.Tests.Controllers;

/// <summary>
/// Unit tests for <see cref="AttachmentController"/>.
/// Covers <see cref="UploadAssetResult"/> to HTTP status code mapping, authorization flow,
/// processing-status gating, Content-Disposition selection based on MIME type, and
/// JWT claim extraction failures. Follows the same pattern as <see cref="TaskControllerTests"/>
/// and <see cref="ColumnControllerTests"/>: the service layer is mocked; the GET endpoint
/// uses EF InMemory because it queries the DbContext directly.
/// </summary>
public class AttachmentControllerTests : IDisposable
{
    private readonly Mock<IAssetService> _assetServiceMock;
    private readonly Mock<ILogger<AttachmentController>> _loggerMock;
    private readonly Mock<IWebHostEnvironment> _environmentMock;
    private readonly Mock<IHubContext<KanbanHub>> _hubContextMock;
    private readonly ApplicationDbContext _context;
    private readonly FileStorageOptions _storageOptions;
    private readonly string _tempStorageRoot;
    private readonly AttachmentController _controller;

    public AttachmentControllerTests()
    {
        _tempStorageRoot = Path.Combine(
            Path.GetTempPath(),
            "AttachmentControllerTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempStorageRoot);

        _assetServiceMock = new Mock<IAssetService>();
        _loggerMock = new Mock<ILogger<AttachmentController>>();
        _hubContextMock = new Mock<IHubContext<KanbanHub>>();

        _environmentMock = new Mock<IWebHostEnvironment>();
        _environmentMock.Setup(e => e.ContentRootPath).Returns(_tempStorageRoot);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        _storageOptions = new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10_485_760,
            AllowedExtensions = [".png", ".pdf", ".txt"]
        };

        _controller = new AttachmentController(
            _assetServiceMock.Object,
            _context,
            _loggerMock.Object,
            _environmentMock.Object,
            Options.Create(_storageOptions),
            _hubContextMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_tempStorageRoot))
        {
            try { Directory.Delete(_tempStorageRoot, true); }
            catch { /* ignore cleanup failures */ }
        }
    }

    #region UploadFile: UploadAssetResult Mapping

    [Fact]
    public async Task UploadFile_ServiceReturnsSuccess_Returns201CreatedWithLocation()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        SetupUserClaims(userId);

        var file = BuildFormFile("hello", "sample.png", "image/png");

        var responseDto = new AssetResponseDto
        {
            Id = Guid.NewGuid().ToString(),
            FileName = "sample.png",
            StorageKey = "abc_sample.png",
            ThumbnailKey = null,
            MimeType = "image/png",
            FileSize = 5,
            ProcessingStatus = ProcessingStatus.Completed,
            KanbanTaskId = taskId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _assetServiceMock
            .Setup(s => s.UploadAssetAsync(
                taskId,
                It.IsAny<Stream>(),
                "sample.png",
                "image/png",
                file.Length,
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((responseDto, UploadAssetResult.Success));

        // Act
        var result = await _controller.UploadFile(taskId, file, CancellationToken.None);

        // Assert
        var created = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        created.StatusCode.Should().Be(201);
        created.ActionName.Should().Be(nameof(AttachmentController.GetFile));
        created.RouteValues!["assetId"].Should().Be(responseDto.Id);

        var apiResponse = created.Value.Should().BeOfType<ApiResponse<AssetResponseDto>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Message.Should().Be("File uploaded successfully.");
        apiResponse.Data.Should().BeEquivalentTo(responseDto);
    }

    [Fact]
    public async Task UploadFile_ServiceReturnsTaskNotFound_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        SetupUserClaims(userId);

        var file = BuildFormFile("content", "sample.png", "image/png");

        _assetServiceMock
            .Setup(s => s.UploadAssetAsync(
                taskId,
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, UploadAssetResult.TaskNotFound));

        // Act
        var result = await _controller.UploadFile(taskId, file, CancellationToken.None);

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);

        var apiResponse = notFound.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Task not found.");
    }

    [Fact]
    public async Task UploadFile_ServiceReturnsUserNotAuthorized_Returns403()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        SetupUserClaims(userId);

        var file = BuildFormFile("content", "sample.png", "image/png");

        _assetServiceMock
            .Setup(s => s.UploadAssetAsync(
                taskId,
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, UploadAssetResult.UserNotAuthorized));

        // Act
        var result = await _controller.UploadFile(taskId, file, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var apiResponse = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("You are not a member of this project.");
    }

    [Fact]
    public async Task UploadFile_ServiceReturnsFileTooLarge_Returns413()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        SetupUserClaims(userId);

        var file = BuildFormFile("content", "sample.png", "image/png");

        _assetServiceMock
            .Setup(s => s.UploadAssetAsync(
                taskId,
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, UploadAssetResult.FileTooLarge));

        // Act
        var result = await _controller.UploadFile(taskId, file, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status413PayloadTooLarge);

        var apiResponse = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("File size exceeds maximum allowed size.");
    }

    [Fact]
    public async Task UploadFile_ServiceReturnsInvalidFileType_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        SetupUserClaims(userId);

        var file = BuildFormFile("content", "malware.exe", "application/x-msdownload");

        _assetServiceMock
            .Setup(s => s.UploadAssetAsync(
                taskId,
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, UploadAssetResult.InvalidFileType));

        // Act
        var result = await _controller.UploadFile(taskId, file, CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);

        var apiResponse = badRequest.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("File type is not allowed.");
    }

    [Fact]
    public async Task UploadFile_ServiceReturnsInvalidFileName_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        SetupUserClaims(userId);

        var file = BuildFormFile("content", "../evil.png", "image/png");

        _assetServiceMock
            .Setup(s => s.UploadAssetAsync(
                taskId,
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, UploadAssetResult.InvalidFileName));

        // Act
        var result = await _controller.UploadFile(taskId, file, CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);

        var apiResponse = badRequest.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("File name is invalid.");
    }

    [Fact]
    public async Task UploadFile_ServiceReturnsStorageError_Returns500()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        SetupUserClaims(userId);

        var file = BuildFormFile("content", "sample.png", "image/png");

        _assetServiceMock
            .Setup(s => s.UploadAssetAsync(
                taskId,
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, UploadAssetResult.StorageError));

        // Act
        var result = await _controller.UploadFile(taskId, file, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        var apiResponse = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Failed to save file. Please try again.");
    }

    #endregion

    #region UploadFile: File Validation (pre-service)

    [Fact]
    public async Task UploadFile_NullFile_Returns400AndDoesNotCallService()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        SetupUserClaims(userId);

        // Act
        var result = await _controller.UploadFile(taskId, file: null, CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);

        var apiResponse = badRequest.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("File is required.");

        _assetServiceMock.Verify(s => s.UploadAssetAsync(
            It.IsAny<Guid>(),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadFile_EmptyFile_Returns400AndDoesNotCallService()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        SetupUserClaims(userId);

        var file = BuildFormFile(string.Empty, "empty.png", "image/png");

        // Act
        var result = await _controller.UploadFile(taskId, file, CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);

        var apiResponse = badRequest.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("File cannot be empty.");

        _assetServiceMock.Verify(s => s.UploadAssetAsync(
            It.IsAny<Guid>(),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UploadFile_PassesCancellationTokenToService()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        SetupUserClaims(userId);

        var file = BuildFormFile("content", "sample.png", "image/png");
        using var cts = new CancellationTokenSource();
        var token = cts.Token;

        _assetServiceMock
            .Setup(s => s.UploadAssetAsync(
                taskId,
                It.IsAny<Stream>(),
                "sample.png",
                "image/png",
                file.Length,
                userId,
                token))
            .ReturnsAsync((null, UploadAssetResult.TaskNotFound));

        // Act
        await _controller.UploadFile(taskId, file, token);

        // Assert - the token handed to the controller must be forwarded to the service
        _assetServiceMock.Verify(s => s.UploadAssetAsync(
            taskId,
            It.IsAny<Stream>(),
            "sample.png",
            "image/png",
            file.Length,
            userId,
            token),
            Times.Once);
    }

    #endregion

    #region UploadFile: Claims Extraction

    [Fact]
    public async Task UploadFile_MissingNameIdentifierClaim_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };

        var file = BuildFormFile("content", "sample.png", "image/png");

        // Act
        Func<Task> act = () => _controller.UploadFile(Guid.NewGuid(), file, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or missing user ID in token.");
    }

    [Fact]
    public async Task UploadFile_InvalidNameIdentifierClaim_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "not-a-guid")
        };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };

        var file = BuildFormFile("content", "sample.png", "image/png");

        // Act
        Func<Task> act = () => _controller.UploadFile(Guid.NewGuid(), file, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or missing user ID in token.");
    }

    #endregion

    #region GetFile: Authorization & Not Found

    [Fact]
    public async Task GetFile_AssetDoesNotExist_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        // Act
        var result = await _controller.GetFile(Guid.NewGuid());

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);

        var apiResponse = notFound.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Message.Should().Be("File not found.");
    }

    [Fact]
    public async Task GetFile_UserNotProjectMember_Returns403()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        SetupUserClaims(outsiderId);

        var asset = await SeedAsset(ownerId, ProcessingStatus.Completed, "sample.png", "image/png");

        // Act
        var result = await _controller.GetFile(asset.Id);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var apiResponse = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Message.Should().Be("You are not authorized to access this file.");
    }

    #endregion

    #region GetFile: Processing Status Gating

    [Fact]
    public async Task GetFile_AssetInPendingStatus_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Pending, "pending.png", "image/png");

        // Act
        var result = await _controller.GetFile(asset.Id);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);

        var apiResponse = badRequest.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Message.Should().Be("File is still being processed.");
    }

    [Fact]
    public async Task GetFile_AssetInProcessingStatus_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Processing, "inflight.png", "image/png");

        // Act
        var result = await _controller.GetFile(asset.Id);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);

        var apiResponse = badRequest.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Message.Should().Be("File is still being processed.");
    }

    [Fact]
    public async Task GetFile_AssetInFailedStatus_Returns404WithFailedMessage()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Failed, "failed.png", "image/png");

        // Act
        var result = await _controller.GetFile(asset.Id);

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);

        var apiResponse = notFound.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Message.Should().Be("File upload failed.");
    }

    #endregion

    #region GetFile: Disk & Serving

    [Fact]
    public async Task GetFile_CompletedAssetButFileMissingFromDisk_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        // Seed Completed asset but do NOT write a file to disk.
        var asset = await SeedAsset(userId, ProcessingStatus.Completed, "orphaned.png", "image/png");

        // Act
        var result = await _controller.GetFile(asset.Id);

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);

        var apiResponse = notFound.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Message.Should().Be("File not found.");
    }

    [Fact]
    public async Task GetFile_CompletedImageAsset_Returns200WithInlineDisposition()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Completed, "photo.png", "image/png");
        WriteFileForAsset(asset, Encoding.UTF8.GetBytes("png-bytes"));

        // Act
        var result = await _controller.GetFile(asset.Id);

        // Assert
        var fileResult = result.Should().BeOfType<PhysicalFileResult>().Subject;
        fileResult.ContentType.Should().Be("image/png");
        fileResult.EnableRangeProcessing.Should().BeFalse();

        var dispositionHeader = _controller.Response.Headers.ContentDisposition.ToString();
        dispositionHeader.Should().StartWith("inline");
        dispositionHeader.Should().Contain("photo.png");
    }

    [Fact]
    public async Task GetFile_CompletedImageJpegAsset_UsesInlineDispositionCaseInsensitive()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        // Mixed-case MIME type verifies StartsWith uses OrdinalIgnoreCase.
        var asset = await SeedAsset(userId, ProcessingStatus.Completed, "snap.jpg", "Image/JPEG");
        WriteFileForAsset(asset, Encoding.UTF8.GetBytes("jpeg-bytes"));

        // Act
        var result = await _controller.GetFile(asset.Id);

        // Assert
        result.Should().BeOfType<PhysicalFileResult>();
        _controller.Response.Headers.ContentDisposition.ToString().Should().StartWith("inline");
    }

    [Fact]
    public async Task GetFile_CompletedPdfAsset_Returns200WithAttachmentDisposition()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Completed, "report.pdf", "application/pdf");
        WriteFileForAsset(asset, Encoding.UTF8.GetBytes("%PDF-1.4"));

        // Act
        var result = await _controller.GetFile(asset.Id);

        // Assert
        var fileResult = result.Should().BeOfType<PhysicalFileResult>().Subject;
        fileResult.ContentType.Should().Be("application/pdf");

        var dispositionHeader = _controller.Response.Headers.ContentDisposition.ToString();
        dispositionHeader.Should().StartWith("attachment");
        dispositionHeader.Should().Contain("report.pdf");
    }

    [Fact]
    public async Task GetFile_FilenameWithNonAsciiCharacters_RfcEncodedInDisposition()
    {
        // Arrange - spec §7.3: RFC 5987 extended notation for non-ASCII filenames.
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Completed, "Résumé.pdf", "application/pdf");
        WriteFileForAsset(asset, Encoding.UTF8.GetBytes("%PDF-1.4"));

        // Act
        var result = await _controller.GetFile(asset.Id);

        // Assert
        result.Should().BeOfType<PhysicalFileResult>();
        var dispositionHeader = _controller.Response.Headers.ContentDisposition.ToString();
        dispositionHeader.Should().StartWith("attachment");
        // RFC 5987 encodes non-ASCII chars with filename*=UTF-8''...
        dispositionHeader.Should().Contain("filename*=");
        dispositionHeader.Should().Contain("UTF-8");
    }

    #endregion

    #region GetFile: Claims Extraction

    [Fact]
    public async Task GetFile_MissingNameIdentifierClaim_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };

        // Act
        Func<Task> act = () => _controller.GetFile(Guid.NewGuid());

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or missing user ID in token.");
    }

    #endregion

    #region ListAttachmentsForTask

    [Fact]
    public async Task ListAttachmentsForTask_TaskExistsWithCompletedAssets_Returns200WithOrderedDtos()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var task = await SeedTaskWithProject(userId);

        // Seed assets with specific CreatedAt timestamps to test ordering.
        // The SaveChangesAsync override will overwrite CreatedAt, so we set it after.
        var baseTime = DateTimeOffset.UtcNow;
        var asset1 = SeedAssetForTask(task.Id, ProcessingStatus.Completed, "first.png", baseTime.AddMinutes(-10));
        var asset2 = SeedAssetForTask(task.Id, ProcessingStatus.Completed, "second.png", baseTime.AddMinutes(-5));
        var asset3 = SeedAssetForTask(task.Id, ProcessingStatus.Completed, "third.png", baseTime);
        await _context.SaveChangesAsync();

        // Fix timestamps after save (SaveChangesAsync override sets them to UtcNow).
        asset1.CreatedAt = baseTime.AddMinutes(-10);
        asset2.CreatedAt = baseTime.AddMinutes(-5);
        asset3.CreatedAt = baseTime;
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.ListAttachmentsForTask(task.Id, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<IEnumerable<AssetResponseDto>>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Message.Should().Be("Attachments retrieved successfully.");

        var data = apiResponse.Data.Should().NotBeNull().And.Subject.ToList();
        data.Should().HaveCount(3);
        data[0].FileName.Should().Be("third.png");
        data[1].FileName.Should().Be("second.png");
        data[2].FileName.Should().Be("first.png");
    }

    [Fact]
    public async Task ListAttachmentsForTask_TaskExistsWithNoAssets_Returns200WithEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var task = await SeedTaskWithProject(userId);

        // Act
        var result = await _controller.ListAttachmentsForTask(task.Id, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<IEnumerable<AssetResponseDto>>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Message.Should().Be("Attachments retrieved successfully.");
        apiResponse.Data.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task ListAttachmentsForTask_TaskExistsWithMixedStatusAssets_ReturnsOnlyCompleted()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var task = await SeedTaskWithProject(userId);
        var pending = SeedAssetForTask(task.Id, ProcessingStatus.Pending, "pending.png", DateTimeOffset.UtcNow);
        var processing = SeedAssetForTask(task.Id, ProcessingStatus.Processing, "processing.png", DateTimeOffset.UtcNow);
        var completed = SeedAssetForTask(task.Id, ProcessingStatus.Completed, "completed.png", DateTimeOffset.UtcNow);
        var failed = SeedAssetForTask(task.Id, ProcessingStatus.Failed, "failed.png", DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.ListAttachmentsForTask(task.Id, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<IEnumerable<AssetResponseDto>>>().Subject;

        var data = apiResponse.Data.Should().NotBeNull().And.Subject.ToList();
        data.Should().HaveCount(1);
        data[0].Id.Should().Be(completed.Id.ToString());
    }

    [Fact]
    public async Task ListAttachmentsForTask_TaskDoesNotExist_Returns404NotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        // Act
        var result = await _controller.ListAttachmentsForTask(Guid.NewGuid(), CancellationToken.None);

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);

        var apiResponse = notFound.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Task not found.");
    }

    [Fact]
    public async Task ListAttachmentsForTask_UserNotProjectMember_Returns403Forbidden()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        SetupUserClaims(outsiderId);

        var task = await SeedTaskWithProject(ownerId);
        SeedAssetForTask(task.Id, ProcessingStatus.Completed, "restricted.png", DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.ListAttachmentsForTask(task.Id, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var apiResponse = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("You are not authorized to access this task's attachments.");
    }

    [Fact]
    public async Task ListAttachmentsForTask_UserIsProjectMember_DoesNotLogAuthorizationFailure()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var task = await SeedTaskWithProject(userId);
        SeedAssetForTask(task.Id, ProcessingStatus.Completed, "allowed.png", DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync();

        // Act
        await _controller.ListAttachmentsForTask(task.Id, CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task ListAttachmentsForTask_MultipleTasksExist_ReturnsAssetsForRequestedTaskOnly()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var taskA = await SeedTaskWithProject(userId);
        var taskB = await SeedTaskInSameProject(taskA, userId);

        var assetA = SeedAssetForTask(taskA.Id, ProcessingStatus.Completed, "taskA.png", DateTimeOffset.UtcNow);
        var assetB = SeedAssetForTask(taskB.Id, ProcessingStatus.Completed, "taskB.png", DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.ListAttachmentsForTask(taskA.Id, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<IEnumerable<AssetResponseDto>>>().Subject;

        var data = apiResponse.Data.Should().NotBeNull().And.Subject.ToList();
        data.Should().HaveCount(1);
        data[0].Id.Should().Be(assetA.Id.ToString());
    }

    [Fact]
    public async Task ListAttachmentsForTask_AssetsFromOtherProjects_AreNotReturned()
    {
        // Arrange
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        SetupUserClaims(user1);

        var taskInProject1 = await SeedTaskWithProject(user1);
        var taskInProject2 = await SeedTaskWithProject(user2);

        SeedAssetForTask(taskInProject2.Id, ProcessingStatus.Completed, "otherProject.png", DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync();

        // Act
        var result = await _controller.ListAttachmentsForTask(taskInProject2.Id, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task ListAttachmentsForTask_InvalidJwtClaim_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };

        // Act
        Func<Task> act = () => _controller.ListAttachmentsForTask(Guid.NewGuid(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or missing user ID in token.");
    }

    [Fact]
    public async Task ListAttachmentsForTask_SuccessfulRetrieval_LogsInformationWithCount()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var task = await SeedTaskWithProject(userId);
        SeedAssetForTask(task.Id, ProcessingStatus.Completed, "file1.png", DateTimeOffset.UtcNow);
        SeedAssetForTask(task.Id, ProcessingStatus.Completed, "file2.png", DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync();

        // Act
        await _controller.ListAttachmentsForTask(task.Id, CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("retrieved") && v.ToString()!.Contains("2")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region DeleteFile: Authorization, Edge Cases, SignalR Broadcast

    [Fact]
    public async Task DeleteFile_AssetExistsAndUserIsMember_Returns204AndDeletesBothFileAndDbRecord()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Completed, "sample.png", "image/png");
        WriteFileForAsset(asset, Encoding.UTF8.GetBytes("file-content"));

        var storageDir = Path.Combine(_tempStorageRoot, _storageOptions.StoragePath);
        var filePath = Path.Combine(storageDir, asset.StorageKey);

        // Verify file exists before deletion
        System.IO.File.Exists(filePath).Should().BeTrue();

        var hubContextMock = new Mock<IHubContext<KanbanHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var controller = new AttachmentController(
            _assetServiceMock.Object,
            _context,
            _loggerMock.Object,
            _environmentMock.Object,
            Options.Create(_storageOptions),
            hubContextMock.Object);
        controller.ControllerContext = _controller.ControllerContext;

        // Act
        var result = await controller.DeleteFile(asset.Id, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        // Verify DB record is deleted
        var deletedAsset = await _context.Assets.FindAsync(asset.Id);
        deletedAsset.Should().BeNull();

        // Verify physical file is deleted
        System.IO.File.Exists(filePath).Should().BeFalse();

        // Verify SignalR broadcast was sent
        var projectId = asset.KanbanTask.Column.ProjectId;
        var expectedGroupName = $"project_{projectId.ToString().ToLowerInvariant()}";
        clientsMock.Verify(
            c => c.Group(expectedGroupName),
            Times.Once);
        clientProxyMock.Verify(
            p => p.SendCoreAsync(
                "AttachmentDeleted",
                It.Is<object[]>(args =>
                    args.Length == 1 &&
                    args[0] != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteFile_AssetDoesNotExist_Returns404NotFound()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var hubContextMock = new Mock<IHubContext<KanbanHub>>();
        var controller = new AttachmentController(
            _assetServiceMock.Object,
            _context,
            _loggerMock.Object,
            _environmentMock.Object,
            Options.Create(_storageOptions),
            hubContextMock.Object);
        controller.ControllerContext = _controller.ControllerContext;

        // Act
        var result = await controller.DeleteFile(Guid.NewGuid(), CancellationToken.None);

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);

        var apiResponse = notFound.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("File not found.");
    }

    [Fact]
    public async Task DeleteFile_UserIsNotProjectMember_Returns403Forbidden()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        SetupUserClaims(outsiderId);

        var asset = await SeedAsset(ownerId, ProcessingStatus.Completed, "sample.png", "image/png");

        var hubContextMock = new Mock<IHubContext<KanbanHub>>();
        var controller = new AttachmentController(
            _assetServiceMock.Object,
            _context,
            _loggerMock.Object,
            _environmentMock.Object,
            Options.Create(_storageOptions),
            hubContextMock.Object);
        controller.ControllerContext = _controller.ControllerContext;

        // Act
        var result = await controller.DeleteFile(asset.Id, CancellationToken.None);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var apiResponse = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("You are not authorized to delete this file.");

        // Verify asset was NOT deleted
        var assetStillExists = await _context.Assets.FindAsync(asset.Id);
        assetStillExists.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteFile_PhysicalFileMissing_Returns204AndDeletesDbRecord()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Completed, "orphaned.png", "image/png");
        // Do NOT write physical file - simulate orphaned DB record

        var hubContextMock = new Mock<IHubContext<KanbanHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var controller = new AttachmentController(
            _assetServiceMock.Object,
            _context,
            _loggerMock.Object,
            _environmentMock.Object,
            Options.Create(_storageOptions),
            hubContextMock.Object);
        controller.ControllerContext = _controller.ControllerContext;

        // Act
        var result = await controller.DeleteFile(asset.Id, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        // Verify DB record is deleted (cleanup case)
        var deletedAsset = await _context.Assets.FindAsync(asset.Id);
        deletedAsset.Should().BeNull();
    }

    [Fact]
    public async Task DeleteFile_AssetInPendingStatus_Returns204AndDeletes()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Pending, "pending.png", "image/png");
        WriteFileForAsset(asset, Encoding.UTF8.GetBytes("pending-content"));

        var hubContextMock = new Mock<IHubContext<KanbanHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var controller = new AttachmentController(
            _assetServiceMock.Object,
            _context,
            _loggerMock.Object,
            _environmentMock.Object,
            Options.Create(_storageOptions),
            hubContextMock.Object);
        controller.ControllerContext = _controller.ControllerContext;

        // Act
        var result = await controller.DeleteFile(asset.Id, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        // Verify DB record is deleted
        var deletedAsset = await _context.Assets.FindAsync(asset.Id);
        deletedAsset.Should().BeNull();
    }

    [Fact]
    public async Task DeleteFile_AssetInProcessingStatus_Returns204AndDeletes()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Processing, "processing.png", "image/png");
        WriteFileForAsset(asset, Encoding.UTF8.GetBytes("processing-content"));

        var hubContextMock = new Mock<IHubContext<KanbanHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);

        var controller = new AttachmentController(
            _assetServiceMock.Object,
            _context,
            _loggerMock.Object,
            _environmentMock.Object,
            Options.Create(_storageOptions),
            hubContextMock.Object);
        controller.ControllerContext = _controller.ControllerContext;

        // Act
        var result = await controller.DeleteFile(asset.Id, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        // Verify DB record is deleted
        var deletedAsset = await _context.Assets.FindAsync(asset.Id);
        deletedAsset.Should().BeNull();
    }

    [Fact]
    public async Task DeleteFile_DiskDeletionFails_Returns500AndLeavesDbRecordIntact()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Completed, "locked.png", "image/png");
        WriteFileForAsset(asset, Encoding.UTF8.GetBytes("locked-content"));

        var storageDir = Path.Combine(_tempStorageRoot, _storageOptions.StoragePath);
        var filePath = Path.Combine(storageDir, asset.StorageKey);

        // Make file read-only to simulate disk deletion failure
        var fileInfo = new FileInfo(filePath);
        fileInfo.IsReadOnly = true;

        var hubContextMock = new Mock<IHubContext<KanbanHub>>();
        var controller = new AttachmentController(
            _assetServiceMock.Object,
            _context,
            _loggerMock.Object,
            _environmentMock.Object,
            Options.Create(_storageOptions),
            hubContextMock.Object);
        controller.ControllerContext = _controller.ControllerContext;

        try
        {
            // Act
            var result = await controller.DeleteFile(asset.Id, CancellationToken.None);

            // Assert
            var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
            objectResult.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

            var apiResponse = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
            apiResponse.Success.Should().BeFalse();
            apiResponse.Message.Should().Be("Failed to delete file. Please try again.");

            // Verify DB record is NOT deleted
            var assetStillExists = await _context.Assets.FindAsync(asset.Id);
            assetStillExists.Should().NotBeNull();

            // Verify physical file still exists
            System.IO.File.Exists(filePath).Should().BeTrue();
        }
        finally
        {
            // Clean up: remove read-only attribute
            if (System.IO.File.Exists(filePath))
            {
                fileInfo.IsReadOnly = false;
            }
        }
    }

    [Fact]
    public async Task DeleteFile_SignalRBroadcastSent_AfterSuccessfulDeletion()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var asset = await SeedAsset(userId, ProcessingStatus.Completed, "test.png", "image/png");
        WriteFileForAsset(asset, Encoding.UTF8.GetBytes("test-content"));

        var projectId = asset.KanbanTask.Column.ProjectId;
        var expectedGroupName = $"project_{projectId.ToString().ToLowerInvariant()}";

        var hubContextMock = new Mock<IHubContext<KanbanHub>>();
        var clientsMock = new Mock<IHubClients>();
        var clientProxyMock = new Mock<IClientProxy>();
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);
        clientsMock.Setup(c => c.Group(expectedGroupName)).Returns(clientProxyMock.Object);

        var controller = new AttachmentController(
            _assetServiceMock.Object,
            _context,
            _loggerMock.Object,
            _environmentMock.Object,
            Options.Create(_storageOptions),
            hubContextMock.Object);
        controller.ControllerContext = _controller.ControllerContext;

        // Act
        var result = await controller.DeleteFile(asset.Id, CancellationToken.None);

        // Assert
        result.Should().BeOfType<NoContentResult>();

        // Verify SignalR broadcast was called with correct group name
        clientsMock.Verify(
            c => c.Group(expectedGroupName),
            Times.Once);

        // Verify SendAsync was called with correct event name and payload structure
        clientProxyMock.Verify(
            p => p.SendCoreAsync(
                "AttachmentDeleted",
                It.Is<object[]>(args =>
                    args.Length == 1 &&
                    args[0] != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Helpers

    private void SetupUserClaims(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal
            }
        };
    }

    private static IFormFile BuildFormFile(string body, string fileName, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    /// <summary>
    /// Seeds a complete Project → Member → Column → Task → Asset graph in the InMemory DB.
    /// <paramref name="userId"/> becomes the project's Owner so membership checks pass.
    /// </summary>
    private async Task<Asset> SeedAsset(
        Guid userId,
        ProcessingStatus status,
        string fileName,
        string mimeType)
    {
        var user = new User { Id = userId, Name = "testuser", Email = "test@example.com", PasswordHash = "h" };
        var project = new Project { Name = "Test Project", Description = "Test" };
        var member = new ProjectMember { Project = project, UserId = userId, Role = ProjectRole.Owner };
        var column = new BoardColumn { Name = "To Do", Project = project, ColumnOrder = 0 };
        var task = new KanbanTask { Title = "Test Task", Column = column, TaskOrder = 0 };
        var asset = new Asset
        {
            FileName = fileName,
            StorageKey = $"{Guid.NewGuid():N}_{fileName}",
            MimeType = mimeType,
            FileSize = 10,
            ProcessingStatus = status,
            KanbanTask = task
        };

        _context.Users.Add(user);
        _context.Projects.Add(project);
        _context.ProjectMembers.Add(member);
        _context.BoardColumns.Add(column);
        _context.KanbanTasks.Add(task);
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        return asset;
    }

    private void WriteFileForAsset(Asset asset, byte[] bytes)
    {
        var storageDir = Path.Combine(_tempStorageRoot, _storageOptions.StoragePath);
        Directory.CreateDirectory(storageDir);
        var path = Path.Combine(storageDir, asset.StorageKey);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// Seeds a complete Project → Member → Column → Task graph for ListAttachmentsForTask tests.
    /// </summary>
    private async Task<KanbanTask> SeedTaskWithProject(Guid userId)
    {
        var user = new User { Id = userId, Name = "testuser", Email = "test@example.com", PasswordHash = "h" };
        var project = new Project { Name = "Test Project", Description = "Test" };
        var member = new ProjectMember { Project = project, UserId = userId, Role = ProjectRole.Owner };
        var column = new BoardColumn { Name = "To Do", Project = project, ColumnOrder = 0 };
        var task = new KanbanTask { Title = "Test Task", Column = column, TaskOrder = 0 };

        _context.Users.Add(user);
        _context.Projects.Add(project);
        _context.ProjectMembers.Add(member);
        _context.BoardColumns.Add(column);
        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        return task;
    }

    /// <summary>
    /// Seeds a second task in the same project as the provided task.
    /// </summary>
    private async Task<KanbanTask> SeedTaskInSameProject(KanbanTask existingTask, Guid userId)
    {
        var column = await _context.BoardColumns
            .Include(c => c.Project)
            .FirstAsync(c => c.Id == existingTask.ColumnId);

        var taskB = new KanbanTask { Title = "Second Task", Column = column, TaskOrder = 1 };
        _context.KanbanTasks.Add(taskB);
        await _context.SaveChangesAsync();

        return taskB;
    }

    /// <summary>
    /// Seeds an Asset entity attached to the specified task without creating the physical file.
    /// </summary>
    private Asset SeedAssetForTask(
        Guid taskId,
        ProcessingStatus status,
        string fileName,
        DateTimeOffset createdAt)
    {
        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            StorageKey = $"{Guid.NewGuid():N}_{fileName}",
            ThumbnailKey = null,
            MimeType = "image/png",
            FileSize = 1024,
            ProcessingStatus = status,
            KanbanTaskId = taskId,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
        _context.Assets.Add(asset);
        return asset;
    }

    #endregion
}
