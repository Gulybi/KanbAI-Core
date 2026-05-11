namespace KanbAI_Core.DTOs;

public record TaskDeletedEventDto
{
    public required string TaskId { get; init; }
    public required string ColumnId { get; init; }
}
