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

        var result = await _projectService.CreateProjectAsync(dto, userId);

        return CreatedAtAction(
            nameof(GetProjectById),
            new { id = result.Id },
            ApiResponse<ProjectResponseDto>.Ok(result, "Project created successfully."));
    }

    [HttpGet]
    public async Task<IActionResult> GetUserProjects()
    {
        var userId = GetCurrentUserId();

        var projects = await _projectService.GetUserProjectsAsync(userId);

        return Ok(ApiResponse<List<ProjectResponseDto>>.Ok(projects));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProjectById(Guid id)
    {
        var userId = GetCurrentUserId();

        var project = await _projectService.GetProjectByIdAsync(id, userId);

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

        var result = await _projectService.UpdateProjectAsync(id, dto, userId);

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

        var (isDeleted, errorMessage) = await _projectService.DeleteProjectAsync(id, userId);

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

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            _logger.LogError("Invalid or missing NameIdentifier claim in JWT token");
            throw new UnauthorizedAccessException("Invalid or missing user ID in token.");
        }

        return userId;
    }
}
