namespace KanbAI_Core.Models.Entities;

public class TaskComment : BaseEntity
{
    public string Content { get; set; } = string.Empty;

    public Guid KanbanTaskId { get; set; }
    public KanbanTask KanbanTask { get; set; } = null!;

    public Guid AuthorId { get; set; }
    public User Author { get; set; } = null!;
}
