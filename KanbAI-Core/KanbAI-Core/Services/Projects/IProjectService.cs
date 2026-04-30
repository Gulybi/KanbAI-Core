namespace KanbAI_Core.Services.Projects;

using KanbAI_Core.DTOs;

public interface IProjectService
{
    /// <summary>
    /// Creates a new project and assigns the specified user as the Owner.
    /// </summary>
    /// <param name="dto">Project creation data (Name, Description).</param>
    /// <param name="userId">The ID of the user creating the project (extracted from JWT claims).</param>
    /// <returns>The created project with the user's role (Owner).</returns>
    Task<ProjectResponseDto> CreateProjectAsync(CreateProjectDto dto, Guid userId);

    /// <summary>
    /// Retrieves all projects where the specified user is a member (Owner or Member role).
    /// </summary>
    /// <param name="userId">The ID of the authenticated user.</param>
    /// <returns>A list of projects with the user's role in each project.</returns>
    Task<List<ProjectResponseDto>> GetUserProjectsAsync(Guid userId);

    /// <summary>
    /// Retrieves a single project by ID if the user is a member.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="userId">The ID of the authenticated user.</param>
    /// <returns>The project details with the user's role, or null if not found/not a member.</returns>
    Task<ProjectResponseDto?> GetProjectByIdAsync(Guid projectId, Guid userId);

    /// <summary>
    /// Updates a project's name and description if the user is a member.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="dto">Updated project data (Name, Description).</param>
    /// <param name="userId">The ID of the authenticated user.</param>
    /// <returns>The updated project, or null if not found/not authorized.</returns>
    Task<ProjectResponseDto?> UpdateProjectAsync(Guid projectId, UpdateProjectDto dto, Guid userId);

    /// <summary>
    /// Deletes a project if the user is the Owner. Returns true if deleted, false if not found/not authorized.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="userId">The ID of the authenticated user.</param>
    /// <returns>
    /// A tuple: (bool isDeleted, string? errorMessage).
    /// - (true, null) if successfully deleted.
    /// - (false, "Project not found.") if project doesn't exist or user is not a member.
    /// - (false, "Only the project owner can delete the project.") if user is Member (not Owner).
    /// </returns>
    Task<(bool isDeleted, string? errorMessage)> DeleteProjectAsync(Guid projectId, Guid userId);

    /// <summary>
    /// Adds a user to a project as a Member if the requesting user is the project Owner.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="userIdToAdd">The ID of the user to add to the project.</param>
    /// <param name="requestingUserId">The ID of the authenticated user making the request.</param>
    /// <returns>
    /// A tuple: (MemberResponseDto? member, string? errorMessage).
    /// - (memberDto, null) if successfully added.
    /// - (null, "Project not found.") if project doesn't exist or requesting user is not a member.
    /// - (null, "Only the project owner can add members.") if requesting user is not an Owner.
    /// - (null, "User not found.") if userIdToAdd does not exist in the Users table.
    /// - (null, "User is already a member of this project.") if duplicate membership detected.
    /// </returns>
    Task<(MemberResponseDto? member, string? errorMessage)> AddMemberAsync(
        Guid projectId,
        Guid userIdToAdd,
        Guid requestingUserId);

    /// <summary>
    /// Removes a user from a project if the requesting user is the project Owner.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="userIdToRemove">The ID of the user to remove from the project.</param>
    /// <param name="requestingUserId">The ID of the authenticated user making the request.</param>
    /// <returns>
    /// A tuple: (bool isRemoved, string? errorMessage).
    /// - (true, null) if successfully removed.
    /// - (false, "Project not found.") if project doesn't exist or requesting user is not a member.
    /// - (false, "Only the project owner can remove members.") if requesting user is not an Owner.
    /// - (false, "User is not a member of this project.") if userIdToRemove is not a member.
    /// - (false, "Cannot remove the last owner from the project.") if attempting to remove the only remaining Owner.
    /// </returns>
    Task<(bool isRemoved, string? errorMessage)> RemoveMemberAsync(
        Guid projectId,
        Guid userIdToRemove,
        Guid requestingUserId);

    /// <summary>
    /// Retrieves all members of a project if the requesting user is a member (Owner or Member).
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="requestingUserId">The ID of the authenticated user making the request.</param>
    /// <returns>
    /// A list of MemberResponseDto ordered by role descending (Owners first), then by join date ascending.
    /// Returns null if the project does not exist or the requesting user is not a member.
    /// </returns>
    Task<List<MemberResponseDto>?> GetProjectMembersAsync(
        Guid projectId,
        Guid requestingUserId);

    /// <summary>
    /// Adds a user to a project as a Member if the requesting user is the project Owner.
    /// Overload that accepts AddMemberDto for email-based lookup.
    /// </summary>
    Task<(MemberResponseDto? member, string? errorMessage)> AddMemberAsync(
        Guid projectId,
        AddMemberDto dto,
        Guid requestingUserId);
}
