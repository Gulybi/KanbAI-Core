namespace KanbAI_Core.DTOs;

public record UserProfileDto(
    string Id,
    string Name,
    string Email
);

public record AuthResponseDto(
    string Token,
    UserProfileDto User
);