using System.Security.Claims;
using FluentAssertions;
using KanbAI_Core.Controllers;
using KanbAI_Core.DTOs;
using KanbAI_Core.Services.Projects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace KanbAI_Core.Tests.Controllers;

public class ProjectControllerTests
{
    private readonly Mock<IProjectService> _projectServiceMock;
    private readonly Mock<ILogger<ProjectController>> _loggerMock;
    private readonly ProjectController _controller;

    public ProjectControllerTests()
    {
        _projectServiceMock = new Mock<IProjectService>();
        _loggerMock = new Mock<ILogger<ProjectController>>();
        _controller = new ProjectController(_projectServiceMock.Object, _loggerMock.Object);
    }

    #region CreateProject Tests

    [Fact]
    public async Task CreateProject_ValidDto_Returns201WithLocationHeader()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var dto = new CreateProjectDto
        {
            Name = "Test Project",
            Description = "Test Description"
        };

        var responseDto = new ProjectResponseDto
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test Project",
            Description = "Test Description",
            Role = "Owner",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(s => s.CreateProjectAsync(dto, userId))
            .ReturnsAsync(responseDto);

        // Act
        var result = await _controller.CreateProject(dto);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
        createdResult.ActionName.Should().Be(nameof(ProjectController.GetProjectById));
        createdResult.RouteValues!["id"].Should().Be(responseDto.Id);

        var apiResponse = createdResult.Value.Should().BeOfType<ApiResponse<ProjectResponseDto>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().BeEquivalentTo(responseDto);
        apiResponse.Message.Should().Be("Project created successfully.");
    }

    [Fact]
    public async Task CreateProject_MissingNameIdentifierClaim_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };

        var dto = new CreateProjectDto
        {
            Name = "Test Project",
            Description = "Test Description"
        };

        // Act
        Func<Task> act = async () => await _controller.CreateProject(dto);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or missing user ID in token.");
    }

    #endregion

    #region GetUserProjects Tests

    [Fact]
    public async Task GetUserProjects_UserHasProjects_Returns200WithList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        var projects = new List<ProjectResponseDto>
        {
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Project 1",
                Description = "Desc 1",
                Role = "Owner",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Project 2",
                Description = "Desc 2",
                Role = "Member",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        _projectServiceMock
            .Setup(s => s.GetUserProjectsAsync(userId))
            .ReturnsAsync(projects);

        // Act
        var result = await _controller.GetUserProjects();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<List<ProjectResponseDto>>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().HaveCount(2);
        apiResponse.Data.Should().BeEquivalentTo(projects);
    }

    [Fact]
    public async Task GetUserProjects_UserHasNoProjects_Returns200WithEmptyList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        SetupUserClaims(userId);

        _projectServiceMock
            .Setup(s => s.GetUserProjectsAsync(userId))
            .ReturnsAsync(new List<ProjectResponseDto>());

        // Act
        var result = await _controller.GetUserProjects();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<List<ProjectResponseDto>>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().BeEmpty();
    }

    #endregion

    #region GetProjectById Tests

    [Fact]
    public async Task GetProjectById_ProjectExists_Returns200()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        var projectDto = new ProjectResponseDto
        {
            Id = projectId.ToString(),
            Name = "Test Project",
            Description = "Test Description",
            Role = "Owner",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(s => s.GetProjectByIdAsync(projectId, userId))
            .ReturnsAsync(projectDto);

        // Act
        var result = await _controller.GetProjectById(projectId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<ProjectResponseDto>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().BeEquivalentTo(projectDto);
    }

    [Fact]
    public async Task GetProjectById_ProjectNotFound_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        _projectServiceMock
            .Setup(s => s.GetProjectByIdAsync(projectId, userId))
            .ReturnsAsync((ProjectResponseDto?)null);

        // Act
        var result = await _controller.GetProjectById(projectId);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Project not found.");
    }

    #endregion

    #region UpdateProject Tests

    [Fact]
    public async Task UpdateProject_ValidDto_Returns200()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        var updateDto = new UpdateProjectDto
        {
            Name = "Updated Name",
            Description = "Updated Description"
        };

        var responseDto = new ProjectResponseDto
        {
            Id = projectId.ToString(),
            Name = "Updated Name",
            Description = "Updated Description",
            Role = "Owner",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(s => s.UpdateProjectAsync(projectId, updateDto, userId))
            .ReturnsAsync(responseDto);

        // Act
        var result = await _controller.UpdateProject(projectId, updateDto);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<ProjectResponseDto>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().BeEquivalentTo(responseDto);
        apiResponse.Message.Should().Be("Project updated successfully.");
    }

    [Fact]
    public async Task UpdateProject_ProjectNotFound_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        var updateDto = new UpdateProjectDto
        {
            Name = "Updated Name",
            Description = "Updated Description"
        };

        _projectServiceMock
            .Setup(s => s.UpdateProjectAsync(projectId, updateDto, userId))
            .ReturnsAsync((ProjectResponseDto?)null);

        // Act
        var result = await _controller.UpdateProject(projectId, updateDto);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Project not found.");
    }

    #endregion

    #region DeleteProject Tests

    [Fact]
    public async Task DeleteProject_UserIsOwner_Returns204()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        _projectServiceMock
            .Setup(s => s.DeleteProjectAsync(projectId, userId))
            .ReturnsAsync((true, (string?)null));

        // Act
        var result = await _controller.DeleteProject(projectId);

        // Assert
        var noContentResult = result.Should().BeOfType<NoContentResult>().Subject;
        noContentResult.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task DeleteProject_UserNotOwner_Returns403()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        _projectServiceMock
            .Setup(s => s.DeleteProjectAsync(projectId, userId))
            .ReturnsAsync((false, "Only the project owner can delete the project."));

        // Act
        var result = await _controller.DeleteProject(projectId);

        // Assert
        var forbiddenResult = result.Should().BeOfType<ObjectResult>().Subject;
        forbiddenResult.StatusCode.Should().Be(403);

        var apiResponse = forbiddenResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Only the project owner can delete the project.");
    }

    [Fact]
    public async Task DeleteProject_ProjectNotFound_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        _projectServiceMock
            .Setup(s => s.DeleteProjectAsync(projectId, userId))
            .ReturnsAsync((false, "Project not found."));

        // Act
        var result = await _controller.DeleteProject(projectId);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Project not found.");
    }

    #endregion

    #region AddMember Tests

    [Fact]
    public async Task AddMember_ValidDto_Returns201WithMemberDetails()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToAdd = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        var dto = new AddMemberDto
        {
            UserId = userIdToAdd
        };

        var memberResponse = new MemberResponseDto
        {
            UserId = userIdToAdd.ToString(),
            Name = "Jane Doe",
            Email = "jane@example.com",
            Role = "Member",
            JoinedAt = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(s => s.AddMemberAsync(projectId, dto, requestingUserId))
            .ReturnsAsync((memberResponse, (string?)null));

        // Act
        var result = await _controller.AddMember(projectId, dto);

        // Assert
        var createdResult = result.Should().BeOfType<ObjectResult>().Subject;
        createdResult.StatusCode.Should().Be(201);

        var apiResponse = createdResult.Value.Should().BeOfType<ApiResponse<MemberResponseDto>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().BeEquivalentTo(memberResponse);
        apiResponse.Message.Should().Be("Member added successfully.");
    }

    [Fact]
    public async Task AddMember_UserNotOwner_Returns403()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToAdd = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        var dto = new AddMemberDto
        {
            UserId = userIdToAdd
        };

        _projectServiceMock
            .Setup(s => s.AddMemberAsync(projectId, dto, requestingUserId))
            .ReturnsAsync(((MemberResponseDto?)null, "Only the project owner can add members."));

        // Act
        var result = await _controller.AddMember(projectId, dto);

        // Assert
        var forbiddenResult = result.Should().BeOfType<ObjectResult>().Subject;
        forbiddenResult.StatusCode.Should().Be(403);

        var apiResponse = forbiddenResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Only the project owner can add members.");
    }

    [Fact]
    public async Task AddMember_ProjectNotFound_Returns404()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToAdd = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        var dto = new AddMemberDto
        {
            UserId = userIdToAdd
        };

        _projectServiceMock
            .Setup(s => s.AddMemberAsync(projectId, dto, requestingUserId))
            .ReturnsAsync(((MemberResponseDto?)null, "Project not found."));

        // Act
        var result = await _controller.AddMember(projectId, dto);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Project not found.");
    }

    [Fact]
    public async Task AddMember_UserNotFound_Returns400()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToAdd = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        var dto = new AddMemberDto
        {
            UserId = userIdToAdd
        };

        _projectServiceMock
            .Setup(s => s.AddMemberAsync(projectId, dto, requestingUserId))
            .ReturnsAsync(((MemberResponseDto?)null, "User not found."));

        // Act
        var result = await _controller.AddMember(projectId, dto);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        var apiResponse = badRequestResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("User not found.");
    }

    [Fact]
    public async Task AddMember_UserAlreadyMember_Returns400()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToAdd = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        var dto = new AddMemberDto
        {
            UserId = userIdToAdd
        };

        _projectServiceMock
            .Setup(s => s.AddMemberAsync(projectId, dto, requestingUserId))
            .ReturnsAsync(((MemberResponseDto?)null, "User is already a member of this project."));

        // Act
        var result = await _controller.AddMember(projectId, dto);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        var apiResponse = badRequestResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("User is already a member of this project.");
    }

    #endregion

    #region RemoveMember Tests

    [Fact]
    public async Task RemoveMember_ValidRequest_Returns204()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToRemove = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        _projectServiceMock
            .Setup(s => s.RemoveMemberAsync(projectId, userIdToRemove, requestingUserId))
            .ReturnsAsync((true, (string?)null));

        // Act
        var result = await _controller.RemoveMember(projectId, userIdToRemove);

        // Assert
        var noContentResult = result.Should().BeOfType<NoContentResult>().Subject;
        noContentResult.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task RemoveMember_UserNotOwner_Returns403()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToRemove = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        _projectServiceMock
            .Setup(s => s.RemoveMemberAsync(projectId, userIdToRemove, requestingUserId))
            .ReturnsAsync((false, "Only the project owner can remove members."));

        // Act
        var result = await _controller.RemoveMember(projectId, userIdToRemove);

        // Assert
        var forbiddenResult = result.Should().BeOfType<ObjectResult>().Subject;
        forbiddenResult.StatusCode.Should().Be(403);

        var apiResponse = forbiddenResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Only the project owner can remove members.");
    }

    [Fact]
    public async Task RemoveMember_ProjectNotFound_Returns404()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToRemove = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        _projectServiceMock
            .Setup(s => s.RemoveMemberAsync(projectId, userIdToRemove, requestingUserId))
            .ReturnsAsync((false, "Project not found."));

        // Act
        var result = await _controller.RemoveMember(projectId, userIdToRemove);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Project not found.");
    }

    [Fact]
    public async Task RemoveMember_UserNotMember_Returns404()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToRemove = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        _projectServiceMock
            .Setup(s => s.RemoveMemberAsync(projectId, userIdToRemove, requestingUserId))
            .ReturnsAsync((false, "User is not a member of this project."));

        // Act
        var result = await _controller.RemoveMember(projectId, userIdToRemove);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("User is not a member of this project.");
    }

    [Fact]
    public async Task RemoveMember_LastOwner_Returns400()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToRemove = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        _projectServiceMock
            .Setup(s => s.RemoveMemberAsync(projectId, userIdToRemove, requestingUserId))
            .ReturnsAsync((false, "Cannot remove the last owner from the project."));

        // Act
        var result = await _controller.RemoveMember(projectId, userIdToRemove);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        var apiResponse = badRequestResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Cannot remove the last owner from the project.");
    }

    #endregion

    #region GetProjectMembers Tests

    [Fact]
    public async Task GetProjectMembers_ValidRequest_Returns200WithMemberList()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        var members = new List<MemberResponseDto>
        {
            new()
            {
                UserId = Guid.NewGuid().ToString(),
                Name = "Owner User",
                Email = "owner@example.com",
                Role = "Owner",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-5)
            },
            new()
            {
                UserId = Guid.NewGuid().ToString(),
                Name = "Member User",
                Email = "member@example.com",
                Role = "Member",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-2)
            }
        };

        _projectServiceMock
            .Setup(s => s.GetProjectMembersAsync(projectId, requestingUserId))
            .ReturnsAsync(members);

        // Act
        var result = await _controller.GetProjectMembers(projectId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<List<MemberResponseDto>>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().HaveCount(2);
        apiResponse.Data.Should().BeEquivalentTo(members);
    }

    [Fact]
    public async Task GetProjectMembers_ProjectNotFound_Returns404()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        _projectServiceMock
            .Setup(s => s.GetProjectMembersAsync(projectId, requestingUserId))
            .ReturnsAsync((List<MemberResponseDto>?)null);

        // Act
        var result = await _controller.GetProjectMembers(projectId);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Project not found.");
    }

    [Fact]
    public async Task GetProjectMembers_UserNotMember_Returns404()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        _projectServiceMock
            .Setup(s => s.GetProjectMembersAsync(projectId, requestingUserId))
            .ReturnsAsync((List<MemberResponseDto>?)null);

        // Act
        var result = await _controller.GetProjectMembers(projectId);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Project not found.");
    }

    #endregion

    #region Enhanced AddMember Tests

    [Fact]
    public async Task AddMember_ValidEmail_Returns201()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var userIdToAdd = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        var dto = new AddMemberDto
        {
            Email = "newmember@example.com"
        };

        var memberResponse = new MemberResponseDto
        {
            UserId = userIdToAdd.ToString(),
            Name = "New Member",
            Email = "newmember@example.com",
            Role = "Member",
            JoinedAt = DateTimeOffset.UtcNow
        };

        _projectServiceMock
            .Setup(s => s.AddMemberAsync(projectId, dto, requestingUserId))
            .ReturnsAsync((memberResponse, (string?)null));

        // Act
        var result = await _controller.AddMember(projectId, dto);

        // Assert
        var createdResult = result.Should().BeOfType<ObjectResult>().Subject;
        createdResult.StatusCode.Should().Be(201);

        var apiResponse = createdResult.Value.Should().BeOfType<ApiResponse<MemberResponseDto>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().BeEquivalentTo(memberResponse);
        apiResponse.Message.Should().Be("Member added successfully.");
    }

    [Fact]
    public async Task AddMember_EmailNotFound_Returns400()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        var dto = new AddMemberDto
        {
            Email = "nonexistent@example.com"
        };

        _projectServiceMock
            .Setup(s => s.AddMemberAsync(projectId, dto, requestingUserId))
            .ReturnsAsync(((MemberResponseDto?)null, "No user found with email address: nonexistent@example.com"));

        // Act
        var result = await _controller.AddMember(projectId, dto);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        var apiResponse = badRequestResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("No user found with email address: nonexistent@example.com");
    }

    [Fact]
    public async Task AddMember_BothIdentifiers_Returns400()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        var dto = new AddMemberDto
        {
            UserId = Guid.NewGuid(),
            Email = "user@example.com"
        };

        _projectServiceMock
            .Setup(s => s.AddMemberAsync(projectId, dto, requestingUserId))
            .ReturnsAsync(((MemberResponseDto?)null, "Provide either UserId or Email, not both."));

        // Act
        var result = await _controller.AddMember(projectId, dto);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        var apiResponse = badRequestResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Provide either UserId or Email, not both.");
    }

    [Fact]
    public async Task AddMember_NoIdentifiers_Returns400()
    {
        // Arrange
        var requestingUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(requestingUserId);

        var dto = new AddMemberDto
        {
            UserId = null,
            Email = null
        };

        _projectServiceMock
            .Setup(s => s.AddMemberAsync(projectId, dto, requestingUserId))
            .ReturnsAsync(((MemberResponseDto?)null, "Either UserId or Email is required."));

        // Act
        var result = await _controller.AddMember(projectId, dto);

        // Assert
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequestResult.StatusCode.Should().Be(400);

        var apiResponse = badRequestResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Either UserId or Email is required.");
    }

    #endregion

    #region Helper Methods

    private void SetupUserClaims(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = claimsPrincipal
            }
        };
    }

    #endregion
}
