using System.Net;
using System.Text.Encodings.Web;
using FluentAssertions;
using KanbAI_Core.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Middleware;

public class ScalarApiReferenceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ScalarApiReferenceTests(CustomWebApplicationFactory factory)
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

    #region Scalar UI — Development

    [Fact(Skip = "Scalar.AspNetCore blocked by WDAC policy — re-enable when DLL is allowlisted")]
    public async Task ScalarUi_InDevelopment_ReturnsSuccessStatusCode()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/scalar/v1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(Skip = "Scalar.AspNetCore blocked by WDAC policy — re-enable when DLL is allowlisted")]
    public async Task ScalarUi_InDevelopment_ReturnsHtmlContent()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/scalar/v1");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().StartWith("text/html");
    }

    #endregion

    #region Scalar UI — Production

    [Fact]
    public async Task ScalarUi_InProduction_ReturnsNotFound()
    {
        // Arrange
        var client = CreateClient("Production");

        // Act
        var response = await client.GetAsync("/scalar/v1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region OpenAPI Document

    [Fact]
    public async Task OpenApiDocument_InDevelopment_ReturnsSuccessStatusCode()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/openapi/v1.json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApiDocument_InProduction_ReturnsNotFound()
    {
        // Arrange
        var client = CreateClient("Production");

        // Act
        var response = await client.GetAsync("/openapi/v1.json");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
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
