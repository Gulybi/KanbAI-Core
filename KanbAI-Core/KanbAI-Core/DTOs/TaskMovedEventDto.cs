namespace KanbAI_Core.DTOs;

public record TaskMovedEventDto
{
    public required string TaskId { get; init; }
    public required string OldColumnId { get; init; }
    public required string NewColumnId { get; init; }
    public required int OldTaskOrder { get; init; }
    public required int NewTaskOrder { get; init; }
    public required TaskResponseDto Task { get; init; }
}
