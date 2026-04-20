using System.ComponentModel.DataAnnotations;

namespace KanbAI_Core.DTOs;

public record LoginRequestDto(
    [Required]
    [EmailAddress]
    string Email,
    
    [Required]
    string Password
);