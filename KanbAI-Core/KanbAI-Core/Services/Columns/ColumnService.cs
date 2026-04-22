namespace KanbAI_Core.Services.Columns;

using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class ColumnService : IColumnService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ColumnService> _logger;

    public ColumnService(ApplicationDbContext context, ILogger<ColumnService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<List<ColumnResponseDto>?> GetProjectColumnsAsync(Guid projectId, Guid userId)
    {
        var project = await _context.Projects
            .Include(p => p.Members)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found", projectId);
            return null;
        }

        var isMember = project.Members.Any(m => m.UserId == userId);
        if (!isMember)
        {
            _logger.LogWarning("User {UserId} attempted to access project {ProjectId} without membership", userId, projectId);
            return null;
        }

        var columns = await _context.BoardColumns
            .AsNoTracking()
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.ColumnOrder)
            .ToListAsync();

        _logger.LogInformation("Retrieved {Count} columns for project {ProjectId}", columns.Count, projectId);

        return columns.Select(MapToDto).ToList();
    }

    public async Task<ColumnResponseDto?> CreateColumnAsync(Guid projectId, CreateColumnDto dto, Guid userId)
    {
        var project = await _context.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found", projectId);
            return null;
        }

        var isMember = project.Members.Any(m => m.UserId == userId);
        if (!isMember)
        {
            _logger.LogWarning("User {UserId} attempted to create column in project {ProjectId} without membership", userId, projectId);
            return null;
        }

        int columnOrder = dto.ColumnOrder ?? await ComputeNextColumnOrderAsync(projectId);

        var column = new BoardColumn
        {
            Name = dto.Name,
            ColorCode = dto.ColorCode,
            ColumnOrder = columnOrder,
            ProjectId = projectId
        };

        _context.BoardColumns.Add(column);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} created column {ColumnId} in project {ProjectId}", userId, column.Id, projectId);

        return MapToDto(column);
    }

    public async Task<bool> DeleteColumnAsync(Guid columnId, Guid userId)
    {
        var column = await _context.BoardColumns
            .Include(c => c.Project)
                .ThenInclude(p => p.Members)
            .FirstOrDefaultAsync(c => c.Id == columnId);

        if (column == null)
        {
            _logger.LogWarning("Column {ColumnId} not found", columnId);
            return false;
        }

        var isMember = column.Project.Members.Any(m => m.UserId == userId);
        if (!isMember)
        {
            _logger.LogWarning("User {UserId} attempted to delete column {ColumnId} without project membership", userId, columnId);
            return false;
        }

        _context.BoardColumns.Remove(column);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} deleted column {ColumnId}", userId, columnId);

        return true;
    }

    private async Task<int> ComputeNextColumnOrderAsync(Guid projectId)
    {
        var maxOrder = await _context.BoardColumns
            .Where(c => c.ProjectId == projectId)
            .MaxAsync(c => (int?)c.ColumnOrder);

        return (maxOrder ?? -1) + 1;
    }

    private static ColumnResponseDto MapToDto(BoardColumn column)
    {
        return new ColumnResponseDto
        {
            Id = column.Id.ToString(),
            Name = column.Name,
            ColorCode = column.ColorCode,
            ColumnOrder = column.ColumnOrder,
            ProjectId = column.ProjectId.ToString(),
            CreatedAt = column.CreatedAt,
            UpdatedAt = column.UpdatedAt
        };
    }
}
