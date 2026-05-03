namespace KanbAI_Core.DTOs;

public record ColumnDeletedEventDto
{
    public required string ColumnId { get; init; }
    public required string ProjectId { get; init; }
}
