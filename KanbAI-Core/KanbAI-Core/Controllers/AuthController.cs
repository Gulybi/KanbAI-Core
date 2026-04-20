using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    
    public AuthController(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ITokenService tokenService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
    {
        var emailExists = await _context.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower());
        if(emailExists)
        {
            return new BadRequestObjectResult(new { Message = "Email is already registered." });
        }
        
        var hashedPassword = _passwordHasher.HashPassword(request.Password);

        var newUser = new User
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = hashedPassword,
            Role = UserRole.Member
        };
        
        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();
        
        var userProfile = new UserProfileDto(
            newUser.Id.ToString(),
            newUser.Name,
            newUser.Email
        );
        
        var response = new AuthResponseDto(
            Token: _tokenService.GenerateToken(newUser),
            User: userProfile
        );

        return new CreatedAtActionResult(nameof(Register), "Auth", null, response);
    }
}