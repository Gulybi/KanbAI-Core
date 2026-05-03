namespace KanbAI_Core.DTOs;

public record ProjectUpdatedEventDto
{
    public required string ProjectId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
