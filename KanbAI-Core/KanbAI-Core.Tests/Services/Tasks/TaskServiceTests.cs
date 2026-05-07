using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Hubs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace KanbAI_Core.Tests.Services.Tasks;

public class TaskServiceTests
{
    private readonly Mock<ILogger<TaskService>> _loggerMock;
    private readonly Mock<IHubContext<KanbanHub>> _hubContextMock;

    public TaskServiceTests()
    {
        _loggerMock = new Mock<ILogger<TaskService>>();
        _hubContextMock = CreateHubContextMock();
    }

    private static Mock<IHubContext<KanbanHub>> CreateHubContextMock()
    {
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);
        clients.Setup(c => c.All).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<KanbanHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);
        return hubContext;
    }

    [Fact]
    public async Task CreateTaskAsync_UserIsMember_EmptyColumn_CreatesTaskWithOrderZero()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);

        var dto = new CreateTaskDto { Title = "First Task" };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.Success);
        data.Should().NotBeNull();
        data!.TaskOrder.Should().Be(0);
        data.Title.Should().Be("First Task");
        data.ColumnId.Should().Be(columnId.ToString());
        data.AssignedId.Should().BeNull();

        var persisted = await context.KanbanTasks.SingleAsync();
        persisted.TaskOrder.Should().Be(0);
        persisted.ColumnId.Should().Be(columnId);
    }

    [Fact]
    public async Task CreateTaskAsync_UserIsMember_NonEmptyColumn_TaskOrderIsMaxPlusOne()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);

        context.KanbanTasks.AddRange(
            new KanbanTask { Title = "A", TaskOrder = 0, ColumnId = columnId },
            new KanbanTask { Title = "B", TaskOrder = 1, ColumnId = columnId },
            new KanbanTask { Title = "C", TaskOrder = 2, ColumnId = columnId });
        await context.SaveChangesAsync();

        var dto = new CreateTaskDto { Title = "Next" };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.Success);
        data.Should().NotBeNull();
        data!.TaskOrder.Should().Be(3);
    }

    [Fact]
    public async Task CreateTaskAsync_MultipleSequentialCreations_AssignsDistinctOrders()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);

        // Act
        var orders = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            var (data, result) = await service.CreateTaskAsync(
                columnId,
                new CreateTaskDto { Title = $"Task {i}" },
                userId);

            result.Should().Be(CreateTaskResult.Success);
            orders.Add(data!.TaskOrder);
        }

        // Assert
        orders.Should().BeEquivalentTo(new[] { 0, 1, 2, 3 }, opts => opts.WithStrictOrdering());
        (await context.KanbanTasks.CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task CreateTaskAsync_UserIsMember_NoAssignment_PersistsWithNullAssignedId()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);

        var dto = new CreateTaskDto { Title = "Unassigned", Content = null };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.Success);
        data!.AssignedId.Should().BeNull();

        var persisted = await context.KanbanTasks.SingleAsync();
        persisted.AssignedId.Should().BeNull();
    }

    [Fact]
    public async Task CreateTaskAsync_UserIsMember_ValidAssignment_PersistsWithAssignedId()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);

        var assigneeId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = assigneeId,
            Name = "Alice",
            Email = "alice@example.com",
            PasswordHash = "hash"
        });
        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = assigneeId,
            Role = ProjectRole.Member
        });
        await context.SaveChangesAsync();

        var dto = new CreateTaskDto { Title = "Assigned", AssignedId = assigneeId };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.Success);
        data!.AssignedId.Should().Be(assigneeId.ToString());

        var persisted = await context.KanbanTasks.SingleAsync();
        persisted.AssignedId.Should().Be(assigneeId);
    }

    [Fact]
    public async Task CreateTaskAsync_ColumnDoesNotExist_ReturnsColumnNotFound()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var nonExistentColumnId = Guid.NewGuid();

        var dto = new CreateTaskDto { Title = "Ghost" };

        // Act
        var (data, result) = await service.CreateTaskAsync(nonExistentColumnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.ColumnNotFound);
        data.Should().BeNull();
        (await context.KanbanTasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateTaskAsync_UserNotProjectMember_ReturnsUserNotProjectMember()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var memberId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, memberId);

        var dto = new CreateTaskDto { Title = "Should be forbidden" };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, outsiderId);

        // Assert
        result.Should().Be(CreateTaskResult.UserNotProjectMember);
        data.Should().BeNull();
        (await context.KanbanTasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateTaskAsync_AssignedUserDoesNotExist_ReturnsAssignedUserNotFound()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);

        var dto = new CreateTaskDto
        {
            Title = "Bogus assignment",
            AssignedId = Guid.NewGuid()
        };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.AssignedUserNotFound);
        data.Should().BeNull();
        (await context.KanbanTasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateTaskAsync_AssignedUserIsNotProjectMember_ReturnsAssignedUserNotProjectMember()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);

        var outsiderUserId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = outsiderUserId,
            Name = "Outsider",
            Email = "out@example.com",
            PasswordHash = "hash"
        });
        await context.SaveChangesAsync();

        var dto = new CreateTaskDto
        {
            Title = "Wrong project assignment",
            AssignedId = outsiderUserId
        };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.AssignedUserNotProjectMember);
        data.Should().BeNull();
        (await context.KanbanTasks.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task CreateTaskAsync_WhitespaceTitle_ReturnsInvalidTitle(string title)
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);

        var dto = new CreateTaskDto { Title = title };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.InvalidTitle);
        data.Should().BeNull();
        (await context.KanbanTasks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateTaskAsync_EmptyContent_PersistsWithNullContent()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);

        var dto = new CreateTaskDto { Title = "No description", Content = null };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.Success);
        data!.Content.Should().BeNull();

        var persisted = await context.KanbanTasks.SingleAsync();
        persisted.Content.Should().BeNull();
    }

    [Fact]
    public async Task CreateTaskAsync_FailureResultPath_DoesNotPersistEntity()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);

        // Trigger all four non-Success paths and verify none create a task.
        var outsiderId = Guid.NewGuid();

        // Act
        await service.CreateTaskAsync(Guid.NewGuid(), new CreateTaskDto { Title = "a" }, userId);
        await service.CreateTaskAsync(columnId, new CreateTaskDto { Title = "b" }, outsiderId);
        await service.CreateTaskAsync(columnId, new CreateTaskDto { Title = "c", AssignedId = Guid.NewGuid() }, userId);
        await service.CreateTaskAsync(columnId, new CreateTaskDto { Title = "   " }, userId);

        // Assert
        (await context.KanbanTasks.CountAsync()).Should().Be(0);
    }

    #region GetProjectTasksAsync

    [Fact]
    public async Task GetProjectTasksAsync_ProjectExistsWithTasks_ReturnsOrderedDtos()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        context.Users.Add(new User
        {
            Id = userId,
            Name = "Member",
            Email = $"{userId}@example.com",
            PasswordHash = "hash"
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = ProjectRole.Owner
        });

        var columnAId = Guid.Parse("AAAAAAAA-0000-0000-0000-000000000000");
        var columnBId = Guid.Parse("BBBBBBBB-0000-0000-0000-000000000000");

        var columnA = new BoardColumn
        {
            Id = columnAId,
            Name = "Column A",
            ColumnOrder = 0,
            ProjectId = projectId
        };
        var columnB = new BoardColumn
        {
            Id = columnBId,
            Name = "Column B",
            ColumnOrder = 1,
            ProjectId = projectId
        };
        context.BoardColumns.AddRange(columnA, columnB);

        var task1 = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Task A0",
            TaskOrder = 0,
            ColumnId = columnAId
        };
        var task2 = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Task A1",
            TaskOrder = 1,
            ColumnId = columnAId
        };
        var task3 = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Task A2",
            TaskOrder = 2,
            ColumnId = columnAId
        };
        var task4 = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Task B0",
            TaskOrder = 0,
            ColumnId = columnBId
        };
        var task5 = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Task B1",
            TaskOrder = 1,
            ColumnId = columnBId
        };
        context.KanbanTasks.AddRange(task1, task2, task3, task4, task5);
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectTasksAsync(projectId, userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(5);

        result![0].Id.Should().Be(task1.Id.ToString());
        result[0].Title.Should().Be("Task A0");
        result[0].ColumnId.Should().Be(columnAId.ToString());
        result[0].TaskOrder.Should().Be(0);

        result[1].Id.Should().Be(task2.Id.ToString());
        result[1].TaskOrder.Should().Be(1);

        result[2].Id.Should().Be(task3.Id.ToString());
        result[2].TaskOrder.Should().Be(2);

        result[3].Id.Should().Be(task4.Id.ToString());
        result[3].ColumnId.Should().Be(columnBId.ToString());
        result[3].TaskOrder.Should().Be(0);

        result[4].Id.Should().Be(task5.Id.ToString());
        result[4].TaskOrder.Should().Be(1);
    }

    [Fact]
    public async Task GetProjectTasksAsync_ProjectExistsWithNoTasks_ReturnsEmptyList()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Empty Project" };
        context.Projects.Add(project);

        context.Users.Add(new User
        {
            Id = userId,
            Name = "Member",
            Email = $"{userId}@example.com",
            PasswordHash = "hash"
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = ProjectRole.Owner
        });

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "Column",
            ColumnOrder = 0,
            ProjectId = projectId
        };
        context.BoardColumns.Add(column);
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectTasksAsync(projectId, userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProjectTasksAsync_ProjectDoesNotExist_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var nonExistentProjectId = Guid.NewGuid();

        // Act
        var result = await service.GetProjectTasksAsync(nonExistentProjectId, userId);

        // Assert
        result.Should().BeNull();

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("non-existent project")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProjectTasksAsync_UserNotProjectMember_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var memberId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Private Project" };
        context.Projects.Add(project);

        context.Users.Add(new User
        {
            Id = memberId,
            Name = "Member",
            Email = $"{memberId}@example.com",
            PasswordHash = "hash"
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = memberId,
            Role = ProjectRole.Owner
        });

        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectTasksAsync(projectId, outsiderId);

        // Assert
        result.Should().BeNull();

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("without authorization")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProjectTasksAsync_MultipleProjectsExist_ReturnsTasksForRequestedProjectOnly()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var projectAId = Guid.NewGuid();
        var projectBId = Guid.NewGuid();

        context.Users.Add(new User
        {
            Id = userId,
            Name = "Member",
            Email = $"{userId}@example.com",
            PasswordHash = "hash"
        });

        var projectA = new Project { Id = projectAId, Name = "Project A" };
        var projectB = new Project { Id = projectBId, Name = "Project B" };
        context.Projects.AddRange(projectA, projectB);

        context.ProjectMembers.AddRange(
            new ProjectMember { ProjectId = projectAId, UserId = userId, Role = ProjectRole.Owner },
            new ProjectMember { ProjectId = projectBId, UserId = userId, Role = ProjectRole.Owner });

        var columnA = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "Column A",
            ColumnOrder = 0,
            ProjectId = projectAId
        };
        var columnB = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "Column B",
            ColumnOrder = 0,
            ProjectId = projectBId
        };
        context.BoardColumns.AddRange(columnA, columnB);

        var taskA1 = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Task A1",
            TaskOrder = 0,
            ColumnId = columnA.Id
        };
        var taskA2 = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Task A2",
            TaskOrder = 1,
            ColumnId = columnA.Id
        };
        var taskB1 = new KanbanTask
        {
            Id = Guid.NewGuid(),
            Title = "Task B1",
            TaskOrder = 0,
            ColumnId = columnB.Id
        };
        context.KanbanTasks.AddRange(taskA1, taskA2, taskB1);
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectTasksAsync(projectAId, userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result!.Should().OnlyContain(t => t.Title.StartsWith("Task A"));
        result.Should().NotContain(t => t.Title == "Task B1");
    }

    [Fact]
    public async Task GetProjectTasksAsync_OrderingByColumnIdThenTaskOrder_IsCorrect()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        context.Users.Add(new User
        {
            Id = userId,
            Name = "Member",
            Email = $"{userId}@example.com",
            PasswordHash = "hash"
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = ProjectRole.Owner
        });

        var colC = new BoardColumn { Id = Guid.Parse("CCCCCCCC-0000-0000-0000-000000000000"), Name = "C", ColumnOrder = 2, ProjectId = projectId };
        var colA = new BoardColumn { Id = Guid.Parse("AAAAAAAA-0000-0000-0000-000000000000"), Name = "A", ColumnOrder = 0, ProjectId = projectId };
        var colB = new BoardColumn { Id = Guid.Parse("BBBBBBBB-0000-0000-0000-000000000000"), Name = "B", ColumnOrder = 1, ProjectId = projectId };
        context.BoardColumns.AddRange(colC, colA, colB);

        var taskC0 = new KanbanTask { Id = Guid.NewGuid(), Title = "C0", TaskOrder = 0, ColumnId = colC.Id };
        var taskC2 = new KanbanTask { Id = Guid.NewGuid(), Title = "C2", TaskOrder = 2, ColumnId = colC.Id };
        var taskC1 = new KanbanTask { Id = Guid.NewGuid(), Title = "C1", TaskOrder = 1, ColumnId = colC.Id };
        var taskA1 = new KanbanTask { Id = Guid.NewGuid(), Title = "A1", TaskOrder = 1, ColumnId = colA.Id };
        var taskA0 = new KanbanTask { Id = Guid.NewGuid(), Title = "A0", TaskOrder = 0, ColumnId = colA.Id };
        var taskB0 = new KanbanTask { Id = Guid.NewGuid(), Title = "B0", TaskOrder = 0, ColumnId = colB.Id };
        context.KanbanTasks.AddRange(taskC0, taskC2, taskC1, taskA1, taskA0, taskB0);
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectTasksAsync(projectId, userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(6);

        var titles = result!.Select(t => t.Title).ToList();
        titles.Should().Equal("A0", "A1", "B0", "C0", "C1", "C2");
    }

    [Fact]
    public async Task GetProjectTasksAsync_SuccessfulRetrieval_LogsInformationWithCount()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        context.Users.Add(new User
        {
            Id = userId,
            Name = "Member",
            Email = $"{userId}@example.com",
            PasswordHash = "hash"
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = ProjectRole.Owner
        });

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "Column",
            ColumnOrder = 0,
            ProjectId = projectId
        };
        context.BoardColumns.Add(column);

        context.KanbanTasks.AddRange(
            new KanbanTask { Title = "Task 1", TaskOrder = 0, ColumnId = column.Id },
            new KanbanTask { Title = "Task 2", TaskOrder = 1, ColumnId = column.Id },
            new KanbanTask { Title = "Task 3", TaskOrder = 2, ColumnId = column.Id });
        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectTasksAsync(projectId, userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);

        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("retrieved") && v.ToString()!.Contains("3 tasks")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProjectTasksAsync_QueryUsesAsNoTracking_DoesNotTrackEntities()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object, _hubContextMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        context.Users.Add(new User
        {
            Id = userId,
            Name = "Member",
            Email = $"{userId}@example.com",
            PasswordHash = "hash"
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = ProjectRole.Owner
        });

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "Column",
            ColumnOrder = 0,
            ProjectId = projectId
        };
        context.BoardColumns.Add(column);

        context.KanbanTasks.AddRange(
            new KanbanTask { Title = "Task 1", TaskOrder = 0, ColumnId = column.Id },
            new KanbanTask { Title = "Task 2", TaskOrder = 1, ColumnId = column.Id });
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();

        // Act
        var result = await service.GetProjectTasksAsync(projectId, userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);

        context.ChangeTracker.Entries<KanbanTask>().Should().BeEmpty();
    }

    #endregion

    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Guid projectId, Guid columnId)> SeedProjectWithColumnAsync(
        ApplicationDbContext context,
        Guid memberUserId)
    {
        var projectId = Guid.NewGuid();
        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        context.Users.Add(new User
        {
            Id = memberUserId,
            Name = "Member",
            Email = $"{memberUserId}@example.com",
            PasswordHash = "hash"
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = memberUserId,
            Role = ProjectRole.Owner
        });

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "To Do",
            ColumnOrder = 0,
            ProjectId = projectId
        };
        context.BoardColumns.Add(column);

        await context.SaveChangesAsync();

        return (projectId, column.Id);
    }
}
