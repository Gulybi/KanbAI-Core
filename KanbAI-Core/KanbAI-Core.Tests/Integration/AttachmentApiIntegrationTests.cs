using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using FluentAssertions;
using KanbAI_Core.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Integration;

/// <summary>
/// Integration tests for the Attachment API endpoints.
/// Focuses on HTTP-layer concerns: authentication, request validation, multipart parsing,
/// and unknown-asset handling. Database-driven happy-path scenarios are covered by
/// <see cref="Services.Assets.AssetServiceIntegrationTests"/>.
/// </summary>
public class AttachmentApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AttachmentApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Security Tests

    [Fact]
    public async Task UploadFile_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var taskId = Guid.NewGuid();

        using var content = BuildMultipartFile("hello", "sample.png", "image/png");

        // Act
        var response = await client.PostAsync($"/api/attachment/task/{taskId}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFile_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var assetId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/attachment/{assetId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task UploadFile_MissingFile_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var taskId = Guid.NewGuid();

        using var content = new MultipartFormDataContent();

        // Act
        var response = await client.PostAsync($"/api/attachment/task/{taskId}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadFile_EmptyFile_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var taskId = Guid.NewGuid();

        using var content = BuildMultipartFile(string.Empty, "empty.png", "image/png");

        // Act
        var response = await client.PostAsync($"/api/attachment/task/{taskId}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region GetFile Tests

    [Fact]
    public async Task GetFile_InvalidAssetIdFormat_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);

        // Act
        var response = await client.GetAsync("/api/attachment/not-a-guid");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Test Infrastructure

    private static MultipartFormDataContent BuildMultipartFile(string body, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

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
