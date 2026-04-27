namespace KanbAI_Core.DTOs;

using System.ComponentModel.DataAnnotations;

public record CreateTaskDto
{
    [Required(ErrorMessage = "Task title is required.")]
    [MaxLength(200, ErrorMessage = "Task title cannot exceed 200 characters.")]
    public required string Title { get; init; }

    public string? Content { get; init; }

    public Guid? AssignedId { get; init; }
}
