using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Models.Entities;

public class Asset : BaseEntity
{
    public string FileName { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string? ThumbnailKey { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; }

    public Guid KanbanTaskId { get; set; }
    public KanbanTask KanbanTask { get; set; } = null!;
}
