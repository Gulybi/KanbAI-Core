using System.ComponentModel.DataAnnotations;

namespace KanbAI_Core.DTOs;

public record RegisterRequestDto(
    [Required]
    string Name,
    
    [Required]
    [EmailAddress]
    string Email,
    
    [Required]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters long.")]
    string Password
);