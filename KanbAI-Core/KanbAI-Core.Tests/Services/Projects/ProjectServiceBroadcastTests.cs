using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Hubs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Projects;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace KanbAI_Core.Tests.Services.Projects;

public class ProjectServiceBroadcastTests
{
    private readonly Mock<ILogger<ProjectService>> _loggerMock = new();

    #region UpdateProjectAsync Broadcast Tests

    [Fact]
    public async Task UpdateProjectAsync_Success_BroadcastsProjectUpdatedEventWithNameDescriptionUpdatedAt()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, userId, ProjectRole.Owner);
        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        var dto = new UpdateProjectDto { Name = "Renamed", Description = "New description" };

        // Act
        var result = await service.UpdateProjectAsync(projectId, dto, userId);

        // Assert
        result.Should().NotBeNull();

        var expectedGroup = $"project_{projectId.ToString().ToLowerInvariant()}";
        clients.Verify(c => c.Group(expectedGroup), Times.Once);
        clientProxy.Verify(
            p => p.SendCoreAsync(
                "ProjectUpdated",
                It.Is<object[]>(args =>
                    args.Length == 1 &&
                    args[0] is ProjectUpdatedEventDto &&
                    ((ProjectUpdatedEventDto)args[0]).ProjectId == projectId.ToString() &&
                    ((ProjectUpdatedEventDto)args[0]).Name == "Renamed" &&
                    ((ProjectUpdatedEventDto)args[0]).Description == "New description"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UpdateProjectAsync_ProjectNotFound_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var result = await service.UpdateProjectAsync(
            Guid.NewGuid(),
            new UpdateProjectDto { Name = "Ghost" },
            Guid.NewGuid());

        // Assert
        result.Should().BeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task UpdateProjectAsync_UserNotMember_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var ownerId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId, ProjectRole.Owner);
        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var result = await service.UpdateProjectAsync(
            projectId,
            new UpdateProjectDto { Name = "Forbidden" },
            outsiderId);

        // Assert
        result.Should().BeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task UpdateProjectAsync_BroadcastThrows_StillReturnsDto()
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
        var projectId = await SeedProjectAsync(context, userId, ProjectRole.Owner);
        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var result = await service.UpdateProjectAsync(
            projectId,
            new UpdateProjectDto { Name = "Swallowed" },
            userId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Swallowed");
    }

    #endregion

    #region DeleteProjectAsync Broadcast Tests

    [Fact]
    public async Task DeleteProjectAsync_Success_BroadcastsProjectDeletedEventWithProjectId()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var userId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, userId, ProjectRole.Owner);
        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (isDeleted, error) = await service.DeleteProjectAsync(projectId, userId);

        // Assert
        isDeleted.Should().BeTrue();
        error.Should().BeNull();

        var expectedGroup = $"project_{projectId.ToString().ToLowerInvariant()}";
        clients.Verify(c => c.Group(expectedGroup), Times.Once);
        clientProxy.Verify(
            p => p.SendCoreAsync(
                "ProjectDeleted",
                It.Is<object[]>(args =>
                    args.Length == 1 &&
                    args[0] is ProjectDeletedEventDto &&
                    ((ProjectDeletedEventDto)args[0]).ProjectId == projectId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeleteProjectAsync_NonOwner_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId, ProjectRole.Owner);
        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = memberId,
            Role = ProjectRole.Member
        });
        await context.SaveChangesAsync();

        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (isDeleted, error) = await service.DeleteProjectAsync(projectId, memberId);

        // Assert
        isDeleted.Should().BeFalse();
        error.Should().NotBeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task DeleteProjectAsync_BroadcastThrows_StillReturnsSuccess()
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
        var projectId = await SeedProjectAsync(context, userId, ProjectRole.Owner);
        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (isDeleted, error) = await service.DeleteProjectAsync(projectId, userId);

        // Assert
        isDeleted.Should().BeTrue();
        error.Should().BeNull();
        (await context.Projects.CountAsync()).Should().Be(0);
    }

    #endregion

    #region AddMemberAsync Broadcast Tests

    [Fact]
    public async Task AddMemberAsync_Success_BroadcastsMemberAddedEventWithMemberDto()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var ownerId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId, ProjectRole.Owner);

        var newUserId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = newUserId,
            Name = "New Guy",
            Email = "new@example.com",
            PasswordHash = "hash"
        });
        await context.SaveChangesAsync();

        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (member, error) = await service.AddMemberAsync(projectId, newUserId, ownerId);

        // Assert
        member.Should().NotBeNull();
        error.Should().BeNull();

        var expectedGroup = $"project_{projectId.ToString().ToLowerInvariant()}";
        clients.Verify(c => c.Group(expectedGroup), Times.Once);
        clientProxy.Verify(
            p => p.SendCoreAsync(
                "MemberAdded",
                It.Is<object[]>(args =>
                    args.Length == 1 &&
                    args[0] is MemberResponseDto &&
                    ((MemberResponseDto)args[0]).UserId == newUserId.ToString() &&
                    ((MemberResponseDto)args[0]).Email == "new@example.com"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddMemberAsync_UserAlreadyMember_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var ownerId = Guid.NewGuid();
        var existingMemberId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId, ProjectRole.Owner);

        context.Users.Add(new User
        {
            Id = existingMemberId,
            Name = "Existing",
            Email = "ex@example.com",
            PasswordHash = "hash"
        });
        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = existingMemberId,
            Role = ProjectRole.Member
        });
        await context.SaveChangesAsync();

        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (member, error) = await service.AddMemberAsync(projectId, existingMemberId, ownerId);

        // Assert
        member.Should().BeNull();
        error.Should().NotBeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task AddMemberAsync_RequestingUserNotOwner_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId, ProjectRole.Owner);
        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = memberId,
            Role = ProjectRole.Member
        });

        var newUserId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = newUserId,
            Name = "New",
            Email = "new2@example.com",
            PasswordHash = "hash"
        });
        await context.SaveChangesAsync();

        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act: requester is a member, not owner — must be rejected
        var (member, error) = await service.AddMemberAsync(projectId, newUserId, memberId);

        // Assert
        member.Should().BeNull();
        error.Should().NotBeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task AddMemberAsync_BroadcastThrows_StillReturnsMember()
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

        var ownerId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId, ProjectRole.Owner);

        var newUserId = Guid.NewGuid();
        context.Users.Add(new User
        {
            Id = newUserId,
            Name = "Swallowed",
            Email = "sw@example.com",
            PasswordHash = "hash"
        });
        await context.SaveChangesAsync();

        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (member, error) = await service.AddMemberAsync(projectId, newUserId, ownerId);

        // Assert
        member.Should().NotBeNull();
        error.Should().BeNull();
        (await context.ProjectMembers.CountAsync(m => m.ProjectId == projectId)).Should().Be(2);
    }

    #endregion

    #region RemoveMemberAsync Broadcast Tests

    [Fact]
    public async Task RemoveMemberAsync_Success_BroadcastsMemberRemovedEventWithUserIdAndProjectId()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId, ProjectRole.Owner);
        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = memberId,
            Role = ProjectRole.Member
        });
        await context.SaveChangesAsync();

        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (isRemoved, error) = await service.RemoveMemberAsync(projectId, memberId, ownerId);

        // Assert
        isRemoved.Should().BeTrue();
        error.Should().BeNull();

        var expectedGroup = $"project_{projectId.ToString().ToLowerInvariant()}";
        clients.Verify(c => c.Group(expectedGroup), Times.Once);
        clientProxy.Verify(
            p => p.SendCoreAsync(
                "MemberRemoved",
                It.Is<object[]>(args =>
                    args.Length == 1 &&
                    args[0] is MemberRemovedEventDto &&
                    ((MemberRemovedEventDto)args[0]).UserId == memberId.ToString() &&
                    ((MemberRemovedEventDto)args[0]).ProjectId == projectId.ToString()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RemoveMemberAsync_LastOwner_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var ownerId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId, ProjectRole.Owner);
        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act: owner tries to remove themselves; they are the only owner
        var (isRemoved, error) = await service.RemoveMemberAsync(projectId, ownerId, ownerId);

        // Assert
        isRemoved.Should().BeFalse();
        error.Should().NotBeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task RemoveMemberAsync_UserNotMember_DoesNotBroadcast()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var (clients, clientProxy, hubContext) = CreateHubContextMock();
        var ownerId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId, ProjectRole.Owner);
        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act: try to remove someone who isn't in the project
        var (isRemoved, error) = await service.RemoveMemberAsync(projectId, Guid.NewGuid(), ownerId);

        // Assert
        isRemoved.Should().BeFalse();
        error.Should().NotBeNull();
        VerifyNoBroadcast(clients, clientProxy);
    }

    [Fact]
    public async Task RemoveMemberAsync_BroadcastThrows_StillReturnsSuccess()
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

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var projectId = await SeedProjectAsync(context, ownerId, ProjectRole.Owner);
        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = memberId,
            Role = ProjectRole.Member
        });
        await context.SaveChangesAsync();

        var service = new ProjectService(context, _loggerMock.Object, hubContext.Object);

        // Act
        var (isRemoved, error) = await service.RemoveMemberAsync(projectId, memberId, ownerId);

        // Assert
        isRemoved.Should().BeTrue();
        error.Should().BeNull();
        (await context.ProjectMembers.CountAsync(m => m.ProjectId == projectId)).Should().Be(1);
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

    private static async Task<Guid> SeedProjectAsync(
        ApplicationDbContext context,
        Guid userId,
        ProjectRole role)
    {
        var projectId = Guid.NewGuid();
        context.Projects.Add(new Project { Id = projectId, Name = "Test Project" });
        context.Users.Add(new User
        {
            Id = userId,
            Name = "User",
            Email = $"{userId}@example.com",
            PasswordHash = "hash"
        });
        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = userId,
            Role = role
        });
        await context.SaveChangesAsync();
        return projectId;
    }

    #endregion
}
