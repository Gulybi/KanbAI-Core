namespace KanbAI_Core.DTOs;

public record ProjectDeletedEventDto
{
    public required string ProjectId { get; init; }
}
