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
