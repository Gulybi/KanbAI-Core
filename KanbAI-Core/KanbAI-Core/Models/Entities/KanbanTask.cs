namespace KanbAI_Core.Models.Entities;

public class KanbanTask : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string? Content { get; set; }
    public int TaskOrder { get; set; }

    public Guid ColumnId { get; set; }
    public BoardColumn Column { get; set; } = null!;

    public Guid? AssignedId { get; set; }
    public User? AssignedUser { get; set; }

    public ICollection<Asset> Assets { get; set; } = new List<Asset>();
    public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
}
