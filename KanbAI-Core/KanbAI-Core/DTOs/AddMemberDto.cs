namespace KanbAI_Core.DTOs;

using System.ComponentModel.DataAnnotations;

public record AddMemberDto
{
    public Guid? UserId { get; init; }

    [EmailAddress(ErrorMessage = "Invalid email format.")]
    public string? Email { get; init; }
}
