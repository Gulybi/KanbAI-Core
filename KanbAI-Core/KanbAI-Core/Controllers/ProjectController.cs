namespace KanbAI_Core.Controllers;

using KanbAI_Core.DTOs;
using KanbAI_Core.Services.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ProjectController : ControllerBase
{
    private readonly IProjectService _projectService;
    private readonly ILogger<ProjectController> _logger;

    public ProjectController(IProjectService projectService, ILogger<ProjectController> logger)
    {
        _projectService = projectService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateProject([FromBody] CreateProjectDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return UnauthorizedResponse();
        }

        var result = await _projectService.CreateProjectAsync(dto, userId.Value);

        return CreatedAtAction(
            nameof(GetProjectById),
            new { id = result.Id },
            ApiResponse<ProjectResponseDto>.Ok(result, "Project created successfully."));
    }

    [HttpGet]
    public async Task<IActionResult> GetUserProjects()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return UnauthorizedResponse();
        }

        var projects = await _projectService.GetUserProjectsAsync(userId.Value);

        return Ok(ApiResponse<List<ProjectResponseDto>>.Ok(projects));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProjectById(Guid id)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return UnauthorizedResponse();
        }

        var project = await _projectService.GetProjectByIdAsync(id, userId.Value);

        if (project == null)
        {
            return NotFound(ApiResponse.Fail("Project not found."));
        }

        return Ok(ApiResponse<ProjectResponseDto>.Ok(project));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProject(Guid id, [FromBody] UpdateProjectDto dto)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return UnauthorizedResponse();
        }

        var result = await _projectService.UpdateProjectAsync(id, dto, userId.Value);

        if (result == null)
        {
            return NotFound(ApiResponse.Fail("Project not found."));
        }

        return Ok(ApiResponse<ProjectResponseDto>.Ok(result, "Project updated successfully."));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProject(Guid id)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return UnauthorizedResponse();
        }

        var (isDeleted, errorMessage) = await _projectService.DeleteProjectAsync(id, userId.Value);

        if (!isDeleted)
        {
            if (errorMessage == "Only the project owner can delete the project.")
            {
                return StatusCode(403, ApiResponse.Fail(errorMessage));
            }
            return NotFound(ApiResponse.Fail(errorMessage!));
        }

        return NoContent();
    }

    [HttpPost("{projectId}/members")]
    public async Task<IActionResult> AddMember(Guid projectId, [FromBody] AddMemberDto dto)
    {
        var requestingUserId = GetCurrentUserId();
        if (requestingUserId == null)
        {
            return UnauthorizedResponse();
        }

        var (member, errorMessage) = await _projectService.AddMemberAsync(
            projectId,
            dto,
            requestingUserId.Value);

        if (member == null)
        {
            if (errorMessage == "Only the project owner can add members.")
            {
                return StatusCode(403, ApiResponse.Fail(errorMessage));
            }
            if (errorMessage == "User not found." ||
                errorMessage == "User is already a member of this project." ||
                errorMessage!.StartsWith("No user found with email address:") ||
                errorMessage == "Provide either UserId or Email, not both." ||
                errorMessage == "Either UserId or Email is required.")
            {
                return BadRequest(ApiResponse.Fail(errorMessage));
            }
            return NotFound(ApiResponse.Fail(errorMessage!));
        }

        return StatusCode(201, ApiResponse<MemberResponseDto>.Ok(member, "Member added successfully."));
    }

    [HttpDelete("{projectId}/members/{userId}")]
    public async Task<IActionResult> RemoveMember(Guid projectId, Guid userId)
    {
        var requestingUserId = GetCurrentUserId();
        if (requestingUserId == null)
        {
            return UnauthorizedResponse();
        }

        var (isRemoved, errorMessage) = await _projectService.RemoveMemberAsync(
            projectId,
            userId,
            requestingUserId.Value);

        if (!isRemoved)
        {
            if (errorMessage == "Only the project owner can remove members.")
            {
                return StatusCode(403, ApiResponse.Fail(errorMessage));
            }
            if (errorMessage == "Cannot remove the last owner from the project.")
            {
                return BadRequest(ApiResponse.Fail(errorMessage));
            }
            return NotFound(ApiResponse.Fail(errorMessage!));
        }

        return NoContent();
    }

    [HttpGet("{projectId}/members")]
    public async Task<IActionResult> GetProjectMembers(Guid projectId)
    {
        var requestingUserId = GetCurrentUserId();
        if (requestingUserId == null)
        {
            return UnauthorizedResponse();
        }

        var members = await _projectService.GetProjectMembersAsync(projectId, requestingUserId.Value);

        if (members == null)
        {
            return NotFound(ApiResponse.Fail("Project not found."));
        }

        return Ok(ApiResponse<List<MemberResponseDto>>.Ok(members));
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        _logger.LogDebug(
            "GetCurrentUserId invoked. NameIdentifier claim present: {ClaimPresent}",
            userIdClaim != null);

        if (string.IsNullOrEmpty(userIdClaim))
        {
            _logger.LogWarning("Authenticated request missing NameIdentifier claim");
            return null;
        }

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogWarning(
                "Authenticated request has unparseable NameIdentifier claim: {RawValue}",
                userIdClaim);
            return null;
        }

        return userId;
    }

    private IActionResult UnauthorizedResponse() =>
        Unauthorized(ApiResponse.Fail("Invalid or missing user ID in token."));
}
