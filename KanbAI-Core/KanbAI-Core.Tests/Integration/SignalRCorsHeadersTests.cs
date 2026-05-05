using System.Net;
using FluentAssertions;
using KanbAI_Core.Tests.Fixtures;

namespace KanbAI_Core.Tests.Integration;

/// <summary>
/// Integration tests for the CORS policy's support for SignalR-specific headers.
/// These tests verify that the CORS policy allows the headers required by SignalR
/// client libraries during negotiation and connection establishment.
///
/// Background: SignalR client libraries send headers like "x-requested-with" and
/// "x-signalr-user-agent" during the negotiation protocol. The CORS policy must
/// allow these headers to prevent preflight failures.
///
/// Issue #77: Fixed CORS policy to use .AllowAnyHeader() instead of explicit
/// header list to support SignalR's dynamic header requirements.
/// </summary>
public class SignalRCorsHeadersTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AllowedOrigin = "http://localhost:4200";
    private const string DisallowedOrigin = "http://evil.com";

    private readonly CustomWebApplicationFactory _factory;

    public SignalRCorsHeadersTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OptionsRequest_WithXRequestedWithHeader_ReturnsAllowHeaderInResponse()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/kanban/negotiate");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "x-requested-with");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Should().ContainKey("Access-Control-Allow-Headers",
            "SignalR requires x-requested-with header during negotiation");
        var allowHeaders = response.Headers.GetValues("Access-Control-Allow-Headers").First();
        allowHeaders.Should().Contain("x-requested-with",
            "because SignalR client libraries send this header during negotiation");
    }

    [Fact]
    public async Task OptionsRequest_WithMultipleHeaders_ReturnsAllRequestedHeadersInResponse()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/kanban/negotiate");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type, authorization, x-requested-with, x-signalr-user-agent");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Should().ContainKey("Access-Control-Allow-Headers",
            "CORS preflight must return allowed headers for SignalR negotiation");
        var allowHeaders = response.Headers.GetValues("Access-Control-Allow-Headers").First();
        allowHeaders.Should().Contain("content-type",
            "because SignalR sends Content-Type header");
        allowHeaders.Should().Contain("authorization",
            "because authenticated SignalR connections send JWT tokens in Authorization header");
        allowHeaders.Should().Contain("x-requested-with",
            "because SignalR client libraries send x-requested-with header");
        allowHeaders.Should().Contain("x-signalr-user-agent",
            "because SignalR client libraries send x-signalr-user-agent header for diagnostics");
    }

    [Fact]
    public async Task OptionsRequest_FromAllowedOrigin_IncludesCredentialsHeader()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/kanban/negotiate");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "x-requested-with");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Should().ContainKey("Access-Control-Allow-Credentials",
            "SignalR requires credentials for authenticated connections");
        var allowCredentials = response.Headers.GetValues("Access-Control-Allow-Credentials").First();
        allowCredentials.Should().Be("true",
            "because SignalR WebSocket handshakes are authenticated; without this header the browser blocks the upgrade");
    }

    [Fact]
    public async Task OptionsRequest_FromDisallowedOrigin_DoesNotReturnCorsHeaders()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/hubs/kanban/negotiate");
        request.Headers.Add("Origin", DisallowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "x-requested-with");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse(
            "Allow-Credentials must not be returned for origins outside the CORS allow-list");
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse(
            "Allow-Origin must not be returned for origins outside the CORS allow-list");
        response.Headers.Contains("Access-Control-Allow-Headers").Should().BeFalse(
            "Allow-Headers must not be returned for origins outside the CORS allow-list");
    }

    [Fact]
    public async Task OptionsRequest_ToRestApiEndpoint_WithCustomHeaders_ReturnsAllowHeaders()
    {
        // Arrange - verify backward compatibility with REST API endpoints
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/project");
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type, authorization");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        response.Headers.Should().ContainKey("Access-Control-Allow-Headers",
            "REST API endpoints must continue to work after AllowAnyHeader change");
        var allowHeaders = response.Headers.GetValues("Access-Control-Allow-Headers").First();
        allowHeaders.Should().Contain("content-type",
            "because REST API endpoints send Content-Type header");
        allowHeaders.Should().Contain("authorization",
            "because REST API endpoints send Authorization header with JWT tokens");
    }
}
