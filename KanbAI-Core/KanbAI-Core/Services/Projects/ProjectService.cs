namespace KanbAI_Core.Services.Projects;

using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class ProjectService : IProjectService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(ApplicationDbContext context, ILogger<ProjectService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ProjectResponseDto> CreateProjectAsync(CreateProjectDto dto, Guid userId)
    {
        var project = new Project
        {
            Name = dto.Name,
            Description = dto.Description
        };

        _context.Projects.Add(project);

        var projectMember = new ProjectMember
        {
            ProjectId = project.Id,
            UserId = userId,
            Role = ProjectRole.Owner
        };

        _context.ProjectMembers.Add(projectMember);

        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} created project {ProjectId} with name {ProjectName}",
            userId, project.Id, project.Name);

        return MapToDto(project, ProjectRole.Owner);
    }

    public async Task<List<ProjectResponseDto>> GetUserProjectsAsync(Guid userId)
    {
        var projects = await _context.Projects
            .AsNoTracking()
            .Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.UserId == userId))
            .ToListAsync();

        var result = projects.Select(project =>
        {
            var userRole = project.Members.First(m => m.UserId == userId).Role;
            return MapToDto(project, userRole);
        }).ToList();

        _logger.LogInformation("Retrieved {Count} projects for user {UserId}", result.Count, userId);

        return result;
    }

    public async Task<ProjectResponseDto?> GetProjectByIdAsync(Guid projectId, Guid userId)
    {
        var project = await _context.Projects
            .AsNoTracking()
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found", projectId);
            return null;
        }

        var member = project.Members.FirstOrDefault(m => m.UserId == userId);
        if (member == null)
        {
            _logger.LogWarning("User {UserId} attempted to access project {ProjectId} without membership",
                userId, projectId);
            return null;
        }

        return MapToDto(project, member.Role);
    }

    public async Task<ProjectResponseDto?> UpdateProjectAsync(Guid projectId, UpdateProjectDto dto, Guid userId)
    {
        var project = await _context.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found for update", projectId);
            return null;
        }

        var member = project.Members.FirstOrDefault(m => m.UserId == userId);
        if (member == null)
        {
            _logger.LogWarning("User {UserId} attempted to update project {ProjectId} without membership",
                userId, projectId);
            return null;
        }

        project.Name = dto.Name;
        project.Description = dto.Description;

        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} updated project {ProjectId}", userId, projectId);

        return MapToDto(project, member.Role);
    }

    public async Task<(bool isDeleted, string? errorMessage)> DeleteProjectAsync(Guid projectId, Guid userId)
    {
        var project = await _context.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found for deletion", projectId);
            return (false, "Project not found.");
        }

        var member = project.Members.FirstOrDefault(m => m.UserId == userId);
        if (member == null)
        {
            _logger.LogWarning("User {UserId} attempted to delete project {ProjectId} without membership",
                userId, projectId);
            return (false, "Project not found.");
        }

        if (member.Role != ProjectRole.Owner)
        {
            _logger.LogWarning("User {UserId} attempted to delete project {ProjectId} without Owner role",
                userId, projectId);
            return (false, "Only the project owner can delete the project.");
        }

        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} deleted project {ProjectId}", userId, projectId);

        return (true, null);
    }

    private static ProjectResponseDto MapToDto(Project project, ProjectRole userRole)
    {
        return new ProjectResponseDto
        {
            Id = project.Id.ToString(),
            Name = project.Name,
            Description = project.Description,
            Role = userRole.ToString(),
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt
        };
    }
}
