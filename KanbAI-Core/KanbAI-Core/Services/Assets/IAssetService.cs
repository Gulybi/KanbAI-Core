namespace KanbAI_Core.Services.Assets;

using KanbAI_Core.DTOs;

public interface IAssetService
{
    Task<(AssetResponseDto? data, UploadAssetResult result)> UploadAssetAsync(
        Guid taskId,
        Stream fileStream,
        string fileName,
        string contentType,
        long fileSize,
        Guid userId,
        CancellationToken cancellationToken = default);
}
