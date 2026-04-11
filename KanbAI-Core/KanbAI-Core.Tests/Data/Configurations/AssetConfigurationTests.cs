using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Tests.Data.Configurations;

public class AssetConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public AssetConfigurationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task AssetConfiguration_FileName_IsRequired()
    {
        // Arrange
        var (project, column, task) = await SeedTaskHierarchy();

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            FileName = null!,
            StorageKey = $"assets/{Guid.NewGuid()}",
            MimeType = "image/png",
            FileSize = 1024,
            KanbanTaskId = task.Id
        };

        // Act
        _context.Assets.Add(asset);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AssetConfiguration_FileName_MaxLength255_Accepted()
    {
        // Arrange
        var (project, column, task) = await SeedTaskHierarchy();

        var assetId = Guid.NewGuid();
        var asset = new Asset
        {
            Id = assetId,
            FileName = new string('A', 255),
            StorageKey = $"assets/{Guid.NewGuid()}",
            MimeType = "image/png",
            FileSize = 1024,
            KanbanTaskId = task.Id
        };

        // Act
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedAsset = await _context.Assets.FindAsync(assetId);

        // Assert
        savedAsset.Should().NotBeNull();
        savedAsset!.FileName.Should().HaveLength(255);
    }

    [Fact]
    public async Task AssetConfiguration_StorageKey_IsRequired()
    {
        // Arrange
        var (project, column, task) = await SeedTaskHierarchy();

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            FileName = "test.png",
            StorageKey = null!,
            MimeType = "image/png",
            FileSize = 1024,
            KanbanTaskId = task.Id
        };

        // Act
        _context.Assets.Add(asset);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AssetConfiguration_StorageKey_IsUnique()
    {
        // Arrange
        var (project, column, task) = await SeedTaskHierarchy();
        var sharedKey = "assets/shared-key";

        var asset1 = new Asset
        {
            Id = Guid.NewGuid(),
            FileName = "file1.png",
            StorageKey = sharedKey,
            MimeType = "image/png",
            FileSize = 1024,
            KanbanTaskId = task.Id
        };
        _context.Assets.Add(asset1);
        await _context.SaveChangesAsync();

        var asset2 = new Asset
        {
            Id = Guid.NewGuid(),
            FileName = "file2.png",
            StorageKey = sharedKey,
            MimeType = "image/png",
            FileSize = 2048,
            KanbanTaskId = task.Id
        };

        // Act
        _context.Assets.Add(asset2);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AssetConfiguration_ThumbnailKey_IsOptional()
    {
        // Arrange
        var (project, column, task) = await SeedTaskHierarchy();

        var assetId = Guid.NewGuid();
        var asset = new Asset
        {
            Id = assetId,
            FileName = "document.pdf",
            StorageKey = $"assets/{Guid.NewGuid()}",
            ThumbnailKey = null,
            MimeType = "application/pdf",
            FileSize = 5000,
            KanbanTaskId = task.Id
        };

        // Act
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedAsset = await _context.Assets.FindAsync(assetId);

        // Assert
        savedAsset.Should().NotBeNull();
        savedAsset!.ThumbnailKey.Should().BeNull();
    }

    [Fact]
    public async Task AssetConfiguration_MimeType_IsRequired()
    {
        // Arrange
        var (project, column, task) = await SeedTaskHierarchy();

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            FileName = "test.png",
            StorageKey = $"assets/{Guid.NewGuid()}",
            MimeType = null!,
            FileSize = 1024,
            KanbanTaskId = task.Id
        };

        // Act
        _context.Assets.Add(asset);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AssetConfiguration_CascadeDelete_RemovesAssetsWhenTaskDeleted()
    {
        // Arrange
        var (project, column, task) = await SeedTaskHierarchy();

        var asset = new Asset
        {
            Id = Guid.NewGuid(),
            FileName = "test.png",
            StorageKey = $"assets/{Guid.NewGuid()}",
            MimeType = "image/png",
            FileSize = 1024,
            KanbanTaskId = task.Id
        };
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        // Act
        _context.ChangeTracker.Clear();
        var taskToDelete = await _context.KanbanTasks.FindAsync(task.Id);
        _context.KanbanTasks.Remove(taskToDelete!);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var remainingAssets = await _context.Assets
            .Where(a => a.KanbanTaskId == task.Id)
            .ToListAsync();

        // Assert
        remainingAssets.Should().BeEmpty();
    }

    [Fact]
    public async Task AssetConfiguration_ProcessingStatus_DefaultValue_IsPending()
    {
        // Arrange
        var (project, column, task) = await SeedTaskHierarchy();

        var assetId = Guid.NewGuid();
        var asset = new Asset
        {
            Id = assetId,
            FileName = "test.png",
            StorageKey = $"assets/{Guid.NewGuid()}",
            MimeType = "image/png",
            FileSize = 1024,
            KanbanTaskId = task.Id
        };

        // Act
        _context.Assets.Add(asset);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedAsset = await _context.Assets.FindAsync(assetId);

        // Assert
        savedAsset.Should().NotBeNull();
        savedAsset!.ProcessingStatus.Should().Be(ProcessingStatus.Pending);
    }

    private async Task<(Project project, BoardColumn column, KanbanTask task)> SeedTaskHierarchy()
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Name = "Test Project"
        };
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "To Do",
            ColumnOrder = 0,
            ProjectId = project.Id
        };
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        var task = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Test Task",
            TaskOrder = 0,
            ColumnId = column.Id
        };
        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        return (project, column, task);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
