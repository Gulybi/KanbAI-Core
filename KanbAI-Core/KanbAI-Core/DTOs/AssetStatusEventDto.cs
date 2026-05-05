namespace KanbAI_Core.DTOs;

using KanbAI_Core.Models.Enums;

public record AssetStatusEventDto
{
    public required string AssetId { get; init; }
    public required string TaskId { get; init; }
    public required string FileName { get; init; }
    public required ProcessingStatus ProcessingStatus { get; init; }
}
