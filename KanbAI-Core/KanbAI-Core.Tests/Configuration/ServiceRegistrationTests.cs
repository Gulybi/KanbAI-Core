using FluentAssertions;
using KanbAI_Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KanbAI_Core.Tests.Configuration;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddDbContext_WithSqlServerProvider_RegistersApplicationDbContext()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("TestDb_DI_Registration"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var context = serviceProvider.GetService<ApplicationDbContext>();

        // Assert
        context.Should().NotBeNull(
            "ApplicationDbContext must be resolvable from the DI container");
    }

    [Fact]
    public void AddDbContext_WithSqlServerProvider_RegistersDbContextOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("TestDb_DI_Options"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetService<DbContextOptions<ApplicationDbContext>>();

        // Assert
        options.Should().NotBeNull(
            "DbContextOptions<ApplicationDbContext> must be resolvable from the DI container");
    }

    [Fact]
    public void AddDbContext_WithScopedLifetime_CreatesDifferentInstancesPerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("TestDb_DI_Scoped"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        ApplicationDbContext context1;
        ApplicationDbContext context2;

        using (var scope1 = serviceProvider.CreateScope())
        {
            context1 = scope1.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        using (var scope2 = serviceProvider.CreateScope())
        {
            context2 = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }

        // Assert
        context1.Should().NotBeSameAs(context2,
            "each scope should receive its own ApplicationDbContext instance");
    }

    [Fact]
    public void AddDbContext_WithSameScope_ReturnsSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("TestDb_DI_SameScope"));

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert
        using var scope = serviceProvider.CreateScope();
        var context1 = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var context2 = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        context1.Should().BeSameAs(context2,
            "within the same scope, the same DbContext instance should be returned");
    }

    [Fact]
    public void AddDbContext_WithoutRegistration_ReturnsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var context = serviceProvider.GetService<ApplicationDbContext>();

        // Assert
        context.Should().BeNull(
            "ApplicationDbContext should not be available when it is not registered");
    }

    [Fact]
    public async Task ResolvedDbContext_CanPerformBasicOperations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase("TestDb_DI_Operations"));

        var serviceProvider = services.BuildServiceProvider();

        // Act
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var canConnect = await context.Database.CanConnectAsync();

        // Assert
        canConnect.Should().BeTrue(
            "the resolved ApplicationDbContext should be able to connect to the database");
    }
}
