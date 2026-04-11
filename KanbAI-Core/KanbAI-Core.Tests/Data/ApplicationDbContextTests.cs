using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Tests.Data;

public class ApplicationDbContextTests
{
    [Fact]
    public void Constructor_WithInMemoryOptions_CreatesInstance()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_Constructor")
            .Options;

        // Act
        using var context = new ApplicationDbContext(options);

        // Assert
        context.Should().NotBeNull();
        context.Should().BeAssignableTo<DbContext>();
    }

    [Fact]
    public async Task Database_WithInMemoryProvider_CanConnect()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_CanConnect")
            .Options;

        // Act
        using var context = new ApplicationDbContext(options);
        var canConnect = await context.Database.CanConnectAsync();

        // Assert
        canConnect.Should().BeTrue();
    }

    [Fact]
    public async Task Database_WithInMemoryProvider_EnsureCreatedSucceeds()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_EnsureCreated")
            .Options;

        // Act
        using var context = new ApplicationDbContext(options);
        var created = await context.Database.EnsureCreatedAsync();

        // Assert
        created.Should().BeTrue();
    }

    [Fact]
    public void Constructor_InheritsFromDbContext()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_Inheritance")
            .Options;

        // Act
        using var context = new ApplicationDbContext(options);

        // Assert
        context.Should().BeAssignableTo<DbContext>();
    }

    [Fact]
    public void Constructor_WithDifferentDatabaseNames_CreatesIsolatedInstances()
    {
        // Arrange
        var options1 = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_Isolated_1")
            .Options;

        var options2 = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_Isolated_2")
            .Options;

        // Act
        using var context1 = new ApplicationDbContext(options1);
        using var context2 = new ApplicationDbContext(options2);

        // Assert
        context1.Should().NotBeSameAs(context2);
        context1.Database.ProviderName
            .Should().Be(context2.Database.ProviderName,
                "both contexts use the same provider but are distinct instances");
    }

    [Fact]
    public void Dispose_AfterCreation_DoesNotThrow()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_Dispose")
            .Options;

        // Act
        var context = new ApplicationDbContext(options);
        var dispose = () => context.Dispose();

        // Assert
        dispose.Should().NotThrow();
    }

    [Fact]
    public async Task SaveChangesAsync_WithNoEntities_ReturnsZero()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_SaveChanges")
            .Options;

        // Act
        using var context = new ApplicationDbContext(options);
        var result = await context.SaveChangesAsync();

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void ApplicationDbContext_UsersDbSet_IsNotNull()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_UsersDbSet")
            .Options;

        // Act
        using var context = new ApplicationDbContext(options);

        // Assert
        context.Users.Should().NotBeNull();
        context.Users.Should().BeAssignableTo<DbSet<User>>();
    }

    [Fact]
    public void ApplicationDbContext_ProjectsDbSet_IsNotNull()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_ProjectsDbSet")
            .Options;

        // Act
        using var context = new ApplicationDbContext(options);

        // Assert
        context.Projects.Should().NotBeNull();
        context.Projects.Should().BeAssignableTo<DbSet<Project>>();
    }

    [Fact]
    public void ApplicationDbContext_ProjectMembersDbSet_IsNotNull()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_ProjectMembersDbSet")
            .Options;

        // Act
        using var context = new ApplicationDbContext(options);

        // Assert
        context.ProjectMembers.Should().NotBeNull();
        context.ProjectMembers.Should().BeAssignableTo<DbSet<ProjectMember>>();
    }

    [Fact]
    public void ApplicationDbContext_BoardColumnsDbSet_IsNotNull()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_BoardColumnsDbSet")
            .Options;

        // Act
        using var context = new ApplicationDbContext(options);

        // Assert
        context.BoardColumns.Should().NotBeNull();
        context.BoardColumns.Should().BeAssignableTo<DbSet<BoardColumn>>();
    }

    [Fact]
    public void ApplicationDbContext_KanbanTasksDbSet_IsNotNull()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb_KanbanTasksDbSet")
            .Options;

        // Act
        using var context = new ApplicationDbContext(options);

        // Assert
        context.KanbanTasks.Should().NotBeNull();
        context.KanbanTasks.Should().BeAssignableTo<DbSet<KanbanTask>>();
    }
}
