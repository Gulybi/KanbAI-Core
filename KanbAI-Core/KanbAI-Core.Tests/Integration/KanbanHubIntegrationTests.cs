using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using KanbAI_Core.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="Hubs.KanbanHub"/> covering authentication enforcement,
/// hub method invocation over a real SignalR connection, and validation-error propagation.
///
/// TestServer does not support the default WebSocket transport used by the SignalR client,
/// so these tests force the <see cref="HttpTransportType.LongPolling"/> transport and
/// wire the factory's HTTP handler into the connection. Authentication is enforced at the
/// HTTP negotiate request, which runs through the same <see cref="AuthenticationMiddleware"/>
/// as any other endpoint — the standard <c>TestAuthHandler</c> pattern used elsewhere in this
/// test project therefore works here too.
/// </summary>
public class KanbanHubIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string HubPath = "/hubs/kanban";
    private const string NegotiatePath = "/hubs/kanban/negotiate?negotiateVersion=1";

    private readonly CustomWebApplicationFactory _factory;

    public KanbanHubIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Auth Enforcement - HTTP Negotiate

    [Fact]
    public async Task Negotiate_Unauthenticated_Returns401()
    {
        // Arrange
        var factory = CreateFactory(userId: null);
        var client = factory.CreateClient();

        // Act — the SignalR client's first call is an HTTP POST to the negotiate endpoint;
        // this runs through AuthenticationMiddleware and should be rejected without a valid identity.
        var response = await client.PostAsync(NegotiatePath, content: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "unauthenticated negotiate requests must be rejected before a SignalR connection is established");
    }

    [Fact]
    public async Task Negotiate_Authenticated_ReturnsSuccess()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var factory = CreateFactory(userId);
        var client = factory.CreateClient();

        // Act
        var response = await client.PostAsync(NegotiatePath, content: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "authenticated negotiate requests should succeed and return a connection id / available transports");

        var body = await response.Content.ReadFromJsonAsync<NegotiateResponseStub>();
        body.Should().NotBeNull();
        body!.ConnectionId.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Connection Lifecycle

    [Fact]
    public async Task Connect_WithValidAuthentication_EstablishesConnection()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var factory = CreateFactory(userId);

        // Act
        await using var connection = BuildHubConnection(factory);
        await connection.StartAsync();

        // Assert
        connection.State.Should().Be(HubConnectionState.Connected);
    }

    [Fact]
    public async Task Connect_Unauthenticated_FailsToStart()
    {
        // Arrange
        var factory = CreateFactory(userId: null);

        // Act
        await using var connection = BuildHubConnection(factory);
        var act = async () => await connection.StartAsync();

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>(
            "the SignalR client should surface the 401 from the negotiate request");
        connection.State.Should().Be(HubConnectionState.Disconnected);
    }

    #endregion

    #region Hub Method Invocation - Happy Path

    [Fact]
    public async Task JoinProjectGroup_ValidProjectId_SuccessfullyJoins()
    {
        // Arrange
        var factory = CreateFactory(Guid.NewGuid());
        await using var connection = BuildHubConnection(factory);
        await connection.StartAsync();

        // Act
        var act = async () => await connection.InvokeAsync("JoinProjectGroup", Guid.NewGuid().ToString());

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LeaveProjectGroup_ValidProjectId_SuccessfullyLeaves()
    {
        // Arrange
        var factory = CreateFactory(Guid.NewGuid());
        await using var connection = BuildHubConnection(factory);
        await connection.StartAsync();
        var projectId = Guid.NewGuid().ToString();
        await connection.InvokeAsync("JoinProjectGroup", projectId);

        // Act
        var act = async () => await connection.InvokeAsync("LeaveProjectGroup", projectId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task LeaveProjectGroup_NeverJoined_DoesNotThrow()
    {
        // Arrange
        var factory = CreateFactory(Guid.NewGuid());
        await using var connection = BuildHubConnection(factory);
        await connection.StartAsync();

        // Act — leaving a group the client never joined is an idempotent no-op in SignalR.
        var act = async () => await connection.InvokeAsync("LeaveProjectGroup", Guid.NewGuid().ToString());

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task JoinMultipleGroups_SameConnection_AllInvocationsSucceed()
    {
        // Arrange
        var factory = CreateFactory(Guid.NewGuid());
        await using var connection = BuildHubConnection(factory);
        await connection.StartAsync();

        // Act — a single connection must be able to join multiple project groups simultaneously
        // (e.g., a user watching two boards in two browser tabs sharing one underlying connection).
        var project1 = Guid.NewGuid().ToString();
        var project2 = Guid.NewGuid().ToString();
        var project3 = Guid.NewGuid().ToString();

        var act = async () =>
        {
            await connection.InvokeAsync("JoinProjectGroup", project1);
            await connection.InvokeAsync("JoinProjectGroup", project2);
            await connection.InvokeAsync("JoinProjectGroup", project3);
        };

        // Assert
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Hub Method Invocation - Validation Errors

    [Fact]
    public async Task JoinProjectGroup_InvalidGuid_ReceivesHubException()
    {
        // Arrange
        var factory = CreateFactory(Guid.NewGuid());
        await using var connection = BuildHubConnection(factory);
        await connection.StartAsync();

        // Act
        var act = async () => await connection.InvokeAsync("JoinProjectGroup", "not-a-guid");

        // Assert — the server-side HubException is marshalled to the client as HubException
        // carrying the original message from the hub method.
        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Contain("Invalid project ID format.");
    }

    [Fact]
    public async Task JoinProjectGroup_EmptyString_ReceivesHubException()
    {
        // Arrange
        var factory = CreateFactory(Guid.NewGuid());
        await using var connection = BuildHubConnection(factory);
        await connection.StartAsync();

        // Act
        var act = async () => await connection.InvokeAsync("JoinProjectGroup", string.Empty);

        // Assert
        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Contain("Project ID is required.");
    }

    [Fact]
    public async Task LeaveProjectGroup_InvalidGuid_ReceivesHubException()
    {
        // Arrange
        var factory = CreateFactory(Guid.NewGuid());
        await using var connection = BuildHubConnection(factory);
        await connection.StartAsync();

        // Act
        var act = async () => await connection.InvokeAsync("LeaveProjectGroup", "abc-123");

        // Assert
        var exception = await act.Should().ThrowAsync<HubException>();
        exception.Which.Message.Should().Contain("Invalid project ID format.");
    }

    #endregion

    #region Test Infrastructure

    private WebApplicationFactory<Program> CreateFactory(Guid? userId)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var config in authConfigs)
                {
                    services.Remove(config);
                }

                services.AddAuthentication("Test")
                    .AddScheme<TestAuthSchemeOptions, TestAuthHandler>(
                        "Test",
                        options => options.UserId = userId);

                services.AddAuthorization(options => options.FallbackPolicy = null);
            });
        });
    }

    /// <summary>
    /// Builds a SignalR <see cref="HubConnection"/> configured to route through the TestServer
    /// using LongPolling transport. WebSocket transport is not supported through TestServer's
    /// in-memory <see cref="HttpMessageHandler"/>, so LongPolling is the simplest way to run
    /// a real client↔server SignalR round-trip in-process.
    /// </summary>
    private static HubConnection BuildHubConnection(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();

        return new HubConnectionBuilder()
            .WithUrl(new Uri(client.BaseAddress!, HubPath), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            })
            .Build();
    }

    private sealed class NegotiateResponseStub
    {
        public string? ConnectionId { get; set; }
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
