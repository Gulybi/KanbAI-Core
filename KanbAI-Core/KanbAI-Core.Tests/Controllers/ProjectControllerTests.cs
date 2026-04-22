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
