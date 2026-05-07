namespace KanbAI_Core.Services.Tasks;

using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Hubs;
using KanbAI_Core.Models.Entities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class TaskService : ITaskService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TaskService> _logger;
    private readonly IHubContext<KanbanHub> _hubContext;

    public TaskService(
        ApplicationDbContext context,
        ILogger<TaskService> logger,
        IHubContext<KanbanHub> hubContext)
    {
        _context = context;
        _logger = logger;
        _hubContext = hubContext;
    }

    public async Task<(TaskResponseDto? data, CreateTaskResult result)> CreateTaskAsync(
        Guid columnId,
        CreateTaskDto dto,
        Guid userId)
    {
        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            return (null, CreateTaskResult.InvalidTitle);
        }

        var column = await _context.BoardColumns
            .Include(c => c.Project)
                .ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(c => c.Id == columnId);

        if (column == null)
        {
            _logger.LogWarning("Column {ColumnId} not found", columnId);
            return (null, CreateTaskResult.ColumnNotFound);
        }

        var projectMembers = column.Project.Members;

        if (!projectMembers.Any(m => m.UserId == userId))
        {
            _logger.LogWarning(
                "User {UserId} attempted to create a task in column {ColumnId} without project membership",
                userId, columnId);
            return (null, CreateTaskResult.UserNotProjectMember);
        }

        if (dto.AssignedId.HasValue)
        {
            var assignedId = dto.AssignedId.Value;

            var assignedUserExists = await _context.Users
                .AsNoTracking()
                .AnyAsync(u => u.Id == assignedId);

            if (!assignedUserExists)
            {
                _logger.LogWarning("Assigned user {AssignedId} not found", assignedId);
                return (null, CreateTaskResult.AssignedUserNotFound);
            }

            if (!projectMembers.Any(m => m.UserId == assignedId))
            {
                _logger.LogWarning(
                    "Assigned user {AssignedId} is not a member of project {ProjectId}",
                    assignedId, column.ProjectId);
                return (null, CreateTaskResult.AssignedUserNotProjectMember);
            }
        }

        var maxOrder = await _context.KanbanTasks
            .Where(t => t.ColumnId == columnId)
            .MaxAsync(t => (int?)t.TaskOrder);

        var taskOrder = (maxOrder ?? -1) + 1;

        var task = new KanbanTask
        {
            Title = dto.Title,
            Content = dto.Content,
            TaskOrder = taskOrder,
            ColumnId = columnId,
            AssignedId = dto.AssignedId
        };

        _context.KanbanTasks.Add(task);
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} created task {TaskId} in column {ColumnId} at order {TaskOrder}",
            userId, task.Id, columnId, task.TaskOrder);

        var payload = MapToDto(task);
        await BroadcastAsync(
            BuildProjectGroupName(column.ProjectId),
            "TaskCreated",
            payload);

        return (payload, CreateTaskResult.Success);
    }

    public async Task<(TaskResponseDto? data, MoveTaskResult result)> MoveTaskAsync(
        Guid taskId,
        MoveTaskDto dto,
        Guid userId)
    {
        if (dto.TaskOrder < 0)
        {
            _logger.LogWarning("Negative TaskOrder {TaskOrder} for task {TaskId}", dto.TaskOrder, taskId);
            return (null, MoveTaskResult.InvalidTaskOrder);
        }

        var task = await _context.KanbanTasks
            .Include(t => t.Column)
                .ThenInclude(c => c.Project)
                    .ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null)
        {
            _logger.LogWarning("Task {TaskId} not found", taskId);
            return (null, MoveTaskResult.TaskNotFound);
        }

        if (!task.Column.Project.Members.Any(m => m.UserId == userId))
        {
            _logger.LogWarning(
                "User {UserId} attempted to move task {TaskId} without project membership",
                userId, taskId);
            return (null, MoveTaskResult.UserNotProjectMember);
        }

        var isSameColumn = dto.ColumnId == task.ColumnId;

        var originalColumnId = task.ColumnId;
        var originalTaskOrder = task.TaskOrder;
        var projectId = task.Column.ProjectId;

        if (!isSameColumn)
        {
            var targetColumn = await _context.BoardColumns
                .Include(c => c.Project)
                .FirstOrDefaultAsync(c => c.Id == dto.ColumnId);

            if (targetColumn == null)
            {
                _logger.LogWarning("Target column {ColumnId} not found", dto.ColumnId);
                return (null, MoveTaskResult.TargetColumnNotFound);
            }

            if (targetColumn.ProjectId != task.Column.ProjectId)
            {
                _logger.LogWarning(
                    "User {UserId} attempted to move task {TaskId} to a column in a different project",
                    userId, taskId);
                return (null, MoveTaskResult.CrossProjectMove);
            }
        }

        if (!isSameColumn)
        {
            var targetColumnTaskCount = await _context.KanbanTasks
                .CountAsync(t => t.ColumnId == dto.ColumnId);

            if (dto.TaskOrder > targetColumnTaskCount)
            {
                _logger.LogWarning(
                    "Invalid TaskOrder {TaskOrder} for target column {ColumnId} with {Count} tasks",
                    dto.TaskOrder, dto.ColumnId, targetColumnTaskCount);
                return (null, MoveTaskResult.InvalidTaskOrder);
            }
        }
        else
        {
            var currentColumnTaskCount = await _context.KanbanTasks
                .CountAsync(t => t.ColumnId == task.ColumnId);

            if (dto.TaskOrder > currentColumnTaskCount - 1)
            {
                _logger.LogWarning(
                    "Invalid TaskOrder {TaskOrder} for same-column reorder with {Count} tasks",
                    dto.TaskOrder, currentColumnTaskCount);
                return (null, MoveTaskResult.InvalidTaskOrder);
            }
        }

        if (isSameColumn && dto.TaskOrder == task.TaskOrder)
        {
            _logger.LogInformation(
                "Task {TaskId} is already at position {TaskOrder} in column {ColumnId} - no changes needed",
                taskId, dto.TaskOrder, dto.ColumnId);
            return (MapToDto(task), MoveTaskResult.Success);
        }

        if (!isSameColumn)
        {
            var oldColumnId = task.ColumnId;
            var oldTaskOrder = task.TaskOrder;

            var sourceColumnTasks = await _context.KanbanTasks
                .Where(t => t.ColumnId == oldColumnId && t.TaskOrder > oldTaskOrder)
                .ToListAsync();

            foreach (var t in sourceColumnTasks)
            {
                t.TaskOrder -= 1;
            }

            var targetColumnTasks = await _context.KanbanTasks
                .Where(t => t.ColumnId == dto.ColumnId && t.TaskOrder >= dto.TaskOrder)
                .ToListAsync();

            foreach (var t in targetColumnTasks)
            {
                t.TaskOrder += 1;
            }

            task.ColumnId = dto.ColumnId;
            task.TaskOrder = dto.TaskOrder;
        }
        else
        {
            var oldTaskOrder = task.TaskOrder;
            var newTaskOrder = dto.TaskOrder;

            if (newTaskOrder < oldTaskOrder)
            {
                var affectedTasks = await _context.KanbanTasks
                    .Where(t => t.ColumnId == task.ColumnId
                                && t.TaskOrder >= newTaskOrder
                                && t.TaskOrder < oldTaskOrder)
                    .ToListAsync();

                foreach (var t in affectedTasks)
                {
                    t.TaskOrder += 1;
                }
            }
            else
            {
                var affectedTasks = await _context.KanbanTasks
                    .Where(t => t.ColumnId == task.ColumnId
                                && t.TaskOrder > oldTaskOrder
                                && t.TaskOrder <= newTaskOrder)
                    .ToListAsync();

                foreach (var t in affectedTasks)
                {
                    t.TaskOrder -= 1;
                }
            }

            task.TaskOrder = newTaskOrder;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} moved task {TaskId} to column {ColumnId} at order {TaskOrder}",
            userId, taskId, task.ColumnId, task.TaskOrder);

        var payload = MapToDto(task);
        var eventPayload = new TaskMovedEventDto
        {
            TaskId = task.Id.ToString(),
            OldColumnId = originalColumnId.ToString(),
            NewColumnId = task.ColumnId.ToString(),
            OldTaskOrder = originalTaskOrder,
            NewTaskOrder = task.TaskOrder,
            Task = payload
        };
        await BroadcastAsync(
            BuildProjectGroupName(projectId),
            "TaskMoved",
            eventPayload);

        return (payload, MoveTaskResult.Success);
    }

    public async Task<(TaskResponseDto? data, UpdateTaskDescriptionResult result)> UpdateTaskDescriptionAsync(
        Guid taskId,
        UpdateTaskDescriptionDto dto,
        Guid userId)
    {
        var trimmedContent = dto.Content?.Trim();

        if (string.IsNullOrWhiteSpace(trimmedContent))
        {
            return (null, UpdateTaskDescriptionResult.ContentEmpty);
        }

        if (trimmedContent.Length > 10_000)
        {
            return (null, UpdateTaskDescriptionResult.ContentTooLong);
        }

        var task = await _context.KanbanTasks
            .Include(t => t.Column)
                .ThenInclude(c => c.Project)
                    .ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null)
        {
            _logger.LogInformation("User {UserId} attempted to update description for non-existent task {TaskId}", userId, taskId);
            return (null, UpdateTaskDescriptionResult.TaskNotFound);
        }

        if (!task.Column.Project.Members.Any(m => m.UserId == userId))
        {
            _logger.LogWarning(
                "User {UserId} attempted to update description for task {TaskId} in project {ProjectId} without authorization",
                userId, taskId, task.Column.ProjectId);
            return (null, UpdateTaskDescriptionResult.UserNotProjectMember);
        }

        task.Content = trimmedContent;
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} updated description for task {TaskId} in project {ProjectId}",
            userId, taskId, task.Column.ProjectId);

        var payload = MapToDto(task);
        await BroadcastAsync(
            BuildProjectGroupName(task.Column.ProjectId),
            "TaskUpdated",
            payload);

        return (payload, UpdateTaskDescriptionResult.Success);
    }

    public async Task<(TaskResponseDto? data, ClearTaskDescriptionResult result)> ClearTaskDescriptionAsync(
        Guid taskId,
        Guid userId)
    {
        var task = await _context.KanbanTasks
            .Include(t => t.Column)
                .ThenInclude(c => c.Project)
                    .ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(t => t.Id == taskId);

        if (task == null)
        {
            _logger.LogInformation("User {UserId} attempted to clear description for non-existent task {TaskId}", userId, taskId);
            return (null, ClearTaskDescriptionResult.TaskNotFound);
        }

        if (!task.Column.Project.Members.Any(m => m.UserId == userId))
        {
            _logger.LogWarning(
                "User {UserId} attempted to clear description for task {TaskId} in project {ProjectId} without authorization",
                userId, taskId, task.Column.ProjectId);
            return (null, ClearTaskDescriptionResult.UserNotProjectMember);
        }

        task.Content = null;
        await _context.SaveChangesAsync();

        _logger.LogInformation(
            "User {UserId} cleared description for task {TaskId} in project {ProjectId}",
            userId, taskId, task.Column.ProjectId);

        var payload = MapToDto(task);
        await BroadcastAsync(
            BuildProjectGroupName(task.Column.ProjectId),
            "TaskUpdated",
            payload);

        return (payload, ClearTaskDescriptionResult.Success);
    }

    private static string BuildProjectGroupName(Guid projectId) =>
        $"project_{projectId.ToString().ToLowerInvariant()}";

    private async Task BroadcastAsync(string groupName, string eventName, object payload)
    {
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync(eventName, payload);
            _logger.LogInformation(
                "Broadcast {EventName} event to group {GroupName}",
                eventName, groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to broadcast {EventName} event to group {GroupName}",
                eventName, groupName);
        }
    }

    private static TaskResponseDto MapToDto(KanbanTask task) =>
        new()
        {
            Id = task.Id.ToString(),
            Title = task.Title,
            Content = task.Content,
            TaskOrder = task.TaskOrder,
            ColumnId = task.ColumnId.ToString(),
            AssignedId = task.AssignedId?.ToString(),
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
}
