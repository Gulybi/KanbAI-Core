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

    /// <summary>
    /// Moves a task to a new column and/or reorders it within its current column.
    /// Authorization: the caller must be a member of the project that owns the task's current column.
    /// Validation:
    /// - Both the task's current column and the target column must belong to the same project.
    /// - The new TaskOrder must be within the valid range:
    ///   - If moving to a different column: 0 &lt;= TaskOrder &lt;= (target column task count)
    ///   - If reordering within the same column: 0 &lt;= TaskOrder &lt;= (current column task count - 1)
    /// Automatic recalculation:
    /// - If moving to a different column: TaskOrder values in both source and target columns are recalculated to remove gaps.
    /// - If reordering within the same column: TaskOrder values between the old and new positions are adjusted.
    /// </summary>
    /// <param name="taskId">The ID of the task to move.</param>
    /// <param name="dto">Move operation data (target ColumnId and TaskOrder).</param>
    /// <param name="userId">The authenticated user's ID (from JWT claims).</param>
    /// <returns>A tuple containing the updated task (on success) and a <see cref="MoveTaskResult"/> discriminator.</returns>
    Task<(TaskResponseDto? data, MoveTaskResult result)> MoveTaskAsync(
        Guid taskId,
        MoveTaskDto dto,
        Guid userId);

    Task<(TaskResponseDto? data, UpdateTaskDescriptionResult result)> UpdateTaskDescriptionAsync(
        Guid taskId,
        UpdateTaskDescriptionDto dto,
        Guid userId);

    Task<(TaskResponseDto? data, ClearTaskDescriptionResult result)> ClearTaskDescriptionAsync(
        Guid taskId,
        Guid userId);
}
