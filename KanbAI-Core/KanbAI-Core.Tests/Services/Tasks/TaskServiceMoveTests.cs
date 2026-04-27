using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace KanbAI_Core.Tests.Services.Tasks;

public class TaskServiceMoveTests
{
    private readonly Mock<ILogger<TaskService>> _loggerMock;

    public TaskServiceMoveTests()
    {
        _loggerMock = new Mock<ILogger<TaskService>>();
    }

    #region Cross-Column Moves

    [Fact]
    public async Task MoveTaskAsync_DifferentColumn_UpdatesColumnIdAndTaskOrder()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, sourceColumnId, targetColumnId) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = Guid.NewGuid(), Title = "A", TaskOrder = 0, ColumnId = sourceColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "B", TaskOrder = 1, ColumnId = sourceColumnId },
            new KanbanTask { Id = movedTaskId, Title = "C", TaskOrder = 2, ColumnId = sourceColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "X", TaskOrder = 0, ColumnId = targetColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "Y", TaskOrder = 1, ColumnId = targetColumnId });
        await context.SaveChangesAsync();

        var dto = new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 1 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);
        data.Should().NotBeNull();
        data!.ColumnId.Should().Be(targetColumnId.ToString());
        data.TaskOrder.Should().Be(1);

        var movedTask = await context.KanbanTasks.AsNoTracking().SingleAsync(t => t.Id == movedTaskId);
        movedTask.ColumnId.Should().Be(targetColumnId);
        movedTask.TaskOrder.Should().Be(1);
    }

    [Fact]
    public async Task MoveTaskAsync_DifferentColumn_RecalculatesSourceColumnOrders()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, sourceColumnId, targetColumnId) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = Guid.NewGuid(), Title = "A", TaskOrder = 0, ColumnId = sourceColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "B", TaskOrder = 1, ColumnId = sourceColumnId },
            new KanbanTask { Id = movedTaskId, Title = "C", TaskOrder = 2, ColumnId = sourceColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "D", TaskOrder = 3, ColumnId = sourceColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "E", TaskOrder = 4, ColumnId = sourceColumnId });
        await context.SaveChangesAsync();

        var dto = new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 0 };

        // Act
        var (_, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);

        var sourceOrders = await context.KanbanTasks
            .AsNoTracking()
            .Where(t => t.ColumnId == sourceColumnId)
            .OrderBy(t => t.TaskOrder)
            .Select(t => new { t.Title, t.TaskOrder })
            .ToListAsync();

        sourceOrders.Should().HaveCount(4);
        sourceOrders.Select(x => x.TaskOrder).Should().Equal(0, 1, 2, 3);
        sourceOrders.Select(x => x.Title).Should().Equal("A", "B", "D", "E");
    }

    [Fact]
    public async Task MoveTaskAsync_DifferentColumn_RecalculatesTargetColumnOrders()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, sourceColumnId, targetColumnId) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = movedTaskId, Title = "M", TaskOrder = 0, ColumnId = sourceColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "X", TaskOrder = 0, ColumnId = targetColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "Y", TaskOrder = 1, ColumnId = targetColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "Z", TaskOrder = 2, ColumnId = targetColumnId });
        await context.SaveChangesAsync();

        var dto = new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 1 };

        // Act
        var (_, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);

        var targetOrders = await context.KanbanTasks
            .AsNoTracking()
            .Where(t => t.ColumnId == targetColumnId)
            .OrderBy(t => t.TaskOrder)
            .Select(t => new { t.Title, t.TaskOrder })
            .ToListAsync();

        targetOrders.Should().HaveCount(4);
        targetOrders.Select(x => x.TaskOrder).Should().Equal(0, 1, 2, 3);
        targetOrders.Select(x => x.Title).Should().Equal("X", "M", "Y", "Z");
    }

    [Fact]
    public async Task MoveTaskAsync_DifferentColumn_AppendToEnd_AddsAtBoundary()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, sourceColumnId, targetColumnId) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = movedTaskId, Title = "M", TaskOrder = 0, ColumnId = sourceColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "X", TaskOrder = 0, ColumnId = targetColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "Y", TaskOrder = 1, ColumnId = targetColumnId });
        await context.SaveChangesAsync();

        // TaskOrder = 2 is the count of tasks in target column → valid "append" position
        var dto = new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 2 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);
        data!.TaskOrder.Should().Be(2);

        var targetOrders = await context.KanbanTasks
            .AsNoTracking()
            .Where(t => t.ColumnId == targetColumnId)
            .OrderBy(t => t.TaskOrder)
            .Select(t => new { t.Title, t.TaskOrder })
            .ToListAsync();

        targetOrders.Select(x => x.TaskOrder).Should().Equal(0, 1, 2);
        targetOrders.Select(x => x.Title).Should().Equal("X", "Y", "M");
    }

    #endregion

    #region Same-Column Reorder

    [Fact]
    public async Task MoveTaskAsync_SameColumn_MoveUp_RecalculatesOrders()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId, _) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = Guid.NewGuid(), Title = "A", TaskOrder = 0, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "B", TaskOrder = 1, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "C", TaskOrder = 2, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "D", TaskOrder = 3, ColumnId = columnId },
            new KanbanTask { Id = movedTaskId, Title = "E", TaskOrder = 4, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "F", TaskOrder = 5, ColumnId = columnId });
        await context.SaveChangesAsync();

        var dto = new MoveTaskDto { ColumnId = columnId, TaskOrder = 1 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);
        data!.TaskOrder.Should().Be(1);

        var orders = await context.KanbanTasks
            .AsNoTracking()
            .Where(t => t.ColumnId == columnId)
            .OrderBy(t => t.TaskOrder)
            .Select(t => new { t.Title, t.TaskOrder })
            .ToListAsync();

        orders.Select(x => x.TaskOrder).Should().Equal(0, 1, 2, 3, 4, 5);
        orders.Select(x => x.Title).Should().Equal("A", "E", "B", "C", "D", "F");
    }

    [Fact]
    public async Task MoveTaskAsync_SameColumn_MoveDown_RecalculatesOrders()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId, _) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = Guid.NewGuid(), Title = "A", TaskOrder = 0, ColumnId = columnId },
            new KanbanTask { Id = movedTaskId, Title = "B", TaskOrder = 1, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "C", TaskOrder = 2, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "D", TaskOrder = 3, ColumnId = columnId });
        await context.SaveChangesAsync();

        var dto = new MoveTaskDto { ColumnId = columnId, TaskOrder = 3 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);
        data!.TaskOrder.Should().Be(3);

        var orders = await context.KanbanTasks
            .AsNoTracking()
            .Where(t => t.ColumnId == columnId)
            .OrderBy(t => t.TaskOrder)
            .Select(t => new { t.Title, t.TaskOrder })
            .ToListAsync();

        orders.Select(x => x.TaskOrder).Should().Equal(0, 1, 2, 3);
        orders.Select(x => x.Title).Should().Equal("A", "C", "D", "B");
    }

    [Fact]
    public async Task MoveTaskAsync_SameColumn_SamePosition_NoOp()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId, _) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = Guid.NewGuid(), Title = "A", TaskOrder = 0, ColumnId = columnId },
            new KanbanTask { Id = movedTaskId, Title = "B", TaskOrder = 1, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "C", TaskOrder = 2, ColumnId = columnId });
        await context.SaveChangesAsync();

        var dto = new MoveTaskDto { ColumnId = columnId, TaskOrder = 1 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.Success);
        data!.TaskOrder.Should().Be(1);

        var orders = await context.KanbanTasks
            .AsNoTracking()
            .Where(t => t.ColumnId == columnId)
            .OrderBy(t => t.TaskOrder)
            .Select(t => new { t.Title, t.TaskOrder })
            .ToListAsync();

        orders.Select(x => x.TaskOrder).Should().Equal(0, 1, 2);
        orders.Select(x => x.Title).Should().Equal("A", "B", "C");
    }

    #endregion

    #region Authorization & Not-Found Paths

    [Fact]
    public async Task MoveTaskAsync_TaskNotFound_ReturnsTaskNotFound()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId, _) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var dto = new MoveTaskDto { ColumnId = columnId, TaskOrder = 0 };

        // Act
        var (data, result) = await service.MoveTaskAsync(Guid.NewGuid(), dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.TaskNotFound);
        data.Should().BeNull();
    }

    [Fact]
    public async Task MoveTaskAsync_UserNotProjectMember_ReturnsUserNotProjectMember()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var memberId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var (projectId, columnId, _) = await SeedProjectWithTwoColumnsAsync(context, memberId);

        var taskId = Guid.NewGuid();
        context.KanbanTasks.Add(new KanbanTask
        {
            Id = taskId,
            Title = "T",
            TaskOrder = 0,
            ColumnId = columnId
        });
        await context.SaveChangesAsync();

        var dto = new MoveTaskDto { ColumnId = columnId, TaskOrder = 0 };

        // Act
        var (data, result) = await service.MoveTaskAsync(taskId, dto, outsiderId);

        // Assert
        result.Should().Be(MoveTaskResult.UserNotProjectMember);
        data.Should().BeNull();
    }

    [Fact]
    public async Task MoveTaskAsync_TargetColumnNotFound_ReturnsTargetColumnNotFound()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId, _) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var taskId = Guid.NewGuid();
        context.KanbanTasks.Add(new KanbanTask
        {
            Id = taskId,
            Title = "T",
            TaskOrder = 0,
            ColumnId = columnId
        });
        await context.SaveChangesAsync();

        var dto = new MoveTaskDto { ColumnId = Guid.NewGuid(), TaskOrder = 0 };

        // Act
        var (data, result) = await service.MoveTaskAsync(taskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.TargetColumnNotFound);
        data.Should().BeNull();
    }

    [Fact]
    public async Task MoveTaskAsync_CrossProjectMove_ReturnsCrossProjectMove()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectAId, sourceColumnId, _) = await SeedProjectWithTwoColumnsAsync(context, userId);
        var targetColumnInProjectB = await SeedSecondProjectWithColumnAsync(context, userId);

        var taskId = Guid.NewGuid();
        context.KanbanTasks.Add(new KanbanTask
        {
            Id = taskId,
            Title = "T",
            TaskOrder = 0,
            ColumnId = sourceColumnId
        });
        await context.SaveChangesAsync();

        var dto = new MoveTaskDto { ColumnId = targetColumnInProjectB, TaskOrder = 0 };

        // Act
        var (data, result) = await service.MoveTaskAsync(taskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.CrossProjectMove);
        data.Should().BeNull();
    }

    #endregion

    #region TaskOrder Validation

    [Fact]
    public async Task MoveTaskAsync_InvalidTaskOrder_Negative_ReturnsInvalidTaskOrder()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId, _) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var taskId = Guid.NewGuid();
        context.KanbanTasks.Add(new KanbanTask
        {
            Id = taskId,
            Title = "T",
            TaskOrder = 0,
            ColumnId = columnId
        });
        await context.SaveChangesAsync();

        var dto = new MoveTaskDto { ColumnId = columnId, TaskOrder = -1 };

        // Act
        var (data, result) = await service.MoveTaskAsync(taskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.InvalidTaskOrder);
        data.Should().BeNull();
    }

    [Fact]
    public async Task MoveTaskAsync_InvalidTaskOrder_ExceedsTargetColumnCount_ReturnsInvalidTaskOrder()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, sourceColumnId, targetColumnId) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = movedTaskId, Title = "M", TaskOrder = 0, ColumnId = sourceColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "X", TaskOrder = 0, ColumnId = targetColumnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "Y", TaskOrder = 1, ColumnId = targetColumnId });
        await context.SaveChangesAsync();

        // Target column has 2 tasks → valid TaskOrder is 0..2. TaskOrder=3 is invalid.
        var dto = new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 3 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.InvalidTaskOrder);
        data.Should().BeNull();
    }

    [Fact]
    public async Task MoveTaskAsync_InvalidTaskOrder_ExceedsSameColumnCount_ReturnsInvalidTaskOrder()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, columnId, _) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var movedTaskId = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = movedTaskId, Title = "A", TaskOrder = 0, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "B", TaskOrder = 1, ColumnId = columnId },
            new KanbanTask { Id = Guid.NewGuid(), Title = "C", TaskOrder = 2, ColumnId = columnId });
        await context.SaveChangesAsync();

        // Same-column reorder: 3 tasks → valid TaskOrder is 0..2. TaskOrder=3 is invalid.
        var dto = new MoveTaskDto { ColumnId = columnId, TaskOrder = 3 };

        // Act
        var (data, result) = await service.MoveTaskAsync(movedTaskId, dto, userId);

        // Assert
        result.Should().Be(MoveTaskResult.InvalidTaskOrder);
        data.Should().BeNull();
    }

    #endregion

    #region Sequential Invariant

    [Fact]
    public async Task MoveTaskAsync_MultipleSequentialMoves_MaintainsSequentialOrders()
    {
        // Arrange
        var context = CreateInMemoryContext();
        var service = new TaskService(context, _loggerMock.Object);
        var userId = Guid.NewGuid();
        var (projectId, sourceColumnId, targetColumnId) = await SeedProjectWithTwoColumnsAsync(context, userId);

        var task1 = Guid.NewGuid();
        var task2 = Guid.NewGuid();
        var task3 = Guid.NewGuid();
        context.KanbanTasks.AddRange(
            new KanbanTask { Id = task1, Title = "A", TaskOrder = 0, ColumnId = sourceColumnId },
            new KanbanTask { Id = task2, Title = "B", TaskOrder = 1, ColumnId = sourceColumnId },
            new KanbanTask { Id = task3, Title = "C", TaskOrder = 2, ColumnId = sourceColumnId });
        await context.SaveChangesAsync();

        // Act — move three tasks across columns in sequence
        var (_, result1) = await service.MoveTaskAsync(task2, new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 0 }, userId);
        var (_, result2) = await service.MoveTaskAsync(task3, new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 0 }, userId);
        var (_, result3) = await service.MoveTaskAsync(task1, new MoveTaskDto { ColumnId = targetColumnId, TaskOrder = 1 }, userId);

        // Assert
        result1.Should().Be(MoveTaskResult.Success);
        result2.Should().Be(MoveTaskResult.Success);
        result3.Should().Be(MoveTaskResult.Success);

        var sourceOrders = await context.KanbanTasks
            .AsNoTracking()
            .Where(t => t.ColumnId == sourceColumnId)
            .OrderBy(t => t.TaskOrder)
            .Select(t => t.TaskOrder)
            .ToListAsync();
        sourceOrders.Should().BeEmpty();

        var targetOrders = await context.KanbanTasks
            .AsNoTracking()
            .Where(t => t.ColumnId == targetColumnId)
            .OrderBy(t => t.TaskOrder)
            .Select(t => t.TaskOrder)
            .ToListAsync();
        targetOrders.Should().Equal(0, 1, 2);
    }

    #endregion

    #region Helpers

    private static ApplicationDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Guid projectId, Guid columnAId, Guid columnBId)> SeedProjectWithTwoColumnsAsync(
        ApplicationDbContext context,
        Guid memberUserId)
    {
        var projectId = Guid.NewGuid();
        var project = new Project { Id = projectId, Name = "Test Project" };
        context.Projects.Add(project);

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
        var project = new Project { Id = projectId, Name = "Other Project" };
        context.Projects.Add(project);

        // Caller is a member of this second project too, so cross-project is the ONLY reason to fail.
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
