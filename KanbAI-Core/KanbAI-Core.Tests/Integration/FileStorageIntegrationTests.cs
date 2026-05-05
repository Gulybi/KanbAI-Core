using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using KanbAI_Core.Models.Configuration;
using KanbAI_Core.Tests.Fixtures;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KanbAI_Core.Tests.Integration;

/// <summary>
/// Integration tests for file storage configuration infrastructure.
/// Tests DI resolution, configuration binding, and fail-fast validation behavior.
/// </summary>
public class FileStorageIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public FileStorageIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region DI Registration Tests

    [Fact]
    public void FileStorageOptions_IsResolvableFromDI()
    {
        // Arrange
        var client = CreateClient();

        // Act - Access the service provider through the factory
        var options = _factory.Services.GetService<IOptions<FileStorageOptions>>();

        // Assert
        options.Should().NotBeNull();
        options!.Value.Should().NotBeNull();
    }

    #endregion

    #region Configuration Binding Tests

    [Fact]
    public void FileStorageOptions_BindsFromConfiguration()
    {
        // Arrange & Act
        IOptions<FileStorageOptions>? capturedOptions = null;

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((context, config) =>
            {
                // Clear existing configuration sources and add only our test config
                config.Sources.Clear();
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileStorage:StoragePath"] = "test/uploads",
                    ["FileStorage:MaxFileSizeBytes"] = "20971520",
                    ["FileStorage:AllowedExtensions:0"] = ".jpg",
                    ["FileStorage:AllowedExtensions:1"] = ".png",
                    ["FileStorage:AllowedExtensions:2"] = ".pdf",
                    // Add minimal config to make the app start
                    ["Testing:SkipScalar"] = "true",
                    ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=Test;Trusted_Connection=True;"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                // Remove Negotiate auth
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var descriptor in authConfigs)
                    services.Remove(descriptor);

                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                services.AddAuthorization(options => options.FallbackPolicy = null);
            });
        });

        // Create client to build the app
        _ = factory.CreateClient();

        // Capture options from the factory's service provider
        capturedOptions = factory.Services.GetRequiredService<IOptions<FileStorageOptions>>();

        // Assert
        capturedOptions.Value.StoragePath.Should().Be("test/uploads");
        capturedOptions.Value.MaxFileSizeBytes.Should().Be(20971520);
        capturedOptions.Value.AllowedExtensions.Should().BeEquivalentTo(new[] { ".jpg", ".png", ".pdf" });
    }

    #endregion

    #region Fail-Fast Validation Tests

    [Fact]
    public void Startup_WithInvalidConfiguration_EmptyAllowedExtensions_ThrowsOptionsValidationException()
    {
        // Arrange & Act
        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.Sources.Clear();
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["FileStorage:StoragePath"] = "wwwroot/uploads",
                        ["FileStorage:MaxFileSizeBytes"] = "10485760",
                        // AllowedExtensions is missing - will be empty array
                        ["Testing:SkipScalar"] = "true",
                        ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=Test;Trusted_Connection=True;"
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    var authConfigs = services
                        .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                        .ToList();
                    foreach (var descriptor in authConfigs)
                        services.Remove(descriptor);

                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.AddAuthorization(options => options.FallbackPolicy = null);
                });
            });

            _ = factory.CreateClient();

            // Force validation by accessing the options
            var options = factory.Services.GetRequiredService<IOptions<FileStorageOptions>>();
            _ = options.Value; // This triggers ValidateOnStart()
        });

        // Assert
        exception.Message.Should().Contain("FileStorage configuration is invalid");
    }

    [Fact]
    public void Startup_WithInvalidConfiguration_DangerousExtension_ThrowsOptionsValidationException()
    {
        // Arrange & Act
        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.Sources.Clear();
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["FileStorage:StoragePath"] = "wwwroot/uploads",
                        ["FileStorage:MaxFileSizeBytes"] = "10485760",
                        ["FileStorage:AllowedExtensions:0"] = ".jpg",
                        ["FileStorage:AllowedExtensions:1"] = ".exe", // Dangerous extension
                        ["Testing:SkipScalar"] = "true",
                        ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=Test;Trusted_Connection=True;"
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    var authConfigs = services
                        .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                        .ToList();
                    foreach (var descriptor in authConfigs)
                        services.Remove(descriptor);

                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.AddAuthorization(options => options.FallbackPolicy = null);
                });
            });

            _ = factory.CreateClient();

            // Force validation by accessing the options
            var options = factory.Services.GetRequiredService<IOptions<FileStorageOptions>>();
            _ = options.Value; // This triggers ValidateOnStart()
        });

        // Assert
        exception.Message.Should().Contain("FileStorage configuration is invalid");
    }

    [Fact]
    public void Startup_WithInvalidConfiguration_NegativeMaxFileSize_ThrowsOptionsValidationException()
    {
        // Arrange & Act
        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.Sources.Clear();
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["FileStorage:StoragePath"] = "wwwroot/uploads",
                        ["FileStorage:MaxFileSizeBytes"] = "-1", // Invalid: negative
                        ["FileStorage:AllowedExtensions:0"] = ".jpg",
                        ["Testing:SkipScalar"] = "true",
                        ["ConnectionStrings:DefaultConnection"] = "Server=localhost;Database=Test;Trusted_Connection=True;"
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    var authConfigs = services
                        .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                        .ToList();
                    foreach (var descriptor in authConfigs)
                        services.Remove(descriptor);

                    services.AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                    services.AddAuthorization(options => options.FallbackPolicy = null);
                });
            });

            _ = factory.CreateClient();

            // Force validation by accessing the options
            var options = factory.Services.GetRequiredService<IOptions<FileStorageOptions>>();
            _ = options.Value; // This triggers ValidateOnStart()
        });

        // Assert
        exception.Message.Should().Contain("FileStorage configuration is invalid");
    }

    #endregion

    #region Test Infrastructure

    private HttpClient CreateClient(string environment = "Development")
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                // Remove all Negotiate auth scheme registrations
                var authConfigs = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<AuthenticationOptions>))
                    .ToList();
                foreach (var descriptor in authConfigs)
                    services.Remove(descriptor);

                // Register a no-op test auth scheme
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

                // Disable the authorization fallback policy
                services.AddAuthorization(options => options.FallbackPolicy = null);
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
