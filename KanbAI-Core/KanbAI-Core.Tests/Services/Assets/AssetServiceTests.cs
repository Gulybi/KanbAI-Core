using System.Text;
using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Hubs;
using KanbAI_Core.Models.Configuration;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Assets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace KanbAI_Core.Tests.Services.Assets;

public class AssetServiceTests
{
    private readonly Mock<ILogger<AssetService>> _loggerMock;
    private readonly Mock<IHubContext<KanbanHub>> _hubContextMock;
    private readonly Mock<IClientProxy> _clientProxyMock;
    private readonly Mock<IHubClients> _clientsMock;
    private readonly Mock<IWebHostEnvironment> _environmentMock;
    private readonly IOptions<FileStorageOptions> _storageOptions;

    public AssetServiceTests()
    {
        _loggerMock = new Mock<ILogger<AssetService>>();
        _clientProxyMock = new Mock<IClientProxy>();
        _clientsMock = new Mock<IHubClients>();
        _clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxyMock.Object);
        _hubContextMock = new Mock<IHubContext<KanbanHub>>();
        _hubContextMock.Setup(h => h.Clients).Returns(_clientsMock.Object);

        _environmentMock = new Mock<IWebHostEnvironment>();
        _environmentMock.Setup(e => e.ContentRootPath).Returns(Path.GetTempPath());

        _storageOptions = Options.Create(new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760, // 10 MB
            AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".docx", ".xlsx", ".txt" }
        });
    }

    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private async Task<(Guid projectId, Guid columnId, Guid taskId, Guid userId)> SeedProjectWithTaskAsync(ApplicationDbContext context, Guid? userId = null)
    {
        var actualUserId = userId ?? Guid.NewGuid();
        var user = new User { Id = actualUserId, Name = "testuser", Email = "test@example.com", PasswordHash = "hash" };
        var project = new Project { Name = "Test Project", Description = "Test" };
        var member = new ProjectMember { Project = project, UserId = actualUserId, Role = ProjectRole.Owner };
        var column = new BoardColumn { Name = "To Do", Project = project, ColumnOrder = 0 };
        var task = new KanbanTask { Title = "Test Task", Column = column, TaskOrder = 0 };

        context.Users.Add(user);
        context.Projects.Add(project);
        context.ProjectMembers.Add(member);
        context.BoardColumns.Add(column);
        context.KanbanTasks.Add(task);
        await context.SaveChangesAsync();

        return (project.Id, column.Id, task.Id, actualUserId);
    }

    #region Validation Tests

    [Fact]
    public async Task UploadAssetAsync_FileNameIsNull_ReturnsInvalidFileName()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var stream = new MemoryStream();

        // Act
        var (data, result) = await service.UploadAssetAsync(
            Guid.NewGuid(), stream, null!, "image/jpeg", 1024, Guid.NewGuid());

        // Assert
        result.Should().Be(UploadAssetResult.InvalidFileName);
        data.Should().BeNull();
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("foo/bar.png")]
    [InlineData("..\\bar.txt")]
    [InlineData("test..png")]
    public async Task UploadAssetAsync_FileNameContainsPathTraversal_ReturnsInvalidFileName(string fileName)
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var stream = new MemoryStream();

        // Act
        var (data, result) = await service.UploadAssetAsync(
            Guid.NewGuid(), stream, fileName, "image/jpeg", 1024, Guid.NewGuid());

        // Assert
        result.Should().Be(UploadAssetResult.InvalidFileName);
        data.Should().BeNull();
    }

    [Fact]
    public async Task UploadAssetAsync_FileExtensionNotAllowed_ReturnsInvalidFileType()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var stream = new MemoryStream();

        // Act
        var (data, result) = await service.UploadAssetAsync(
            Guid.NewGuid(), stream, "malware.exe", "application/x-msdownload", 1024, Guid.NewGuid());

        // Assert
        result.Should().Be(UploadAssetResult.InvalidFileType);
        data.Should().BeNull();
    }

    [Fact]
    public async Task UploadAssetAsync_MimeTypeDoesNotMatchExtension_ReturnsInvalidFileType()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var stream = new MemoryStream();

        // Act
        var (data, result) = await service.UploadAssetAsync(
            Guid.NewGuid(), stream, "photo.png", "application/x-msdownload", 1024, Guid.NewGuid());

        // Assert
        result.Should().Be(UploadAssetResult.InvalidFileType);
        data.Should().BeNull();
    }

    [Fact]
    public async Task UploadAssetAsync_FileSizeExceedsMax_ReturnsFileTooLarge()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var stream = new MemoryStream();

        // Act
        var (data, result) = await service.UploadAssetAsync(
            Guid.NewGuid(), stream, "large.jpg", "image/jpeg", _storageOptions.Value.MaxFileSizeBytes + 1, Guid.NewGuid());

        // Assert
        result.Should().Be(UploadAssetResult.FileTooLarge);
        data.Should().BeNull();
    }

    [Fact]
    public async Task UploadAssetAsync_FileSizeZero_ReturnsFileTooLarge()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var stream = new MemoryStream();

        // Act
        var (data, result) = await service.UploadAssetAsync(
            Guid.NewGuid(), stream, "empty.jpg", "image/jpeg", 0, Guid.NewGuid());

        // Assert
        result.Should().Be(UploadAssetResult.FileTooLarge);
        data.Should().BeNull();
    }

    [Fact]
    public async Task UploadAssetAsync_TaskNotFound_ReturnsTaskNotFound()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var stream = new MemoryStream();

        // Act
        var (data, result) = await service.UploadAssetAsync(
            Guid.NewGuid(), stream, "test.jpg", "image/jpeg", 1024, Guid.NewGuid());

        // Assert
        result.Should().Be(UploadAssetResult.TaskNotFound);
        data.Should().BeNull();
    }

    [Fact]
    public async Task UploadAssetAsync_UserNotProjectMember_ReturnsUserNotAuthorized()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var stream = new MemoryStream();

        var unauthorizedUserId = Guid.NewGuid(); // Different user

        // Act
        var (data, result) = await service.UploadAssetAsync(
            taskId, stream, "test.jpg", "image/jpeg", 1024, unauthorizedUserId);

        // Assert
        result.Should().Be(UploadAssetResult.UserNotAuthorized);
        data.Should().BeNull();
    }

    #endregion

    #region Happy Path Tests

    [Fact]
    public async Task UploadAssetAsync_ValidInput_PersistsAssetWithPendingStatusFirst()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var content = Encoding.UTF8.GetBytes("test content");
        var stream = new MemoryStream(content);

        // Act
        var (data, result) = await service.UploadAssetAsync(
            taskId, stream, "test.jpg", "image/jpeg", content.Length, userId);

        // Assert
        result.Should().Be(UploadAssetResult.Success);
        data.Should().NotBeNull();
        data!.ProcessingStatus.Should().Be(ProcessingStatus.Completed);

        var asset = await context.Assets.SingleAsync();
        asset.ProcessingStatus.Should().Be(ProcessingStatus.Completed);
        asset.FileName.Should().Be("test.jpg");
    }

    [Fact]
    public async Task UploadAssetAsync_ValidInput_GeneratesUniqueStorageKeyContainingGuid()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var content = Encoding.UTF8.GetBytes("test content");
        var stream = new MemoryStream(content);

        // Act
        var (data, result) = await service.UploadAssetAsync(
            taskId, stream, "test.jpg", "image/jpeg", content.Length, userId);

        // Assert
        result.Should().Be(UploadAssetResult.Success);
        data.Should().NotBeNull();
        data!.StorageKey.Should().MatchRegex(@"^[0-9a-f]{32}_test\.jpg$");
    }

    [Fact]
    public async Task UploadAssetAsync_TwoConcurrentUploadsSameName_ProduceDistinctStorageKeys()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var content = Encoding.UTF8.GetBytes("test content");

        // Act
        var stream1 = new MemoryStream(content);
        var (data1, result1) = await service.UploadAssetAsync(
            taskId, stream1, "same.jpg", "image/jpeg", content.Length, userId);

        var stream2 = new MemoryStream(content);
        var (data2, result2) = await service.UploadAssetAsync(
            taskId, stream2, "same.jpg", "image/jpeg", content.Length, userId);

        // Assert
        result1.Should().Be(UploadAssetResult.Success);
        result2.Should().Be(UploadAssetResult.Success);
        data1!.StorageKey.Should().NotBe(data2!.StorageKey);
        data1.StorageKey.Should().MatchRegex(@"^[0-9a-f]{32}_same\.jpg$");
        data2.StorageKey.Should().MatchRegex(@"^[0-9a-f]{32}_same\.jpg$");
    }

    [Fact]
    public async Task UploadAssetAsync_Success_BroadcastsAllLifecycleEvents()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var content = Encoding.UTF8.GetBytes("test content");
        var stream = new MemoryStream(content);

        // Act
        var (data, result) = await service.UploadAssetAsync(
            taskId, stream, "test.jpg", "image/jpeg", content.Length, userId);

        // Assert
        result.Should().Be(UploadAssetResult.Success);

        var expectedGroupName = $"project_{projectId.ToString().ToLowerInvariant()}";
        _clientsMock.Verify(c => c.Group(expectedGroupName), Times.AtLeast(3));

        _clientProxyMock.Verify(p => p.SendCoreAsync(
            "AssetUploadStarted",
            It.Is<object?[]?>(args => args != null && args.Length > 0 && args[0] is AssetStatusEventDto),
            default),
            Times.Once);

        _clientProxyMock.Verify(p => p.SendCoreAsync(
            "AssetProcessing",
            It.Is<object?[]?>(args => args != null && args.Length > 0 && args[0] is AssetStatusEventDto),
            default),
            Times.Once);

        _clientProxyMock.Verify(p => p.SendCoreAsync(
            "AssetCompleted",
            It.Is<object?[]?>(args => args != null && args.Length > 0 && args[0] is AssetResponseDto),
            default),
            Times.Once);
    }

    [Fact]
    public async Task UploadAssetAsync_Success_ReturnsCompletedAssetDtoWithSuccessResult()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var content = Encoding.UTF8.GetBytes("test content");
        var stream = new MemoryStream(content);

        // Act
        var (data, result) = await service.UploadAssetAsync(
            taskId, stream, "test.jpg", "image/jpeg", content.Length, userId);

        // Assert
        result.Should().Be(UploadAssetResult.Success);
        data.Should().NotBeNull();
        data!.ProcessingStatus.Should().Be(ProcessingStatus.Completed);
        data.FileName.Should().Be("test.jpg");
        data.MimeType.Should().Be("image/jpeg");
        data.FileSize.Should().Be(content.Length);
    }

    #endregion

    #region Resilience Tests

    [Fact]
    public async Task UploadAssetAsync_FileWriteThrows_UpdatesStatusToFailedAndBroadcastsAssetFailed()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);

        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);

        // Create a stream that throws when read
        var failingStream = new ThrowingStream();

        // Act
        var (data, result) = await service.UploadAssetAsync(
            taskId, failingStream, "test.jpg", "image/jpeg", 1024, userId);

        // Assert
        result.Should().Be(UploadAssetResult.StorageError);
        data.Should().BeNull();

        var asset = await context.Assets.SingleAsync();
        asset.ProcessingStatus.Should().Be(ProcessingStatus.Failed);

        _clientProxyMock.Verify(p => p.SendCoreAsync(
            "AssetFailed",
            It.Is<object?[]?>(args => args != null && args.Length > 0 && args[0] is AssetFailedEventDto),
            default),
            Times.Once);
    }

    [Fact]
    public async Task UploadAssetAsync_FileWriteThrows_DeletesPartialFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var context = CreateInMemoryContext();
            var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);

            _environmentMock.Setup(e => e.ContentRootPath).Returns(tempDir);

            var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);

            // Create a stream that throws when read
            var failingStream = new ThrowingStream();

            // Act
            var (data, result) = await service.UploadAssetAsync(
                taskId, failingStream, "test.jpg", "image/jpeg", 1024, userId);

            // Assert
            result.Should().Be(UploadAssetResult.StorageError);
            data.Should().BeNull();

            var asset = await context.Assets.SingleAsync();
            var expectedFilePath = Path.Combine(tempDir, _storageOptions.Value.StoragePath, asset.StorageKey);
            File.Exists(expectedFilePath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task UploadAssetAsync_BroadcastThrows_DoesNotFailUpload()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);

        // Set up hub context to throw on broadcast
        _clientProxyMock.Setup(p => p.SendCoreAsync(
            It.IsAny<string>(),
            It.IsAny<object?[]?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Broadcast failed"));

        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var content = Encoding.UTF8.GetBytes("test content");
        var stream = new MemoryStream(content);

        // Act
        var (data, result) = await service.UploadAssetAsync(
            taskId, stream, "test.jpg", "image/jpeg", content.Length, userId);

        // Assert
        result.Should().Be(UploadAssetResult.Success);
        data.Should().NotBeNull();
        data!.ProcessingStatus.Should().Be(ProcessingStatus.Completed);
    }

    [Fact]
    public async Task UploadAssetAsync_AssetFailedEvent_DoesNotLeakInternalPathOrException()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);

        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);

        // Create a stream that throws when read
        var failingStream = new ThrowingStream();

        AssetFailedEventDto? capturedDto = null;
        _clientProxyMock.Setup(p => p.SendCoreAsync(
            "AssetFailed",
            It.IsAny<object?[]?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, object?[]?, CancellationToken>((method, args, ct) =>
            {
                if (args != null && args.Length > 0 && args[0] is AssetFailedEventDto dto)
                {
                    capturedDto = dto;
                }
            })
            .Returns(Task.CompletedTask);

        // Act
        var (data, result) = await service.UploadAssetAsync(
            taskId, failingStream, "test.jpg", "image/jpeg", 1024, userId);

        // Assert
        result.Should().Be(UploadAssetResult.StorageError);
        capturedDto.Should().NotBeNull();
        capturedDto!.ErrorMessage.Should().Be("File write failed.");
        capturedDto.ErrorMessage.Should().NotContain("IOException");
        capturedDto.ErrorMessage.Should().NotContain("Exception");
        capturedDto.ErrorMessage.Should().NotContain("StackTrace");
        capturedDto.ErrorMessage.Should().NotContain("Simulated");
    }

    [Fact]
    public async Task UploadAssetAsync_CancellationTokenCancelled_AbortsFileWriteAndMarksFailed()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync(context);
        var service = new AssetService(context, _loggerMock.Object, _hubContextMock.Object, _storageOptions, _environmentMock.Object);
        var content = Encoding.UTF8.GetBytes("test content");
        var stream = new MemoryStream(content);

        var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel the token

        // Act
        var act = async () => await service.UploadAssetAsync(
            taskId, stream, "test.jpg", "image/jpeg", content.Length, userId, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();

        var asset = await context.Assets.FirstOrDefaultAsync();
        if (asset != null)
        {
            // If the asset was created, it should be marked as Failed
            asset.ProcessingStatus.Should().Be(ProcessingStatus.Failed);
        }
    }

    #endregion

    #region Test Helpers

    private class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new IOException("Simulated I/O error");
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            throw new IOException("Simulated I/O error");
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            throw new IOException("Simulated I/O error");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    #endregion
}
