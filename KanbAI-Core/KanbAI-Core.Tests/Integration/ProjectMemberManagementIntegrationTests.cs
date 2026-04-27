using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using KanbAI_Core.DTOs;
using KanbAI_Core.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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
        var client = CreateAuthenticatedClient(ownerId);

        var invalidDto = new { };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", invalidDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    private HttpClient CreateAuthenticatedClient(Guid userId)
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

                // Register test auth scheme with the user ID
                services.AddAuthentication("Test")
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>("Test", options =>
                    {
                        options.UserId = userId;
                    });

                // Keep the FallbackPolicy to enforce authentication
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

                // Keep the FallbackPolicy to enforce authentication
            });
        }).CreateClient();
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
