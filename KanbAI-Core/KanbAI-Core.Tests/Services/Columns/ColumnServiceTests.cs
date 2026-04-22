using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Columns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace KanbAI_Core.Tests.Services.Columns;

public class ColumnServiceTests
{
    private readonly Mock<ILogger<ColumnService>> _loggerMock;

    public ColumnServiceTests()
    {
        _loggerMock = new Mock<ILogger<ColumnService>>();
    }

    #region GetProjectColumnsAsync Tests

    [Fact]
    public async Task GetProjectColumnsAsync_UserIsMember_ReturnsColumnsOrderedByColumnOrder()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = projectId, UserId = userId, Role = ProjectRole.Member };
        context.ProjectMembers.Add(member);

        var column1 = new BoardColumn { Name = "Done", ColorCode = "#green", ColumnOrder = 2, ProjectId = projectId };
        var column2 = new BoardColumn { Name = "To Do", ColorCode = "#blue", ColumnOrder = 0, ProjectId = projectId };
        var column3 = new BoardColumn { Name = "In Progress", ColorCode = "#yellow", ColumnOrder = 1, ProjectId = projectId };
        context.BoardColumns.AddRange(column1, column2, column3);

        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectColumnsAsync(projectId, userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result![0].Name.Should().Be("To Do");
        result[0].ColumnOrder.Should().Be(0);
        result[1].Name.Should().Be("In Progress");
        result[1].ColumnOrder.Should().Be(1);
        result[2].Name.Should().Be("Done");
        result[2].ColumnOrder.Should().Be(2);
    }

    [Fact]
    public async Task GetProjectColumnsAsync_UserNotMember_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = projectId, UserId = otherUserId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        var column = new BoardColumn { Name = "Column", ColumnOrder = 0, ProjectId = projectId };
        context.BoardColumns.Add(column);

        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectColumnsAsync(projectId, userId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectColumnsAsync_ProjectDoesNotExist_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var nonExistentProjectId = Guid.NewGuid();

        // Act
        var result = await service.GetProjectColumnsAsync(nonExistentProjectId, userId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetProjectColumnsAsync_NoColumns_ReturnsEmptyList()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = projectId, UserId = userId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        // Act
        var result = await service.GetProjectColumnsAsync(projectId, userId);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    #endregion

    #region CreateColumnAsync Tests

    [Fact]
    public async Task CreateColumnAsync_UserIsMember_CreatesColumn()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = projectId, UserId = userId, Role = ProjectRole.Member };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        var dto = new CreateColumnDto
        {
            Name = "New Column",
            ColorCode = "#ff0000",
            ColumnOrder = 5
        };

        // Act
        var result = await service.CreateColumnAsync(projectId, dto, userId);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("New Column");
        result.ColorCode.Should().Be("#ff0000");
        result.ColumnOrder.Should().Be(5);
        result.ProjectId.Should().Be(projectId.ToString());

        var column = await context.BoardColumns.FirstOrDefaultAsync(c => c.Name == "New Column");
        column.Should().NotBeNull();
        column!.ProjectId.Should().Be(projectId);
    }

    [Fact]
    public async Task CreateColumnAsync_NoOrderProvided_AutoComputesOrder()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = projectId, UserId = userId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        var existingColumn1 = new BoardColumn { Name = "Column 1", ColumnOrder = 0, ProjectId = projectId };
        var existingColumn2 = new BoardColumn { Name = "Column 2", ColumnOrder = 1, ProjectId = projectId };
        var existingColumn3 = new BoardColumn { Name = "Column 3", ColumnOrder = 2, ProjectId = projectId };
        context.BoardColumns.AddRange(existingColumn1, existingColumn2, existingColumn3);

        await context.SaveChangesAsync();

        var dto = new CreateColumnDto
        {
            Name = "Auto-ordered Column",
            ColorCode = null,
            ColumnOrder = null
        };

        // Act
        var result = await service.CreateColumnAsync(projectId, dto, userId);

        // Assert
        result.Should().NotBeNull();
        result!.ColumnOrder.Should().Be(3);
    }

    [Fact]
    public async Task CreateColumnAsync_FirstColumn_OrderIsZero()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = projectId, UserId = userId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        var dto = new CreateColumnDto
        {
            Name = "First Column",
            ColumnOrder = null
        };

        // Act
        var result = await service.CreateColumnAsync(projectId, dto, userId);

        // Assert
        result.Should().NotBeNull();
        result!.ColumnOrder.Should().Be(0);
    }

    [Fact]
    public async Task CreateColumnAsync_UserNotMember_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = projectId, UserId = otherUserId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        await context.SaveChangesAsync();

        var dto = new CreateColumnDto { Name = "Unauthorized Column" };

        // Act
        var result = await service.CreateColumnAsync(projectId, dto, userId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateColumnAsync_ProjectDoesNotExist_ReturnsNull()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var nonExistentProjectId = Guid.NewGuid();

        var dto = new CreateColumnDto { Name = "Column" };

        // Act
        var result = await service.CreateColumnAsync(nonExistentProjectId, dto, userId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region DeleteColumnAsync Tests

    [Fact]
    public async Task DeleteColumnAsync_UserIsMember_DeletesColumn()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = projectId, UserId = userId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        var column = new BoardColumn { Name = "Column to Delete", ColumnOrder = 0, ProjectId = projectId };
        context.BoardColumns.Add(column);

        await context.SaveChangesAsync();

        var columnId = column.Id;

        // Act
        var result = await service.DeleteColumnAsync(columnId, userId);

        // Assert
        result.Should().BeTrue();

        var deletedColumn = await context.BoardColumns.FindAsync(columnId);
        deletedColumn.Should().BeNull();
    }

    [Fact]
    public async Task DeleteColumnAsync_UserNotMember_ReturnsFalse()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

        var member = new ProjectMember { ProjectId = projectId, UserId = otherUserId, Role = ProjectRole.Owner };
        context.ProjectMembers.Add(member);

        var column = new BoardColumn { Name = "Protected Column", ColumnOrder = 0, ProjectId = projectId };
        context.BoardColumns.Add(column);

        await context.SaveChangesAsync();

        var columnId = column.Id;

        // Act
        var result = await service.DeleteColumnAsync(columnId, userId);

        // Assert
        result.Should().BeFalse();

        var stillExistingColumn = await context.BoardColumns.FindAsync(columnId);
        stillExistingColumn.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteColumnAsync_ColumnDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new ColumnService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var nonExistentColumnId = Guid.NewGuid();

        // Act
        var result = await service.DeleteColumnAsync(nonExistentColumnId, userId);

        // Assert
        result.Should().BeFalse();
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
