using FluentAssertions;
using KanbAI_Core.Extensions;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Extensions;

/// <summary>
/// Unit tests for the SignalR infrastructure DI and CORS configuration
/// introduced in issue #61. These tests verify that:
/// - <see cref="ServiceCollectionExtensions.AddSignalRInfrastructure"/>
///   wires the SignalR services required for hub registration (issue #62+).
/// - <see cref="ServiceCollectionExtensions.AddCorsPolicy"/> produces a
///   policy with <c>SupportsCredentials = true</c>, which is mandatory for
///   authenticated SignalR WebSocket connections.
/// </summary>
public class SignalRInfrastructureTests
{
    private static IConfiguration BuildTestConfiguration(
        Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }

    #region AddSignalRInfrastructure

    [Fact]
    public void AddSignalRInfrastructure_ReturnsSameServiceCollection_ForFluentChaining()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddSignalRInfrastructure();

        // Assert
        result.Should().BeSameAs(services,
            "AddSignalRInfrastructure must return the same IServiceCollection so callers can fluently chain it with other Add* extensions");
    }

    [Fact]
    public void AddSignalRInfrastructure_RegistersHubLifetimeManager()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddSignalRInfrastructure();
        var provider = services.BuildServiceProvider();

        // Assert - HubLifetimeManager<T> is the foundational SignalR service
        // that manages hub connections. Its presence confirms AddSignalR() ran.
        var hubLifetimeManager = provider.GetService<HubLifetimeManager<TestHub>>();
        hubLifetimeManager.Should().NotBeNull(
            "HubLifetimeManager<TestHub> must be resolvable from DI after AddSignalRInfrastructure() — this is the core service registered by AddSignalR()");
    }

    [Fact]
    public void AddSignalRInfrastructure_RegistersHubContext()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddSignalRInfrastructure();
        var provider = services.BuildServiceProvider();

        // Assert - IHubContext<T> is how application code (services,
        // controllers) broadcasts messages to hub clients. It MUST be
        // resolvable for issue #63 (service refactor to broadcast events).
        var hubContext = provider.GetService<IHubContext<TestHub>>();
        hubContext.Should().NotBeNull(
            "IHubContext<TestHub> must be resolvable — this is the entry point for server-side broadcasts from non-hub classes");
    }

    [Fact]
    public void AddSignalRInfrastructure_RegistersSignalRMarkerServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddSignalRInfrastructure();

        // Assert - AddSignalR() registers several known singletons;
        // HubLifetimeManager<> and HubConnectionStore are distinctive markers.
        services.Should().Contain(
            d => d.ServiceType.IsGenericType
                 && d.ServiceType.GetGenericTypeDefinition() == typeof(HubLifetimeManager<>),
            "AddSignalRInfrastructure must register the open-generic HubLifetimeManager<>");
    }

    /// <summary>
    /// Dummy hub used exclusively for DI resolution tests. No hub classes
    /// exist in production code yet (issue #62 adds the first one).
    /// </summary>
    private sealed class TestHub : Hub
    {
    }

    #endregion

    #region AddCorsPolicy - AllowCredentials

    [Fact]
    public void AddCorsPolicy_PolicyHasSupportsCredentialsTrue()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = BuildTestConfiguration(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200"
        });

        // Act
        services.AddCorsPolicy(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = corsOptions.GetPolicy(ServiceCollectionExtensions.CorsPolicyName);

        policy.Should().NotBeNull();
        policy!.SupportsCredentials.Should().BeTrue(
            "SignalR WebSocket handshakes require credentials to be sent with the CORS preflight. " +
            "The policy MUST have SupportsCredentials = true or the browser will block the WebSocket upgrade.");
    }

    [Fact]
    public void AddCorsPolicy_PolicyHasExplicitOriginsNotWildcard()
    {
        // Arrange - .AllowCredentials() is incompatible with wildcard origins.
        // This test protects against a future regression where someone replaces
        // .WithOrigins(allowedOrigins) with .AllowAnyOrigin().
        var services = new ServiceCollection();
        var configuration = BuildTestConfiguration(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200"
        });

        // Act
        services.AddCorsPolicy(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = corsOptions.GetPolicy(ServiceCollectionExtensions.CorsPolicyName);

        policy.Should().NotBeNull();
        policy!.Origins.Should().NotBeEmpty(
            "the policy must have explicit origins; wildcard origins are incompatible with AllowCredentials()");
        policy.Origins.Should().Contain("http://localhost:4200");
    }

    #endregion
}
