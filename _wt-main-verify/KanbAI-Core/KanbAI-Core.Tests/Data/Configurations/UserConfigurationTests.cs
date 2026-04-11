using FluentAssertions;
using KanbAI_Core.Data;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace KanbAI_Core.Tests.Data.Configurations;

public class UserConfigurationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _context;

    public UserConfigurationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task UserConfiguration_Email_HasUniqueIndex()
    {
        // Arrange
        var sharedEmail = "duplicate@example.com";
        var user1 = CreateValidUser();
        user1.Email = sharedEmail;

        var user2 = CreateValidUser();
        user2.Email = sharedEmail;

        _context.Users.Add(user1);
        await _context.SaveChangesAsync();

        // Act
        _context.Users.Add(user2);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UserConfiguration_Name_IsRequired()
    {
        // Arrange
        var user = CreateValidUser();
        user.Name = null!;

        // Act
        _context.Users.Add(user);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UserConfiguration_Email_IsRequired()
    {
        // Arrange
        var user = CreateValidUser();
        user.Email = null!;

        // Act
        _context.Users.Add(user);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UserConfiguration_PasswordHash_IsRequired()
    {
        // Arrange
        var user = CreateValidUser();
        user.PasswordHash = null!;

        // Act
        _context.Users.Add(user);
        var act = () => _context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task UserConfiguration_Role_DefaultsToMember()
    {
        // Arrange
        var user = CreateValidUser();

        // Act
        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();
        var savedUser = await _context.Users.FindAsync(user.Id);

        // Assert
        savedUser.Should().NotBeNull();
        savedUser!.Role.Should().Be(UserRole.Member);
    }

    private static User CreateValidUser() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test User",
        Email = $"{Guid.NewGuid()}@example.com",
        PasswordHash = "$2a$11$examplehashvalue",
        Role = UserRole.Member
    };

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
