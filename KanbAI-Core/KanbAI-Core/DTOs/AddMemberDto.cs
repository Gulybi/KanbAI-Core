namespace KanbAI_Core.DTOs;

using System.ComponentModel.DataAnnotations;

public record AddMemberDto
{
    [Required(ErrorMessage = "User ID is required.")]
    public required Guid UserId { get; init; }
}
