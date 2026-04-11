using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace KanbAI_Core.Tests.Data;

public class MigrationTests
{
    [Fact]
    public void InitialCreate_Exists_AndIsAnnotatedWithCorrectMigrationId()
    {
        // Arrange
        var migrationType = typeof(InitialCreate);

        // Act
        var migrationAttribute = migrationType
            .GetCustomAttributes(typeof(MigrationAttribute), inherit: false)
            .Cast<MigrationAttribute>()
            .FirstOrDefault();

        // Assert
        migrationAttribute.Should().NotBeNull(
            "the InitialCreate migration must have a [Migration] attribute");
        migrationAttribute!.Id.Should().Be("20260406124410_InitialCreate");
    }

    [Fact]
    public void InitialCreate_IsAnnotatedWithDbContextType()
    {
        // Arrange
        var migrationType = typeof(InitialCreate);

        // Act
        var dbContextAttribute = migrationType
            .GetCustomAttributes(typeof(DbContextAttribute), inherit: false)
            .Cast<DbContextAttribute>()
            .FirstOrDefault();

        // Assert
        dbContextAttribute.Should().NotBeNull(
            "the migration must reference the correct DbContext type");
        dbContextAttribute!.ContextType.Should().Be(typeof(ApplicationDbContext));
    }

    [Fact]
    public void InitialCreate_InheritsFromMigration()
    {
        // Arrange & Act
        var migrationType = typeof(InitialCreate);

        // Assert
        migrationType.Should().BeAssignableTo<Migration>(
            "all EF Core migrations must inherit from the Migration base class");
    }
    
    [Fact]
    public void ApplicationDbContextModelSnapshot_Exists_AndReferencesApplicationDbContext()
    {
        // Arrange – resolve by name since the class is internal
        var snapshotType = typeof(ApplicationDbContext).Assembly
            .GetType("KanbAI_Core.Migrations.ApplicationDbContextModelSnapshot");

        // Assert
        snapshotType.Should().NotBeNull(
            "ApplicationDbContextModelSnapshot should exist in the main assembly");
        snapshotType!.Should().BeAssignableTo<ModelSnapshot>();

        var dbContextAttribute = snapshotType
            .GetCustomAttributes(typeof(DbContextAttribute), inherit: false)
            .Cast<DbContextAttribute>()
            .FirstOrDefault();

        dbContextAttribute.Should().NotBeNull();
        dbContextAttribute!.ContextType.Should().Be(typeof(ApplicationDbContext));
    }
}
