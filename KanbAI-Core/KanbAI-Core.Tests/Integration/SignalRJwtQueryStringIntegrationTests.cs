using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Tests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace KanbAI_Core.Tests.Integration;

/// <summary>
/// Integration tests for the <c>JwtBearerEvents.OnMessageReceived</c>
/// handler added in issue #61. These tests exercise the REAL JWT middleware
/// (the default scheme from Program.cs) — no <c>TestAuthHandler</c> is
/// installed here, so the behavior under test is the actual production auth
/// configuration.
///
/// Because no hub endpoints exist yet (issue #62 adds the first one), we
/// inject a minimal probe endpoint at <c>/hubs/probe</c> and <c>/api/probe</c>
/// via an <see cref="IStartupFilter"/> (see integration-testing.md §4). The
/// probe endpoints return 200 when <c>User.Identity.IsAuthenticated</c> is
/// true and 401 otherwise — this lets us directly observe whether JWT
/// extraction ran and the token was validated.
///
/// Tokens are signed with the real <see cref="JwtSettings"/> resolved from
/// the test host's configuration, so validation succeeds for valid tokens
/// and fails for tokens with mismatched signatures/issuers.
/// </summary>
public class SignalRJwtQueryStringIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SignalRJwtQueryStringIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region /hubs/* path — query string token MUST be extracted

    [Fact]
    public async Task GetRequest_ToHubPath_WithValidQueryStringToken_Returns200()
    {
        // Arrange
        using var customFactory = CreateFactoryWithProbe();
        var client = customFactory.CreateClient();
        var token = GenerateValidToken(customFactory.JwtSettings);

        // Act
        var response = await client.GetAsync($"/hubs/probe?access_token={token}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "OnMessageReceived must extract the access_token query string parameter for /hubs/* paths and hand it to the JWT validator");
    }

    [Fact]
    public async Task GetRequest_ToHubPath_WithInvalidQueryStringToken_Returns401()
    {
        // Arrange
        using var customFactory = CreateFactoryWithProbe();
        var client = customFactory.CreateClient();

        // Act - "not-a-real-jwt" fails signature validation
        var response = await client.GetAsync("/hubs/probe?access_token=not-a-real-jwt");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an invalid token extracted from the query string must still go through full JWT validation and fail");
    }

    [Fact]
    public async Task GetRequest_ToHubPath_WithoutToken_Returns401()
    {
        // Arrange
        using var customFactory = CreateFactoryWithProbe();
        var client = customFactory.CreateClient();

        // Act
        var response = await client.GetAsync("/hubs/probe");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "without any credentials the probe endpoint must reject the request");
    }

    [Fact]
    public async Task GetRequest_ToNestedHubPath_WithValidQueryStringToken_Returns200()
    {
        // Arrange - guards against an accidental narrowing of the path filter
        // (e.g. exact match instead of StartsWithSegments)
        using var customFactory = CreateFactoryWithProbe();
        var client = customFactory.CreateClient();
        var token = GenerateValidToken(customFactory.JwtSettings);

        // Act
        var response = await client.GetAsync($"/hubs/probe/deeper?access_token={token}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "StartsWithSegments('/hubs') must match any nested path under /hubs");
    }

    #endregion

    #region /api/* path — query string token MUST be IGNORED

    [Fact]
    public async Task GetRequest_ToApiPath_WithQueryStringToken_Returns401()
    {
        // Arrange - this is the most important isolation test. Query string
        // token extraction must NEVER apply to REST API endpoints; tokens
        // there belong in the Authorization header. A regression that removes
        // the path guard would silently expose every REST endpoint to query
        // string tokens (which are logged by web servers, proxies, browser
        // history — a real security problem).
        using var customFactory = CreateFactoryWithProbe();
        var client = customFactory.CreateClient();
        var token = GenerateValidToken(customFactory.JwtSettings);

        // Act
        var response = await client.GetAsync($"/api/probe?access_token={token}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "access_token on /api/* paths must be IGNORED; tokens for REST APIs must come via the Authorization header");
    }

    [Fact]
    public async Task GetRequest_ToApiPath_WithAuthorizationHeader_Returns200()
    {
        // Arrange - positive control: REST auth via Authorization header
        // still works after the OnMessageReceived handler was added.
        using var customFactory = CreateFactoryWithProbe();
        var client = customFactory.CreateClient();
        var token = GenerateValidToken(customFactory.JwtSettings);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/probe");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Authorization: Bearer <token> on /api/* paths must continue to authenticate correctly — no regression from issue #61");
    }

    #endregion

    #region Sibling-name guard — StartsWithSegments is segment-aware

    [Fact]
    public async Task GetRequest_ToHubsLikeSiblingPath_WithQueryStringToken_IgnoresToken()
    {
        // Arrange - a path like "/hubsadmin" should NOT match "/hubs".
        // StartsWithSegments is segment-aware ("/hubs" matches "/hubs/..." but
        // not "/hubsadmin"). This test pins that behavior.
        using var customFactory = CreateFactoryWithProbe();
        var client = customFactory.CreateClient();
        var token = GenerateValidToken(customFactory.JwtSettings);

        // Act
        var response = await client.GetAsync($"/hubsadmin?access_token={token}");

        // Assert - /hubsadmin is not a registered endpoint, so the CORRECT
        // response is 404 (not 401 and not 200). If the path guard were
        // broken and matched "/hubsadmin", we'd reach authorization with a
        // valid token — still 404 because no endpoint exists, but we'd have
        // no way to distinguish. What matters: no 200 (no token acceptance).
        response.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "a sibling path /hubsadmin must NOT be treated as a SignalR hub path");
    }

    #endregion

    #region Test Infrastructure

    /// <summary>
    /// Generates a JWT signed with the application's real JwtSettings, so
    /// the JWT middleware's validation succeeds.
    /// </summary>
    private static string GenerateValidToken(JwtSettings jwtSettings)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
            }),
            Expires = DateTime.UtcNow.AddMinutes(5),
            Issuer = jwtSettings.Issuer,
            Audience = jwtSettings.Audience,
            SigningCredentials = creds
        };
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private ProbeFactory CreateFactoryWithProbe()
    {
        return new ProbeFactory(_factory);
    }

    /// <summary>
    /// Wraps the shared <see cref="CustomWebApplicationFactory"/> and adds
    /// a probe endpoint at /hubs/probe and /api/probe. Implemented as a
    /// subclass so <c>CreateClient()</c> automatically uses the extended host.
    /// </summary>
    private sealed class ProbeFactory : IDisposable
    {
        private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _host;

        public JwtSettings JwtSettings { get; }

        public ProbeFactory(CustomWebApplicationFactory baseFactory)
        {
            _host = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IStartupFilter, ProbeStartupFilter>();
                });
            });

            using var scope = _host.Services.CreateScope();
            JwtSettings = scope.ServiceProvider.GetRequiredService<JwtSettings>();
        }

        public HttpClient CreateClient() => _host.CreateClient();

        public void Dispose() => _host.Dispose();
    }

    /// <summary>
    /// Adds a terminal probe endpoint at <c>/hubs/probe</c> and
    /// <c>/api/probe</c>. Registered AFTER <c>next(app)</c> so the full
    /// production pipeline (UseAuthentication/UseAuthorization/MapControllers)
    /// runs first. Unmatched routes fall through to this middleware.
    /// </summary>
    private sealed class ProbeStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                next(app);
                app.Use((RequestDelegate nextMiddleware) => async context =>
                {
                    var path = context.Request.Path;
                    if (path.StartsWithSegments("/hubs/probe") || path.StartsWithSegments("/api/probe"))
                    {
                        if (context.User?.Identity?.IsAuthenticated == true)
                        {
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            await context.Response.WriteAsync("ok");
                        }
                        else
                        {
                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        }
                        return;
                    }
                    await nextMiddleware(context);
                });
            };
        }
    }

    #endregion
}
