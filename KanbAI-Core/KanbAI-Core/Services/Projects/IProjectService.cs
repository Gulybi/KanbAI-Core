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
}
