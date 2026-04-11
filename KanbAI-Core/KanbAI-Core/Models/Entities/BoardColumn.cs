namespace KanbAI_Core.Models.Entities;

public class BoardColumn : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ColorCode { get; set; }
    public int ColumnOrder { get; set; }

    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public ICollection<KanbanTask> Tasks { get; set; } = new List<KanbanTask>();
}
