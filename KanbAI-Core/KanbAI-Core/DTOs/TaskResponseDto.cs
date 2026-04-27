namespace KanbAI_Core.DTOs;

public record TaskResponseDto
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Content { get; init; }
    public required int TaskOrder { get; init; }
    public required string ColumnId { get; init; }
    public string? AssignedId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
