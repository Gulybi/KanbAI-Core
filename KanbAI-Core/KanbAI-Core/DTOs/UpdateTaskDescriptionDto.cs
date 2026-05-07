namespace KanbAI_Core.DTOs;

public record UpdateTaskDescriptionDto
{
    public required string Content { get; init; }
}
