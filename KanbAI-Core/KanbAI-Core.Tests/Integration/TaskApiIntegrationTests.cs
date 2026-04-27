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
/// Integration tests for Task API endpoints.
/// These tests focus on HTTP-level concerns (status codes, validation, authentication).
/// Database-level concerns are covered by unit tests using in-memory EF Core.
/// </summary>
public class TaskApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TaskApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Security Tests

    [Fact]
    public async Task PostTask_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var columnId = Guid.NewGuid();
        var dto = new CreateTaskDto { Title = "Task" };

        // Act
        var response = await client.PostAsJsonAsync($"/api/task/column/{columnId}", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task PostTask_MissingTitleInBody_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var columnId = Guid.NewGuid();

        var invalidBody = new { content = "missing title" };

        // Act
        var response = await client.PostAsJsonAsync($"/api/task/column/{columnId}", invalidBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTask_TitleTooLong_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var columnId = Guid.NewGuid();

        var dto = new CreateTaskDto { Title = new string('A', 201) };

        // Act
        var response = await client.PostAsJsonAsync($"/api/task/column/{columnId}", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostTask_EmptyTitleInBody_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var columnId = Guid.NewGuid();

        var body = new { title = "" };

        // Act
        var response = await client.PostAsJsonAsync($"/api/task/column/{columnId}", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region MoveTask Security & Validation Tests

    [Fact]
    public async Task PutMoveTask_Unauthenticated_Returns401()
    {
        // Arrange
        var client = CreateUnauthenticatedClient();
        var taskId = Guid.NewGuid();
        var dto = new MoveTaskDto { ColumnId = Guid.NewGuid(), TaskOrder = 0 };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/move", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PutMoveTask_MissingColumnId_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var taskId = Guid.NewGuid();

        var invalidBody = new { taskOrder = 0 };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/move", invalidBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutMoveTask_NegativeTaskOrder_Returns400()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var client = CreateAuthenticatedClient(userId);
        var taskId = Guid.NewGuid();

        var body = new { columnId = Guid.NewGuid(), taskOrder = -1 };

        // Act
        var response = await client.PutAsJsonAsync($"/api/task/{taskId}/move", body);

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
