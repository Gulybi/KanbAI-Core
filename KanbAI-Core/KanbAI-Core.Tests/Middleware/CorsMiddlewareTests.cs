using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using FluentAssertions;
using KanbAI_Core.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Middleware;

public class CorsMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CorsMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

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

    #region Preflight — Allowed Origin

    [Fact]
    public async Task Preflight_WithAllowedOrigin_ReturnsCorsPolicyHeaders()
    {
        // Arrange
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/weatherforecast");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().Contain("http://localhost:4200");
    }

    [Fact]
    public async Task Preflight_WithAllowedOrigin_AllowsRequiredMethods()
    {
        // Arrange
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/weatherforecast");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        var allowedMethods = response.Headers.GetValues("Access-Control-Allow-Methods")
            .SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries))
            .ToList();

        allowedMethods.Should().Contain("GET");
        allowedMethods.Should().Contain("POST");
        allowedMethods.Should().Contain("PUT");
        allowedMethods.Should().Contain("DELETE");
        allowedMethods.Should().Contain("PATCH");
    }

    [Fact]
    public async Task Preflight_WithAllowedOrigin_AllowsRequiredHeaders()
    {
        // Arrange
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/weatherforecast");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type, Authorization");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        var allowedHeaders = response.Headers.GetValues("Access-Control-Allow-Headers")
            .SelectMany(v => v.Split(',', StringSplitOptions.TrimEntries))
            .ToList();

        allowedHeaders.Should().Contain(h => h.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
        allowedHeaders.Should().Contain(h => h.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Actual Request — Allowed Origin

    [Fact]
    public async Task ActualRequest_WithAllowedOrigin_IncludesAccessControlAllowOriginHeader()
    {
        // Arrange
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/weatherforecast");
        request.Headers.Add("Origin", "http://localhost:4200");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().Contain("http://localhost:4200");
    }

    #endregion

    #region Actual Request — Disallowed Origin

    [Fact]
    public async Task ActualRequest_WithDisallowedOrigin_DoesNotIncludeAccessControlAllowOriginHeader()
    {
        // Arrange
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/weatherforecast");
        request.Headers.Add("Origin", "http://evil.example.com");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    #endregion

    #region Production Environment

    [Fact]
    public async Task Preflight_InProduction_DoesNotReturnCorsHeaders()
    {
        // Arrange
        var client = CreateClient("Production");
        var request = new HttpRequestMessage(HttpMethod.Options, "/weatherforecast");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "Content-Type");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    #endregion

    #region Regression — Existing Endpoints

    [Fact]
    public async Task ExistingWeatherEndpoint_ContinuesToWork()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/weatherforecast");
        var body = await response.Content.ReadFromJsonAsync<ApiResponse>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
    }

    #endregion

    #region Test Infrastructure

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
