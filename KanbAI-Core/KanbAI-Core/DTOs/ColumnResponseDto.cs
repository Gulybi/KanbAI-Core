namespace KanbAI_Core.DTOs;

public record ColumnResponseDto
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ColorCode { get; init; }
    public required int ColumnOrder { get; init; }
    public required string ProjectId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}
