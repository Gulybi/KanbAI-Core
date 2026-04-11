using FluentAssertions;
using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Tests.Models.Enums;

public class UserRoleTests
{
    [Fact]
    public void UserRole_MemberValue_IsZero()
    {
        // Arrange & Act
        var value = (int)UserRole.Member;

        // Assert
        value.Should().Be(0);
    }

    [Fact]
    public void UserRole_AdminValue_IsOne()
    {
        // Arrange & Act
        var value = (int)UserRole.Admin;

        // Assert
        value.Should().Be(1);
    }

    [Fact]
    public void UserRole_DefaultValue_IsMember()
    {
        // Arrange & Act
        var defaultRole = default(UserRole);

        // Assert
        defaultRole.Should().Be(UserRole.Member);
    }

    [Fact]
    public void UserRole_IsEnum()
    {
        // Arrange & Act
        var isEnum = typeof(UserRole).IsEnum;

        // Assert
        isEnum.Should().BeTrue();
    }
}
