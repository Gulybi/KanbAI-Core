namespace KanbAI_Core.Services.Tasks;

public enum UpdateTaskDescriptionResult
{
    Success,
    TaskNotFound,
    UserNotProjectMember,
    ContentEmpty,
    ContentTooLong
}
