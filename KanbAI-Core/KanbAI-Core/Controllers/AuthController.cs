using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController(
    ApplicationDbContext context,
    IPasswordHasher passwordHasher,
    ITokenService tokenService)
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
    {
        var emailExists = await context.Users.AnyAsync(u => u.Email.ToLower() == request.Email.ToLower());
        if(emailExists)
        {
            return new BadRequestObjectResult(new { Message = "Email is already registered." });
        }
        
        var hashedPassword = passwordHasher.HashPassword(request.Password);

        var newUser = new User
        {
            Name = request.Name,
            Email = request.Email,
            PasswordHash = hashedPassword,
            Role = UserRole.Member
        };
        
        context.Users.Add(newUser);
        await context.SaveChangesAsync();
        
        var userProfile = new UserProfileDto(
            newUser.Id.ToString(),
            newUser.Name,
            newUser.Email
        );
        
        var response = new AuthResponseDto(
            Token: tokenService.GenerateToken(newUser),
            User: userProfile
        );

        return new CreatedAtActionResult(nameof(Register), "Auth", null, response);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
        if(user == null || !passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return new UnauthorizedObjectResult(new { Message = "Invalid email or password." });
        }
        
        var token = tokenService.GenerateToken(user);
        
        var userProfile = new UserProfileDto(
            user.Id.ToString(),
            user.Name,
            user.Email
        );
        
        var response = new AuthResponseDto(
            Token: tokenService.GenerateToken(user),
            User: userProfile
        );

        return new OkObjectResult(response);
    }
}