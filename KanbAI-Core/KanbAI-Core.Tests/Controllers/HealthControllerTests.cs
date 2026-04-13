using System.Net;
using System.Net.Http.Json;
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

namespace KanbAI_Core.Tests.Controllers;

public class HealthControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public HealthControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Happy Path

    [Fact]
    public async Task HealthEndpoint_Returns200OK()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsApiResponseFormat()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadFromJsonAsync<ApiResponse>();

        // Assert
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
        body.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsExpectedMessage()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadFromJsonAsync<ApiResponse>();

        // Assert
        body.Should().NotBeNull();
        body!.Message.Should().Be("KanbAI API is running smoothly.");
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsJsonContentType()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Regression — Removed Endpoints

    [Fact]
    public async Task WeatherForecastEndpoint_Returns404()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/weatherforecast");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Security — AllowAnonymous Override

    [Fact]
    public async Task HealthEndpoint_IsAccessibleWithoutAuthentication()
    {
        // Arrange — create a client that keeps the production FallbackPolicy
        // (require authentication) to verify [AllowAnonymous] overrides it.
        var client = _factory.WithWebHostBuilder(builder =>
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
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

                // Deliberately NOT setting FallbackPolicy = null
                // so the production policy (require auth) remains active.
            });
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "[AllowAnonymous] on HealthController should override the FallbackPolicy");
    }

    #endregion

    #region Test Infrastructure

    private HttpClient CreateClient(string environment = "Development")
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var descriptor in authConfigs)
                    services.Remove(descriptor);

                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                services.AddAuthorization(options => options.FallbackPolicy = null);
            });
        }).CreateClient();
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }

    #endregion
}
