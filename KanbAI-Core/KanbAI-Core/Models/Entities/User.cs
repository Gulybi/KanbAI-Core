using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Models.Entities;

public class User : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    public ICollection<ProjectMember> ProjectMemberships { get; set; } = new List<ProjectMember>();
    public ICollection<KanbanTask> AssignedTasks { get; set; } = new List<KanbanTask>();
    public ICollection<TaskComment> AuthoredComments { get; set; } = new List<TaskComment>();
}
