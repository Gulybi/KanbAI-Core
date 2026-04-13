using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using FluentAssertions;
using KanbAI_Core.DTOs;
using KanbAI_Core.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Middleware;

public class GlobalExceptionHandlerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public GlobalExceptionHandlerTests(CustomWebApplicationFactory factory)
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
                services.AddSingleton<IStartupFilter>(new ThrowingEndpointStartupFilter());
            });
        }).CreateClient();
    }

    #region Status Code & Headers

    [Fact]
    public async Task UnhandledException_Returns500StatusCode()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/test/throw");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task UnhandledException_ReturnsJsonContentType()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/test/throw");

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    #endregion

    #region Response Body Format

    [Fact]
    public async Task UnhandledException_ReturnsApiResponseFormat()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/test/throw");
        var body = await response.Content.ReadFromJsonAsync<ApiResponse>();

        // Assert
        body.Should().NotBeNull();
        body!.Success.Should().BeFalse();
        body.Message.Should().NotBeNullOrEmpty();
        body.Errors.Should().BeEmpty();
    }

    #endregion

    #region Environment-Specific Behavior

    [Fact]
    public async Task UnhandledException_InNonDevelopment_DoesNotLeakExceptionDetails()
    {
        // Arrange
        var client = CreateClient("Production");

        // Act
        var response = await client.GetAsync("/test/throw");
        var body = await response.Content.ReadFromJsonAsync<ApiResponse>();

        // Assert
        body.Should().NotBeNull();
        body!.Message.Should().NotContain("InvalidOperationException");
        body.Message.Should().NotContain("Test exception message");
    }

    [Fact]
    public async Task UnhandledException_InDevelopment_IncludesExceptionTypeAndMessage()
    {
        // Arrange
        var client = CreateClient("Development");

        // Act
        var response = await client.GetAsync("/test/throw");
        var body = await response.Content.ReadFromJsonAsync<ApiResponse>();

        // Assert
        body.Should().NotBeNull();
        body!.Message.Should().Contain("InvalidOperationException");
        body.Message.Should().Contain("Test exception message");
    }

    #endregion

    #region Stack Trace Protection

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task UnhandledException_ResponseBody_NeverContainsStackTrace(string environment)
    {
        // Arrange
        var client = CreateClient(environment);

        // Act
        var response = await client.GetAsync("/test/throw");
        var rawBody = await response.Content.ReadAsStringAsync();

        // Assert
        rawBody.Should().NotContain("   at ");
    }

    #endregion

    #region Regression — Existing Endpoints

    [Fact]
    public async Task HealthEndpoint_Returns200OK()
    {
        // Arrange
        var client = CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadFromJsonAsync<ApiResponse>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotBeNull();
        body!.Success.Should().BeTrue();
    }

    #endregion

    #region Test Infrastructure

    /// <summary>
    /// Inserts an inline middleware that throws <see cref="InvalidOperationException"/>
    /// for requests to <c>/test/throw</c>, enabling end-to-end validation of the
    /// global exception handler without modifying the production pipeline.
    /// </summary>
    private sealed class ThrowingEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                next(app);
                app.Use(nextMiddleware => context =>
                {
                    if (context.Request.Path.StartsWithSegments("/test/throw"))
                    {
                        throw new InvalidOperationException("Test exception message");
                    }
                    return nextMiddleware(context);
                });
            };
        }
    }

    /// <summary>
    /// No-op authentication handler that replaces Negotiate auth in the test
    /// server (TestServer does not support Kestrel's IConnectionItemsFeature).
    /// </summary>
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
