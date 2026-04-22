namespace KanbAI_Core.DTOs;

using System.ComponentModel.DataAnnotations;

public record CreateColumnDto
{
    [Required(ErrorMessage = "Column name is required.")]
    [MaxLength(100, ErrorMessage = "Column name cannot exceed 100 characters.")]
    public required string Name { get; init; }

    [MaxLength(20, ErrorMessage = "Color code cannot exceed 20 characters.")]
    public string? ColorCode { get; init; }

    public int? ColumnOrder { get; init; }
}
