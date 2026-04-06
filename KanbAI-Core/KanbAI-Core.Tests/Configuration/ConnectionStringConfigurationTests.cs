using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace KanbAI_Core.Tests.Configuration;

public class ConnectionStringConfigurationTests
{
    private readonly IConfiguration _configuration;

    public ConnectionStringConfigurationTests()
    {
        _configuration = new ConfigurationBuilder()
            .SetBasePath(GetProjectBasePath())
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();
    }

    [Fact]
    public void DevelopmentConfig_DefaultConnection_Exists()
    {
        // Arrange & Act
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        // Assert
        connectionString.Should().NotBeNullOrWhiteSpace(
            "the DefaultConnection connection string must be configured in appsettings.Development.json");
    }

    [Fact]
    public void DevelopmentConfig_DefaultConnection_ContainsServerComponent()
    {
        // Arrange & Act
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        // Assert
        connectionString.Should().Contain("Server=",
            "the connection string must specify a SQL Server instance");
    }

    [Fact]
    public void DevelopmentConfig_DefaultConnection_ContainsDatabaseName()
    {
        // Arrange & Act
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        // Assert
        connectionString.Should().Contain("Database=",
            "the connection string must specify a database name");
    }

    [Fact]
    public void DevelopmentConfig_DefaultConnection_UsesTrustServerCertificate()
    {
        // Arrange & Act
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        // Assert
        connectionString.Should().Contain("TrustServerCertificate=True",
            "local development should trust the server certificate to avoid SSL errors");
    }

    [Fact]
    public void DevelopmentConfig_DefaultConnection_PointsToLocalhost()
    {
        // Arrange & Act
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        // Assert
        connectionString.Should().Contain("Server=localhost",
            "the development connection string should point to the local SQL Server instance");
    }

    [Fact]
    public void DevelopmentConfig_DefaultConnection_TargetsKanbAIDatabase()
    {
        // Arrange & Act
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        // Assert
        connectionString.Should().Contain("Database=KanbAI",
            "the development database should be named KanbAI");
    }

    [Fact]
    public void DevelopmentConfig_ConnectionStringsSection_Exists()
    {
        // Arrange & Act
        var section = _configuration.GetSection("ConnectionStrings");

        // Assert
        section.Exists().Should().BeTrue(
            "the ConnectionStrings section must exist in appsettings.Development.json");
    }

    [Fact]
    public void DevelopmentConfig_NonExistentConnection_ReturnsNull()
    {
        // Arrange & Act
        var connectionString = _configuration.GetConnectionString("NonExistentConnection");

        // Assert
        connectionString.Should().BeNull(
            "requesting an undefined connection string should return null");
    }

    /// <summary>
    /// Resolves the path to the main KanbAI-Core project directory so
    /// appsettings files can be loaded during test runs.
    /// </summary>
    private static string GetProjectBasePath()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var solutionDir = FindParentDirectoryContaining(currentDir, "KanbAI-Core.sln");

        if (solutionDir is null)
            throw new InvalidOperationException(
                "Could not locate the solution directory. Ensure tests run from within the repository.");

        return Path.Combine(solutionDir, "KanbAI-Core");
    }

    private static string? FindParentDirectoryContaining(string startPath, string fileName)
    {
        var directory = new DirectoryInfo(startPath);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, fileName)))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }
}
