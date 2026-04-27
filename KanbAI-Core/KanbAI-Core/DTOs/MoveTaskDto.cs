namespace KanbAI_Core.DTOs;

using System.ComponentModel.DataAnnotations;

public record MoveTaskDto
{
    [Required(ErrorMessage = "Target column ID is required.")]
    public required Guid ColumnId { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "Task order must be non-negative.")]
    public required int TaskOrder { get; init; }
}
