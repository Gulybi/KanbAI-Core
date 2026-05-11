namespace KanbAI_Core.Services.Tasks;

public enum DeleteTaskResult
{
    Success,
    TaskNotFound,
    UserNotProjectMember,
    UnexpectedError
}
