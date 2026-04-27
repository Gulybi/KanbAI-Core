using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace KanbAI_Core.Tests.Services.Projects;

public class ProjectServiceTests
{
    private readonly Mock<ILogger<ProjectService>> _loggerMock;

    public ProjectServiceTests()
    {
        _loggerMock = new Mock<ILogger<ProjectService>>();
    }

    #region CreateProjectAsync Tests

    [Fact]
    public async Task CreateProjectAsync_ValidDto_CreatesProjectAndAssignsOwner()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var dto = new CreateProjectDto
        {
            Name = "Test Project",
            Description = "Test Description"
        };

        // Act
        var result = await service.CreateProjectAsync(dto, userId);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Test Project");
        result.Description.Should().Be("Test Description");
        result.Role.Should().Be("Owner");

        var project = await context.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == Guid.Parse(result.Id));

        project.Should().NotBeNull();
        project!.Members.Should().ContainSingle();
        project.Members.First().UserId.Should().Be(userId);
        project.Members.First().Role.Should().Be(ProjectRole.Owner);
    }

    [Fact]
    public async Task CreateProjectAsync_NullDescription_CreatesProjectSuccessfully()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var dto = new CreateProjectDto
        {
            Name = "Test Project",
            Description = null
        };

        // Act
        var result = await service.CreateProjectAsync(dto, userId);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Test Project");
        result.Description.Should().BeNull();
        result.Role.Should().Be("Owner");
    }

    #endregion

    #region GetUserProjectsAsync Tests

    [Fact]
    public async Task GetUserProjectsAsync_UserHasMultipleProjects_ReturnsAllWithRoles()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();

        var project1 = new Project { Name = "Project 1", Description = "Desc 1" };
        var project2 = new Project { Name = "Project 2", Description = "Desc 2" };
        context.Projects.AddRange(project1, project2);

        var member1 = new ProjectMember { ProjectId = project1.Id, UserId = userId, Role = ProjectRole.Owner };
        var member2 = new ProjectMember { ProjectId = project2.Id, UserId = userId, Role = ProjectRole.Member };
        context.ProjectMembers.AddRange(member1, member2);

        await context.SaveChangesAsync();

        // Act
        var result = await service.GetUserProjectsAsync(userId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Name == "Project 1" && p.Role == "Owner");
        result.Should().Contain(p => p.Name == "Project 2" && p.Role == "Member");
    }

    [Fact]
    public async Task GetUserProjectsAsync_UserHasNoProjects_ReturnsEmptyList()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();

        // Act
        var result = await service.GetUserProjectsAsync(userId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserProjectsAsync_OtherUsersProjects_DoesNotReturnThem()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var project1 = new Project { Name = "My Project" };
        var project2 = new Project { Name = "Other Project" };
        context.Projects.AddRange(project1, project2);

        var member1 = new ProjectMember { ProjectId = project1.Id, UserId = userId, Role = ProjectRole.Owner };
        var member2 = new ProjectMember { ProjectId = project2.Id, UserId = otherUserId, Role = ProjectRole.Owner };
        context.ProjectMembers.AddRange(member1, member2);

        await context.SaveChangesAsync();

        // Act
        var result = await service.GetUserProjectsAsync(userId);

        // Assert
        result.Should().ContainSingle();
        result.First().Name.Should().Be("My Project");
    }

    #endregion

    #region GetProjectByIdAsync Tests

    [Fact]
    public async Task GetProjectByIdAsync_UserIsMember_ReturnsProjectWithRole()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();

        var project = new Project { Name = "Test Project", Description = "Test Desc" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = project.Id, UserId = userId, Role = ProjectRole.Member };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectByIdAsync(project.Id, userId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Test Project");
        result.Description.Should().Be("Test Desc");
        result.Role.Should().Be("Member");
    }

    [Fact]
    public async Task GetProjectByIdAsync_UserNotMember_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = project.Id, UserId = otherUserId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectByIdAsync(project.Id, userId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectByIdAsync_ProjectDoesNotExist_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var nonExistentProjectId = Guid.NewGuid();

        // Act
        var result = await service.GetProjectByIdAsync(nonExistentProjectId, userId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region UpdateProjectAsync Tests

    [Fact]
    public async Task UpdateProjectAsync_UserIsMember_UpdatesAndReturnsProject()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();

        var project = new Project { Name = "Original Name", Description = "Original Desc" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = project.Id, UserId = userId, Role = ProjectRole.Member };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        var originalUpdatedAt = project.UpdatedAt;

        var updateDto = new UpdateProjectDto
        {
            Name = "Updated Name",
            Description = "Updated Description"
        };

        // Act
        var result = await service.UpdateProjectAsync(project.Id, updateDto, userId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Name");
        result.Description.Should().Be("Updated Description");
        result.Role.Should().Be("Member");

        var updatedProject = await context.Projects.FindAsync(project.Id);
        updatedProject!.Name.Should().Be("Updated Name");
        updatedProject.Description.Should().Be("Updated Description");
        updatedProject.UpdatedAt.Should().BeAfter(originalUpdatedAt);
    }

    [Fact]
    public async Task UpdateProjectAsync_UserNotMember_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = project.Id, UserId = otherUserId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        var updateDto = new UpdateProjectDto
        {
            Name = "Updated Name",
            Description = "Updated Description"
        };

        // Act
        var result = await service.UpdateProjectAsync(project.Id, updateDto, userId);

        // Assert
        result.Should().BeNull();

        var unchangedProject = await context.Projects.FindAsync(project.Id);
        unchangedProject!.Name.Should().Be("Test Project");
    }

    [Fact]
    public async Task UpdateProjectAsync_ProjectDoesNotExist_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var nonExistentProjectId = Guid.NewGuid();

        var updateDto = new UpdateProjectDto
        {
            Name = "Updated Name",
            Description = "Updated Description"
        };

        // Act
        var result = await service.UpdateProjectAsync(nonExistentProjectId, updateDto, userId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region DeleteProjectAsync Tests

    [Fact]
    public async Task DeleteProjectAsync_UserIsOwner_DeletesProject()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = project.Id, UserId = userId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        // Act
        var (isDeleted, errorMessage) = await service.DeleteProjectAsync(project.Id, userId);

        // Assert
        isDeleted.Should().BeTrue();
        errorMessage.Should().BeNull();

        var deletedProject = await context.Projects.FindAsync(project.Id);
        deletedProject.Should().BeNull();

        var deletedMember = await context.ProjectMembers.FindAsync(member.Id);
        deletedMember.Should().BeNull();
    }

    [Fact]
    public async Task DeleteProjectAsync_UserIsMemberNotOwner_ReturnsForbiddenMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = project.Id, UserId = userId, Role = ProjectRole.Member };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        // Act
        var (isDeleted, errorMessage) = await service.DeleteProjectAsync(project.Id, userId);

        // Assert
        isDeleted.Should().BeFalse();
        errorMessage.Should().Be("Only the project owner can delete the project.");

        var stillExistingProject = await context.Projects.FindAsync(project.Id);
        stillExistingProject.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteProjectAsync_UserNotMember_ReturnsNotFoundMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = project.Id, UserId = otherUserId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        // Act
        var (isDeleted, errorMessage) = await service.DeleteProjectAsync(project.Id, userId);

        // Assert
        isDeleted.Should().BeFalse();
        errorMessage.Should().Be("Project not found.");

        var stillExistingProject = await context.Projects.FindAsync(project.Id);
        stillExistingProject.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteProjectAsync_ProjectDoesNotExist_ReturnsNotFoundMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var nonExistentProjectId = Guid.NewGuid();

        // Act
        var (isDeleted, errorMessage) = await service.DeleteProjectAsync(nonExistentProjectId, userId);

        // Assert
        isDeleted.Should().BeFalse();
        errorMessage.Should().Be("Project not found.");
    }

    #endregion

    #region AddMemberAsync Tests

    [Fact]
    public async Task AddMemberAsync_ValidRequest_AddsMemberWithMemberRole()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();
        var userToAddId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var owner = new ProjectMember { ProjectId = project.Id, UserId = ownerId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(owner);

        var userToAdd = new User { Id = userToAddId, Name = "Jane Doe", Email = "jane@example.com", PasswordHash = "hash" };
        context.Users.Add(userToAdd);

        await context.SaveChangesAsync();

        // Act
        var (member, errorMessage) = await service.AddMemberAsync(project.Id, userToAddId, ownerId);

        // Assert
        member.Should().NotBeNull();
        errorMessage.Should().BeNull();
        member!.UserId.Should().Be(userToAddId.ToString());
        member.Name.Should().Be("Jane Doe");
        member.Email.Should().Be("jane@example.com");
        member.Role.Should().Be("Member");
        member.JoinedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        var addedMember = await context.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == project.Id && m.UserId == userToAddId);
        addedMember.Should().NotBeNull();
        addedMember!.Role.Should().Be(ProjectRole.Member);
    }

    [Fact]
    public async Task AddMemberAsync_RequestingUserNotOwner_ReturnsForbiddenMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var memberId = Guid.NewGuid();
        var userToAddId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = project.Id, UserId = memberId, Role = ProjectRole.Member };
        context.ProjectMembers.Add(member);

        var userToAdd = new User { Id = userToAddId, Name = "Jane Doe", Email = "jane@example.com", PasswordHash = "hash" };
        context.Users.Add(userToAdd);

        await context.SaveChangesAsync();

        // Act
        var (memberDto, errorMessage) = await service.AddMemberAsync(project.Id, userToAddId, memberId);

        // Assert
        memberDto.Should().BeNull();
        errorMessage.Should().Be("Only the project owner can add members.");

        var notAddedMember = await context.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == project.Id && m.UserId == userToAddId);
        notAddedMember.Should().BeNull();
    }

    [Fact]
    public async Task AddMemberAsync_ProjectNotFound_ReturnsNotFoundMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();
        var userToAddId = Guid.NewGuid();
        var nonExistentProjectId = Guid.NewGuid();

        // Act
        var (member, errorMessage) = await service.AddMemberAsync(nonExistentProjectId, userToAddId, ownerId);

        // Assert
        member.Should().BeNull();
        errorMessage.Should().Be("Project not found.");
    }

    [Fact]
    public async Task AddMemberAsync_RequestingUserNotMember_ReturnsNotFoundMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();
        var requestingUserId = Guid.NewGuid();
        var userToAddId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var owner = new ProjectMember { ProjectId = project.Id, UserId = ownerId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(owner);

        var userToAdd = new User { Id = userToAddId, Name = "Jane Doe", Email = "jane@example.com", PasswordHash = "hash" };
        context.Users.Add(userToAdd);

        await context.SaveChangesAsync();

        // Act
        var (member, errorMessage) = await service.AddMemberAsync(project.Id, userToAddId, requestingUserId);

        // Assert
        member.Should().BeNull();
        errorMessage.Should().Be("Project not found.");
    }

    [Fact]
    public async Task AddMemberAsync_UserToAddNotFound_ReturnsUserNotFoundMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();
        var nonExistentUserId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var owner = new ProjectMember { ProjectId = project.Id, UserId = ownerId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(owner);

        await context.SaveChangesAsync();

        // Act
        var (member, errorMessage) = await service.AddMemberAsync(project.Id, nonExistentUserId, ownerId);

        // Assert
        member.Should().BeNull();
        errorMessage.Should().Be("User not found.");
    }

    [Fact]
    public async Task AddMemberAsync_UserAlreadyMember_ReturnsDuplicateMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();
        var existingMemberId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var owner = new ProjectMember { ProjectId = project.Id, UserId = ownerId, Role = ProjectRole.Owner };
        var existingMember = new ProjectMember { ProjectId = project.Id, UserId = existingMemberId, Role = ProjectRole.Member };
        context.ProjectMembers.AddRange(owner, existingMember);

        var user = new User { Id = existingMemberId, Name = "Existing Member", Email = "existing@example.com", PasswordHash = "hash" };
        context.Users.Add(user);

        await context.SaveChangesAsync();

        var memberCountBefore = await context.ProjectMembers.CountAsync(m => m.ProjectId == project.Id);

        // Act
        var (member, errorMessage) = await service.AddMemberAsync(project.Id, existingMemberId, ownerId);

        // Assert
        member.Should().BeNull();
        errorMessage.Should().Be("User is already a member of this project.");

        var memberCountAfter = await context.ProjectMembers.CountAsync(m => m.ProjectId == project.Id);
        memberCountAfter.Should().Be(memberCountBefore);
    }

    #endregion

    #region RemoveMemberAsync Tests

    [Fact]
    public async Task RemoveMemberAsync_ValidRequest_RemovesMember()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();
        var memberToRemoveId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var owner = new ProjectMember { ProjectId = project.Id, UserId = ownerId, Role = ProjectRole.Owner };
        var memberToRemove = new ProjectMember { ProjectId = project.Id, UserId = memberToRemoveId, Role = ProjectRole.Member };
        context.ProjectMembers.AddRange(owner, memberToRemove);

        await context.SaveChangesAsync();

        // Act
        var (isRemoved, errorMessage) = await service.RemoveMemberAsync(project.Id, memberToRemoveId, ownerId);

        // Assert
        isRemoved.Should().BeTrue();
        errorMessage.Should().BeNull();

        var removedMember = await context.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == project.Id && m.UserId == memberToRemoveId);
        removedMember.Should().BeNull();
    }

    [Fact]
    public async Task RemoveMemberAsync_RequestingUserNotOwner_ReturnsForbiddenMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var memberToRemoveId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var owner = new ProjectMember { ProjectId = project.Id, UserId = ownerId, Role = ProjectRole.Owner };
        var member = new ProjectMember { ProjectId = project.Id, UserId = memberId, Role = ProjectRole.Member };
        var memberToRemove = new ProjectMember { ProjectId = project.Id, UserId = memberToRemoveId, Role = ProjectRole.Member };
        context.ProjectMembers.AddRange(owner, member, memberToRemove);

        await context.SaveChangesAsync();

        // Act
        var (isRemoved, errorMessage) = await service.RemoveMemberAsync(project.Id, memberToRemoveId, memberId);

        // Assert
        isRemoved.Should().BeFalse();
        errorMessage.Should().Be("Only the project owner can remove members.");

        var stillExistingMember = await context.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == project.Id && m.UserId == memberToRemoveId);
        stillExistingMember.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveMemberAsync_ProjectNotFound_ReturnsNotFoundMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();
        var memberToRemoveId = Guid.NewGuid();
        var nonExistentProjectId = Guid.NewGuid();

        // Act
        var (isRemoved, errorMessage) = await service.RemoveMemberAsync(nonExistentProjectId, memberToRemoveId, ownerId);

        // Assert
        isRemoved.Should().BeFalse();
        errorMessage.Should().Be("Project not found.");
    }

    [Fact]
    public async Task RemoveMemberAsync_RequestingUserNotMember_ReturnsNotFoundMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();
        var requestingUserId = Guid.NewGuid();
        var memberToRemoveId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var owner = new ProjectMember { ProjectId = project.Id, UserId = ownerId, Role = ProjectRole.Owner };
        var memberToRemove = new ProjectMember { ProjectId = project.Id, UserId = memberToRemoveId, Role = ProjectRole.Member };
        context.ProjectMembers.AddRange(owner, memberToRemove);

        await context.SaveChangesAsync();

        // Act
        var (isRemoved, errorMessage) = await service.RemoveMemberAsync(project.Id, memberToRemoveId, requestingUserId);

        // Assert
        isRemoved.Should().BeFalse();
        errorMessage.Should().Be("Project not found.");
    }

    [Fact]
    public async Task RemoveMemberAsync_UserToRemoveNotMember_ReturnsNotMemberMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();
        var nonMemberId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var owner = new ProjectMember { ProjectId = project.Id, UserId = ownerId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(owner);

        await context.SaveChangesAsync();

        // Act
        var (isRemoved, errorMessage) = await service.RemoveMemberAsync(project.Id, nonMemberId, ownerId);

        // Assert
        isRemoved.Should().BeFalse();
        errorMessage.Should().Be("User is not a member of this project.");
    }

    [Fact]
    public async Task RemoveMemberAsync_LastOwner_ReturnsLastOwnerMessage()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var ownerId = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var owner = new ProjectMember { ProjectId = project.Id, UserId = ownerId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(owner);

        await context.SaveChangesAsync();

        // Act
        var (isRemoved, errorMessage) = await service.RemoveMemberAsync(project.Id, ownerId, ownerId);

        // Assert
        isRemoved.Should().BeFalse();
        errorMessage.Should().Be("Cannot remove the last owner from the project.");

        var stillExistingOwner = await context.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == project.Id && m.UserId == ownerId);
        stillExistingOwner.Should().NotBeNull();
    }

    [Fact]
    public async Task RemoveMemberAsync_MultipleOwnersRemoveOne_Success()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ProjectService(context, _loggerMock.Object);
        var owner1Id = Guid.NewGuid();
        var owner2Id = Guid.NewGuid();

        var project = new Project { Name = "Test Project" };
        context.Projects.Add(project);

        var owner1 = new ProjectMember { ProjectId = project.Id, UserId = owner1Id, Role = ProjectRole.Owner };
        var owner2 = new ProjectMember { ProjectId = project.Id, UserId = owner2Id, Role = ProjectRole.Owner };
        context.ProjectMembers.AddRange(owner1, owner2);

        await context.SaveChangesAsync();

        // Act
        var (isRemoved, errorMessage) = await service.RemoveMemberAsync(project.Id, owner2Id, owner1Id);

        // Assert
        isRemoved.Should().BeTrue();
        errorMessage.Should().BeNull();

        var removedOwner = await context.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == project.Id && m.UserId == owner2Id);
        removedOwner.Should().BeNull();

        var remainingOwner = await context.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == project.Id && m.UserId == owner1Id);
        remainingOwner.Should().NotBeNull();
        remainingOwner!.Role.Should().Be(ProjectRole.Owner);
    }

    #endregion

    #region Helper Methods

    private ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    #endregion
}
