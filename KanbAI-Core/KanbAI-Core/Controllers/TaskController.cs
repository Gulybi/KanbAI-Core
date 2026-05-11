namespace KanbAI_Core.Controllers;

using KanbAI_Core.DTOs;
using KanbAI_Core.Services.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class TaskController : ControllerBase
{
    private readonly ITaskService _taskService;
    private readonly ILogger<TaskController> _logger;

    public TaskController(ITaskService taskService, ILogger<TaskController> logger)
    {
        _taskService = taskService;
        _logger = logger;
    }

    [HttpPost("column/{columnId}")]
    public async Task<IActionResult> CreateTask(Guid columnId, [FromBody] CreateTaskDto dto)
    {
        var userId = GetCurrentUserId();

        var (data, result) = await _taskService.CreateTaskAsync(columnId, dto, userId);

        return result switch
        {
            CreateTaskResult.Success =>
                CreatedAtAction(
                    nameof(CreateTask),
                    new { columnId = data!.ColumnId },
                    ApiResponse<TaskResponseDto>.Ok(data, "Task created successfully.")),
            CreateTaskResult.ColumnNotFound =>
                NotFound(ApiResponse.Fail("Column not found.")),
            CreateTaskResult.UserNotProjectMember =>
                StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse.Fail("You are not a member of this project.")),
            CreateTaskResult.InvalidTitle =>
                BadRequest(ApiResponse.Fail("Task title is required.")),
            CreateTaskResult.AssignedUserNotFound =>
                BadRequest(ApiResponse.Fail("Assigned user not found.")),
            CreateTaskResult.AssignedUserNotProjectMember =>
                BadRequest(ApiResponse.Fail("Assigned user is not a member of this project.")),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                     ApiResponse.Fail("Unexpected error."))
        };
    }

    [HttpPut("{taskId}/move")]
    public async Task<IActionResult> MoveTask(Guid taskId, [FromBody] MoveTaskDto dto)
    {
        var userId = GetCurrentUserId();

        var (data, result) = await _taskService.MoveTaskAsync(taskId, dto, userId);

        return result switch
        {
            MoveTaskResult.Success =>
                Ok(ApiResponse<TaskResponseDto>.Ok(data!, "Task moved successfully.")),
            MoveTaskResult.TaskNotFound =>
                NotFound(ApiResponse.Fail("Task not found.")),
            MoveTaskResult.UserNotProjectMember =>
                StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse.Fail("You are not a member of this project.")),
            MoveTaskResult.TargetColumnNotFound =>
                NotFound(ApiResponse.Fail("Target column not found.")),
            MoveTaskResult.CrossProjectMove =>
                BadRequest(ApiResponse.Fail("Cannot move task to a column in a different project.")),
            MoveTaskResult.InvalidTaskOrder =>
                BadRequest(ApiResponse.Fail("TaskOrder is invalid.")),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                     ApiResponse.Fail("Unexpected error."))
        };
    }

    [HttpPut("{taskId}/description")]
    public async Task<IActionResult> UpdateTaskDescription(Guid taskId, [FromBody] UpdateTaskDescriptionDto dto)
    {
        var userId = GetCurrentUserId();

        var (data, result) = await _taskService.UpdateTaskDescriptionAsync(taskId, dto, userId);

        return result switch
        {
            UpdateTaskDescriptionResult.Success =>
                Ok(ApiResponse<TaskResponseDto>.Ok(data!, "Task description updated successfully.")),
            UpdateTaskDescriptionResult.TaskNotFound =>
                NotFound(ApiResponse.Fail("Task not found.")),
            UpdateTaskDescriptionResult.UserNotProjectMember =>
                StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse.Fail("You are not a member of this project.")),
            UpdateTaskDescriptionResult.ContentEmpty =>
                BadRequest(ApiResponse.Fail("Task description cannot be empty.")),
            UpdateTaskDescriptionResult.ContentTooLong =>
                BadRequest(ApiResponse.Fail("Task description cannot exceed 10,000 characters.")),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                     ApiResponse.Fail("Unexpected error."))
        };
    }

    [HttpDelete("{taskId}/description")]
    public async Task<IActionResult> ClearTaskDescription(Guid taskId)
    {
        var userId = GetCurrentUserId();

        var (data, result) = await _taskService.ClearTaskDescriptionAsync(taskId, userId);

        return result switch
        {
            ClearTaskDescriptionResult.Success =>
                NoContent(),
            ClearTaskDescriptionResult.TaskNotFound =>
                NotFound(ApiResponse.Fail("Task not found.")),
            ClearTaskDescriptionResult.UserNotProjectMember =>
                StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse.Fail("You are not a member of this project.")),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                     ApiResponse.Fail("Unexpected error."))
        };
    }

    [HttpGet("project/{projectId}")]
    public async Task<IActionResult> GetProjectTasks(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var tasks = await _taskService.GetProjectTasksAsync(projectId, userId);

        if (tasks == null)
        {
            return NotFound(ApiResponse.Fail("Project not found."));
        }

        _logger.LogInformation(
            "User {UserId} retrieved {Count} tasks for project {ProjectId}",
            userId, tasks.Count, projectId);

        return Ok(ApiResponse<List<TaskResponseDto>>.Ok(tasks, "Tasks retrieved successfully."));
    }

    [HttpDelete("{taskId}")]
    public async Task<IActionResult> DeleteTask(Guid taskId)
    {
        var userId = GetCurrentUserId();

        var result = await _taskService.DeleteTaskAsync(taskId, userId);

        return result switch
        {
            DeleteTaskResult.Success =>
                NoContent(),
            DeleteTaskResult.TaskNotFound =>
                NotFound(ApiResponse.Fail("Task not found.")),
            DeleteTaskResult.UserNotProjectMember =>
                StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse.Fail("You are not a member of this project.")),
            DeleteTaskResult.UnexpectedError =>
                StatusCode(StatusCodes.Status500InternalServerError,
                    ApiResponse.Fail("An unexpected error occurred.")),
            _ => StatusCode(StatusCodes.Status500InternalServerError,
                     ApiResponse.Fail("Unexpected error."))
        };
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim in JWT token");
            throw new UnauthorizedAccessException("Invalid or missing user ID in token.");
        }

        return userId;
    }
}
