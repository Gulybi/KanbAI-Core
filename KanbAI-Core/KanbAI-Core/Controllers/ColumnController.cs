namespace KanbAI_Core.Controllers;

using KanbAI_Core.DTOs;
using KanbAI_Core.Services.Columns;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ColumnController : ControllerBase
{
    private readonly IColumnService _columnService;
    private readonly ILogger<ColumnController> _logger;

    public ColumnController(IColumnService columnService, ILogger<ColumnController> logger)
    {
        _columnService = columnService;
        _logger = logger;
    }

    [HttpGet("project/{projectId}")]
    public async Task<IActionResult> GetProjectColumns(Guid projectId)
    {
        var userId = GetCurrentUserId();

        var columns = await _columnService.GetProjectColumnsAsync(projectId, userId);

        if (columns == null)
        {
            return NotFound(ApiResponse.Fail("Project not found."));
        }

        return Ok(ApiResponse<List<ColumnResponseDto>>.Ok(columns));
    }

    [HttpPost("project/{projectId}")]
    public async Task<IActionResult> CreateColumn(Guid projectId, [FromBody] CreateColumnDto dto)
    {
        var userId = GetCurrentUserId();

        var result = await _columnService.CreateColumnAsync(projectId, dto, userId);

        if (result == null)
        {
            return NotFound(ApiResponse.Fail("Project not found."));
        }

        return CreatedAtAction(
            nameof(GetProjectColumns),
            new { projectId = result.ProjectId },
            ApiResponse<ColumnResponseDto>.Ok(result, "Column created successfully."));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteColumn(Guid id)
    {
        var userId = GetCurrentUserId();

        var isDeleted = await _columnService.DeleteColumnAsync(id, userId);

        if (!isDeleted)
        {
            return NotFound(ApiResponse.Fail("Column not found."));
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
