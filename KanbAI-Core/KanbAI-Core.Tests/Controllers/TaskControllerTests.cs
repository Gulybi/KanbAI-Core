using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using FluentAssertions;
using KanbAI_Core.Controllers;
using KanbAI_Core.DTOs;
using KanbAI_Core.Services.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace KanbAI_Core.Tests.Controllers;

public class TaskControllerTests
{
    private readonly Mock<ITaskService> _taskServiceMock;
    private readonly Mock<ILogger<TaskController>> _loggerMock;
    private readonly TaskController _controller;

    public TaskControllerTests()
    {
        _taskServiceMock = new Mock<ITaskService>();
        _loggerMock = new Mock<ILogger<TaskController>>();
        _controller = new TaskController(_taskServiceMock.Object, _loggerMock.Object);
    }

    #region CreateTask HTTP Mapping Tests

    [Fact]
    public async Task CreateTask_ServiceReturnsSuccess_Returns201CreatedWithLocationHeader()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        SetupUserClaims(userId);

        var dto = new CreateTaskDto { Title = "New Task" };
        var responseDto = new TaskResponseDto
        {
            Id = Guid.NewGuid().ToString(),
            Title = "New Task",
            Content = null,
            TaskOrder = 0,
            ColumnId = columnId.ToString(),
            AssignedId = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _taskServiceMock
            .Setup(s => s.CreateTaskAsync(columnId, dto, userId))
            .ReturnsAsync((responseDto, CreateTaskResult.Success));

        // Act
        var result = await _controller.CreateTask(columnId, dto);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
        createdResult.ActionName.Should().Be(nameof(TaskController.CreateTask));
        createdResult.RouteValues!["columnId"].Should().Be(responseDto.ColumnId);

        var apiResponse = createdResult.Value.Should().BeOfType<ApiResponse<TaskResponseDto>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Message.Should().Be("Task created successfully.");
        apiResponse.Data.Should().BeEquivalentTo(responseDto);
    }

    [Fact]
    public async Task CreateTask_ServiceReturnsColumnNotFound_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        SetupUserClaims(userId);

        var dto = new CreateTaskDto { Title = "Task" };

        _taskServiceMock
            .Setup(s => s.CreateTaskAsync(columnId, dto, userId))
            .ReturnsAsync((null, CreateTaskResult.ColumnNotFound));

        // Act
        var result = await _controller.CreateTask(columnId, dto);

        // Assert
        var notFound = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(404);

        var apiResponse = notFound.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Column not found.");
    }

    [Fact]
    public async Task CreateTask_ServiceReturnsUserNotProjectMember_Returns403()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        SetupUserClaims(userId);

        var dto = new CreateTaskDto { Title = "Task" };

        _taskServiceMock
            .Setup(s => s.CreateTaskAsync(columnId, dto, userId))
            .ReturnsAsync((null, CreateTaskResult.UserNotProjectMember));

        // Act
        var result = await _controller.CreateTask(columnId, dto);

        // Assert
        var objectResult = result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var apiResponse = objectResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("You are not a member of this project.");
    }

    [Fact]
    public async Task CreateTask_ServiceReturnsAssignedUserNotFound_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        SetupUserClaims(userId);

        var dto = new CreateTaskDto { Title = "Task", AssignedId = Guid.NewGuid() };

        _taskServiceMock
            .Setup(s => s.CreateTaskAsync(columnId, dto, userId))
            .ReturnsAsync((null, CreateTaskResult.AssignedUserNotFound));

        // Act
        var result = await _controller.CreateTask(columnId, dto);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);

        var apiResponse = badRequest.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Assigned user not found.");
    }

    [Fact]
    public async Task CreateTask_ServiceReturnsAssignedUserNotProjectMember_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        SetupUserClaims(userId);

        var dto = new CreateTaskDto { Title = "Task", AssignedId = Guid.NewGuid() };

        _taskServiceMock
            .Setup(s => s.CreateTaskAsync(columnId, dto, userId))
            .ReturnsAsync((null, CreateTaskResult.AssignedUserNotProjectMember));

        // Act
        var result = await _controller.CreateTask(columnId, dto);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);

        var apiResponse = badRequest.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Assigned user is not a member of this project.");
    }

    [Fact]
    public async Task CreateTask_ServiceReturnsInvalidTitle_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        SetupUserClaims(userId);

        var dto = new CreateTaskDto { Title = "   " };

        _taskServiceMock
            .Setup(s => s.CreateTaskAsync(columnId, dto, userId))
            .ReturnsAsync((null, CreateTaskResult.InvalidTitle));

        // Act
        var result = await _controller.CreateTask(columnId, dto);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        badRequest.StatusCode.Should().Be(400);

        var apiResponse = badRequest.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Task title is required.");
    }

    #endregion

    #region Claims Extraction Tests

    [Fact]
    public async Task CreateTask_MissingNameIdentifierClaim_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };

        var columnId = Guid.NewGuid();
        var dto = new CreateTaskDto { Title = "Task" };

        // Act
        Func<Task> act = async () => await _controller.CreateTask(columnId, dto);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or missing user ID in token.");
    }

    [Fact]
    public async Task CreateTask_InvalidNameIdentifierClaim_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "not-a-guid")
        };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };

        var columnId = Guid.NewGuid();
        var dto = new CreateTaskDto { Title = "Task" };

        // Act
        Func<Task> act = async () => await _controller.CreateTask(columnId, dto);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or missing user ID in token.");
    }

    #endregion

    #region DTO Validation Tests

    [Fact]
    public void CreateTaskDto_MissingTitle_FailsDataAnnotationsValidation()
    {
        // Arrange
        var dto = new CreateTaskDto { Title = "" };

        // Act
        var validationContext = new ValidationContext(dto);
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, validationContext, validationResults, true);

        // Assert
        isValid.Should().BeFalse();
        validationResults.Should().Contain(vr => vr.MemberNames.Contains("Title"));
    }

    [Fact]
    public void CreateTaskDto_TitleExceeds200Chars_FailsDataAnnotationsValidation()
    {
        // Arrange
        var dto = new CreateTaskDto { Title = new string('A', 201) };

        // Act
        var validationContext = new ValidationContext(dto);
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, validationContext, validationResults, true);

        // Assert
        isValid.Should().BeFalse();
        validationResults.Should().Contain(vr => vr.MemberNames.Contains("Title"));
    }

    [Fact]
    public void CreateTaskDto_TitleAtBoundary_PassesDataAnnotationsValidation()
    {
        // Arrange
        var dto = new CreateTaskDto { Title = new string('A', 200) };

        // Act
        var validationContext = new ValidationContext(dto);
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(dto, validationContext, validationResults, true);

        // Assert
        isValid.Should().BeTrue();
        validationResults.Should().BeEmpty();
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
