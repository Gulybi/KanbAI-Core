namespace KanbAI_Core.Models.Entities;

public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
}
