namespace KanbAI_Core.Services.Projects;

using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Hubs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class ProjectService : IProjectService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ProjectService> _logger;
    private readonly IHubContext<KanbanHub> _hubContext;

    public ProjectService(
        ApplicationDbContext context,
        ILogger<ProjectService> logger,
        IHubContext<KanbanHub> hubContext)
    {
        _context = context;
        _logger = logger;
        _hubContext = hubContext;
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

        var payload = MapToDto(project, member.Role);

        await BroadcastAsync(
            BuildProjectGroupName(projectId),
            "ProjectUpdated",
            new ProjectUpdatedEventDto
            {
                ProjectId = projectId.ToString(),
                Name = project.Name,
                Description = project.Description,
                UpdatedAt = project.UpdatedAt
            });

        return payload;
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

        await BroadcastAsync(
            BuildProjectGroupName(projectId),
            "ProjectDeleted",
            new ProjectDeletedEventDto { ProjectId = projectId.ToString() });

        return (true, null);
    }

    public async Task<(MemberResponseDto? member, string? errorMessage)> AddMemberAsync(
        Guid projectId,
        Guid userIdToAdd,
        Guid requestingUserId)
    {
        var project = await _context.Projects
            .Include(p => p.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found for add member operation", projectId);
            return (null, "Project not found.");
        }

        var requestingMember = project.Members.FirstOrDefault(m => m.UserId == requestingUserId);
        if (requestingMember == null)
        {
            _logger.LogWarning("User {UserId} attempted to add member to project {ProjectId} without membership",
                requestingUserId, projectId);
            return (null, "Project not found.");
        }

        if (requestingMember.Role != ProjectRole.Owner)
        {
            _logger.LogWarning("User {UserId} attempted to add member to project {ProjectId} without Owner role",
                requestingUserId, projectId);
            return (null, "Only the project owner can add members.");
        }

        var userToAdd = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userIdToAdd);

        if (userToAdd == null)
        {
            _logger.LogWarning("User {UserId} not found for add member operation", userIdToAdd);
            return (null, "User not found.");
        }

        if (project.Members.Any(m => m.UserId == userIdToAdd))
        {
            _logger.LogWarning("User {UserId} is already a member of project {ProjectId}",
                userIdToAdd, projectId);
            return (null, "User is already a member of this project.");
        }

        var newMember = new ProjectMember
        {
            ProjectId = projectId,
            UserId = userIdToAdd,
            Role = ProjectRole.Member
        };

        _context.ProjectMembers.Add(newMember);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {RequestingUserId} added user {UserId} as Member to project {ProjectId}",
            requestingUserId, userIdToAdd, projectId);

        var payload = MapToMemberDto(newMember, userToAdd);

        await BroadcastAsync(
            BuildProjectGroupName(projectId),
            "MemberAdded",
            payload);

        return (payload, null);
    }

    public async Task<(bool isRemoved, string? errorMessage)> RemoveMemberAsync(
        Guid projectId,
        Guid userIdToRemove,
        Guid requestingUserId)
    {
        var project = await _context.Projects
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found for remove member operation", projectId);
            return (false, "Project not found.");
        }

        var requestingMember = project.Members.FirstOrDefault(m => m.UserId == requestingUserId);
        if (requestingMember == null)
        {
            _logger.LogWarning("User {UserId} attempted to remove member from project {ProjectId} without membership",
                requestingUserId, projectId);
            return (false, "Project not found.");
        }

        if (requestingMember.Role != ProjectRole.Owner)
        {
            _logger.LogWarning("User {UserId} attempted to remove member from project {ProjectId} without Owner role",
                requestingUserId, projectId);
            return (false, "Only the project owner can remove members.");
        }

        var memberToRemove = project.Members.FirstOrDefault(m => m.UserId == userIdToRemove);
        if (memberToRemove == null)
        {
            _logger.LogWarning("User {UserId} is not a member of project {ProjectId}",
                userIdToRemove, projectId);
            return (false, "User is not a member of this project.");
        }

        if (memberToRemove.Role == ProjectRole.Owner)
        {
            var ownerCount = project.Members.Count(m => m.Role == ProjectRole.Owner);
            if (ownerCount == 1)
            {
                _logger.LogWarning("User {UserId} attempted to remove the last owner from project {ProjectId}",
                    requestingUserId, projectId);
                return (false, "Cannot remove the last owner from the project.");
            }
        }

        _context.ProjectMembers.Remove(memberToRemove);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {RequestingUserId} removed user {UserId} from project {ProjectId}",
            requestingUserId, userIdToRemove, projectId);

        await BroadcastAsync(
            BuildProjectGroupName(projectId),
            "MemberRemoved",
            new MemberRemovedEventDto
            {
                UserId = userIdToRemove.ToString(),
                ProjectId = projectId.ToString()
            });

        return (true, null);
    }

    public async Task<List<MemberResponseDto>?> GetProjectMembersAsync(
        Guid projectId,
        Guid requestingUserId)
    {
        var project = await _context.Projects
            .AsNoTracking()
            .Include(p => p.Members)
                .ThenInclude(m => m.User)
            .FirstOrDefaultAsync(p => p.Id == projectId);

        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found for get members operation", projectId);
            return null;
        }

        var requestingMember = project.Members.FirstOrDefault(m => m.UserId == requestingUserId);
        if (requestingMember == null)
        {
            _logger.LogWarning("User {UserId} attempted to get members for project {ProjectId} without membership",
                requestingUserId, projectId);
            return null;
        }

        var members = project.Members
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.CreatedAt)
            .Select(m => MapToMemberDto(m, m.User))
            .ToList();

        _logger.LogInformation("User {UserId} retrieved {Count} members for project {ProjectId}",
            requestingUserId, members.Count, projectId);

        return members;
    }

    public async Task<(MemberResponseDto? member, string? errorMessage)> AddMemberAsync(
        Guid projectId,
        AddMemberDto dto,
        Guid requestingUserId)
    {
        var (userId, resolveError) = await ResolveUserIdAsync(dto.UserId, dto.Email);
        if (userId == null)
        {
            return (null, resolveError);
        }

        return await AddMemberAsync(projectId, userId.Value, requestingUserId);
    }

    private async Task<(Guid? userId, string? errorMessage)> ResolveUserIdAsync(
        Guid? userIdFromDto,
        string? emailFromDto)
    {
        if (userIdFromDto.HasValue && !string.IsNullOrWhiteSpace(emailFromDto))
        {
            return (null, "Provide either UserId or Email, not both.");
        }

        if (!userIdFromDto.HasValue && string.IsNullOrWhiteSpace(emailFromDto))
        {
            return (null, "Either UserId or Email is required.");
        }

        if (userIdFromDto.HasValue)
        {
            return (userIdFromDto.Value, null);
        }

        var trimmedEmail = emailFromDto!.Trim();
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == trimmedEmail.ToLower());

        if (user == null)
        {
            _logger.LogWarning("No user found with email address: {Email}", trimmedEmail);
            return (null, $"No user found with email address: {trimmedEmail}");
        }

        _logger.LogInformation("Resolved email {Email} to user {UserId}", trimmedEmail, user.Id);
        return (user.Id, null);
    }

    private static string BuildProjectGroupName(Guid projectId) =>
        $"project_{projectId.ToString().ToLowerInvariant()}";

    private async Task BroadcastAsync(string groupName, string eventName, object payload)
    {
        try
        {
            await _hubContext.Clients.Group(groupName).SendAsync(eventName, payload);
            _logger.LogInformation(
                "Broadcast {EventName} event to group {GroupName}",
                eventName, groupName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to broadcast {EventName} event to group {GroupName}",
                eventName, groupName);
        }
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

    private static MemberResponseDto MapToMemberDto(ProjectMember member, User user)
    {
        return new MemberResponseDto
        {
            UserId = user.Id.ToString(),
            Name = user.Name,
            Email = user.Email,
            Role = member.Role.ToString(),
            JoinedAt = member.CreatedAt
        };
    }
}
