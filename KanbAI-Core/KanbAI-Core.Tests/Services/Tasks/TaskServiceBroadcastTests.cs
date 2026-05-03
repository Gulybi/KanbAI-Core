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

public class TaskServiceBroadcastTests
{
    private readonly Mock<ILogger<TaskService>> _loggerMock = new();

    #region CreateTaskAsync Broadcast Tests

    [Fact]
    public async Task CreateTaskAsync_Success_BroadcastsTaskCreatedEventToProjectGroup()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);
        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);

        var dto = new CreateTaskDto { Title = "Broadcast Me" };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.Success);
        data.Should().NotBeNull();

        var expectedGroup = $"project_{projectId.ToString().ToLowerInvariant()}";
        clients.Verify(c => c.Group(expectedGroup), Times.Once);
        clientProxy.Verify(
            p => p.SendCoreAsync(
                "TaskCreated",
                It.Is<object[]>(args => args.Length == 1 && args[0] is TaskResponseDto),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateTaskAsync_ValidationFails_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var (_, columnId) = await SeedProjectWithColumnAsync(context, userId);
        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);

        var dto = new CreateTaskDto { Title = "   " };

        // Act
        var (data, result) = await service.CreateTaskAsync(columnId, dto, userId);

        // Assert
        result.Should().Be(CreateTaskResult.InvalidTitle);
        data.Should().BeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task CreateTaskAsync_ColumnNotFound_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (data, result) = await service.CreateTaskAsync(
            Guid.NewGuid(),
            new CreateTaskDto { Title = "Title" },
            Guid.NewGuid());

        // Assert
        result.Should().Be(CreateTaskResult.ColumnNotFound);
        data.Should().BeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task CreateTaskAsync_UserNotMember_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var memberId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var (_, columnId) = await SeedProjectWithColumnAsync(context, memberId);
        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (data, result) = await service.CreateTaskAsync(
            columnId,
            new CreateTaskDto { Title = "Forbidden" },
            outsiderId);

        // Assert
        result.Should().Be(CreateTaskResult.UserNotProjectMember);
        data.Should().BeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task CreateTaskAsync_BroadcastThrows_StillReturnsSuccess()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (_, clientProxy, hubContext) = CreateHubContextMock();
        clientProxy
            .Setup(p => p.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated hub failure"));

        var userId = Guid.NewGuid();
        var (_, columnId) = await SeedProjectWithColumnAsync(context, userId);
        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (data, result) = await service.CreateTaskAsync(
            columnId,
            new CreateTaskDto { Title = "Swallowed broadcast" },
            userId);

        // Assert
        result.Should().Be(CreateTaskResult.Success);
        data.Should().NotBeNull();
        (await context.KanbanTasks.CountAsync()).Should().Be(1);
    }

    #endregion

    #region MoveTaskAsync Broadcast Tests

    [Fact]
    public async Task MoveTaskAsync_SuccessCrossColumn_BroadcastsTaskMovedWithOldAndNewPositions()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var (projectId, sourceColumnId, targetColumnId) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = movedTaskId, Title = "M", TaskOrder = 0, ColumnId = sourceColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "X", TaskOrder = 0, ColumnId = targetColumnId });
        await context.SaveChangesAsync();

        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);
        var dto = new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 1 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);
        data.Should().NotBeNull();

        var expectedGroup = $"project_{projectId.ToString().ToLowerInvariant()}";
        clients.Verify(c => c.Group(expectedGroup), Times.Once);
        clientProxy.Verify(
            p => p.SendCoreAsync(
                "TaskMoved",
                It.Is<object[]>(args =>
                    args.Length == 1 &&
                    args[0] is TaskMovedEventDto &&
                    ((TaskMovedEventDto)args[0]).TaskId == movedTaskId.ToString() &&
                    ((TaskMovedEventDto)args[0]).OldColumnId == sourceColumnId.ToString() &&
                    ((TaskMovedEventDto)args[0]).NewColumnId == targetColumnId.ToString() &&
                    ((TaskMovedEventDto)args[0]).OldTaskOrder == 0 &&
                    ((TaskMovedEventDto)args[0]).NewTaskOrder == 1 &&
                    ((TaskMovedEventDto)args[0]).Task != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MoveTaskAsync_SuccessSameColumnReorder_BroadcastsTaskMoved()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (_, clientProxy, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var (_, columnId) = await SeedProjectWithColumnAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = movedTaskId, Title = "A", TaskOrder = 0, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "B", TaskOrder = 1, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "C", TaskOrder = 2, ColumnId = columnId });
        await context.SaveChangesAsync();

        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);
        var dto = new MoveTaskDto { ColumnId = columnId, TaskOrder = 2 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);
        data.Should().NotBeNull();

        clientProxy.Verify(
            p => p.SendCoreAsync(
                "TaskMoved",
                It.Is<object[]>(args =>
                    args[0] is TaskMovedEventDto &&
                    ((TaskMovedEventDto)args[0]).OldColumnId == columnId.ToString() &&
                    ((TaskMovedEventDto)args[0]).NewColumnId == columnId.ToString() &&
                    ((TaskMovedEventDto)args[0]).OldTaskOrder == 0 &&
                    ((TaskMovedEventDto)args[0]).NewTaskOrder == 2),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MoveTaskAsync_SameColumnSamePosition_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var (_, columnId) = await SeedProjectWithColumnAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.Add(
            new KanbanTask { Id = movedTaskId, Title = "A", TaskOrder = 0, ColumnId = columnId });
        await context.SaveChangesAsync();

        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);
        var dto = new MoveTaskDto { ColumnId = columnId, TaskOrder = 0 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);
        data.Should().NotBeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task MoveTaskAsync_CrossProjectMove_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var (_, sourceColumnId) = await SeedProjectWithColumnAsync(context, userId);
        var otherProjectColumnId = await SeedSecondProjectWithColumnAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.Add(
            new KanbanTask { Id = movedTaskId, Title = "A", TaskOrder = 0, ColumnId = sourceColumnId });
        await context.SaveChangesAsync();

        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);
        var dto = new MoveTaskDto { ColumnId = otherProjectColumnId, TaskOrder = 0 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.CrossProjectMove);
        data.Should().BeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task MoveTaskAsync_BroadcastThrows_StillReturnsSuccess()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (_, clientProxy, hubContext) = CreateHubContextMock();
        clientProxy
            .Setup(p => p.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated hub failure"));

        var userId = Guid.NewGuid();
        var (_, sourceColumnId, targetColumnId) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.Add(
            new KanbanTask { Id = movedTaskId, Title = "A", TaskOrder = 0, ColumnId = sourceColumnId });
        await context.SaveChangesAsync();

        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);
        var dto = new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 0 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);
        data.Should().NotBeNull();

        var persisted = await context.KanbanTasks.AsNoTracking().SingleAsync(t => t.Id == movedTaskId);
        persisted.ColumnId.Should().Be(targetColumnId);
    }

    [Fact]
    public async Task MoveTaskAsync_Success_GroupNameIsLowercaseProjectGuid()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, _, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var (projectId, sourceColumnId, targetColumnId) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.Add(
            new KanbanTask { Id = movedTaskId, Title = "A", TaskOrder = 0, ColumnId = sourceColumnId });
        await context.SaveChangesAsync();

        var service = new TaskService(context, _loggerMock.Object, hubContext.Object);
        var dto = new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 0 };

        // Act
        var (_, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);

        var expectedGroup = $"project_{projectId.ToString().ToLowerInvariant()}";
        clients.Verify(c => c.Group(expectedGroup), Times.Once);
        clients.Verify(c => c.Group(It.Is<string>(s => s != expectedGroup)), Times.Never);
    }

    #endregion

    #region Helpers

    private static (Mock<IHubClients> clients, Mock<IClientProxy> clientProxy, Mock<IHubContext<KanbanHub>> hubContext)
        CreateHubContextMock()
    {
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Group(It.IsAny<string>())).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<KanbanHub>>();
        hubContext.Setup(h => h.Clients).Returns(clients.Object);

        return (clients, clientProxy, hubContext);
    }

    private static void VerifyNoBroadcast(Mock<IHubClients> clients, Mock<IClientProxy> clientProxy)
    {
        clients.Verify(c => c.Group(It.IsAny<string>()), Times.Never);
        clientProxy.Verify(
            p => p.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object[]>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
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
        context.Projects.Add(new Project { Id = projectId, Name = "Test Project" });
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

    private static async Task<(Guid projectId, Guid columnAId, Guid columnBId)> SeedProjectWithTwoColumnsAsync(
        ApplicationDbContext context,
        Guid memberUserId)
    {
        var projectId = Guid.NewGuid();
        context.Projects.Add(new Project { Id = projectId, Name = "Test Project" });
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

        var columnA = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "To Do",
            ColumnOrder = 0,
            ProjectId = projectId
        };
        var columnB = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "In Progress",
            ColumnOrder = 1,
            ProjectId = projectId
        };
        context.BoardColumns.AddRange(columnA, columnB);
        await context.SaveChangesAsync();

        return (projectId, columnA.Id, columnB.Id);
    }

    private static async Task<Guid> SeedSecondProjectWithColumnAsync(
        ApplicationDbContext context,
        Guid memberUserId)
    {
        var projectId = Guid.NewGuid();
        context.Projects.Add(new Project { Id = projectId, Name = "Other Project" });
        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = memberUserId,
            Role = ProjectRole.Owner
        });

        var column = new BoardColumn
        {
            Id = Guid.NewGuid(),
            Name = "Other Column",
            ColumnOrder = 0,
            ProjectId = projectId
        };
        context.BoardColumns.Add(column);
        await context.SaveChangesAsync();

        return column.Id;
    }

    #endregion
}
