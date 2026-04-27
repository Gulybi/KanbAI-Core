namespace KanbAI_Core.DTOs;

public record MemberResponseDto
{
    public required string UserId { get; init; }
    public required string Name { get; init; }
    public required string Email { get; init; }
    public required string Role { get; init; }
    public required DateTimeOffset JoinedAt { get; init; }
}
