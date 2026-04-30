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

public class ProjectMemberFetchingIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProjectMemberFetchingIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region GetProjectMembers Authentication Tests

    [Fact]
    public async Task GetProjectMembers_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var client = CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync($"/api/project/{projectId}/members");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region AddMember Validation Tests

    [Fact]
    public async Task AddMember_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var projectId = Guid.NewGuid();
        var client = CreateUnauthenticatedClient();

        var dto = new AddMemberDto
        {
            Email = "user@example.com"
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AddMember_MalformedEmail_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        var dto = new AddMemberDto
        {
            Email = "notanemail"
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddMember_BothUserIdAndEmail_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        var dto = new AddMemberDto
        {
            UserId = Guid.NewGuid(),
            Email = "user@example.com"
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AddMember_EmptyDto_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        var dto = new AddMemberDto
        {
            UserId = null,
            Email = null
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/project/{projectId}/members", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var descriptor in authConfigs)
                    services.Remove(descriptor);

                services.AddAuthentication("Test")
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>("Test", options =>
                    {
                        options.UserId = userId;
                    });
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
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var descriptor in authConfigs)
                    services.Remove(descriptor);

                services.AddAuthentication("Test")
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>("Test", _ => { });
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
