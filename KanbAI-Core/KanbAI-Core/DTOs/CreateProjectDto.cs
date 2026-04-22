namespace KanbAI_Core.DTOs;

using System.ComponentModel.DataAnnotations;

public record CreateProjectDto
{
    [Required(ErrorMessage = "Project name is required.")]
    [MaxLength(200, ErrorMessage = "Project name cannot exceed 200 characters.")]
    public required string Name { get; init; }

    [MaxLength(500, ErrorMessage = "Project description cannot exceed 500 characters.")]
    public string? Description { get; init; }
}
