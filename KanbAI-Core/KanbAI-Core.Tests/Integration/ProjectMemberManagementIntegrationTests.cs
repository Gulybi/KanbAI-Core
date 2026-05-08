using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using KanbAI_Core.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Integration;

/// <summary>
/// Integration tests for Project Member Management API endpoints.
/// These tests verify end-to-end HTTP-level concerns including authentication,
/// authorization, validation, and error handling for add/remove member operations.
/// </summary>
public class ProjectMemberManagementIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProjectMemberManagementIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region AddMember Validation Tests

    [Fact]
    public async Task AddMember_MissingUserId_Returns400()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(ownerId, out _);

        var invalidDto = new { };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", invalidDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddMember_MissingBothUserIdAndEmail_Returns400ValidationError()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(ownerId, out var databaseName);

        await SeedOwnedProjectAsync(databaseName, ownerId, projectId);

        var dto = new AddMemberDto { UserId = null, Email = null };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Either UserId or Email is required.");
    }

    [Fact]
    public async Task AddMember_ProvidingBothUserIdAndEmail_Returns400ValidationError()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(ownerId, out var databaseName);

        await SeedOwnedProjectAsync(databaseName, ownerId, projectId);

        var dto = new AddMemberDto
        {
            UserId = Guid.NewGuid(),
            Email = "user@example.com"
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Provide either UserId or Email, not both.");
    }

    #endregion

    #region AddMember Business Logic Tests (Issue #85)

    [Fact]
    public async Task AddMember_ValidJwtUnregisteredEmail_Returns400NotFound()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(ownerId, out var databaseName);

        await SeedOwnedProjectAsync(databaseName, ownerId, projectId);

        var dto = new AddMemberDto { Email = "unregistered@example.com" };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "valid owner JWT with an unregistered email must return 400, not 401");
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("No user found with email address:");
    }

    [Fact]
    public async Task AddMember_ValidJwtUserAlreadyMember_Returns400AlreadyMember()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var existingMemberId = Guid.NewGuid();
        var existingMemberEmail = $"existing-{Guid.NewGuid()}@example.com";
        var client = CreateAuthenticatedClient(ownerId, out var databaseName);

        await SeedOwnedProjectWithMemberAsync(
            databaseName,
            ownerId,
            projectId,
            existingMemberId,
            existingMemberEmail);

        var dto = new AddMemberDto { Email = existingMemberEmail };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("User is already a member of this project.");
    }

    [Fact]
    public async Task AddMember_ValidJwtNonOwnerCaller_Returns403Forbidden()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var nonOwnerId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var nonOwnerEmail = $"member-{Guid.NewGuid()}@example.com";
        var targetEmail = $"target-{Guid.NewGuid()}@example.com";
        var client = CreateAuthenticatedClient(nonOwnerId, out var databaseName);

        await SeedOwnedProjectWithMemberAndExternalUserAsync(
            databaseName,
            ownerId,
            projectId,
            nonOwnerId,
            nonOwnerEmail,
            targetUserId,
            targetEmail);

        var dto = new AddMemberDto { Email = targetEmail };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Only the project owner can add members.");
    }

    [Fact]
    public async Task AddMember_ValidJwtNonExistentProject_Returns404NotFound()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var targetUserId = Guid.NewGuid();
        var targetEmail = $"target-{Guid.NewGuid()}@example.com";
        var nonExistentProjectId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(ownerId, out var databaseName);

        await SeedTwoUsersAsync(databaseName, ownerId, targetUserId, targetEmail);

        var dto = new AddMemberDto { Email = targetEmail };

        // Act
        var response = await client.PostAsJsonAsync(
            $"/api/project/{nonExistentProjectId}/members",
            dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Project not found.");
    }

    #endregion

    #region AddMember Security Tests

    [Fact]
    public async Task AddMember_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var client = CreateUnauthenticatedClient();

        var dto = new AddMemberDto
        {
            UserId = Guid.NewGuid()
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region RemoveMember Security Tests

    [Fact]
    public async Task RemoveMember_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var client = CreateUnauthenticatedClient();

        // Act
        var response = await client.DeleteAsync($"/api/project/{projectId}/members/{userId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Helper Methods

    private HttpClient CreateAuthenticatedClient(Guid userId, out string databaseName)
    {
        var capturedDbName = $"ProjectMemberManagementTests-{Guid.NewGuid()}";
        databaseName = capturedDbName;

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Remove all Negotiate auth scheme registrations
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var descriptor in authConfigs)
                    services.Remove(descriptor);

                // Register test auth scheme with the user ID
                services.AddAuthentication("Test")
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>("Test", options =>
                    {
                        options.UserId = userId;
                    });

                ReplaceDbContextWithInMemory(services, capturedDbName);
            });
        }).CreateClient();
    }

    private HttpClient CreateUnauthenticatedClient()
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Remove all Negotiate auth scheme registrations
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var descriptor in authConfigs)
                    services.Remove(descriptor);

                // Register test auth scheme but without a user ID (NoResult)
                services.AddAuthentication("Test")
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>("Test", _ => { });
            });
        }).CreateClient();
    }

    private static void ReplaceDbContextWithInMemory(IServiceCollection services, string databaseName)
    {
        var dbContextDescriptors = services
            .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                     || d.ServiceType == typeof(DbContextOptions)
                     || d.ServiceType == typeof(ApplicationDbContext)
                     || (d.ServiceType.FullName?.StartsWith("Microsoft.EntityFrameworkCore") ?? false))
            .ToList();
        foreach (var descriptor in dbContextDescriptors)
        {
            services.Remove(descriptor);
        }

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
    }

    private async Task SeedTwoUsersAsync(
        string databaseName,
        Guid ownerId,
        Guid targetUserId,
        string targetEmail)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using var context = new ApplicationDbContext(options);

        context.Users.Add(new User
        {
            Id = ownerId,
            Name = "Owner",
            Email = $"owner-{ownerId}@example.com",
            PasswordHash = "hash",
            Role = UserRole.Member
        });

        context.Users.Add(new User
        {
            Id = targetUserId,
            Name = "Target",
            Email = targetEmail,
            PasswordHash = "hash",
            Role = UserRole.Member
        });

        await context.SaveChangesAsync();
    }

    private async Task SeedOwnedProjectAsync(string databaseName, Guid ownerId, Guid projectId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using var context = new ApplicationDbContext(options);

        context.Users.Add(new User
        {
            Id = ownerId,
            Name = "Owner",
            Email = $"owner-{ownerId}@example.com",
            PasswordHash = "hash",
            Role = UserRole.Member
        });

        context.Projects.Add(new Project
        {
            Id = projectId,
            Name = "Test Project"
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = ownerId,
            Role = ProjectRole.Owner
        });

        await context.SaveChangesAsync();
    }

    private async Task SeedOwnedProjectWithMemberAsync(
        string databaseName,
        Guid ownerId,
        Guid projectId,
        Guid memberUserId,
        string memberEmail)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using var context = new ApplicationDbContext(options);

        context.Users.Add(new User
        {
            Id = ownerId,
            Name = "Owner",
            Email = $"owner-{ownerId}@example.com",
            PasswordHash = "hash",
            Role = UserRole.Member
        });

        context.Users.Add(new User
        {
            Id = memberUserId,
            Name = "Existing Member",
            Email = memberEmail,
            PasswordHash = "hash",
            Role = UserRole.Member
        });

        context.Projects.Add(new Project
        {
            Id = projectId,
            Name = "Test Project"
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = ownerId,
            Role = ProjectRole.Owner
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = memberUserId,
            Role = ProjectRole.Member
        });

        await context.SaveChangesAsync();
    }

    private async Task SeedOwnedProjectWithMemberAndExternalUserAsync(
        string databaseName,
        Guid ownerId,
        Guid projectId,
        Guid memberUserId,
        string memberEmail,
        Guid externalUserId,
        string externalEmail)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using var context = new ApplicationDbContext(options);

        context.Users.Add(new User
        {
            Id = ownerId,
            Name = "Owner",
            Email = $"owner-{ownerId}@example.com",
            PasswordHash = "hash",
            Role = UserRole.Member
        });

        context.Users.Add(new User
        {
            Id = memberUserId,
            Name = "Existing Member",
            Email = memberEmail,
            PasswordHash = "hash",
            Role = UserRole.Member
        });

        context.Users.Add(new User
        {
            Id = externalUserId,
            Name = "External User",
            Email = externalEmail,
            PasswordHash = "hash",
            Role = UserRole.Member
        });

        context.Projects.Add(new Project
        {
            Id = projectId,
            Name = "Test Project"
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = ownerId,
            Role = ProjectRole.Owner
        });

        context.ProjectMembers.Add(new ProjectMember
        {
            ProjectId = projectId,
            UserId = memberUserId,
            Role = ProjectRole.Member
        });

        await context.SaveChangesAsync();
    }

    private sealed class TestAuthSchemeOptions : AuthenticationSchemeOptions
    {
        public Guid? UserId { get; set; }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<TestAuthSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<TestAuthSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.UserId.HasValue)
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, Options.UserId.Value.ToString())
                };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Test");

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }

    #endregion
}
