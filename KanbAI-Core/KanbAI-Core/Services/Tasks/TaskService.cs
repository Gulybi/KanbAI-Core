namespace KanbAI_Core.Services.Tasks;

using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class TaskService : ITaskService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TaskService> _logger;

    public TaskService(ApplicationDbContext context, ILogger<TaskService> logger)
    {
        _context = context;
        _logger = logger;
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

        return (MapToDto(task), CreateTaskResult.Success);
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
