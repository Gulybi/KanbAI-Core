namespace KanbAI_Core.Services.Tasks;

public enum MoveTaskResult
{
    Success,
    TaskNotFound,
    UserNotProjectMember,
    TargetColumnNotFound,
    CrossProjectMove,
    InvalidTaskOrder
}
