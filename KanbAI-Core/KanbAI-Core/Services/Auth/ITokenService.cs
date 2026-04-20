using KanbAI_Core.Models.Entities;

namespace KanbAI_Core.Services.Auth;

public interface ITokenService
{
    string GenerateToken(User user);
}