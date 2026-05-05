namespace KanbAI_Core.DTOs;

using KanbAI_Core.Models.Enums;

public record AssetResponseDto
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string StorageKey { get; init; }
    public string? ThumbnailKey { get; init; }
    public required string MimeType { get; init; }
    public required long FileSize { get; init; }
    public required ProcessingStatus ProcessingStatus { get; init; }
    public required string KanbanTaskId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
