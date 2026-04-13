using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Extensions;
using KanbAI_Core.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Extensions;

public class ServiceCollectionExtensionTests
{
    private static IConfiguration BuildTestConfiguration(
        Dictionary<string, string?>? values = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();
    }

    #region AddPersistence

    [Fact]
    public void AddPersistence_RegistersApplicationDbContext()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = BuildTestConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=TestDb;Trusted_Connection=True"
        });

        // Act
        services.AddPersistence(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetService<ApplicationDbContext>();
        context.Should().NotBeNull(
            "ApplicationDbContext must be resolvable after calling AddPersistence");
    }

    [Fact]
    public void AddPersistence_RegistersDbContextOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = BuildTestConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=TestDb;Trusted_Connection=True"
        });

        // Act
        services.AddPersistence(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var options = provider.GetService<DbContextOptions<ApplicationDbContext>>();
        options.Should().NotBeNull(
            "DbContextOptions<ApplicationDbContext> must be resolvable after calling AddPersistence");
    }

    #endregion

    #region AddApiInfrastructure

    [Fact]
    public void AddApiInfrastructure_RegistersExceptionHandler()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddApiInfrastructure();

        // Assert
        services.Should().Contain(
            d => d.ServiceType == typeof(IExceptionHandler)
                 && d.ImplementationType == typeof(GlobalExceptionHandler),
            "AddApiInfrastructure must register GlobalExceptionHandler as IExceptionHandler");
    }

    [Fact]
    public void AddApiInfrastructure_RegistersProblemDetails()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddApiInfrastructure();

        // Assert
        services.Should().Contain(
            d => d.ServiceType == typeof(IProblemDetailsService),
            "AddApiInfrastructure must register IProblemDetailsService");
    }

    [Fact]
    public void AddApiInfrastructure_RegistersControllerServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddApiInfrastructure();
        var provider = services.BuildServiceProvider();

        // Assert
        var actionDescriptorProvider = provider.GetService<IActionDescriptorCollectionProvider>();
        actionDescriptorProvider.Should().NotBeNull(
            "AddApiInfrastructure must register MVC controller services (IActionDescriptorCollectionProvider should be resolvable)");
    }

    #endregion

    #region AddAuthServices

    [Fact]
    public void AddAuthServices_RegistersAuthentication()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddAuthServices();
        var provider = services.BuildServiceProvider();

        // Assert
        var schemeProvider = provider.GetService<IAuthenticationSchemeProvider>();
        schemeProvider.Should().NotBeNull(
            "IAuthenticationSchemeProvider must be resolvable after calling AddAuthServices");
    }

    [Fact]
    public void AddAuthServices_RegistersAuthorization()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddAuthServices();
        var provider = services.BuildServiceProvider();

        // Assert
        var policyProvider = provider.GetService<IAuthorizationPolicyProvider>();
        policyProvider.Should().NotBeNull(
            "IAuthorizationPolicyProvider must be resolvable after calling AddAuthServices");
    }

    #endregion

    #region AddCorsPolicy

    [Fact]
    public void AddCorsPolicy_RegistersCorsOptions()
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
        policy.Should().NotBeNull(
            $"a CORS policy named '{ServiceCollectionExtensions.CorsPolicyName}' must be registered");
    }

    [Fact]
    public void AddCorsPolicy_WithNoConfiguredOrigins_RegistersEmptyPolicy()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = BuildTestConfiguration();

        // Act
        services.AddCorsPolicy(configuration);
        var provider = services.BuildServiceProvider();

        // Assert
        var corsOptions = provider.GetRequiredService<IOptions<CorsOptions>>().Value;
        var policy = corsOptions.GetPolicy(ServiceCollectionExtensions.CorsPolicyName);
        policy.Should().NotBeNull(
            "AddCorsPolicy must register the policy even when no origins are configured");
    }

    #endregion

    #region Fluent Chaining & Constants

    [Fact]
    public void AllExtensionMethods_ReturnServiceCollection_ForFluentChaining()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = BuildTestConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=.;Database=Test;Trusted_Connection=True"
        });

        // Act & Assert
        services.AddPersistence(configuration).Should().BeSameAs(services);
        services.AddApiInfrastructure().Should().BeSameAs(services);
        services.AddAuthServices().Should().BeSameAs(services);
        services.AddCorsPolicy(configuration).Should().BeSameAs(services);
    }

    [Fact]
    public void CorsPolicyName_HasExpectedValue()
    {
        // Assert
        ServiceCollectionExtensions.CorsPolicyName.Should().Be("AllowAngularFrontend");
    }

    #endregion
}
