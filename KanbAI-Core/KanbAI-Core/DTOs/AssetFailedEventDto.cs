namespace KanbAI_Core.DTOs;

public record AssetFailedEventDto
{
    public required string AssetId { get; init; }
    public required string TaskId { get; init; }
    public required string ErrorMessage { get; init; }
}
