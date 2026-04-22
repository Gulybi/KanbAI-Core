namespace KanbAI_Core.Services.Columns;

using KanbAI_Core.DTOs;

public interface IColumnService
{
    Task<List<ColumnResponseDto>?> GetProjectColumnsAsync(Guid projectId, Guid userId);

    Task<ColumnResponseDto?> CreateColumnAsync(Guid projectId, CreateColumnDto dto, Guid userId);

    Task<bool> DeleteColumnAsync(Guid columnId, Guid userId);
}
