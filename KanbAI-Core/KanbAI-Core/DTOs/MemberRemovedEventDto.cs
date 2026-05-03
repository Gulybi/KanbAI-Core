namespace KanbAI_Core.DTOs;

public record MemberRemovedEventDto
{
    public required string UserId { get; init; }
    public required string ProjectId { get; init; }
}
