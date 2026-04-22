using System.Security.Claims;
using FluentAssertions;
using KanbAI_Core.Controllers;
using KanbAI_Core.DTOs;
using KanbAI_Core.Services.Columns;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;

namespace KanbAI_Core.Tests.Controllers;

public class ColumnControllerTests
{
    private readonly Mock<IColumnService> _columnServiceMock;
    private readonly Mock<ILogger<ColumnController>> _loggerMock;
    private readonly ColumnController _controller;

    public ColumnControllerTests()
    {
        _columnServiceMock = new Mock<IColumnService>();
        _loggerMock = new Mock<ILogger<ColumnController>>();
        _controller = new ColumnController(_columnServiceMock.Object, _loggerMock.Object);
    }

    #region GetProjectColumns Tests

    [Fact]
    public async Task GetProjectColumns_ColumnsExist_Returns200WithList()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        var columns = new List<ColumnResponseDto>
        {
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "To Do",
                ColorCode = "#blue",
                ColumnOrder = 0,
                ProjectId = projectId.ToString(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Done",
                ColorCode = "#green",
                ColumnOrder = 1,
                ProjectId = projectId.ToString(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            }
        };

        _columnServiceMock
            .Setup(s => s.GetProjectColumnsAsync(projectId, userId))
            .ReturnsAsync(columns);

        // Act
        var result = await _controller.GetProjectColumns(projectId);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        okResult.StatusCode.Should().Be(200);

        var apiResponse = okResult.Value.Should().BeOfType<ApiResponse<List<ColumnResponseDto>>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().HaveCount(2);
        apiResponse.Data.Should().BeEquivalentTo(columns);
    }

    [Fact]
    public async Task GetProjectColumns_ProjectNotFound_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        _columnServiceMock
            .Setup(s => s.GetProjectColumnsAsync(projectId, userId))
            .ReturnsAsync((List<ColumnResponseDto>?)null);

        // Act
        var result = await _controller.GetProjectColumns(projectId);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Project not found.");
    }

    #endregion

    #region CreateColumn Tests

    [Fact]
    public async Task CreateColumn_ValidDto_Returns201WithLocationHeader()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        var dto = new CreateColumnDto
        {
            Name = "New Column",
            ColorCode = "#red",
            ColumnOrder = 2
        };

        var responseDto = new ColumnResponseDto
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New Column",
            ColorCode = "#red",
            ColumnOrder = 2,
            ProjectId = projectId.ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _columnServiceMock
            .Setup(s => s.CreateColumnAsync(projectId, dto, userId))
            .ReturnsAsync(responseDto);

        // Act
        var result = await _controller.CreateColumn(projectId, dto);

        // Assert
        var createdResult = result.Should().BeOfType<CreatedAtActionResult>().Subject;
        createdResult.StatusCode.Should().Be(201);
        createdResult.ActionName.Should().Be(nameof(ColumnController.GetProjectColumns));
        createdResult.RouteValues!["projectId"].Should().Be(responseDto.ProjectId);

        var apiResponse = createdResult.Value.Should().BeOfType<ApiResponse<ColumnResponseDto>>().Subject;
        apiResponse.Success.Should().BeTrue();
        apiResponse.Data.Should().BeEquivalentTo(responseDto);
        apiResponse.Message.Should().Be("Column created successfully.");
    }

    [Fact]
    public void CreateColumn_MissingName_ModelStateInvalid()
    {
        // Arrange
        var dto = new CreateColumnDto { Name = "" };

        // Act - Validate the DTO directly
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(dto);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(dto, validationContext, validationResults, true);

        // Assert
        isValid.Should().BeFalse();
        validationResults.Should().Contain(vr => vr.MemberNames.Contains("Name"));
    }

    [Fact]
    public void CreateColumn_NameTooLong_ModelStateInvalid()
    {
        // Arrange
        var dto = new CreateColumnDto { Name = new string('A', 101) };

        // Act - Validate the DTO directly
        var validationContext = new System.ComponentModel.DataAnnotations.ValidationContext(dto);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(dto, validationContext, validationResults, true);

        // Assert
        isValid.Should().BeFalse();
        validationResults.Should().Contain(vr => vr.MemberNames.Contains("Name"));
    }

    [Fact]
    public async Task CreateColumn_ProjectNotFound_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        SetupUserClaims(userId);

        var dto = new CreateColumnDto { Name = "Column" };

        _columnServiceMock
            .Setup(s => s.CreateColumnAsync(projectId, dto, userId))
            .ReturnsAsync((ColumnResponseDto?)null);

        // Act
        var result = await _controller.CreateColumn(projectId, dto);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Project not found.");
    }

    #endregion

    #region DeleteColumn Tests

    [Fact]
    public async Task DeleteColumn_ColumnExists_Returns204()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        SetupUserClaims(userId);

        _columnServiceMock
            .Setup(s => s.DeleteColumnAsync(columnId, userId))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.DeleteColumn(columnId);

        // Assert
        var noContentResult = result.Should().BeOfType<NoContentResult>().Subject;
        noContentResult.StatusCode.Should().Be(204);
    }

    [Fact]
    public async Task DeleteColumn_ColumnNotFound_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var columnId = Guid.NewGuid();
        SetupUserClaims(userId);

        _columnServiceMock
            .Setup(s => s.DeleteColumnAsync(columnId, userId))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.DeleteColumn(columnId);

        // Assert
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        notFoundResult.StatusCode.Should().Be(404);

        var apiResponse = notFoundResult.Value.Should().BeOfType<ApiResponse>().Subject;
        apiResponse.Success.Should().BeFalse();
        apiResponse.Message.Should().Be("Column not found.");
    }

    #endregion

    #region GetCurrentUserId Tests

    [Fact]
    public async Task GetCurrentUserId_MissingClaim_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity())
            }
        };

        var projectId = Guid.NewGuid();

        // Act
        Func<Task> act = async () => await _controller.GetProjectColumns(projectId);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("Invalid or missing user ID in token.");
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
