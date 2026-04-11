using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Tests.Data.Configurations;

public class TaskCommentConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public TaskCommentConfigurationTests()
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
    public async Task TaskCommentConfiguration_Content_IsRequired()
    {
        // Arrange
        var (_, _, task) = await SeedTaskHierarchy();
        var user = CreateValidUser();
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var comment = new TaskComment
        {
            Id = Guid.NewGuid(),
            Content = null!,
            KanbanTaskId = task.Id,
            AuthorId = user.Id
        };

        // Act
        _context.TaskComments.Add(comment);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TaskCommentConfiguration_CascadeDelete_RemovesCommentsWhenTaskDeleted()
    {
        // Arrange
        var (_, _, task) = await SeedTaskHierarchy();
        var user = CreateValidUser();
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var comment = new TaskComment
        {
            Id = Guid.NewGuid(),
            Content = "This is a test comment",
            KanbanTaskId = task.Id,
            AuthorId = user.Id
        };
        _context.TaskComments.Add(comment);
        await _context.SaveChangesAsync();

        // Act
        _context.ChangeTracker.Clear();
        var taskToDelete = await _context.KanbanTasks.FindAsync(task.Id);
        _context.KanbanTasks.Remove(taskToDelete!);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var remainingComments = await _context.TaskComments
            .Where(tc => tc.KanbanTaskId == task.Id)
            .ToListAsync();

        // Assert
        remainingComments.Should().BeEmpty();
    }

    [Fact]
    public async Task TaskCommentConfiguration_RestrictDelete_BlocksUserDeletionWithComments()
    {
        // Arrange
        var (_, _, task) = await SeedTaskHierarchy();
        var user = CreateValidUser();
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var comment = new TaskComment
        {
            Id = Guid.NewGuid(),
            Content = "Author's comment",
            KanbanTaskId = task.Id,
            AuthorId = user.Id
        };
        _context.TaskComments.Add(comment);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var userToDelete = await _context.Users.FindAsync(user.Id);

        // Act
        _context.Users.Remove(userToDelete!);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TaskCommentConfiguration_AuthorId_IsRequired()
    {
        // Arrange
        var (_, _, task) = await SeedTaskHierarchy();

        var comment = new TaskComment
        {
            Id = Guid.NewGuid(),
            Content = "Orphan comment",
            KanbanTaskId = task.Id,
            AuthorId = Guid.NewGuid()
        };

        // Act
        _context.TaskComments.Add(comment);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
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

    private static User CreateValidUser() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test User",
        Email = $"{Guid.NewGuid()}@example.com",
        PasswordHash = "$2a$11$examplehashvalue",
        Role = UserRole.Member
    };

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
