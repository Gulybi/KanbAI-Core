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
