using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Tests.Data.Configurations;

public class KanbanTaskConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public KanbanTaskConfigurationTests()
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
    public async Task KanbanTaskConfiguration_Title_IsRequired()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var column = CreateValidColumn(project.Id);
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        var task = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = null!,
            TaskOrder = 0,
            ColumnId = column.Id
        };

        // Act
        _context.KanbanTasks.Add(task);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task KanbanTaskConfiguration_Title_MaxLength200_Accepted()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var column = CreateValidColumn(project.Id);
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        var taskId = Guid.NewGuid();
        var task = new KanbanTask
        {
            Id = taskId,
            Title = new string('T', 200),
            TaskOrder = 0,
            ColumnId = column.Id
        };

        // Act
        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedTask = await _context.KanbanTasks.FindAsync(taskId);

        // Assert
        savedTask.Should().NotBeNull();
        savedTask!.Title.Should().HaveLength(200);
    }

    [Fact]
    public async Task KanbanTaskConfiguration_Content_IsOptional()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var column = CreateValidColumn(project.Id);
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        var taskId = Guid.NewGuid();
        var task = new KanbanTask
        {
            Id = taskId,
            Title = "Test Task",
            Content = null,
            TaskOrder = 0,
            ColumnId = column.Id
        };

        // Act
        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedTask = await _context.KanbanTasks.FindAsync(taskId);

        // Assert
        savedTask.Should().NotBeNull();
        savedTask!.Content.Should().BeNull();
    }

    [Fact]
    public async Task KanbanTaskConfiguration_AssignedId_IsOptional()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var column = CreateValidColumn(project.Id);
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        var taskId = Guid.NewGuid();
        var task = new KanbanTask
        {
            Id = taskId,
            Title = "Unassigned Task",
            TaskOrder = 0,
            ColumnId = column.Id,
            AssignedId = null
        };

        // Act
        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedTask = await _context.KanbanTasks.FindAsync(taskId);

        // Assert
        savedTask.Should().NotBeNull();
        savedTask!.AssignedId.Should().BeNull();
    }

    [Fact]
    public async Task KanbanTaskConfiguration_CascadeDelete_RemovesTasksWhenColumnDeleted()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var column = CreateValidColumn(project.Id);
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        var task = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Task to cascade",
            TaskOrder = 0,
            ColumnId = column.Id
        };
        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        // Act
        _context.ChangeTracker.Clear();
        var columnToDelete = await _context.BoardColumns.FindAsync(column.Id);
        _context.BoardColumns.Remove(columnToDelete!);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var remainingTasks = await _context.KanbanTasks
            .Where(kt => kt.ColumnId == column.Id)
            .ToListAsync();

        // Assert
        remainingTasks.Should().BeEmpty();
    }

    [Fact]
    public async Task KanbanTaskConfiguration_SetNull_ClearsAssignmentWhenUserDeleted()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var column = CreateValidColumn(project.Id);
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        var user = CreateValidUser();
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var taskId = Guid.NewGuid();
        var task = new KanbanTask
        {
            Id = taskId,
            Title = "Assigned Task",
            TaskOrder = 0,
            ColumnId = column.Id,
            AssignedId = user.Id
        };
        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        // Act
        _context.ChangeTracker.Clear();
        var userToDelete = await _context.Users.FindAsync(user.Id);
        _context.Users.Remove(userToDelete!);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedTask = await _context.KanbanTasks.FindAsync(taskId);

        // Assert
        savedTask.Should().NotBeNull("the task should not be deleted when its assigned user is removed");
        savedTask!.AssignedId.Should().BeNull("the assignment should be cleared (set to null)");
    }

    [Fact]
    public async Task KanbanTaskConfiguration_CascadeDelete_RemovesTasksWhenProjectDeleted()
    {
        // Arrange
        var project = CreateValidProject();
        _context.Projects.Add(project);
        await _context.SaveChangesAsync();

        var column = CreateValidColumn(project.Id);
        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        var task = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Deep cascade task",
            TaskOrder = 0,
            ColumnId = column.Id
        };
        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        // Act
        _context.ChangeTracker.Clear();
        var projectToDelete = await _context.Projects.FindAsync(project.Id);
        _context.Projects.Remove(projectToDelete!);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var remainingTasks = await _context.KanbanTasks.ToListAsync();

        // Assert
        remainingTasks.Should().BeEmpty(
            "deleting a project should cascade through columns to delete all tasks");
    }

    private static Project CreateValidProject() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Project"
    };

    private static BoardColumn CreateValidColumn(Guid projectId) => new()
    {
        Id = Guid.NewGuid(),
        Name = "To Do",
        ColumnOrder = 0,
        ProjectId = projectId
    };

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
