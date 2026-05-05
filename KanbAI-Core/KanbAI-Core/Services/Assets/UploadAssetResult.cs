namespace KanbAI_Core.Services.Assets;

public enum UploadAssetResult
{
    Success,
    TaskNotFound,
    UserNotAuthorized,
    FileTooLarge,
    InvalidFileType,
    InvalidFileName,
    StorageError
}
