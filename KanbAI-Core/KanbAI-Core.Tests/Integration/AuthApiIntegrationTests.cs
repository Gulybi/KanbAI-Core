using System.Net;
using System.Net.Http.Json;
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
/// Integration tests for authentication endpoints (/api/auth/register and /api/auth/login).
/// Verifies that public authentication endpoints accept unauthenticated requests
/// and return proper responses per Issue #70.
/// </summary>
public class AuthApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Auth Success Tests

    [Fact]
    public async Task PostRegister_ValidRequest_Returns201WithToken()
    {
        // Arrange
        var client = CreateClient();
        var request = new RegisterRequestDto(
            Name: $"Test User {Guid.NewGuid()}",
            Email: $"test-{Guid.NewGuid()}@example.com",
            Password: "SecurePassword123!"
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "unauthenticated POST to /api/auth/register with valid data should return 201 Created");

        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        authResponse.Should().NotBeNull();
        authResponse!.Token.Should().NotBeNullOrEmpty("response must include a JWT token");
        authResponse.User.Should().NotBeNull();
        authResponse.User.Email.Should().Be(request.Email);
        authResponse.User.Name.Should().Be(request.Name);
    }

    [Fact]
    public async Task PostLogin_ValidCredentials_Returns200WithToken()
    {
        // Arrange
        var client = CreateClient();

        var email = $"login-test-{Guid.NewGuid()}@example.com";
        var password = "SecurePassword123!";
        var registerRequest = new RegisterRequestDto(
            Name: "Login Test User",
            Email: email,
            Password: password
        );

        await client.PostAsJsonAsync("/api/auth/register", registerRequest);

        var loginRequest = new LoginRequestDto(
            Email: email,
            Password: password
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "unauthenticated POST to /api/auth/login with valid credentials should return 200 OK");

        var authResponse = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        authResponse.Should().NotBeNull();
        authResponse!.Token.Should().NotBeNullOrEmpty("response must include a JWT token");
        authResponse.User.Should().NotBeNull();
        authResponse.User.Email.Should().Be(email);
    }

    #endregion

    #region Auth Failure Tests

    [Fact]
    public async Task PostLogin_InvalidCredentials_Returns401()
    {
        // Arrange
        var client = CreateClient();

        var loginRequest = new LoginRequestDto(
            Email: "nonexistent@example.com",
            Password: "WrongPassword123!"
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", loginRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated POST to /api/auth/login with invalid credentials should return 401 Unauthorized from application logic");
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task PostRegister_DuplicateEmail_Returns400()
    {
        // Arrange
        var client = CreateClient();

        var email = $"duplicate-{Guid.NewGuid()}@example.com";
        var firstRequest = new RegisterRequestDto(
            Name: "First User",
            Email: email,
            Password: "SecurePassword123!"
        );

        await client.PostAsJsonAsync("/api/auth/register", firstRequest);

        var duplicateRequest = new RegisterRequestDto(
            Name: "Duplicate User",
            Email: email,
            Password: "AnotherPassword456!"
        );

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/register", duplicateRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "unauthenticated POST to /api/auth/register with existing email should return 400 Bad Request");
    }

    #endregion

    #region Test Infrastructure

    private HttpClient CreateClient()
    {
        var databaseName = $"AuthApiTests-{Guid.NewGuid()}";

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
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

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
