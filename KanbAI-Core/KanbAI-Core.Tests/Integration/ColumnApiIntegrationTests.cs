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
/// Integration tests for Column API endpoints.
/// These tests focus on HTTP-level concerns (status codes, validation, authentication).
/// Database-level concerns are covered by unit tests using in-memory EF Core.
/// </summary>
public class ColumnApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ColumnApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Security Tests

    [Fact]
    public async Task AllEndpoints_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var unauthenticatedClient = CreateUnauthenticatedClient();
        var projectId = Guid.NewGuid();
        var columnId = Guid.NewGuid();

        // Act & Assert - GET
        var getResponse = await unauthenticatedClient.GetAsync($"/api/column/project/{projectId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Act & Assert - POST
        var createDto = new CreateColumnDto { Name = "Test" };
        var postResponse = await unauthenticatedClient.PostAsJsonAsync($"/api/column/project/{projectId}", createDto);
        postResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Act & Assert - DELETE
        var deleteResponse = await unauthenticatedClient.DeleteAsync($"/api/column/{columnId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProjectColumns_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var projectId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/column/project/{projectId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateColumn_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var projectId = Guid.NewGuid();

        var dto = new CreateColumnDto { Name = "Test" };

        // Act
        var response = await client.PostAsJsonAsync($"/api/column/project/{projectId}", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteColumn_UnauthenticatedRequest_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var columnId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/api/column/{columnId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task CreateColumn_MissingName_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var projectId = Guid.NewGuid();

        var invalidDto = new { ColorCode = "#blue" };

        // Act
        var response = await client.PostAsJsonAsync($"/api/column/project/{projectId}", invalidDto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateColumn_NameTooLong_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var projectId = Guid.NewGuid();

        var dto = new CreateColumnDto
        {
            Name = new string('A', 101)
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/column/project/{projectId}", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateColumn_ColorCodeTooLong_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var projectId = Guid.NewGuid();

        var dto = new CreateColumnDto
        {
            Name = "Valid Name",
            ColorCode = new string('A', 21)
        };

        // Act
        var response = await client.PostAsJsonAsync($"/api/column/project/{projectId}", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
