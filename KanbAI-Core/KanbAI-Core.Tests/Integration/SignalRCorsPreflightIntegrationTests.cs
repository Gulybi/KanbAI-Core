using System.Net;
using FluentAssertions;
using KanbAI_Core.Tests.Fixtures;

namespace KanbAI_Core.Tests.Integration;

/// <summary>
/// Integration tests for the CORS policy behavior on future SignalR hub
/// paths (<c>/hubs/*</c>). These tests do NOT establish WebSocket connections
/// (TestServer does not support WebSockets) — they validate that the CORS
/// preflight response advertises <c>Access-Control-Allow-Credentials: true</c>,
/// which is the browser-level gate for authenticated SignalR connections.
///
/// No hub endpoints exist yet (issue #61 is infrastructure-only), so these
/// requests will still return 404 from the endpoint router — but CORS headers
/// are written by the CORS middleware BEFORE routing, so they are present
/// on the response regardless.
/// </summary>
public class SignalRCorsPreflightIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AllowedOrigin = "http://localhost:4200";
    private const string DisallowedOrigin = "http://evil.com";

    private readonly CustomWebApplicationFactory _factory;

    public SignalRCorsPreflightIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Preflight_ToFutureHubPath_FromAllowedOrigin_IncludesAllowCredentialsHeader()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/test");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Should().ContainKey("Access-Control-Allow-Credentials",
            "SignalR WebSocket handshakes are authenticated; without this header the browser blocks the upgrade");
        response.Headers.GetValues("Access-Control-Allow-Credentials")
            .Should().ContainSingle().Which.Should().Be("true");

        response.Headers.Should().ContainKey("Access-Control-Allow-Origin",
            "the preflight must echo the request origin for authenticated CORS");
        response.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().ContainSingle().Which.Should().Be(AllowedOrigin);
    }

    [Fact]
    public async Task Preflight_ToFutureHubPath_FromDisallowedOrigin_DoesNotIncludeCorsHeaders()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/test");
        request.Headers.Add("Origin", DisallowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await client.SendAsync(request);

        // Assert - a CORS policy that does not match the origin must NOT
        // emit Allow-Credentials (browsers would accept a cross-origin
        // credentialed request if both the origin and credentials headers
        // were present). Missing headers are the correct rejection.
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse(
            "Allow-Credentials must not be returned for origins outside the CORS allow-list");
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse(
            "Allow-Origin must not be returned for origins outside the CORS allow-list");
    }

    [Fact]
    public async Task Preflight_ToRestApiPath_FromAllowedOrigin_AlsoIncludesAllowCredentialsHeader()
    {
        // Arrange - the policy is shared: enabling AllowCredentials() for
        // SignalR must not break REST preflights from the same origin.
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/project");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Should().ContainKey("Access-Control-Allow-Credentials");
        response.Headers.GetValues("Access-Control-Allow-Credentials")
            .Should().ContainSingle().Which.Should().Be("true");
    }
}
