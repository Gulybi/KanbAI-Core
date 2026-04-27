namespace KanbAI_Core.Services.Tasks;

using KanbAI_Core.DTOs;

public interface ITaskService
{
    /// <summary>
    /// Creates a new task in the specified column.
    /// Authorization: the caller must be a member of the project that owns the column.
    /// If <paramref name="dto"/>.AssignedId is provided, it must reference a user who is a member of the same project.
    /// The new task's TaskOrder is automatically set to (max existing TaskOrder in column) + 1, or 0 if the column is empty.
    /// </summary>
    /// <param name="columnId">The target column ID.</param>
    /// <param name="dto">Task creation data.</param>
    /// <param name="userId">The authenticated user's ID (from JWT claims).</param>
    /// <returns>A tuple containing the created task (on success) and a <see cref="CreateTaskResult"/> discriminator.</returns>
    Task<(TaskResponseDto? data, CreateTaskResult result)> CreateTaskAsync(
        Guid columnId,
        CreateTaskDto dto,
        Guid userId);
}
