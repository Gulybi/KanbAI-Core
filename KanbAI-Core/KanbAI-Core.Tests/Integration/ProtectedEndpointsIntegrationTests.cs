using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.DTOs;
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
/// Integration tests for protected endpoints to ensure authorization is working correctly
/// after fixing Issue #70. Verifies that protected endpoints require authentication
/// and public endpoints remain accessible.
/// </summary>
public class ProtectedEndpointsIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ProtectedEndpointsIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Security Tests - Unauthenticated Access

    [Fact]
    public async Task GetProjects_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/project");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated GET to /api/project should return 401 Unauthorized");
    }

    [Fact]
    public async Task PostTask_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var columnId = Guid.NewGuid();

        var taskDto = new CreateTaskDto
        {
            Title = "Test Task",
            Content = "Test Content"
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/task/column/{columnId}", taskDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated POST to /api/task/column/{columnId} should return 401 Unauthorized");
    }

    [Fact]
    public async Task GetHealth_Unauthenticated_Returns200()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "unauthenticated GET to /api/health should return 200 OK (public endpoint)");
    }

    #endregion

    #region Auth Success Tests - Authenticated Access

    [Fact]
    public async Task GetProjects_Authenticated_ReturnsProjects()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        // Act
        var response = await client.GetAsync("/api/project");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "authenticated GET to /api/project with valid JWT should return 200 OK");

        var apiResponse = await response.Content.ReadFromJsonAsync<ApiResponse<List<ProjectResponseDto>>>();
        apiResponse.Should().NotBeNull();
        apiResponse!.Success.Should().BeTrue();
        apiResponse.Data.Should().NotBeNull("response should contain a list of projects (may be empty)");
    }

    #endregion

    #region Test Infrastructure

    private HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var databaseName = $"ProtectedEndpointsTests-{Guid.NewGuid()}";

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                // Remove all authentication scheme registrations
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var config in authConfigs)
                {
                    services.Remove(config);
                }

                // Register authenticated test scheme
                services.AddAuthentication("Test")
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>(
                        "Test",
                        options => options.UserId = userId);

                // Disable the authorization fallback policy
                services.AddAuthorization(options => options.FallbackPolicy = null);

                // Replace SQL Server DbContext with in-memory database for isolated testing.
                // Remove all EF Core service registrations to avoid conflicts between providers.
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
                // Remove all authentication scheme registrations
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var config in authConfigs)
                {
                    services.Remove(config);
                }

                // Register a no-op test authentication scheme
                services.AddAuthentication("Test")
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>("Test", _ => { });

                // Disable the authorization fallback policy
                services.AddAuthorization(options => options.FallbackPolicy = null);
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
