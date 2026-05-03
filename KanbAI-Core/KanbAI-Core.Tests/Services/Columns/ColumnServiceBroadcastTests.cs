using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Hubs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Columns;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace KanbAI_Core.Tests.Services.Columns;

public class ColumnServiceBroadcastTests
{
    private readonly Mock<ILogger<ColumnService>> _loggerMock = new();

    #region CreateColumnAsync Broadcast Tests

    [Fact]
    public async Task CreateColumnAsync_Success_BroadcastsColumnCreatedEventToProjectGroup()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, userId);
        var service = new ColumnService(context, _loggerMock.Object, hubContext.Object);

        var dto = new CreateColumnDto { Name = "Done", ColorCode = "#0f0", ColumnOrder = 0 };

        // Act
        var result = await service.CreateColumnAsync(projectId, dto, userId);

        // Assert
        result.Should().NotBeNull();

        var expectedGroup = $"project_{projectId.ToString().ToLowerInvariant()}";
        clients.Verify(c => c.Group(expectedGroup), Times.Once);
        clientProxy.Verify(
            p => p.SendCoreAsync(
                "ColumnCreated",
                It.Is<object[]>(args =>
                    args.Length == 1 &&
                    args[0] is ColumnResponseDto &&
                    ((ColumnResponseDto)args[0]).Name == "Done" &&
                    ((ColumnResponseDto)args[0]).ProjectId == projectId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateColumnAsync_ProjectNotFound_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var service = new ColumnService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var result = await service.CreateColumnAsync(
            Guid.NewGuid(),
            new CreateColumnDto { Name = "Ghost" },
            Guid.NewGuid());

        // Assert
        result.Should().BeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task CreateColumnAsync_UserNotMember_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var ownerId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId);
        var service = new ColumnService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var result = await service.CreateColumnAsync(
            projectId,
            new CreateColumnDto { Name = "Forbidden" },
            outsiderId);

        // Assert
        result.Should().BeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task CreateColumnAsync_BroadcastThrows_StillReturnsDto()
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
        var projectId = await SeedProjectAsync(context, userId);
        var service = new ColumnService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var result = await service.CreateColumnAsync(
            projectId,
            new CreateColumnDto { Name = "Swallowed" },
            userId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Swallowed");
        (await context.BoardColumns.CountAsync()).Should().Be(1);
    }

    #endregion

    #region DeleteColumnAsync Broadcast Tests

    [Fact]
    public async Task DeleteColumnAsync_Success_BroadcastsColumnDeletedEventWithColumnIdAndProjectId()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var (projectId, columnId) = await SeedProjectWithColumnAsync(context, userId);
        var service = new ColumnService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var result = await service.DeleteColumnAsync(columnId, userId);

        // Assert
        result.Should().BeTrue();

        var expectedGroup = $"project_{projectId.ToString().ToLowerInvariant()}";
        clients.Verify(c => c.Group(expectedGroup), Times.Once);
        clientProxy.Verify(
            p => p.SendCoreAsync(
                "ColumnDeleted",
                It.Is<object[]>(args =>
                    args.Length == 1 &&
                    args[0] is ColumnDeletedEventDto &&
                    ((ColumnDeletedEventDto)args[0]).ColumnId == columnId.ToString() &&
                    ((ColumnDeletedEventDto)args[0]).ProjectId == projectId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteColumnAsync_ColumnNotFound_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var service = new ColumnService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var result = await service.DeleteColumnAsync(Guid.NewGuid(), Guid.NewGuid());

        // Assert
        result.Should().BeFalse();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task DeleteColumnAsync_UserNotMember_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var ownerId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var (_, columnId) = await SeedProjectWithColumnAsync(context, ownerId);
        var service = new ColumnService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var result = await service.DeleteColumnAsync(columnId, outsiderId);

        // Assert
        result.Should().BeFalse();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task DeleteColumnAsync_BroadcastThrows_StillReturnsTrue()
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
        var service = new ColumnService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var result = await service.DeleteColumnAsync(columnId, userId);

        // Assert
        result.Should().BeTrue();
        (await context.BoardColumns.CountAsync()).Should().Be(0);
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

    private static async Task<Guid> SeedProjectAsync(ApplicationDbContext context, Guid memberUserId)
    {
        var projectId = Guid.NewGuid();
        context.Projects.Add(new Project { Id = projectId, Name = "Test Project" });
        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = memberUserId,
            Role = ProjectRole.Owner
        });
        await context.SaveChangesAsync();
        return projectId;
    }

    private static async Task<(Guid projectId, Guid columnId)> SeedProjectWithColumnAsync(
        ApplicationDbContext context,
        Guid memberUserId)
    {
        var projectId = await SeedProjectAsync(context, memberUserId);
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

    #endregion
}
