namespace KanbAI_Core.Models.Entities;

public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public ICollection<BoardColumn> Columns { get; set; } = new List<BoardColumn>();
}
