using FluentAssertions;
using KanbAI_Core.Models.Entities;
using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Tests.Models.Entities;

public class UserTests
{
    [Fact]
    public void User_InheritsFromBaseEntity()
    {
        // Arrange
        var type = typeof(User);

        // Act
        var isSubclass = type.IsSubclassOf(typeof(BaseEntity));

        // Assert
        isSubclass.Should().BeTrue("User must inherit from BaseEntity");
    }

    [Fact]
    public void User_NameProperty_IsStringType()
    {
        // Arrange
        var property = typeof(User).GetProperty(nameof(User.Name));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void User_EmailProperty_IsStringType()
    {
        // Arrange
        var property = typeof(User).GetProperty(nameof(User.Email));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void User_PasswordHashProperty_IsStringType()
    {
        // Arrange
        var property = typeof(User).GetProperty(nameof(User.PasswordHash));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));
    }

    [Fact]
    public void User_RoleProperty_IsUserRoleType()
    {
        // Arrange
        var property = typeof(User).GetProperty(nameof(User.Role));

        // Act & Assert
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(UserRole));
    }

    [Fact]
    public void User_AllProperties_HavePublicGettersAndSetters()
    {
        // Arrange
        var properties = new[] { "Name", "Email", "PasswordHash", "Role", "Id", "CreatedAt", "UpdatedAt" };

        // Act & Assert
        foreach (var name in properties)
        {
            var prop = typeof(User).GetProperty(name);
            prop.Should().NotBeNull($"property '{name}' should exist");
            prop!.GetMethod.Should().NotBeNull($"'{name}' should have a getter");
            prop.GetMethod!.IsPublic.Should().BeTrue($"'{name}' getter should be public");
            prop.SetMethod.Should().NotBeNull($"'{name}' should have a setter");
            prop.SetMethod!.IsPublic.Should().BeTrue($"'{name}' setter should be public");
        }
    }

    [Fact]
    public void User_DefaultPropertyValues_AreExpected()
    {
        // Arrange & Act
        var user = new User();

        // Assert
        user.Name.Should().Be(string.Empty);
        user.Email.Should().Be(string.Empty);
        user.PasswordHash.Should().Be(string.Empty);
        user.Role.Should().Be(UserRole.Member);
    }

    [Fact]
    public void User_CastToBaseEntity_RetainsPropertyValues()
    {
        // Arrange
        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var updated = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = id,
            CreatedAt = created,
            UpdatedAt = updated,
            Name = "Test User",
            Email = "test@example.com",
            PasswordHash = "$2a$11$examplehashvalue",
            Role = UserRole.Admin
        };

        // Act
        BaseEntity baseRef = user;

        // Assert
        baseRef.Id.Should().Be(id);
        baseRef.CreatedAt.Should().Be(created);
        baseRef.UpdatedAt.Should().Be(updated);
    }
}
