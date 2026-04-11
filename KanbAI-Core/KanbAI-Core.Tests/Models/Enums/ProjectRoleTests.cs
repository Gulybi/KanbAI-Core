using FluentAssertions;
using KanbAI_Core.Models.Enums;

namespace KanbAI_Core.Tests.Models.Enums;

public class ProjectRoleTests
{
    [Fact]
    public void ProjectRole_MemberValue_IsZero()
    {
        // Arrange
        var member = ProjectRole.Member;

        // Act
        var value = (int)member;

        // Assert
        value.Should().Be(0);
    }

    [Fact]
    public void ProjectRole_OwnerValue_IsOne()
    {
        // Arrange
        var owner = ProjectRole.Owner;

        // Act
        var value = (int)owner;

        // Assert
        value.Should().Be(1);
    }

    [Fact]
    public void ProjectRole_DefaultValue_IsMember()
    {
        // Arrange & Act
        var defaultRole = default(ProjectRole);

        // Assert
        defaultRole.Should().Be(ProjectRole.Member);
    }

    [Fact]
    public void ProjectRole_IsEnum()
    {
        // Arrange
        var type = typeof(ProjectRole);

        // Act
        var isEnum = type.IsEnum;

        // Assert
        isEnum.Should().BeTrue();
    }
}
