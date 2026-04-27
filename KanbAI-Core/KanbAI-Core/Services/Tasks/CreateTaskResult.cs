namespace KanbAI_Core.Services.Tasks;

public enum CreateTaskResult
{
    Success,
    ColumnNotFound,
    UserNotProjectMember,
    AssignedUserNotFound,
    AssignedUserNotProjectMember,
    InvalidTitle
}
