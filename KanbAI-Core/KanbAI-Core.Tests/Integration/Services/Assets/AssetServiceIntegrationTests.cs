using System.Text;
using FluentAssertions;
using KanbAI_Core.Data;
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

namespace KanbAI_Core.Tests.Integration.Services.Assets;

/// <summary>
/// Integration tests for AssetService using real DbContext and filesystem.
/// Tests end-to-end upload lifecycle including file persistence and database consistency.
/// </summary>
public class AssetServiceIntegrationTests : IDisposable
{
    private readonly string _tempStorageRoot;
    private readonly ApplicationDbContext _context;
    private readonly IAssetService _assetService;
    private readonly IOptions<FileStorageOptions> _storageOptions;

    public AssetServiceIntegrationTests()
    {
        _tempStorageRoot = Path.Combine(Path.GetTempPath(), "AssetServiceIntegrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempStorageRoot);

        // Create in-memory database context
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);

        // Set up mocks
        var loggerMock = new Mock<ILogger<AssetService>>();
        var clientProxyMock = new Mock<IClientProxy>();
        var clientsMock = new Mock<IHubClients>();
        clientsMock.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxyMock.Object);
        var hubContextMock = new Mock<IHubContext<KanbanHub>>();
        hubContextMock.Setup(h => h.Clients).Returns(clientsMock.Object);

        var environmentMock = new Mock<IWebHostEnvironment>();
        environmentMock.Setup(e => e.ContentRootPath).Returns(_tempStorageRoot);

        _storageOptions = Options.Create(new FileStorageOptions
        {
            StoragePath = "wwwroot/uploads",
            MaxFileSizeBytes = 10485760, // 10 MB
            AllowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".docx", ".xlsx", ".txt" }
        });

        _assetService = new AssetService(
            _context,
            loggerMock.Object,
            hubContextMock.Object,
            _storageOptions,
            environmentMock.Object);
    }

    public void Dispose()
    {
        _context.Dispose();
        if (Directory.Exists(_tempStorageRoot))
        {
            try
            {
                Directory.Delete(_tempStorageRoot, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private async Task<(Guid projectId, Guid columnId, Guid taskId, Guid userId)> SeedProjectWithTaskAsync(Guid? userId = null)
    {
        var actualUserId = userId ?? Guid.NewGuid();
        var user = new User { Id = actualUserId, Name = "testuser", Email = "test@example.com", PasswordHash = "hash" };
        var project = new Project { Name = "Test Project", Description = "Test" };
        var member = new ProjectMember { Project = project, UserId = actualUserId, Role = ProjectRole.Owner };
        var column = new BoardColumn { Name = "To Do", Project = project, ColumnOrder = 0 };
        var task = new KanbanTask { Title = "Test Task", Column = column, TaskOrder = 0 };

        _context.Users.Add(user);
        _context.Projects.Add(project);
        _context.ProjectMembers.Add(member);
        _context.BoardColumns.Add(column);
        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        return (project.Id, column.Id, task.Id, actualUserId);
    }

    [Fact]
    public async Task UploadAssetAsync_E2E_SmallJpeg_WritesFileToDiskAndCreatesCompletedRow()
    {
        // Arrange
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync();

        var fileContent = Encoding.UTF8.GetBytes("This is a test JPEG file content");
        var stream = new MemoryStream(fileContent);

        // Act
        var (data, result) = await _assetService.UploadAssetAsync(
            taskId, stream, "photo.jpg", "image/jpeg", fileContent.Length, userId);

        // Assert
        result.Should().Be(UploadAssetResult.Success);
        data.Should().NotBeNull();
        data!.ProcessingStatus.Should().Be(ProcessingStatus.Completed);
        data.FileName.Should().Be("photo.jpg");
        data.FileSize.Should().Be(fileContent.Length);

        var asset = await _context.Assets.SingleAsync();
        asset.ProcessingStatus.Should().Be(ProcessingStatus.Completed);
        asset.FileName.Should().Be("photo.jpg");
        asset.StorageKey.Should().NotBeNullOrWhiteSpace();

        var expectedFilePath = Path.Combine(_tempStorageRoot, _storageOptions.Value.StoragePath, asset.StorageKey);
        File.Exists(expectedFilePath).Should().BeTrue();

        var actualContent = await File.ReadAllBytesAsync(expectedFilePath);
        actualContent.Should().Equal(fileContent);
    }

    [Fact]
    public async Task UploadAssetAsync_E2E_DirectoryDeletedAtRuntime_IsRecreated()
    {
        // Arrange
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync();

        var storageDirectory = Path.Combine(_tempStorageRoot, _storageOptions.Value.StoragePath);

        // Delete the storage directory after service is initialized
        if (Directory.Exists(storageDirectory))
        {
            Directory.Delete(storageDirectory, true);
        }

        Directory.Exists(storageDirectory).Should().BeFalse();

        var fileContent = Encoding.UTF8.GetBytes("Test content");
        var stream = new MemoryStream(fileContent);

        // Act
        var (data, result) = await _assetService.UploadAssetAsync(
            taskId, stream, "test.txt", "text/plain", fileContent.Length, userId);

        // Assert
        result.Should().Be(UploadAssetResult.Success);
        data.Should().NotBeNull();

        Directory.Exists(storageDirectory).Should().BeTrue();

        var asset = await _context.Assets.SingleAsync();
        var expectedFilePath = Path.Combine(_tempStorageRoot, _storageOptions.Value.StoragePath, asset.StorageKey);
        File.Exists(expectedFilePath).Should().BeTrue();
    }

    [Fact]
    public async Task UploadAssetAsync_E2E_StorageKeyUniqueIndexUpheld()
    {
        // Arrange
        var (projectId, columnId, taskId, userId) = await SeedProjectWithTaskAsync();

        var fileContent1 = Encoding.UTF8.GetBytes("First upload");
        var fileContent2 = Encoding.UTF8.GetBytes("Second upload");

        // Act
        var stream1 = new MemoryStream(fileContent1);
        var (data1, result1) = await _assetService.UploadAssetAsync(
            taskId, stream1, "document.pdf", "application/pdf", fileContent1.Length, userId);

        var stream2 = new MemoryStream(fileContent2);
        var (data2, result2) = await _assetService.UploadAssetAsync(
            taskId, stream2, "document.pdf", "application/pdf", fileContent2.Length, userId);

        // Assert
        result1.Should().Be(UploadAssetResult.Success);
        result2.Should().Be(UploadAssetResult.Success);

        data1.Should().NotBeNull();
        data2.Should().NotBeNull();

        data1!.StorageKey.Should().NotBe(data2!.StorageKey);

        var assets = await _context.Assets.ToListAsync();
        assets.Should().HaveCount(2);
        assets.Select(a => a.StorageKey).Should().OnlyHaveUniqueItems();
    }
}
