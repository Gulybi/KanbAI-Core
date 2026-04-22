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
/// Integration tests for Project API endpoints.
/// These tests focus on HTTP-level concerns (status codes, validation, authentication).
/// Database-level concerns are covered by unit tests using in-memory EF Core.
/// </summary>
public class ProjectApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProjectApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Validation Tests

    [Fact]
    public async Task CreateProject_MissingName_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        var invalidDto = new { Description = "No name provided" };

        // Act
        var response = await client.PostAsJsonAsync("/api/project", invalidDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProject_NameTooLong_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        var dto = new CreateProjectDto
        {
            Name = new string('A', 201),
            Description = "Valid description"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/project", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProject_DescriptionTooLong_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        var dto = new CreateProjectDto
        {
            Name = "Valid Name",
            Description = new string('A', 501)
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/project", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProject_MissingName_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var projectId = Guid.NewGuid();

        var invalidDto = new { Description = "No name provided" };

        // Act
        var response = await client.PutAsJsonAsync($"/api/project/{projectId}", invalidDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProject_NameTooLong_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var projectId = Guid.NewGuid();

        var dto = new UpdateProjectDto
        {
            Name = new string('A', 201),
            Description = "Valid description"
        };

        // Act
        var response = await client.PutAsJsonAsync($"/api/project/{projectId}", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Security Tests

    [Fact]
    public async Task CreateProject_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/project", new CreateProjectDto { Name = "Test" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetUserProjects_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/project");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProjectById_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var projectId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/project/{projectId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateProject_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var projectId = Guid.NewGuid();

        // Act
        var response = await client.PutAsJsonAsync($"/api/project/{projectId}", new UpdateProjectDto { Name = "Updated" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteProject_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var projectId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/api/project/{projectId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Test Infrastructure

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
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>(
                        "Test",
                        options => options.UserId = userId);

                services.AddAuthorization(options => options.FallbackPolicy = null);
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
